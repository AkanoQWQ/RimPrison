using HarmonyLib;
using RimPrison.PrisonArea;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimPrison.Patches
{
    // Block colonists from taking joy jobs whose targets lie inside the prison area.
    // This is separate from work restriction because joy jobs do not use WorkGivers.
    internal static class PrisonAreaJoyRestrictionHelper
    {
        public static bool ShouldBlock(Pawn pawn)
        {
            // colonist prisoner will not be blocked here
            if (pawn == null || !pawn.IsColonist || pawn.IsPrisonerOfColony)
            {
                return false;
            }

            return !RimPrisonMod.Settings.AllowColonistRecreationInPrisonArea;
        }

        public static bool JobTouchesPrisonArea(Job job, Area_Prison area)
        {
            if (job == null || area == null)
            {
                return false;
            }

            if (EffectiveTargetInPrisonArea(job.targetA, job.targetQueueA, area)
                || EffectiveTargetInPrisonArea(job.targetB, job.targetQueueB, area)
                || TargetInPrisonArea(job.targetC, area))
            {
                return true;
            }

            return false;
        }

        // Some joy jobs (walk/swim) store the whole travel path in targetQueueA.
        // We only care about the final joy destination, not intermediate path cells.
        private static bool EffectiveTargetInPrisonArea(
            LocalTargetInfo directTarget,
            System.Collections.Generic.List<LocalTargetInfo> queue, Area_Prison area)
        {
            if (queue != null && queue.Count > 0)
            {
                return TargetInPrisonArea(queue[queue.Count - 1], area);
            }

            return TargetInPrisonArea(directTarget, area);
        }

        private static bool TargetInPrisonArea(LocalTargetInfo target, Area_Prison area)
        {
            if (!target.IsValid)
            {
                return false;
            }

            IntVec3 cell = target.Cell;
            if (!cell.IsValid || !cell.InBounds(area.Map))
            {
                return false;
            }

            return area[cell];
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJobFromJoyGiverDefDirect")]
    internal static class Patch_PrisonAreaJoyRestriction_GetJoy
    {
        static void Postfix(Pawn __1, ref Job __result)
        {
            Patch_PrisonAreaJoyRestrictionShared.TryBlockJoyJob(__1, ref __result);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetJoyInBed), "TryGiveJobFromJoyGiverDefDirect")]
    internal static class Patch_PrisonAreaJoyRestriction_GetJoyInBed
    {
        static void Postfix(Pawn __1, ref Job __result)
        {
            Patch_PrisonAreaJoyRestrictionShared.TryBlockJoyJob(__1, ref __result);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetJoyInGatheringArea), "TryGiveJobFromJoyGiverDefDirect")]
    internal static class Patch_PrisonAreaJoyRestriction_GetJoyInGatheringArea
    {
        static void Postfix(Pawn __1, ref Job __result)
        {
            Patch_PrisonAreaJoyRestrictionShared.TryBlockJoyJob(__1, ref __result);
        }
    }

    internal static class Patch_PrisonAreaJoyRestrictionShared
    {
        public static void TryBlockJoyJob(Pawn pawn, ref Job job)
        {
            if (job == null)
            {
                return;
            }

            if (!PrisonAreaJoyRestrictionHelper.ShouldBlock(pawn))
            {
                return;
            }

            Area_Prison area = PrisonAreaWorkRestrictionHelper.CachedPrisonArea(pawn.Map);
            if (area == null)
            {
                return;
            }

            if (PrisonAreaJoyRestrictionHelper.JobTouchesPrisonArea(job, area))
            {
                job = null;
            }
        }
    }
}
