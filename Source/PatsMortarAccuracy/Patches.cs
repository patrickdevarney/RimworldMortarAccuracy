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
            //Harmony.DEBUG = true;
            //LogModError("REMEMBER TO TURN OFF DEBUG");

            var harmony = new Harmony("rimworld.hobtook.mortaraccuracy");
            harmony.PatchAll();

            if (IsYayoCombatActive())
            {
                PatchMod_YayoCombat(harmony);
            }
        }

        static bool IsYayoCombatActive()
        {
            return LoadedModManager.RunningMods
                .Any(mod => mod.assemblies.loadedAssemblies
                .Any(asm => asm.GetName().Name.ToLower() == "yayocombat"));
        }

        static bool SkipYayoPrefix = false;

        static void PatchMod_YayoCombat(Harmony harmony)
        {
            MethodInfo yayoPrefix = AccessTools.Method("yayoCombat.HarmonyPatches.Verb_LaunchProjectile_TryCastShot:Prefix");
            try
            {
                if (yayoPrefix != null)
                {
                    /*  Final execution here is a bit funky.
                     *  Prefix_TryCastShot_BeforeYayo determines if we skip or allow yayo's TryCastShot:Prefix
                     *  Prefix_YayoCombat_TryCastShotPrefix executes skipping yayo combat or not
                     *  Yayo TryCastShot:Prefix may get skipped or execute
                     *      if yayo prefix return false > Original TryCastShot skipped
                     *      else yayo prefix return true > Original TryCastShot runs
                     */

                    // Patch of original TryCastShot, before yayo combat prefix. Determines if we want to skip yayo combat prefix
                    var original = AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot");
                    var prefix = typeof(Patches).GetMethod(nameof(Patches.Prefix_TryCastShot_BeforeYayo), BindingFlags.Static | BindingFlags.Public);

                    var prefixMethod = new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    };

                    harmony.Patch(original, prefix: prefixMethod);

                    // Patch of yayo combat TryCastShot prefix itself. Executes skipping yayo combat prefix
                    harmony.Patch(yayoPrefix, prefix: new HarmonyMethod(typeof(Patches), nameof(Prefix_YayoCombat_TryCastShotPrefix)));
                }
                else
                {
                    LogModError("Detected YayoCombat is enabled, but was unable to find the YayoCombat TryCastShot:Prefix. The game still works, but YayoCombat is preventing MortarAccuracy from running");
                }
            }
            catch
            {
                LogModError("Detected YayoCombat is enabled, but was unable to patch YayoCombat. The game still works, but YayoCombat is preventing MortarAccuracy from running");
            }
        }

        static void LogModError(string message)
        {
            Log.Error($"MortarAccuracy: Error during patching. Please post on Steam Workshop. {message}");
        }

        public static bool Prefix_YayoCombat_TryCastShotPrefix(ref bool __result)
        {
            if (SkipYayoPrefix)
            {
                __result = true;
                return false; // skip yayo
            }
            return true; // continue execution
        }

        public static bool Prefix_TryCastShot_BeforeYayo(Verb_LaunchProjectile __instance)
        {
            if (__instance.verbProps.ForcedMissRadius > 0.5f)
            {
                SkipYayoPrefix = true;
            }
            else
            {
                SkipYayoPrefix = false;
            }

            return true; // continue execution
        }

        [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
        static class Harmony_Verb_LaunchProjectile_TryCastShot
        {
            static IEnumerable<CodeInstruction> Transpiler  (IEnumerable<CodeInstruction> instructions, ILGenerator il)
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
                        // Find the stloc instruction so we know what local var to assign to
                        int currentIndex = -1;
                        for (int j = i + 1; j < codes.Count; j++)
                        {
                            if (codes[j].opcode == OpCodes.Stloc || codes[j].opcode == OpCodes.Stloc_S)
                            {
                                currentIndex = j;
                                break;
                            }
                        }

                        if (currentIndex == -1)
                        {
                            LogModError("Failed to find CalculateAdjustedForcedMiss index");
                            break;
                        }

                        // Store the operand safely
                        var stlocOperand = codes[currentIndex].operand;
                        if (stlocOperand == null)
                        {
                            LogModError("stlocOperand is null");
                            break;
                        }

                        int insertIndex = currentIndex + 1;
                        var newInstructions = new List<CodeInstruction>
                            {
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldfld, typeof(Verse.Verb).GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance)),
                                new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod("GetAdjustedForcedMissRadius", BindingFlags.NonPublic | BindingFlags.Static)),
                                new CodeInstruction(OpCodes.Stloc_S, stlocOperand),
                            };
                        codes.InsertRange(insertIndex, newInstructions);
                        currentIndex = insertIndex + newInstructions.Count;

                        // Replace (num2 > 0.5f) check that prevents perfect or lucky accuracy
                        int removalIndex = -1;
                        object jumpTarget = null;
                        List<Label> labelsToPreserve = null;
                        for (int j = currentIndex; j < codes.Count; j++)
                        {
                            if (codes[j].opcode == OpCodes.Ldc_R4 &&
                                codes[j + 1].opcode == OpCodes.Ble_Un_S)
                            {
                                jumpTarget = codes[j + 1].operand;
                                labelsToPreserve = codes[j + 1].labels;
                                removalIndex = j - 1;
                                break;
                            }
                        }

                        if (removalIndex == -1)
                        {
                            LogModError("Failed to find removalIndex for 0.5 comparison");
                            break;
                        }

                        codes.RemoveRange(removalIndex, 3);
                        var newComparison = new CodeInstruction(OpCodes.Brfalse_S, jumpTarget);
                        foreach (var label in labelsToPreserve)
                            newComparison.labels.Add(label);

                        newInstructions = new List<CodeInstruction>
                        {
                            new CodeInstruction(OpCodes.Ldc_I4_1),
                            newComparison,
                        };
                        codes.InsertRange(removalIndex, newInstructions);

                        // Replace check (if forcedMissTarget != this.currentTarget.Cell) that prevents perfect or lucky accuracy with if(true)
                        currentIndex = removalIndex + newInstructions.Count;
                        removalIndex = -1;
                        jumpTarget = null;
                        labelsToPreserve = null;
                        for (int j = currentIndex; j < codes.Count; j++)
                        {
                            if (codes[j].opcode == OpCodes.Brfalse_S)
                            {
                                jumpTarget = codes[j].operand;
                                labelsToPreserve = codes[j].labels;
                                removalIndex = j - 5;
                                break;
                            }
                        }

                        if (removalIndex == -1)
                        {
                            LogModError("Failed to find removalIndex");
                            break;
                        }

                        codes.RemoveRange(removalIndex, 5);

                        newInstructions = new List<CodeInstruction>()
                        {
                            new CodeInstruction(OpCodes.Ldc_I4_1),
                        };
                        codes.InsertRange(removalIndex, newInstructions);

                        newComparison = new CodeInstruction(OpCodes.Brfalse_S, jumpTarget);
                        foreach(var label in labelsToPreserve)
                            newComparison.labels.Add(label);

                        codes[removalIndex + newInstructions.Count] = newComparison;
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
                    if (Settings.bestSkillAffectsMotarAccuracy)
                    {
                        totalSkill = Math.Max(shooterPawn.skills.GetSkill(SkillDefOf.Intellectual).Level, shooterPawn.skills.GetSkill(SkillDefOf.Shooting).Level);
                        skillsTotaled = 1;
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
                var retVal = VerbUtility.CalculateAdjustedForcedMiss(missRadiusForShot, ___currentTarget.Cell - shootVerb.caster.Position);
                return retVal;
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
