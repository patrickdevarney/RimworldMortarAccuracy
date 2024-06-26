﻿using UnityEngine;
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
        [HarmonyBefore("com.yayo.combat")]
        static class Harmony_Verb_LaunchProjectile_TryCastShot
        {
            //[HarmonyPrefix]
            static bool Prefix(ref bool __result, Verb_LaunchProjectile __instance, LocalTargetInfo ___currentTarget, int ___lastShotTick)
            {
                if (__instance.verbProps.ForcedMissRadius < 0.5f || __instance.verbProps.requireLineOfSight)
                {
                    // Assuming this is not a mortar-like thing
                    // Perform vanilla logic
                    return true;
                }
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
                ShootLine shootLine;
                bool flag = __instance.TryFindShootLineFromTo(__instance.caster.Position, ___currentTarget, out shootLine, false);
                if (__instance.verbProps.stopBurstWithoutLos && !flag)
                {
                    __result = false;
                    return false;
                }
                if (__instance.EquipmentSource != null)
                {
                    CompChangeableProjectile comp = __instance.EquipmentSource.GetComp<CompChangeableProjectile>();
                    if (comp != null)
                    {
                        comp.Notify_ProjectileLaunched();
                    }
                    CompApparelVerbOwner_Charged comp2 = __instance.EquipmentSource.GetComp<CompApparelVerbOwner_Charged>();
                    if (comp2 != null)
                    {
                        comp2.UsedOnce();
                    }
                }
                ___lastShotTick = Find.TickManager.TicksGame;
                Thing thing = __instance.caster;
                Thing equipment = __instance.EquipmentSource;
                CompMannable compMannable = __instance.caster.TryGetComp<CompMannable>();
                if (((compMannable != null) ? compMannable.ManningPawn : null) != null)
                {
                    thing = compMannable.ManningPawn;
                    equipment = __instance.caster;
                    // INSERT SKILL GAINING IF MORTAR
                    GainSkills(__instance, compMannable);
                }
                Vector3 drawPos = __instance.caster.DrawPos;
                Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, shootLine.Source, __instance.caster.Map, WipeMode.Vanish);
                ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.All;

                // If targetting a pawn
                // Assigns new ___currentTarget value
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
                float adjustedForcedMissRadius = GetAdjustedForcedMissRadius(__instance, ___currentTarget);
                IntVec3 targetPosition = ___currentTarget.Cell;

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

                projectile2.Launch(thing, drawPos, targetPosition, ___currentTarget, projectileHitFlags, false, equipment, null);
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Verb), "DrawHighlightFieldRadiusAroundTarget")]
        static class Harmony_Verb_DrawHighlightFieldRadiusAroundTarget
        {
            static bool Prefix(Verb __instance, LocalTargetInfo target)
            {
                if (__instance.verbProps.requireLineOfSight)
                    return true;

                if (!(__instance is Verb_LaunchProjectile))
                    return true;

                var projectileVerb = __instance as Verb_LaunchProjectile;
                if (projectileVerb.verbProps.ForcedMissRadius < 0.5f)
                {
                    // Assuming this is not a mortar-like thing
                    // Do vanilla stuff
                    return true;
                }

                float missRadius = GetAdjustedForcedMissRadius(projectileVerb, target);
                if (missRadius < 1)
                {
                    missRadius = 1f;
                }

                // Draw the explosion radius
                if (Settings.showExplosionRadius)
                {
                    ThingDef projectile = projectileVerb.Projectile;
                    if (projectile != null)
                    {
                        // Nudge the accuracy radius to prevent circles drawing on top of each other
                        if (((int)projectile.projectile.explosionRadius) == ((int)missRadius))
                        {
                            missRadius += 1;
                        }

                        GenDraw.DrawRadiusRing(target.Cell, projectile.projectile.explosionRadius, Color.red);
                    }
                }

                // Draw accuracy radius
                GenDraw.DrawRadiusRing(target.Cell, missRadius, Color.white);

                // Skip normal execution
                return false;
            }
        }

        static float GetAdjustedForcedMissRadius(Verb_LaunchProjectile shootVerb, LocalTargetInfo ___currentTarget)
        {
            if (shootVerb.verbProps.ForcedMissRadius < 0.5f || shootVerb.verbProps.requireLineOfSight)
            {
                return 0;
            }
            else
            {
                Pawn shooterPawn = null;
                CompMannable compMannable = shootVerb.caster.TryGetComp<CompMannable>();
                if (compMannable != null && compMannable.ManningPawn != null)
                {
                    shooterPawn = compMannable.ManningPawn;
                }
                else
                {
                    shooterPawn = shootVerb.CasterPawn;
                }
                // Grab default forced miss radius for this particular weapon
                float missRadiusForShot = shootVerb.verbProps.ForcedMissRadius;
                float skillMultiplier = 1f;
                // We want to multiply this forced miss radius by our pawn's skill modifier
                if (shooterPawn != null && shooterPawn.skills != null)
                {
                    int totalSkill = 0;
                    int skillsTotaled = 0;
                    if (Settings.intellectualAffectsMortarAccuracy)
                    {
                        totalSkill += shooterPawn.skills.GetSkill(SkillDefOf.Intellectual).Level;
                        skillsTotaled++;
                    }
                    if (Settings.shootingAffectsMortarAccuracy)
                    {
                        totalSkill += shooterPawn.skills.GetSkill(SkillDefOf.Shooting).Level;
                        skillsTotaled++;
                    }
                    if (skillsTotaled > 0)
                    {
                        // get average skill
                        int averageSkill = (int)(((float)totalSkill) / skillsTotaled);
                        // Support averageSkill > SkillRecord.MaxLevel
                        averageSkill = Mathf.Clamp(averageSkill, SkillRecord.MinLevel, SkillRecord.MaxLevel);
                        skillMultiplier = 1 - ((averageSkill - SkillRecord.MinLevel) * (Settings.maxSkillSpreadReduction - Settings.minSkillSpreadReduction) / (SkillRecord.MaxLevel - SkillRecord.MinLevel) + Settings.minSkillSpreadReduction);
                    }
                }
                // Weather should affect shot no matter what the skill is
                if (Settings.weatherAffectsMortarAccuracy)
                    missRadiusForShot = (missRadiusForShot * skillMultiplier) + ((1 - shootVerb.caster.Map.weatherManager.CurWeatherAccuracyMultiplier) * missRadiusForShot);
                else
                    missRadiusForShot = (missRadiusForShot * skillMultiplier);
                // TODO: this is wrong. __curentTarget.Cell is origin when we are hovering over it, preview is incorrect
                return VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, ___currentTarget.Cell - shootVerb.caster.Position);
            }
        }

        static void GainSkills(Verb_LaunchProjectile shootVerb, CompMannable compMannable)
        {
            // Earn skills
            if (compMannable.ManningPawn.skills != null)
            {
                int skillsAffectingAccuracy = 0;
                if (Settings.intellectualAffectsMortarAccuracy)
                    skillsAffectingAccuracy++;
                if (Settings.shootingAffectsMortarAccuracy)
                    skillsAffectingAccuracy++;

                if (skillsAffectingAccuracy > 0)
                {
                    float skillXP = shootVerb.verbProps.AdjustedFullCycleTime(shootVerb, shootVerb.CasterPawn) * 100;
                    skillXP = Mathf.Clamp(skillXP, 0, 200);
                    skillXP /= skillsAffectingAccuracy;

                    if (Settings.intellectualAffectsMortarAccuracy)
                        compMannable.ManningPawn.skills.Learn(SkillDefOf.Intellectual, skillXP, false);
                    if (Settings.shootingAffectsMortarAccuracy)
                        compMannable.ManningPawn.skills.Learn(SkillDefOf.Shooting, skillXP, false);
                }
            }
        }
    }
}