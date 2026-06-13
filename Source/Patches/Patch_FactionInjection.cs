using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using RimPrison.PrisonLabor;

namespace RimPrison.Patches
{
    // Replace pawn.Faction getter in work scanning paths with GetWorkFaction,
    // which returns Faction.OfPlayer for labor-enabled prisoners.
    // Inspired by PrisonLabor.
    [HarmonyPatch]
    public static class Patch_FactionInjection
    {
        private static readonly MethodInfo s_getWorkFaction =
            typeof(PrisonLaborUtility).GetMethod(nameof(PrisonLaborUtility.GetWorkFaction));

        private static readonly MethodInfo s_getFaction =
            AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Faction));

        public static IEnumerable<MethodBase> TargetMethods()
        {
            // All WorkGiver_Scanner subclass overrides of key scanning methods,
            // across ALL loaded assemblies (vanilla + mod DLLs).
            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = ass.GetTypes(); }
                catch (Exception) { Log.Warning($"[RimPrison] Failed to scan types in assembly {ass.GetName().Name}"); continue; }

                foreach (var type in types)
                {
                    if (!type.IsClass || type.IsAbstract || !type.IsSubclassOf(typeof(WorkGiver_Scanner)))
                        continue;

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (IsScannerMethod(method))
                        {
                            yield return method;
                        }
                    }
                }
            }

            // Construction methods where JobOnThing is defined on an abstract parent
            yield return AccessTools.Method(typeof(WorkGiver_ConstructFinishFrames), nameof(WorkGiver_ConstructFinishFrames.JobOnThing));
            yield return AccessTools.Method(typeof(WorkGiver_ConstructDeliverResourcesToFrames), nameof(WorkGiver_ConstructDeliverResourcesToFrames.JobOnThing));
            yield return AccessTools.Method(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing));

            // Static utility functions called from work scanning paths
            yield return AccessTools.Method(typeof(RepairUtility), nameof(RepairUtility.PawnCanRepairEver));
            yield return AccessTools.Method(typeof(RepairUtility), nameof(RepairUtility.PawnCanRepairNow));
            yield return AccessTools.Method(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob));

            // JobGiver_OptimizeApparel checks pawn.Faction != Faction.OfPlayer
            yield return AccessTools.Method(typeof(JobGiver_OptimizeApparel), "TryGiveJob");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method)
        {
            var pawnLdarg = method.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1;

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (i > 0
                    && codes[i - 1].opcode == pawnLdarg
                    && codes[i].opcode == OpCodes.Callvirt
                    && codes[i].OperandIs(s_getFaction))
                {
                    yield return new CodeInstruction(OpCodes.Call, s_getWorkFaction);
                }
                else
                {
                    yield return codes[i];
                }
            }
        }

        // Only patch methods whose parameter types match the vanilla
        // WorkGiver_Scanner signatures. Some mods add overloads or reuse
        // the same method names with different parameters, which breaks the
        // transpiler's assumption that ldarg.1 is the pawn argument.
        private static bool IsScannerMethod(MethodInfo method)
        {
            var parameters = method.GetParameters();
            switch (method.Name)
            {
                case "PotentialWorkCellsGlobal":
                case "PotentialWorkThingsGlobal":
                    return parameters.Length == 1
                        && parameters[0].ParameterType == typeof(Pawn);
                case "HasJobOnCell":
                case "HasJobOnThing":
                case "JobOnThing":
                    return parameters.Length == 3
                        && parameters[0].ParameterType == typeof(Pawn)
                        && (parameters[1].ParameterType == typeof(Thing)
                            || parameters[1].ParameterType == typeof(IntVec3))
                        && parameters[2].ParameterType == typeof(bool);
                case "ShouldSkip":
                    return parameters.Length == 2
                        && parameters[0].ParameterType == typeof(Pawn)
                        && parameters[1].ParameterType == typeof(bool);
                case "JobOnCell":
                    return parameters.Length == 3
                        && parameters[0].ParameterType == typeof(Pawn)
                        && parameters[1].ParameterType == typeof(IntVec3)
                        && parameters[2].ParameterType == typeof(bool);
                default:
                    return false;
            }
        }
    }
}
