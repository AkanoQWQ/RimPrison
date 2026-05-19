using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPrison.Patches
{
    // Extend vanilla FindAutofeedBaby to also search prisoner babies.
    // Colonists and labor-enabled prisoners can feed hungry prisoner babies.
    // Uses vanilla's public childcare API (CanFeedBaby, MakeAutofeedBabyJob, etc.).
    // [TODO] FindUnsafeBaby support in the future
    // FindUnsafeBaby is intentionally NOT patched — SafePlaceForBaby always
    // returns a fallback position (the baby's current cell), which causes an
    // infinite bring-to-safety loop when the entire map is cold.
    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.FindAutofeedBaby))]
    internal static class Patch_BabyFeeding
    {
        static void Postfix(Pawn mom, AutofeedMode priorityLevel,
            ref Thing food, ref Pawn __result)
        {
            if (__result != null) return;
            if (priorityLevel != AutofeedMode.Urgent) return;
            if (RimPrisonMod.Settings.BabyFeedingIsolation && mom.IsColonist && !mom.IsPrisonerOfColony) return;
            if (mom.MapHeld == null) return;

            __result = SearchPrisonerBaby(mom, out food);
        }

        static Pawn SearchPrisonerBaby(Pawn mom, out Thing food)
        {
            food = null;
            bool canBreastfeed = ChildcareUtility.CanBreastfeedNow(mom, out _);

            foreach (Pawn baby in mom.MapHeld.mapPawns.PrisonersOfColony)
            {
                if (!baby.DevelopmentalStage.Baby()) continue;
                if (baby.Suspended) continue;
                if (!ChildcareUtility.WantsSuckle(baby, out _)) continue;
                if (!ChildcareUtility.CanFeedBaby(mom, baby, out _)) continue;
                if (!ChildcareUtility.CanHaulBabyToMomNow(mom, mom, baby,
                    ignoreOtherReservations: false, out _)) continue;

                if (canBreastfeed)
                    food = mom;
                else
                    food = ChildcareUtility.FindBabyFoodForBaby(mom, baby);
                if (food == null) continue;

                return baby;
            }
            return null;
        }
    }
}
