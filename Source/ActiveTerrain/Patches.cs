using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using System.Reflection.Emit;

namespace ActiveTerrain
{ 
    [StaticConstructorOnStartup, HarmonyPatch(typeof(TerrainGrid), "SetTerrain")]
    public static class _TerrainGrid
    {
        static _TerrainGrid()
        {
            HarmonyInstance.Create("com.spdskatr.activeterrain.patches").PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("Active Terrain Framework initialized. This mod uses Harmony (all patches are non-destructive): Verse.TerrainGrid.SetTerrain, Verse.TerrainGrid.RemoveTopLayer, Verse.MouseoverReadout.MouseoverReadoutOnGUI");
        }
        [HarmonyPriority(Priority.High)]
        static void Prefix(IntVec3 c, TerrainDef newTerr, TerrainGrid __instance)
        {
            var map = Traverse.Create(__instance).Field("map").GetValue<Map>();
            var oldTerr = map.terrainGrid.TerrainAt(c);
            if (oldTerr is SpecialTerrain special)
            {
                map.GetComponent<SpecialTerrainList>().Notify_RemovedTerrainAt(c);
            }
        }
        static void Postfix(IntVec3 c, TerrainDef newTerr, TerrainGrid __instance)
        {
            if (newTerr is SpecialTerrain special)
            {
                var specialTerrainList = Traverse.Create(__instance).Field("map").GetValue<Map>().GetComponent<SpecialTerrainList>();
                specialTerrainList.RegisterAt(special, c);
            }
        }
    }
    [HarmonyPatch(typeof(TerrainGrid), "RemoveTopLayer")]
    static class __TerrainGrid
    {
        [HarmonyPriority(Priority.High)]
        static void Prefix(IntVec3 c, TerrainGrid __instance)
        {
            if (__instance.TerrainAt(c) is SpecialTerrain special)
            {
                var specialTerrainList = Traverse.Create(__instance).Field("map").GetValue<Map>().GetComponent<SpecialTerrainList>();
                specialTerrainList.Notify_RemovedTerrainAt(c);
            }
        }
    }
    [HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    static class _MouseoverReadout
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> original)
        {
            bool patchedCodeBlock2 = false;
            foreach (var instr in original)
            {
                if (instr.opcode == OpCodes.Ldfld && instr.operand == typeof(MouseoverReadout).GetField("cachedTerrain", BindingFlags.Instance | BindingFlags.NonPublic))
                //Basically enables another check for isinst at ldfld class Verse.TerrainDef Verse.MouseoverReadout::cachedTerrain.
                {
                    yield return instr;
                    yield return new CodeInstruction(OpCodes.Ceq);//Returns true if original value was equal to cache

                    yield return new CodeInstruction(OpCodes.Ldloc_S, 10);// local of TerrainDef
                    yield return new CodeInstruction(OpCodes.Isinst, typeof(SpecialTerrain));//Returns null reference if not of instance
                    yield return new CodeInstruction(OpCodes.Ldnull);
                    yield return new CodeInstruction(OpCodes.Ceq);//Returns true if terrain isn't of type SpecialTerrain

                    yield return new CodeInstruction(OpCodes.And);//true if above 2 values are true
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);//loads true onto stack
                }
                else if (!patchedCodeBlock2 && instr.opcode == OpCodes.Callvirt && instr.operand == typeof(Def).GetMethod("get_LabelCap"))
                    //Calls the method for handling the label query
                {
                    //Remember - TerrainDef was originally on the stack
                    yield return new CodeInstruction(OpCodes.Ldloc_0);//Cell
                    yield return new CodeInstruction(OpCodes.Call, typeof(Find).GetMethod("get_CurrentMap"));//Map
                    yield return new CodeInstruction(OpCodes.Call, typeof(_MouseoverReadout).GetMethod(nameof(HandleLabelQuery)));
                    patchedCodeBlock2 = true;
                }
                else
                    yield return instr;
            }
        }
        public static string HandleLabelQuery(TerrainDef def, IntVec3 loc, Map map)
        {
            if (def is SpecialTerrain)
            {
                var inst = map.GetComponent<SpecialTerrainList>().terrains[loc];
                if (inst.def != def)
                {
                    Log.Warning($"ActiveTerrain :: Got terrain instance at tile {loc} but def of terrain instance ({inst.def.defName}) isn't equal to the def on the mouseover readout ({def.defName}). Using the former.");
                }
                return inst.Label;
            }
            else
            {
                return def.LabelCap;
            }
        }
    }
}
