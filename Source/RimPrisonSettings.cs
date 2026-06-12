using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimPrison
{
    // [UNREVIEWED]
    public class RimPrisonSettings : ModSettings
    {
        public string WorkCouponName = "WorkCoupon";
        public float GlobalWageMultiplier = 1f;
        public int DailyAllowance = 0;
        public int DailyFee = 0;
        public int MaxDebt = 200;
        public int RansomAmount = 500;
        public int DebtHarvestThreshold = 150;
        public int DebtHarvestIntervalDays = 1;
        public bool DoorAccessEnabled;
        public bool RestrictColonistWorkInPrisonArea;
        public bool AllowColonistRecreationInPrisonArea;
        public bool BabyFeedingIsolation = true;

        // Per-work-type wage multiplier, keyed by workTypeDef.defName.
        // Default 1f — entries only stored when user changes from default.
        public Dictionary<string, float> WorkTypeWages = new Dictionary<string, float>();

        // Work types disabled for colonists/mechs inside the prison area.
        // Stored as defNames. Empty = no restrictions.
        public HashSet<string> DisabledWorkInPrisonArea = new HashSet<string>();

        public float GetWorkTypeWage(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return 1f;
            return WorkTypeWages.TryGetValue(defName, out float w) ? w : 1f;
        }

        public void SetWorkTypeWage(string defName, float wage)
        {
            if (string.IsNullOrEmpty(defName)) return;
            WorkTypeWages[defName] = wage;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref WorkCouponName, "WorkCouponName", "WorkCoupon");
            Scribe_Values.Look(ref GlobalWageMultiplier, "CouponsPerHour", 1f); // keep old key
            Scribe_Values.Look(ref DailyAllowance, "DailyAllowance", 0);
            Scribe_Values.Look(ref DailyFee, "DailyFee", 0);
            Scribe_Values.Look(ref MaxDebt, "MaxDebt", 200);
            Scribe_Values.Look(ref RansomAmount, "RansomAmount", 500);
            Scribe_Values.Look(ref DebtHarvestThreshold, "DebtHarvestThreshold", 150);
            Scribe_Values.Look(ref DebtHarvestIntervalDays, "DebtHarvestIntervalDays", 1);
            Scribe_Values.Look(ref DoorAccessEnabled, "DoorAccessEnabled", true);
            // Keep old key for backward compat, no longer used in UI
            Scribe_Values.Look(ref RestrictColonistWorkInPrisonArea, "RestrictColonistWorkInPrisonArea", false);
            Scribe_Values.Look(ref AllowColonistRecreationInPrisonArea, "AllowColonistRecreationInPrisonArea", false);
            Scribe_Values.Look(ref BabyFeedingIsolation, "BabyFeedingIsolation", true);

            // Dictionary → parallel lists for Scribe
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var wageKeys = new List<string>();
                var wageVals = new List<float>();
                foreach (var kv in WorkTypeWages)
                {
                    wageKeys.Add(kv.Key);
                    wageVals.Add(kv.Value);
                }
                Scribe_Collections.Look(ref wageKeys, "wageKeys", LookMode.Value);
                Scribe_Collections.Look(ref wageVals, "wageVals", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var wageKeys = new List<string>();
                var wageVals = new List<float>();
                Scribe_Collections.Look(ref wageKeys, "wageKeys", LookMode.Value);
                Scribe_Collections.Look(ref wageVals, "wageVals", LookMode.Value);
                // Scribe sets lists to null when nodes don't exist (old saves)
                if (wageKeys == null) wageKeys = new List<string>();
                if (wageVals == null) wageVals = new List<float>();
                WorkTypeWages.Clear();
                int n = System.Math.Min(wageKeys.Count, wageVals.Count);
                for (int i = 0; i < n; i++)
                    WorkTypeWages[wageKeys[i]] = wageVals[i];
            }

            // DisabledWorkInPrisonArea — HashSet as parallel lists
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var disabledKeys = new List<string>(DisabledWorkInPrisonArea);
                Scribe_Collections.Look(ref disabledKeys, "disabledWorkKeys", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var disabledKeys = new List<string>();
                Scribe_Collections.Look(ref disabledKeys, "disabledWorkKeys", LookMode.Value);
                DisabledWorkInPrisonArea.Clear();
                if (disabledKeys != null)
                {
                    foreach (var k in disabledKeys)
                        DisabledWorkInPrisonArea.Add(k);
                }
            }
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            // Most settings now live in the in-game Prisoner Management
            // window (Settings tab). Only keep items that don't fit there.

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("RimPrison.GlobalWageMultiplier".Translate(GlobalWageMultiplier.ToString("F1")));
            GlobalWageMultiplier = listing.Slider(GlobalWageMultiplier, 0.1f, 10f);

            listing.Gap(12f);

            listing.CheckboxLabeled("RimPrison.DoorAccessEnabled".Translate(), ref DoorAccessEnabled);

            listing.Gap(12f);

            listing.CheckboxLabeled("RimPrison.BabyFeedingIsolation".Translate(), ref BabyFeedingIsolation);

            listing.Gap(24f);
            if (listing.ButtonText("RimPrison.RemoveModButton".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimPrison.RemoveModConfirm".Translate(),
                    () => RimPrisonRemovalUtility.RemoveModFromAllMaps(),
                    true,
                    "RimPrison.ConfirmRemove".Translate()));
            }

            listing.End();
        }
    }
}
