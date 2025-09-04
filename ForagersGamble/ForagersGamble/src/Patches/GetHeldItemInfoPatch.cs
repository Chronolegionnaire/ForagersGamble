using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches
{
    public static class DisplayGating
    {
        public static float FoodHealthMulIfKnown(float spoil, ItemStack stack, EntityAgent agent)
        {
            float vanilla = GlobalConstants.FoodSpoilageHealthLossMul(spoil, stack, agent);
            var ep = agent as EntityPlayer;
            if (ep?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival)
                return vanilla;
            return Knowledge.IsKnown(agent, stack) ? vanilla : 0f;
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
    public static class Patch_CollectibleObject_GetHeldItemInfo
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
            => HarmonyLib.Transpilers.MethodReplacer(
                instrs,
                AccessTools.Method(typeof(GlobalConstants), nameof(GlobalConstants.FoodSpoilageHealthLossMul)),
                AccessTools.Method(typeof(DisplayGating), nameof(DisplayGating.FoodHealthMulIfKnown)));
    }
}