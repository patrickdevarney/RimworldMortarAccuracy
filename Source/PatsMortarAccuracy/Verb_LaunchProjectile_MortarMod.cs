#if false
using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using Multiplayer.API;

namespace MortarAccuracy
{
    public class Verb_LaunchProjectile_MortarMod : Verb_Shoot
    {
        public override float HighlightFieldRadiusAroundTarget(out bool needLOSToCenter)
        {
            if (!Settings.showAccuracyRadius) 
                return base.HighlightFieldRadiusAroundTarget(out needLOSToCenter);

            needLOSToCenter = false;
            float missRadius = GetAdjustedForcedMissRadius();
            if (missRadius < 1)
                missRadius = 1f;
            return missRadius;
        }

        protected override bool TryCastShot()
        {
            //bool flag = base.TryCastShot();
            bool flag = FireProjectile();
            if (flag && base.CasterIsPawn)
            {
                base.CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }
            return flag;
        }

        float GetAdjustedForcedMissRadius()
        {
            if (base.verbProps.forcedMissRadius > 0.5f)
            {
                CompMannable compMannable = base.caster.TryGetComp<CompMannable>();
                // Grab default forced miss radius for this particular weapon
                float missRadiusForShot = base.verbProps.forcedMissRadius;
                float skillMultiplier = 1f;
                // We want to multiply this forced miss radius by our pawn's skill modifier
                if (compMannable != null && compMannable.ManningPawn != null && compMannable.ManningPawn.skills != null)
                {
                    int totalSkill = 0;
                    int skillsTotaled = 0;
                    if (Settings.intellectualAffectsMortarAccuracy)
                    {
                        totalSkill += compMannable.ManningPawn.skills.GetSkill(SkillDefOf.Intellectual).Level;
                        skillsTotaled++;
                    }
                    if (Settings.shootingAffectsMortarAccuracy)
                    {
                        totalSkill += compMannable.ManningPawn.skills.GetSkill(SkillDefOf.Shooting).Level;
                        skillsTotaled++;
                    }

                    if (skillsTotaled > 0)
                    {
                        // get average skill
                        int averageSkill = (int)(((float)totalSkill) / skillsTotaled);
                        skillMultiplier = 1 - ((averageSkill - SkillRecord.MinLevel) * (Settings.maxSkillSpreadReduction - Settings.minSkillSpreadReduction) / (SkillRecord.MaxLevel - SkillRecord.MinLevel) + Settings.minSkillSpreadReduction);
                    }
                }

                // Weather should affect shot no matter what the skill is
                if (Settings.weatherAffectsMortarAccuracy)
                    missRadiusForShot = (missRadiusForShot * skillMultiplier) + ((1 - caster.Map.weatherManager.CurWeatherAccuracyMultiplier) * missRadiusForShot);
                else
                    missRadiusForShot = (missRadiusForShot * skillMultiplier);

                return VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, base.currentTarget.Cell - base.caster.Position);
            }
            else
            {
                return 0;
            }
        }

        public bool FireProjectile()
        {
            if (base.currentTarget.HasThing && base.currentTarget.Thing.Map != base.caster.Map)
            {
                return false;
            }
            ThingDef projectile = this.Projectile;
            if (projectile == null)
            {
                return false;
            }
            ShootLine shootLine = default(ShootLine);
            bool flag = base.TryFindShootLineFromTo(base.caster.Position, base.currentTarget, out shootLine);
            if (base.verbProps.stopBurstWithoutLos && !flag)
            {
                return false;
            }
            if (base.EquipmentSource != null)
            {
                CompChangeableProjectile comp = base.EquipmentSource.GetComp<CompChangeableProjectile>();
                if (comp != null)
                {
                    comp.Notify_ProjectileLaunched();
                }
            }
            Thing launcher = base.caster;
            Thing equipment = base.EquipmentSource;
            CompMannable compMannable = base.caster.TryGetComp<CompMannable>();
            if (compMannable != null && compMannable.ManningPawn != null)
            {
                launcher = compMannable.ManningPawn;
                equipment = base.caster;
                // Earn skills
                if (compMannable.ManningPawn.skills != null)
                {
                    int skillsAffectingAccuracy = 0;
                    if (Settings.intellectualAffectsMortarAccuracy)
                        skillsAffectingAccuracy++;
                    if (Settings.shootingAffectsMortarAccuracy)
                        skillsAffectingAccuracy++;

                    float skillXP = verbProps.AdjustedFullCycleTime(this, CasterPawn) * 100;
                    skillXP /= skillsAffectingAccuracy;

                    if (Settings.intellectualAffectsMortarAccuracy)
                        compMannable.ManningPawn.skills.Learn(SkillDefOf.Intellectual, skillXP, false);
                    if (Settings.shootingAffectsMortarAccuracy)
                        compMannable.ManningPawn.skills.Learn(SkillDefOf.Shooting, skillXP, false);
                }
            }
            Vector3 drawPos = base.caster.DrawPos;
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, shootLine.Source, base.caster.Map, WipeMode.Vanish);

