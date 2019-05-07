using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Harmony;
using System.Reflection;
using System.Reflection.Emit;

namespace MortarAccuracy
{
    /*[StaticConstructorOnStartup]
    public static class MyMortarMod
    {
        static HarmonyInstance harmony;

        static MyMortarMod()
        {
            if (harmony == null)
            {
                harmony = HarmonyInstance.Create("MortarAccuracy");
            }

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    //[HarmonyPatch(typeof(Verb_LaunchProjectile))]
    //[HarmonyPatch("TryCastShot")]
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Harmony_Verb_LaunchProjectile_TryCastShot
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Log.Error("Thing happened");
            MethodInfo targetMethod = AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot");

            // go through each instruction, if any instruction points to default TryCastShot method, point it to my new one instead
            var codes = new List<CodeInstruction>(instructions);

            if (targetMethod == null)
            {
                Log.Error(string.Format("MortarAccuracy: Transpiler could not find method infor for {0}.{1}", typeof(Verb_LaunchProjectile).Name, "TryCastShot"));
            }
            foreach (CodeInstruction instruction in instructions)
            {
                if(targetMethod != null && instruction.operand != null && instruction.operand.Equals(targetMethod))
                {
                    Log.Message("Setting operand to DoThing");
                    instruction.operand = AccessTools.Method(typeof(Verb_LaunchProjectile_MortarMod), "DoThing");
                }
                yield return instruction;
            }

            //return codes.AsEnumerable();
        }
    }
    /*foreach (CodeInstruction instruction in instructions)
		{
			if (patchPhase == 2)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0, null);
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Harmony_Verb_TryStartCastOn), "CheckReload", null, null));
				yield return new CodeInstruction(OpCodes.Brfalse, branchLabel);
				patchPhase = 3;
			}
			if (patchPhase == 1 && instruction.opcode == OpCodes.Brfalse)
			{
				patchPhase = 2;
				branchLabel = (Label?)(object)(instruction.operand as Label?);
			}
			if (patchPhase == 0 && instruction.opcode == OpCodes.Call && HarmonyBase.doCast((instruction.operand as MethodInfo).Name.Equals("TryFindShootLineFromTo")))
			{
				patchPhase = 1;
			}
			yield return instruction;
		}
     */

    public class Settings : ModSettings
    {
        public static bool intellectualAffectsMortarAccuracy = true;
        public static bool shootingAffectsMortarAccuracy = false;
        public static bool weatherAffectsMortarAccuracy = true;
        public static float maxSkillSpreadReduction = 0.75f;
        public static float minSkillSpreadReduction = -0.4f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref intellectualAffectsMortarAccuracy, "intellectualAffectsMortarAccuracy", true);
            Scribe_Values.Look(ref shootingAffectsMortarAccuracy, "shootingAffectsMortarAccuracy", false);
            Scribe_Values.Look(ref weatherAffectsMortarAccuracy, "weatherAffectsMortarAccuracy", true);
            Scribe_Values.Look(ref maxSkillSpreadReduction, "maxSkillSpreadReduction", 0.75f);
            Scribe_Values.Look(ref minSkillSpreadReduction, "minSkillSpreadReduction", -0.4f);
            base.ExposeData();
        }
    }

    public class MortarMod : Mod
    {
        public Settings settings;

        public MortarMod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<Settings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled(Translator.Translate("OptionWeather"), ref Settings.weatherAffectsMortarAccuracy);
            //listingStandard.CheckboxLabeled("Weather affects accuracy", ref Settings.weatherAffectsMortarAccuracy);
            listingStandard.Label(Translator.Translate("OptionSkills"));
            //listingStandard.Label("Select what Skill(s) affect accuracy. If multiple skills are selected, they are averaged together.");
            listingStandard.CheckboxLabeled(Translator.Translate("OptionSkillIntellectual"), ref Settings.intellectualAffectsMortarAccuracy);
            //listingStandard.CheckboxLabeled("Intellectual skill affects accuracy", ref Settings.intellectualAffectsMortarAccuracy);
            listingStandard.CheckboxLabeled(Translator.Translate("OptionSkillShooting"), ref Settings.shootingAffectsMortarAccuracy);
            //listingStandard.CheckboxLabeled("Shooting skill affects accuracy", ref Settings.shootingAffectsMortarAccuracy);

            listingStandard.Label(TranslatorFormattedStringExtensions.Translate("OptionBestAccuracy", (int)(Settings.maxSkillSpreadReduction * 100)));
            /*listingStandard.Label(string.Format("Best accuracy\n" +
                "Pawns with maximum skill have their mortar accuracy improved to {0}% (vanilla is 0%, mod default is 75%, perfect accuracy is 100%)", (int)(Settings.maxSkillSpreadReduction * 100)));
                */
                Settings.maxSkillSpreadReduction = listingStandard.Slider(Settings.maxSkillSpreadReduction, 0f, 1f);

            string modifierString = Settings.minSkillSpreadReduction < 0 ? Translator.Translate("Reduced") : Translator.Translate("Improved");
            listingStandard.Label(TranslatorFormattedStringExtensions.Translate("OptionWorstAccuracy", modifierString, (int)(Settings.minSkillSpreadReduction * 100)));

            /*listingStandard.Label(string.Format("Worst accuracy\n" +
                "Pawns with no skill have their mortar accuracy " + (Settings.minSkillSpreadReduction < 0 ? "reduced" : "improved") + " by {0}% (vanilla is 0%, mod default is -40%)", (int)(Settings.minSkillSpreadReduction * 100)));
                */
            Settings.minSkillSpreadReduction = listingStandard.Slider(Settings.minSkillSpreadReduction, -1f, 1f);

            if (Settings.minSkillSpreadReduction > Settings.maxSkillSpreadReduction)
                Settings.minSkillSpreadReduction = Settings.maxSkillSpreadReduction;

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return Translator.Translate("MortarAccuracy");
            //return "Mortar Accuracy".Translate();
        }
    }
}
