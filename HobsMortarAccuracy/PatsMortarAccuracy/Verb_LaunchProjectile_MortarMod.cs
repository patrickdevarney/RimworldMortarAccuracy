using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace MortarAccuracy
{
    public class Verb_LaunchProjectile_MortarMod : Verb_Shoot
    {
        public override float HighlightFieldRadiusAroundTarget(out bool needLOSToCenter)
        {
            if (!Settings.showAccuracyRadius) 
                return base.HighlightFieldRadiusAroundTarget(out needLOSToCenter);

            needLOSToCenter = false;
            float missRadiusForShot = base.verbProps.forcedMissRadius;
            float skillMultiplier = 1f;
            CompMannable compMannable = base.caster.TryGetComp<CompMannable>();
            if (compMannable != null)
            {
                if (compMannable.ManningPawn != null)
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
            }

            // Weather should affect shot no matter what the skill is
            if (Settings.weatherAffectsMortarAccuracy)
                missRadiusForShot = (missRadiusForShot * skillMultiplier) + ((1 - caster.Map.weatherManager.CurWeatherAccuracyMultiplier) * missRadiusForShot);
            else
                missRadiusForShot = (missRadiusForShot * skillMultiplier);


            return missRadiusForShot;
        }

        protected override bool TryCastShot()
        {
            //bool flag = base.TryCastShot();
            bool flag = DoThing();
            if (flag && base.CasterIsPawn)
            {
                base.CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }
            return flag;
        }

        public bool DoThing()
        {
            //Log.Message("Thing");
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
            }
            Vector3 drawPos = base.caster.DrawPos;
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, shootLine.Source, base.caster.Map, WipeMode.Vanish);

            // TODO: add target leading
            if (currentTarget.Thing != null)
            {
                if (currentTarget.Thing is Pawn targetPawn)
                {
                    List<IntVec3> nodes = targetPawn.pather.curPath.NodesReversed;
                    // Path of target pawn from current to destination
                    // Need travel speed of pawn, estimate Vec3 they will be in based on travel speed of our projectile
                    float targetMoveSpeed = 0f;
                    float projectileMoveSpeed = projectile.projectile.speed;
                }
            }

            if (base.verbProps.forcedMissRadius > 0.5f)
            {
                // Grab default forced miss radius for this particular weapon
                float missRadiusForShot = base.verbProps.forcedMissRadius;
                float skillMultiplier = 1f;
                // We want to multiply this forced miss radius by our pawn's skill modifier
                if (compMannable != null)
                {
                    if (compMannable.ManningPawn != null)
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
                }

                // Weather should affect shot no matter what the skill is
                if (Settings.weatherAffectsMortarAccuracy)
                    missRadiusForShot = (missRadiusForShot * skillMultiplier) + ((1 - caster.Map.weatherManager.CurWeatherAccuracyMultiplier) * missRadiusForShot);
                else
                    missRadiusForShot = (missRadiusForShot * skillMultiplier);

                float num = VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, base.currentTarget.Cell - base.caster.Position);
                //float num = VerbUtility.CalculateAdjustedForcedMiss(base.verbProps.forcedMissRadius, base.currentTarget.Cell - base.caster.Position);
                if (num > 0.5f)
                {
                    int max = GenRadial.NumCellsInRadius(num);
                    int num2 = Rand.Range(0, max);
                    if (num2 > 0)
                    {
                        IntVec3 c = base.currentTarget.Cell + GenRadial.RadialPattern[num2];
                        this.ThrowDebugText("ToRadius");
                        this.ThrowDebugText("Rad\nDest", c);
                        ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.NonTargetWorld;
                        if (Rand.Chance(0.5f))
                        {
                            projectileHitFlags = ProjectileHitFlags.All;
                        }
                        if (!base.canHitNonTargetPawnsNow)
                        {
                            projectileHitFlags &= ~ProjectileHitFlags.NonTargetPawns;
                        }
                        projectile2.Launch(launcher, drawPos, c, base.currentTarget, projectileHitFlags, equipment, null);
                        return true;
                    }
                }
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