            // If targetting a pawn
            if (Settings.targetLeading)
            {
                if (currentTarget != null && currentTarget.Thing != null && currentTarget.Thing is Pawn targetPawn && targetPawn.pather.curPath != null)
                {
                    List<IntVec3> nodes = new List<IntVec3>(targetPawn.pather.curPath.NodesReversed);
                    nodes.Reverse();
                    // Purge outdated nodes from list
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        if (nodes[i] == targetPawn.Position)
                        {
                            // Remove all previous nodes
                            nodes.RemoveRange(0, i);
                            //Log.Message("Removed " + i + " entries. First node is now " + nodes[0].ToString());
                            break;
                        }
                    }
                    // Path of target pawn from current to destination
                    // Need travel speed of pawn, estimate Vec3 they will be in based on travel speed of our projectile
                    float targetMoveSpeed = targetPawn.GetStatValue(StatDefOf.MoveSpeed);
                    float projectileMoveSpeed = projectile.projectile.speed;

                    // Estimate position target will be in after this amount of time
                    IntVec3 bestTarget = targetPawn.Position;
                    float bestTimeOffset = float.MaxValue;
                    //Log.Message("Default time offset = " + Mathf.Abs(((targetPawn.Position - caster.Position).LengthHorizontal) / projectileMoveSpeed));
                    float accumulatedTargetTime = 0f;

                    IntVec3 previousPosition = targetPawn.Position;
                    foreach (IntVec3 pathPosition in nodes)
                    {
                        float projectileDistanceFromTarget = (pathPosition - caster.Position).LengthHorizontal;
                        float timeForProjectileToReachPosition = projectileDistanceFromTarget / projectileMoveSpeed;

                        //float pawnDistanceFromTarget = (pathPosition - targetPawn.Position).LengthHorizontal;
                        //float timeForPawnToReachPosition = pawnDistanceFromTarget / targetMoveSpeed;

                        float pawnDistanceFromLastPositionToHere = (pathPosition - previousPosition).LengthHorizontal;
                        float timeForPawnToReachPositionFromLastPosition = pawnDistanceFromLastPositionToHere / targetMoveSpeed;
                        accumulatedTargetTime += timeForPawnToReachPositionFromLastPosition;

                        float timeOffset = Mathf.Abs(timeForProjectileToReachPosition - accumulatedTargetTime);
                        if (timeOffset < bestTimeOffset)
                        {
                            bestTarget = pathPosition;
                            bestTimeOffset = timeOffset;
                            //Log.Message("Position " + pathPosition.ToString() + " is better. Time offset is " + timeOffset);
                        }
                        else
                        {
                            //Log.Message("Position " + pathPosition.ToString() + " is not better. Time offset is " + timeOffset);
                        }

                        previousPosition = pathPosition;
                    }
                    //Log.Message("Initial target cell = " + currentTarget.Cell.ToString() + " and new target is " + bestTarget.ToString());
                    currentTarget = new LocalTargetInfo(bestTarget);
                }
            }

            if (base.verbProps.forcedMissRadius > 0.5f)
            {
                float adjustedForcedMissRadius = GetAdjustedForcedMissRadius();
                ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.All;
                IntVec3 targetPosition = currentTarget.Cell;
                if (adjustedForcedMissRadius > 0.5f)
                {
                    int max = GenRadial.NumCellsInRadius(adjustedForcedMissRadius);

                    if (MP.enabled)
                    {
                        Rand.PushState();
                    }

                    // Calculate random target position using a uniform distribution
                    float randomCircleArea = Rand.Range(0, Mathf.PI * adjustedForcedMissRadius * adjustedForcedMissRadius);
                    float radiusOfRandomCircle = Mathf.Sqrt(randomCircleArea / Mathf.PI);
                    float randomAngle = Rand.Range(0, 2 * Mathf.PI);
                    targetPosition = new IntVec3(
                        (int)(targetPosition.x + radiusOfRandomCircle * Mathf.Cos(randomAngle)),
                        targetPosition.y,
                        (int)(targetPosition.z + radiusOfRandomCircle * Mathf.Sin(randomAngle))
                        );

                    if (MP.enabled)
                    {
                        Rand.PopState();
                    }
                }

                //Log.Message("Final target is " + c.ToString());
                projectile2.Launch(launcher, drawPos, targetPosition, base.currentTarget, projectileHitFlags, equipment, null);
                return true;
            }
            ShotReport shotReport = ShotReport.HitReportFor(base.caster, this, base.currentTarget);
            Thing randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
            ThingDef targetCoverDef = (randomCoverToMissInto == null) ? null : randomCoverToMissInto.def;
            /*if (!Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
            {
                shootLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget);
                this.ThrowDebugText("ToWild" + ((!base.canHitNonTargetPawnsNow) ? string.Empty : "\nchntp"));
                this.ThrowDebugText("Wild\nDest", shootLine.Dest);
                ProjectileHitFlags projectileHitFlags2 = ProjectileHitFlags.NonTargetWorld;
                if (Rand.Chance(0.5f) && base.canHitNonTargetPawnsNow)
                {
                    projectileHitFlags2 |= ProjectileHitFlags.NonTargetPawns;
                }
                projectile2.Launch(launcher, drawPos, shootLine.Dest, base.currentTarget, projectileHitFlags2, equipment, targetCoverDef);
                return true;
            }*/
            /*if (base.currentTarget.Thing != null && base.currentTarget.Thing.def.category == ThingCategory.Pawn && !Rand.Chance(shotReport.PassCoverChance))
            {
                this.ThrowDebugText("ToCover" + ((!base.canHitNonTargetPawnsNow) ? string.Empty : "\nchntp"));
                this.ThrowDebugText("Cover\nDest", randomCoverToMissInto.Position);
                ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
                if (base.canHitNonTargetPawnsNow)
                {
                    projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
                }
                projectile2.Launch(launcher, drawPos, randomCoverToMissInto, base.currentTarget, projectileHitFlags3, equipment, targetCoverDef);
                return true;
            }*/
            ProjectileHitFlags projectileHitFlags4 = ProjectileHitFlags.IntendedTarget;
            if (base.canHitNonTargetPawnsNow)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetPawns;
            }
            if (!base.currentTarget.HasThing || base.currentTarget.Thing.def.Fillage == FillCategory.Full)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetWorld;
            }
            this.ThrowDebugText("ToHit" + ((!base.canHitNonTargetPawnsNow) ? string.Empty : "\nchntp"));
            if (base.currentTarget.Thing != null)
            {
                projectile2.Launch(launcher, drawPos, base.currentTarget, base.currentTarget, projectileHitFlags4, equipment, targetCoverDef);
                this.ThrowDebugText("Hit\nDest", base.currentTarget.Cell);
            }
            else
            {
                projectile2.Launch(launcher, drawPos, shootLine.Dest, base.currentTarget, projectileHitFlags4, equipment, targetCoverDef);
                this.ThrowDebugText("Hit\nDest", shootLine.Dest);
            }
            return true;
        }

        private void ThrowDebugText(string text)
        {
            if (DebugViewSettings.drawShooting)
            {
                MoteMaker.ThrowText(base.caster.DrawPos, base.caster.Map, text, -1f);
            }
        }

        private void ThrowDebugText(string text, IntVec3 c)
        {
            if (DebugViewSettings.drawShooting)
            {
                MoteMaker.ThrowText(c.ToVector3Shifted(), base.caster.Map, text, -1f);
            }
        }
    }
}
#endif