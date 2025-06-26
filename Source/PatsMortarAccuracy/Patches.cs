using UnityEngine;
using Verse;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System;
using System.Linq;

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
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                var codes = new List<CodeInstruction>(instructions);
                
                // Helper to get local index from operand
                int? GetLocalIndex(object operand)
                {
                    if (operand is int i)
                        return i;
                    if (operand is LocalBuilder lb)
                        return lb.LocalIndex;
                    return null;
                }

                // Find the projectile local variable (from castclass Verse.Projectile + stloc)
                int projectileLocalIndex = -1;
                for (int i = 0; i < codes.Count - 1; i++)
                {
                    if (codes[i].opcode == OpCodes.Castclass &&
                        codes[i].operand is Type t && t == typeof(Projectile) &&
                        (codes[i + 1].opcode == OpCodes.Stloc || codes[i + 1].opcode == OpCodes.Stloc_S))
                    {
                        var localIndex = GetLocalIndex(codes[i + 1].operand);
                        if (localIndex != null)
                        {
                            projectileLocalIndex = localIndex.Value;
                            break;
                        }
                    }
                }
                
                // Find the location where we need to insert our custom logic
                for (int i = 0; i < codes.Count - 4; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldarg_0 &&
                        codes[i + 1].opcode == OpCodes.Ldfld && 
                        codes[i + 1].operand is FieldInfo fieldInfo && 
                        fieldInfo.Name == "verbProps" &&
                        codes[i + 2].opcode == OpCodes.Callvirt &&
                        codes[i + 2].operand is MethodInfo methodInfo &&
                        methodInfo.Name == "get_ForcedMissRadius" &&
                        codes[i + 3].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[i + 3].operand == 0.5f &&
                        codes[i + 4].opcode == OpCodes.Ble_Un)
                    {
                        var insertIndex = i + 5;
                        var skipToOriginal = il.DefineLabel();
                        var compMannableLocal = il.DeclareLocal(typeof(CompMannable));
                        var injected = new List<CodeInstruction>
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Ldfld, typeof(Verse.Verb).GetField("caster", BindingFlags.Public | BindingFlags.Instance)),
                            new CodeInstruction(OpCodes.Call, typeof(ThingCompUtility).GetMethod("TryGetComp", new System.Type[] { typeof(Thing) }).MakeGenericMethod(typeof(CompMannable))),
                            new CodeInstruction(OpCodes.Stloc, compMannableLocal),
                            new CodeInstruction(OpCodes.Ldloc, compMannableLocal),
                            new CodeInstruction(OpCodes.Brfalse, skipToOriginal),
                            new CodeInstruction(OpCodes.Ldloc, compMannableLocal),
                            new CodeInstruction(OpCodes.Callvirt, typeof(CompMannable).GetProperty("ManningPawn").GetGetMethod()),
                            new CodeInstruction(OpCodes.Brfalse, skipToOriginal),
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Ldloc, compMannableLocal),
                            new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod("GainSkills", BindingFlags.NonPublic | BindingFlags.Static)),
                        };
                        // Only inject if we found the projectile local
                        if (projectileLocalIndex != -1)
                        {
                            injected.Add(new CodeInstruction(OpCodes.Ldarg_0));
                            injected.Add(new CodeInstruction(OpCodes.Ldloc, projectileLocalIndex));
                            injected.Add(new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod("ApplyTargetLeadingIfEnabled", BindingFlags.NonPublic | BindingFlags.Static)));
                        }
                        injected.Add(new CodeInstruction(OpCodes.Nop) { labels = new List<Label> { skipToOriginal } });
                        codes.InsertRange(insertIndex, injected);
                        break;
                    }
                }
                
                // Now find where num2 is calculated and modify it after assignment
                for (int i = 0; i < codes.Count - 1; i++)
                {
                    // Look for the CalculateAdjustedForcedMiss call
                    if (codes[i].opcode == OpCodes.Call &&
                        codes[i].operand is MethodInfo methodInfo &&
                        methodInfo.Name == "CalculateAdjustedForcedMiss" &&
                        methodInfo.DeclaringType == typeof(VerbUtility))
                    {
                        // Find the stloc instruction that follows (might not be immediate)
                        int stlocIndex = -1;
                        for (int j = i + 1; j < codes.Count; j++)
                        {
                            if (codes[j].opcode == OpCodes.Stloc || codes[j].opcode == OpCodes.Stloc_S)
                            {
                                stlocIndex = j;
                                break;
                            }
                        }
                        
                        if (stlocIndex != -1)
                        {
                            var insertIndex = stlocIndex + 1;
                            
                            // Store the operand safely
                            var stlocOperand = codes[stlocIndex].operand;
                            if (stlocOperand == null)
                            {
                                break;
                            }
                            
                            // Only move labels if insertIndex is within bounds
                            List<Label> nextLabels = null;
                            if (insertIndex < codes.Count)
                            {
                                nextLabels = codes[insertIndex].labels;
                                codes[insertIndex].labels = new List<Label>(); // Clear them
                            }

                            var newInstructions = new List<CodeInstruction>
                            {
                                //new CodeInstruction(OpCodes.Ldloc, stlocOperand),
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldfld, typeof(Verse.Verb).GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance)),
                                new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod("GetAdjustedForcedMissRadius", BindingFlags.NonPublic | BindingFlags.Static)),
                                new CodeInstruction(OpCodes.Stloc, stlocOperand)
                            };

                            // Move labels to the first new instruction
                            if (nextLabels != null && nextLabels.Count > 0)
                                newInstructions[0].labels.AddRange(nextLabels);

                            codes.InsertRange(insertIndex, newInstructions);
                        }

                        break;
                    }
                }
                
                return codes;
            }
        }

        static LocalTargetInfo GetTargetWithLeading(LocalTargetInfo ___currentTarget, ThingDef projectile, Verb_LaunchProjectile __instance)
        {
            if (___currentTarget == null || ___currentTarget.Thing == null)
            {
                return ___currentTarget;
            }

            if (!(___currentTarget.Thing is Pawn targetPawn) || targetPawn.pather.curPath == null)
            {
                return ___currentTarget;
            }

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
            return new LocalTargetInfo(bestTarget);
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
                Pawn shooterPawn;
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
                    missRadiusForShot *= skillMultiplier;
                // TODO: this is wrong. __curentTarget.Cell is origin when we are hovering over it, preview is incorrect
                return VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, ___currentTarget.Cell - shootVerb.caster.Position);
            }
        }

        static void GainSkills(Verb_LaunchProjectile shootVerb, CompMannable compMannable)
        {
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

        static void ApplyTargetLeadingIfEnabled(Verb_LaunchProjectile shootVerb, Projectile projectile)
        {
            if (Settings.targetLeading)
            {
                var currentTargetField = typeof(Verb_LaunchProjectile).GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                var currentTarget = (LocalTargetInfo)currentTargetField.GetValue(shootVerb);
                var newTarget = GetTargetWithLeading(currentTarget, projectile.def, shootVerb);
                currentTargetField.SetValue(shootVerb, newTarget);
            }
        }
    }
}
