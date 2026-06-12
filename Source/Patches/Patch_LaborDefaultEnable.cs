using HarmonyLib;
using RimPrison.DefOfs;
using RimPrison.PrisonLabor;
using RimWorld;
using Verse;

namespace RimPrison.Patches
{
    // Auto-enable AllowLabor + auto-assign age-based group for new prisoners.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    internal static class Patch_LaborDefaultEnable
    {
        static void Postfix(Pawn_GuestTracker __instance, GuestStatus guestStatus, Pawn ___pawn)
        {
            if (guestStatus != GuestStatus.Prisoner) return;
            if (___pawn == null || !___pawn.IsPrisonerOfColony) return;
            if (RP_DefOf.RimPrison_AllowLabor == null) return;

            if (!__instance.IsInteractionEnabled(RP_DefOf.RimPrison_AllowLabor))
                __instance.ToggleNonExclusiveInteraction(RP_DefOf.RimPrison_AllowLabor, enabled: true);

            // Auto-assign to age-based default group
            TryAutoAssignGroup(___pawn);
        }

        static void TryAutoAssignGroup(Pawn pawn)
        {
            if (pawn.Map == null) return;
            var manager = pawn.Map.GetComponent<PrisonerGroupManager>();
            if (manager == null) return;
            if (manager.GetGroupFor(pawn) != null) return; // already in a group

            string groupName = pawn.DevelopmentalStage switch
            {
                DevelopmentalStage stage when stage.Newborn() => "RimPrison.GroupBaby".Translate(),
                DevelopmentalStage stage when stage.Baby()  => "RimPrison.GroupBaby".Translate(),
                DevelopmentalStage stage when stage.Child()  => "RimPrison.GroupChild".Translate(),
                _                                           => "RimPrison.GroupAdult".Translate()
            };

            var group = manager.groups.Find(g => g.name == groupName);
            if (group == null)
            {
                group = new PrisonerGroup { name = groupName };
                group.InitDefaults();
                manager.groups.Add(group);
            }

            manager.SetGroup(pawn, group);
        }
    }
}
