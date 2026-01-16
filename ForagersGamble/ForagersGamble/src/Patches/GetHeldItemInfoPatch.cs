using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    public static class DisplayGating
    {
        static bool InSurvival(EntityAgent agent)
        {
            var ep = agent as EntityPlayer;
            return ep?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Survival;
        }
        
        static bool MaskingAllowed(EntityAgent agent)
            => !NameMaskingScope.IsActive && InSurvival(agent);

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

        static bool TryGetLocalizedPrefix(string langKey, out string prefix, string englishFallback = null)
        {
            prefix = null;

            const double NUM = -123456789.0;
            const string STR = "\uE000\uE001__FG_PREFIX__\uE002";

            try
            {
                string loc = Lang.Get(langKey, NUM, STR, NUM, STR, NUM, STR);

                if (string.IsNullOrEmpty(loc) || loc == langKey)
                {
                    if (!string.IsNullOrEmpty(englishFallback))
                    {
                        prefix = englishFallback.Trim();
                        return true;
                    }

                    return false;
                }

                string numSent = NUM.ToString(CultureInfo.InvariantCulture);

                int iNum = loc.IndexOf(numSent, StringComparison.Ordinal);
                int iStr = loc.IndexOf(STR, StringComparison.Ordinal);

                int cut = -1;
                if (iNum >= 0 && iStr >= 0) cut = Math.Min(iNum, iStr);
                else if (iNum >= 0) cut = iNum;
                else if (iStr >= 0) cut = iStr;

                prefix = (cut >= 0 ? loc.Substring(0, cut) : loc).Trim();
                if (prefix.Length == 0 && !string.IsNullOrEmpty(englishFallback))
                {
                    prefix = englishFallback.Trim();
                }

                return prefix.Length > 0;
            }
            catch
            {
                if (!string.IsNullOrEmpty(englishFallback))
                {
                    prefix = englishFallback.Trim();
                    return true;
                }

                return false;
            }
        }

        static bool MatchesLocalizedPrefix(string line, string langKey, string englishFallbackPrefix = null)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (!TryGetLocalizedPrefix(langKey, out var pfx, englishFallbackPrefix)) return false;

            return line.Trim().StartsWith(pfx, StringComparison.InvariantCultureIgnoreCase);
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
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (MatchesLocalizedPrefix(line, "When eaten: {0} sat, {1} hp", "When eaten:")) return true;
            if (MatchesLocalizedPrefix(line, "When eaten: {0} sat", "When eaten:")) return true;

            if (MatchesLocalizedPrefix(line, "liquid-when-drunk-saturation-hp", "When drunk:")) return true;
            if (MatchesLocalizedPrefix(line, "liquid-when-drunk-saturation", "When drunk:")) return true;

            if (MatchesLocalizedPrefix(line, "Food Category: {0}", "Food Category:")) return true;

            if (IsHydrationLine(line)) return true;

            return false;
        }


        static void RemoveLines(StringBuilder dsc, Vintagestory.API.Common.Func<string, bool> predicate)
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
            if (NameMaskingScope.IsActive) return;

            if (HideNutrition(agent, stack))
            {
                RemoveLines(dsc, l => IsNutritionOrHydrationLine(l) || IsDietaryNoveltyLine(l));
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
            if (string.IsNullOrWhiteSpace(line)) return false;
            var li = line.Trim().ToLowerInvariant();

            if (MatchesLocalizedPrefix(line, "When ground: Turns into {0}x {1}", "When ground:")) return true;
            if (MatchesLocalizedPrefix(line, "When pulverized: Turns into {0}x {1}", "When pulverized:"))
                return true;
            if (MatchesLocalizedPrefix(line, "When pulverized: Turns into {0:0.#}x {1}", "When pulverized:"))
                return true;

            if (MatchesLocalizedPrefix(line, "When pressed: Turns into {0}l {1}", "When pressed:")) return true;
            if (MatchesLocalizedPrefix(line, "When pressed: Turns into {0:0.#}l {1}", "When pressed:"))
                return true;

            if (MatchesLocalizedPrefix(line, "When juiced: Turns into {0:0.#}l {1}", "When juiced:")) return true;
            if (MatchesLocalizedPrefix(line, "When juiced: Turns into {0:0.##}l {1}", "When juiced:")) return true;
            if (MatchesLocalizedPrefix(line, "collectibleinfo-juicingproperties", "When juiced:")) return true;

            if (MatchesLocalizedPrefix(line, "Requires Pulverizer tier: {0}", "Requires Pulverizer tier:"))
                return true;
            if (MatchesLocalizedPrefix(line, "Burn temperature: {0}°C", "Burn temperature:")) return true;
            if (MatchesLocalizedPrefix(line, "Burn duration: {0}s", "Burn duration:")) return true;

            if (MatchesLocalizedPrefix(line, "smeltdesc-bake-title", "Bakes into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-smelt-title", "Smelts into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-cook-title", "Cooks into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-convert-title", "When heated, turns into")) return true;

            if (MatchesLocalizedPrefix(line, "smeltdesc-bake-singular", "Bakes into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-smelt-singular", "Smelts into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-cook-singular", "Cooks into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-convert-singular", "When heated, turns into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-fire-singular", "fires into")) return true;

            if (MatchesLocalizedPrefix(line, "smeltdesc-bake-plural", "bake into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-smelt-plural", "smelt into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-cook-plural", "cook into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-convert-plural", "convert into")) return true;
            if (MatchesLocalizedPrefix(line, "smeltdesc-fire-plural", "fire into")) return true;

            if (li.Contains("smelt")) return true;
            if (li.Contains("smelted")) return true;
            if (li.Contains("smelting")) return true;
            if (li.Contains("turns into")) return true;

            return false;
        }

        static bool IsActuallyEdible(ItemStack stack, IWorldAccessor world, EntityAgent agent)
        {
            var props = stack?.Collectible?.GetNutritionProperties(world, stack, agent as EntityPlayer);
            if (props == null) return false;
            return props.FoodCategory != EnumFoodCategory.Unknown &&
                   props.FoodCategory != EnumFoodCategory.NoNutrition;
        }

        public static void AppendKnowledgeProgressLine(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
        {
            var agent = world.Side == EnumAppSide.Client ? (world as IClientWorldAccessor)?.Player?.Entity : null;
            var stack = inSlot?.Itemstack;
            if (agent == null || stack == null) return;
            if (!MaskingAllowed(agent)) return;
            if (!InSurvival(agent)) return;
            if (stack.Collectible is BlockLiquidContainerBase blc)
            {
                var content = blc.GetContent(stack);
                if (content != null)
                {
                    stack = content;
                }
            }

            if (!IsActuallyEdible(stack, world, agent)) return;

            float prog = Knowledge.GetProgress(agent, stack);
            if (prog >= 0.9999f) return;
            int pct = Math.Max(0, Math.Min(99, (int)Math.Round(prog * 100f)));
            string line = Lang.Get("foragersgamble:knowledge-progress", pct);
            if (line == "foragersgamble:knowledge-progress")
            {
                line = $"Knowledge: {pct}% learned";
            }

            dsc.AppendLine(line);
        }
        
        static bool IsDietaryNoveltyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string noveltyLabel = Lang.Get("dietarynovelty:playerinfo-nutrition-novelty");
            if (string.IsNullOrEmpty(noveltyLabel) ||
                noveltyLabel == "dietarynovelty:playerinfo-nutrition-novelty")
            {
                noveltyLabel = "novelty";
            }

            var li = line.Trim();
            if (li.IndexOf(noveltyLabel, StringComparison.InvariantCultureIgnoreCase) < 0)
                return false;

            int colon = li.IndexOf(':');
            int slash = li.LastIndexOf('/');
            return colon >= 0 && slash > colon;
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
        {
            DisplayGating.ScrubTooltipIfHidden(inSlot, dsc, world);
            DisplayGating.AppendKnowledgeProgressLine(inSlot, dsc, world);
        }
    }
}