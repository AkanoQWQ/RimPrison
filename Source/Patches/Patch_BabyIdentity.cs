using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPrison.Patches
{
    // When a prisoner gives birth, the baby inherits the mother's hostile faction.
    // Vanilla does NOT auto-prisonerize the newborn, so we set guest status here.
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
    internal static class Patch_BabyIdentity
    {
        static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
        {
            if (!__result) return;
            if (motherOrEgg is not Pawn mother) return;
            if (!pawn.RaceProps.Humanlike) return;
            // Fix bug : apply to both baby and newborn
            if (!pawn.DevelopmentalStage.Newborn() && !pawn.DevelopmentalStage.Baby()) return;

            if (mother.IsPrisonerOfColony)
            {
                pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                pawn.guest?.ToggleNonExclusiveInteraction(
                    DefOfs.RP_DefOf.RimPrison_AllowLabor, true);
            }
            else if (mother.IsSlaveOfColony)
            {
                pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
            }
            else
            {
                return;
            }
            // AI tried to EnableLabor here...
        }
    }
}
