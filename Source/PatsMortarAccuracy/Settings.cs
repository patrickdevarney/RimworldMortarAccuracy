using System;
using UnityEngine;
using Verse;

namespace MortarAccuracy
{
    public class Settings : ModSettings
    {
        public static bool intellectualAffectsMortarAccuracy = true;
        public static bool shootingAffectsMortarAccuracy = false;
        public static bool weatherAffectsMortarAccuracy = true;
        public static float maxSkillSpreadReduction = 0.75f;
        public static float minSkillSpreadReduction = -0.4f;
        public static bool showExplosionRadius = true;
        public static bool targetLeading = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref intellectualAffectsMortarAccuracy, "intellectualAffectsMortarAccuracy", true);
            Scribe_Values.Look(ref shootingAffectsMortarAccuracy, "shootingAffectsMortarAccuracy", false);
            Scribe_Values.Look(ref weatherAffectsMortarAccuracy, "weatherAffectsMortarAccuracy", true);
            Scribe_Values.Look(ref showExplosionRadius, "showExplosionRadius", true);
            Scribe_Values.Look(ref maxSkillSpreadReduction, "maxSkillSpreadReduction", 0.75f);
            Scribe_Values.Look(ref minSkillSpreadReduction, "minSkillSpreadReduction", -0.5f);
            Scribe_Values.Look(ref targetLeading, "targetLeading", true);
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
            listingStandard.Label(Translator.Translate("OptionSkills"));
            listingStandard.CheckboxLabeled(Translator.Translate("OptionSkillIntellectual"), ref Settings.intellectualAffectsMortarAccuracy);
            listingStandard.CheckboxLabeled(Translator.Translate("OptionSkillShooting"), ref Settings.shootingAffectsMortarAccuracy);

            listingStandard.Label(TranslatorFormattedStringExtensions.Translate("OptionBestAccuracy", (int)Math.Round(Settings.maxSkillSpreadReduction * 100f)));
            Settings.maxSkillSpreadReduction = (float)(Math.Round(listingStandard.Slider(Settings.maxSkillSpreadReduction, 0f, 1f) * 100d) / 100f);

            string modifierString = Settings.minSkillSpreadReduction < 0 ? Translator.Translate("Reduced") : Translator.Translate("Improved");
            listingStandard.Label(TranslatorFormattedStringExtensions.Translate("OptionWorstAccuracy", modifierString, (int)Math.Round(Settings.minSkillSpreadReduction * 100f)));
            Settings.minSkillSpreadReduction = (float)(Math.Round(listingStandard.Slider(Settings.minSkillSpreadReduction, -1f, 1f) * 100d) / 100f);

            if (Settings.minSkillSpreadReduction > Settings.maxSkillSpreadReduction)
                Settings.minSkillSpreadReduction = Settings.maxSkillSpreadReduction;

            listingStandard.CheckboxLabeled(Translator.Translate("OptionWeather"), ref Settings.weatherAffectsMortarAccuracy);
            listingStandard.CheckboxLabeled(Translator.Translate("OptionShowExplosionRadius"), ref Settings.showExplosionRadius);
            listingStandard.CheckboxLabeled(Translator.Translate("OptionTargetLeading"), ref Settings.targetLeading);

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return Translator.Translate("MortarAccuracy");
        }
    }
}