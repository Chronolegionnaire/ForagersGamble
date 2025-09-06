using System.Linq;
using System.Text.RegularExpressions;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionFacts),
    new[] {
        typeof(IWorldAccessor),
        typeof(ItemSlot),
        typeof(ItemStack[]),
        typeof(EntityAgent),
        typeof(bool),
        typeof(float),
        typeof(float)
    })]
static class Patch_BlockMeal_GetContentNutritionFacts_Postfix
{
    static void Postfix(
        IWorldAccessor world,
        ItemSlot inSlotorFirstSlot,
        ItemStack[] contentStacks,
        EntityAgent forEntity,
        bool mulWithStacksize,
        float nutritionMul,
        float healthMul,
        ref string __result)
    {
        bool hideMealSafety = ModConfig.Instance.Main.HideMealSafety;
        if (!hideMealSafety) return;

        var props = BlockMeal.GetContentNutritionProperties(
            world, inSlotorFirstSlot, contentStacks, forEntity, mulWithStacksize, nutritionMul, healthMul);

        float totalHealth = props?.Sum(p => p?.Health ?? 0f) ?? 0f;

        if (totalHealth <= 0f && !string.IsNullOrEmpty(__result))
        {
            __result = Regex.Replace(__result, @"(?m)^\s*-\s*Health:.*\r?\n?", "");
        }
    }
}