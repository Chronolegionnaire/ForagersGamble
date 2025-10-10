using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CookingRecipe), nameof(CookingRecipe.GetOutputName))]
    public static class Patch_CookingRecipe_GetOutputName
    {
        static void Postfix(CookingRecipe __instance, IWorldAccessor worldForResolve, ItemStack[] inputStacks, ref string __result)
        {
            if (string.IsNullOrWhiteSpace(__result) || inputStacks == null || inputStacks.Length == 0) return;

            var cworld = worldForResolve as IClientWorldAccessor;
            var agent  = cworld?.Player?.Entity as EntityPlayer;
            if (agent == null) return;
            if (agent.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

            var api = cworld.Api;
            if (api == null) return;

            var tokensToMask = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stack in inputStacks.Where(s => s != null && s.StackSize > 0 && s.Collectible != null))
            {
                var masked = IngredientMasking.MaskedIngredientLabel(api, agent, stack);
                if (string.IsNullOrWhiteSpace(masked)) continue;

                var dom   = stack.Collectible.Code?.Domain ?? "game";
                var path  = stack.Collectible.Code?.Path   ?? "";
                var first = stack.Collectible.FirstCodePart(0) ?? "";
                foreach (var role in new[] { "primary", "secondary" })
                {
                    var keyFull  = $"meal-ingredient-{__instance.Code}-{role}-{path}";
                    var keyFirst = $"meal-ingredient-{__instance.Code}-{role}-{first}";

                    if (Lang.HasTranslation(keyFull, true, true))
                        tokensToMask[Lang.GetMatching(keyFull)] = masked;

                    if (Lang.HasTranslation(keyFirst, true, true))
                        tokensToMask[Lang.GetMatching(keyFirst)] = masked;
                }
                var iclass = stack.Class.ToString().ToLowerInvariant();
                foreach (var suffix in new[] { "", "-insturmentalcase", "-topping" })
                {
                    var ikeyFull  = $"{dom}:recipeingredient-{iclass}-{path}{suffix}";
                    var ikeyFirst = $"{dom}:recipeingredient-{iclass}-{first}{suffix}";

                    if (Lang.HasTranslation(ikeyFull, true, true))
                        tokensToMask[Lang.GetMatching(ikeyFull)] = masked;

                    if (Lang.HasTranslation(ikeyFirst, true, true))
                        tokensToMask[Lang.GetMatching(ikeyFirst)] = masked;
                }
                var plain = stack.GetName();
                if (!string.IsNullOrWhiteSpace(plain))
                    tokensToMask[plain] = masked;
            }

            if (tokensToMask.Count == 0) return;

            var after = __result;
            foreach (var kvp in tokensToMask)
            {
                var pat = $@"\b{Regex.Escape(kvp.Key)}\b";
                after = Regex.Replace(after, pat, kvp.Value, RegexOptions.IgnoreCase);
            }

            __result = after;
        }
    }
}
