using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(Block), nameof(Block.GetPlacedBlockInfo))]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_Block_GetPlacedBlockInfo
    {
        static void Postfix(Block __instance, IWorldAccessor world, BlockPos pos, IPlayer forPlayer, ref string __result)
        {
            DisplayGating.ScrubPlacedBlockInfoIfHidden(__instance, world, pos, forPlayer, ref __result);
        }
    }
}