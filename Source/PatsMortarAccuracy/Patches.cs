using UnityEngine;
using Verse;
using HarmonyLib;
using RimWorld;
using Multiplayer.API;
using System.Collections.Generic;

namespace MortarAccuracy
{
    [StaticConstructorOnStartup]
    static class Patches
    {
        static Patches()
        {
            var harmony = new Harmony("rimworld.hobtook.mortaraccuracy");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
        static class Harmony_Verb_LaunchProjectile_TryCastShot
        {
            //[HarmonyPrefix]
            static bool Prefix(ref bool __result, Verb_LaunchProjectile __instance, LocalTargetInfo ___currentTarget)
            {
                if (__instance.verbProps.forcedMissRadius < 0.5f || __instance.verbProps.requireLineOfSight)
                {
                    // Assuming this is not a mortar-like thing
                    // Perform vanilla logic
                    return true;
                }
                else
                {
                    // Perform the same vanilla checks
                    if (___currentTarget.HasThing && ___currentTarget.Thing.Map != __instance.caster.Map)
                    {
                        __result = false;
                        return false;
                    }
                    ThingDef projectile = __instance.Projectile;
                    if (projectile == null)
                    {
                        __result = false;
                        return false;
                    }
                    ShootLine shootLine = default(ShootLine);
                    bool flag = __instance.TryFindShootLineFromTo(__instance.caster.Position, ___currentTarget, out shootLine);
                    if (__instance.verbProps.stopBurstWithoutLos && !flag)
                    {
                        __result = false;
                        return false;
                    }

                    // Vanilla checks pass, we can shoot
                    if (__instance.EquipmentSource != null)
                    {
                        CompChangeableProjectile comp = __instance.EquipmentSource.GetComp<CompChangeableProjectile>();
                        if (comp != null)
                        {
                            comp.Notify_ProjectileLaunched();
                        }
                        CompReloadable comp2 = __instance.EquipmentSource.GetComp<CompReloadable>();
                        if (comp2 != null)
                        {
                            comp2.UsedOnce();
                        }
                    }

                    Thing launcher = __instance.caster;
                    Thing equipment = __instance.EquipmentSource;
                    CompMannable compMannable = __instance.caster.TryGetComp<CompMannable>();
                    if (compMannable != null && compMannable.ManningPawn != null)
                    {
                        launcher = compMannable.ManningPawn;
                        equipment = __instance.caster;
                        // Earn skills
                        if (compMannable.ManningPawn.skills != null)
                        {
                            int skillsAffectingAccuracy = 0;
                            if (Settings.intellectualAffectsMortarAccuracy)
                                skillsAffectingAccuracy++;
                            if (Settings.shootingAffectsMortarAccuracy)
                                skillsAffectingAccuracy++;

                            float skillXP = __instance.verbProps.AdjustedFullCycleTime(__instance, __instance.CasterPawn) * 100;
                            skillXP /= skillsAffectingAccuracy;

                            if (Settings.intellectualAffectsMortarAccuracy)
                                compMannable.ManningPawn.skills.Learn(SkillDefOf.Intellectual, skillXP, false);
                            if (Settings.shootingAffectsMortarAccuracy)
                                compMannable.ManningPawn.skills.Learn(SkillDefOf.Shooting, skillXP, false);
                        }
                    }
                    Vector3 drawPos = __instance.caster.DrawPos;
                    Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, shootLine.Source, __instance.caster.Map, WipeMode.Vanish);

                    // If targetting a pawn
                    if (Settings.targetLeading)
                    {
                        if (___currentTarget != null && ___currentTarget.Thing != null && ___currentTarget.Thing is Pawn targetPawn && targetPawn.pather.curPath != null)
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
                                float projectileDistanceFromTarget = (pathPosition - __instance.caster.Position).LengthHorizontal;
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
                            ___currentTarget = new LocalTargetInfo(bestTarget);
                        }
                    }

                    float adjustedForcedMissRadius = GetAdjustedForcedMissRadius(__instance as Verb_Shoot, ___currentTarget);
                    ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.All;
                    IntVec3 targetPosition = ___currentTarget.Cell;
                    //if (adjustedForcedMissRadius > 0.5f)
                    {
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
                    projectile2.Launch(launcher, drawPos, targetPosition, ___currentTarget, projectileHitFlags, equipment, null);
                }
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Verb_LaunchProjectile), "HighlightFieldRadiusAroundTarget")]
        static class Harmony_Verb_LaunchProjectile_HighlightFieldRadiusAroundTarget
        {
            static bool Prefix(ref float __result, Verb_LaunchProjectile __instance, LocalTargetInfo ___currentTarget, out bool needLOSToCenter)
            {
                if (__instance.verbProps.forcedMissRadius < 0.5f || __instance.verbProps.requireLineOfSight)
                {
                    // Assuming this is not a mortar-like thing
                    // Perform vanilla logic
                    needLOSToCenter = true;
                    return true;
                }
                if (!Settings.showAccuracyRadius)
                {
                    needLOSToCenter = true;
                    return true;
                }
                else
                {
                    needLOSToCenter = false;

                    float missRadius = GetAdjustedForcedMissRadius(__instance as Verb_Shoot, ___currentTarget);
                    if (missRadius < 1)
                    {
                        missRadius = 1f;
                    }
                    __result = missRadius;
                    // Skip normal execution, we will draw our own overlay
                    return false;
                }
            }
        }

        static float GetAdjustedForcedMissRadius(Verb_Shoot shootVerb, LocalTargetInfo ___currentTarget)
        {
            if (shootVerb.verbProps.forcedMissRadius < 0.5f || shootVerb.verbProps.requireLineOfSight)
            {
                return 0;
            }
            else
            {
                CompMannable compMannable = shootVerb.caster.TryGetComp<CompMannable>();
                // Grab default forced miss radius for this particular weapon
                float missRadiusForShot = shootVerb.verbProps.forcedMissRadius;
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
                    missRadiusForShot = (missRadiusForShot * skillMultiplier) + ((1 - shootVerb.caster.Map.weatherManager.CurWeatherAccuracyMultiplier) * missRadiusForShot);
                else
                    missRadiusForShot = (missRadiusForShot * skillMultiplier);

                return VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, ___currentTarget.Cell - shootVerb.caster.Position);
            }
        }
    }

}
