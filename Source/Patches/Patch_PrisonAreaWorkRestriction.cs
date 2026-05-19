using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using RimPrison.PrisonArea;

namespace RimPrison.Patches
{
    // Shared cache and helper for prison area work restriction patches.
    // Both HasJobOnThing (thing-based) and HasJobOnCell (cell-based) scans
    // need to check whether the target position is inside the prison area.
    internal static class PrisonAreaWorkRestrictionHelper
    {
        // Per-map throttled cache: HasJobOnThing/HasJobOnCell fire hundreds of
        // times per frame. Refresh every 3000 ticks (~83s at 1x speed).
        // Dictionary-based so multiple maps (e.g. Set Up Camp) each get their
        // own independent cache entry.
        private static readonly Dictionary<Map, (int refreshTick, Area_Prison area)> s_areaCache = new();

        public static Area_Prison CachedPrisonArea(Map map)
        {
            if (map == null) return null;
            int now = Find.TickManager.TicksGame;
            if (!s_areaCache.TryGetValue(map, out var entry) || now >= entry.refreshTick)
            {
                var area = map.areaManager.Get<Area_Prison>();
                s_areaCache[map] = (now + 3000, area);
                return area;
            }
            return entry.area;
        }

        public static bool ShouldBlock(Pawn pawn, WorkTypeDef workType)
        {
            if ((!pawn.IsColonist || pawn.IsPrisonerOfColony) && !pawn.IsColonyMech) return false;
            var disabled = RimPrisonMod.Settings.DisabledWorkInPrisonArea;
            // [TODO] maybe null guard here
            if (disabled.Count == 0) return false;
            if (workType == null) return false;
            return disabled.Contains(workType.defName);
        }
    }

    [HarmonyPatch]
    public static class Patch_PrisonAreaWorkRestriction_Thing
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = ass.GetTypes(); }
                catch (Exception) { Log.Warning($"[RimPrison] Failed to scan types in assembly {ass.GetName().Name}"); continue; }

                foreach (var type in types)
                {
                    if (!type.IsClass || type.IsAbstract || !type.IsSubclassOf(typeof(WorkGiver_Scanner)))
                        continue;

                    var method = type.GetMethod("HasJobOnThing",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(Pawn), typeof(Thing), typeof(bool) },
                        null);
                    if (method != null && method.ReturnType == typeof(bool))
                        yield return method;
                }
            }
        }

        static void Postfix(WorkGiver_Scanner __instance, Pawn __0, Thing __1, ref bool __result)
        {
            Pawn pawn = __0;
            Thing targetThing = __1;

            if (!__result) return;
            if (pawn == null || targetThing == null) return;
            if (!PrisonAreaWorkRestrictionHelper.ShouldBlock(pawn, __instance.def.workType))
                return;

            var area = PrisonAreaWorkRestrictionHelper.CachedPrisonArea(pawn.Map);
            if (area == null) return;
            if (!area[targetThing.Position]) return;

            __result = false;
        }
    }

    [HarmonyPatch]
    public static class Patch_PrisonAreaWorkRestriction_Cell
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = ass.GetTypes(); }
                catch (Exception) { Log.Warning($"[RimPrison] Failed to scan types in assembly {ass.GetName().Name}"); continue; }

                foreach (var type in types)
                {
                    if (!type.IsClass || type.IsAbstract || !type.IsSubclassOf(typeof(WorkGiver_Scanner)))
                        continue;

                    var method = type.GetMethod("HasJobOnCell",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) },
                        null);
                    if (method != null && method.ReturnType == typeof(bool))
                        yield return method;
                }
            }
        }

        static void Postfix(WorkGiver_Scanner __instance, Pawn __0, IntVec3 __1, ref bool __result)
        {
            Pawn pawn = __0;
            IntVec3 cell = __1;

            if (!__result) return;
            if (pawn == null) return;
            if (!PrisonAreaWorkRestrictionHelper.ShouldBlock(pawn, __instance.def.workType))
                return;

            var area = PrisonAreaWorkRestrictionHelper.CachedPrisonArea(pawn.Map);
            if (area == null) return;
            if (!area[cell]) return;

            __result = false;
        }
    }
}
