using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches
{
    public static class DisplayGating
    {
        static bool InSurvival(EntityAgent agent)
        {
            var ep = agent as EntityPlayer;
            return ep?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Survival;
        }

        static bool IsUnknownToPlayer(EntityAgent agent, ItemStack stack)
        {
            return !Knowledge.IsKnown(agent, stack);
        }

        static bool HideNutrition(EntityAgent agent, ItemStack stack)
        {
            if (!InSurvival(agent)) return false;
            if (!ModConfig.Instance.Main.HideNutritionInfo) return false;
            return IsUnknownToPlayer(agent, stack);
        }

        static bool HideCrafting(EntityAgent agent, ItemStack stack)
        {
            if (!InSurvival(agent)) return false;
            if (!ModConfig.Instance.Main.HideCraftingInfo) return false;
            return IsUnknownToPlayer(agent, stack);
        }

        public static float FoodHealthMulIfKnown(float spoil, ItemStack stack, EntityAgent agent)
        {
            float vanilla = GlobalConstants.FoodSpoilageHealthLossMul(spoil, stack, agent);
            if (!InSurvival(agent)) return vanilla;
            return Knowledge.IsHealthKnown(agent, stack) ? vanilla : 0f;
        }

        public static float FoodSatMulIfKnown(float spoil, ItemStack stack, EntityAgent agent)
        {
            float vanilla = GlobalConstants.FoodSpoilageSatLossMul(spoil, stack, agent);
            return HideNutrition(agent, stack) ? 0f : vanilla;
        }

        static bool IsHydrationLine(string line)
        {
            var li = (line ?? "").Trim().ToLowerInvariant();
            if (li.StartsWith("when drunk:")) return true;
            if (li.Contains("hydration")) return true;
            if (li.Contains(" hyd")) return true;
            var whenEatenLbl = Lang.Get("hydrateordiedrate:hydrateordiedrate-whenEaten")?.Trim().ToLowerInvariant();
            var whenDrunkLbl = Lang.Get("hydrateordiedrate:hydrateordiedrate-whenDrunk")?.Trim().ToLowerInvariant();
            var hydLbl      = Lang.Get("hydrateordiedrate:hydrateordiedrate-hyd", 1f)?.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(whenEatenLbl) && li.StartsWith(whenEatenLbl)) return true;
            if (!string.IsNullOrEmpty(whenDrunkLbl) && li.StartsWith(whenDrunkLbl)) return true;
            if (!string.IsNullOrEmpty(hydLbl))
            {
                var token = new string(hydLbl.TakeWhile(ch => char.IsLetter(ch)).ToArray());
                if (!string.IsNullOrEmpty(token) && li.Contains(token)) return true;
            }

            return false;
        }
        static bool IsNutritionOrHydrationLine(string line)
        {
            var li = (line ?? "").Trim().ToLowerInvariant();
            if (li.StartsWith("when eaten:")) return true;
            if (li.StartsWith("liquid-when-drunk")) return true;
            if (li.StartsWith("food category:")) return true;
            if (IsHydrationLine(line)) return true;

            return false;
        }

        static void RemoveLines(StringBuilder dsc, Func<string, bool> predicate)
        {
            var lines = dsc.ToString().Replace("\r\n", "\n").Split('\n').ToList();
            var kept  = lines.Where(l => !predicate(l)).ToList();
            if (kept.Count == lines.Count) return;

            dsc.Clear();
            for (int i = 0; i < kept.Count; i++)
            {
                if (i > 0) dsc.AppendLine();
                dsc.Append(kept[i]);
            }
        }

        public static void ScrubTooltipIfHidden(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
        {
            var agent = world.Side == EnumAppSide.Client ? (world as IClientWorldAccessor)?.Player?.Entity : null;
            var stack = inSlot?.Itemstack;
            if (agent == null || stack == null) return;

            if (HideNutrition(agent, stack))
            {
                RemoveLines(dsc, IsNutritionOrHydrationLine);
            }
            if (IsFood(stack, world, agent) && HideCrafting(agent, stack))
            {
                RemoveLines(dsc, IsCraftingTransformLine);
            }
        }
        static bool IsFood(ItemStack stack, IWorldAccessor world, EntityAgent agent)
        {
            return stack?.Collectible?.GetNutritionProperties(world, stack, agent as EntityPlayer) != null;
        }

        static bool IsCraftingTransformLine(string line)
        {
            var li = line.Trim().ToLowerInvariant();

            if (li.StartsWith("when ground:")) return true;
            if (li.StartsWith("when pulverized:")) return true;
            if (li.StartsWith("requires pulverizer tier:")) return true;
            if (li.StartsWith("burn temperature:")) return true;
            if (li.StartsWith("burn duration:")) return true;
            if (li.Contains("smelt")) return true;
            if (li.Contains("smelted")) return true;
            if (li.Contains("smelting")) return true;
            if (li.Contains("turns into")) return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter(new[] { "com.chronolegionnaire.hydrateordiedrate"})]
    public static class Patch_CollectibleObject_GetHeldItemInfo
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
            => HarmonyLib.Transpilers.MethodReplacer(
                HarmonyLib.Transpilers.MethodReplacer(
                    instrs,
                    AccessTools.Method(typeof(GlobalConstants), nameof(GlobalConstants.FoodSpoilageHealthLossMul)),
                    AccessTools.Method(typeof(DisplayGating), nameof(DisplayGating.FoodHealthMulIfKnown))
                ),
                AccessTools.Method(typeof(GlobalConstants), nameof(GlobalConstants.FoodSpoilageSatLossMul)),
                AccessTools.Method(typeof(DisplayGating), nameof(DisplayGating.FoodSatMulIfKnown))
            );

        static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
            => DisplayGating.ScrubTooltipIfHidden(inSlot, dsc, world);
    }
}