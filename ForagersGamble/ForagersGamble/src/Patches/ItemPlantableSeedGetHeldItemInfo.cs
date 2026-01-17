using System;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(ItemPlantableSeed), nameof(ItemPlantableSeed.GetHeldItemInfo))]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_ItemPlantableSeed_GetHeldItemInfo
    {
        static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            if (inSlot?.Itemstack == null || dsc == null || world == null) return;
            DisplayGating.ScrubTooltipIfHidden(inSlot, dsc, world);
            var agent = world.Side == EnumAppSide.Client ? (world as IClientWorldAccessor)?.Player?.Entity : null;
            if (agent == null) return;
            if (!InSurvival(agent)) return;
            if (NameMaskingScope.IsActive) return;
            if (ShouldHideSeedInfo(agent, inSlot, world))
            {
                RemoveLines(dsc, IsSeedInfoLine);
            }
        }
        private static bool ShouldHideSeedInfo(EntityAgent agent, ItemSlot inSlot, IWorldAccessor world)
        {
            var stack = inSlot?.Itemstack;
            if (stack?.Collectible == null) return false;
            string baseCode;
            try
            {
                if (Knowledge.TryResolveBaseProduceCodeCached(world.Api, stack, out baseCode) &&
                    !string.IsNullOrWhiteSpace(baseCode))
                {
                    return !Knowledge.IsKnown(agent, baseCode);
                }
            }
            catch
            {
            }
            return !Knowledge.IsKnown(agent, stack);
        }
        static bool IsSeedInfoLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (MatchesLocalizedPrefix(line, "soil-nutrition-requirement")) return true;
            if (MatchesLocalizedPrefix(line, "soil-nutrition-consumption")) return true;
            if (MatchesLocalizedPrefix(line, "soil-growth-time")) return true;
            if (MatchesLocalizedPrefix(line, "crop-coldresistance")) return true;
            if (MatchesLocalizedPrefix(line, "crop-heatresistance")) return true;

            return false;
        }

        static bool InSurvival(EntityAgent agent)
        {
            var ep = agent as EntityPlayer;
            return ep?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Survival;
        }

        static bool MatchesLocalizedPrefix(string line, string langKey, string englishFallbackPrefix = null)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (!TryGetLocalizedPrefix(langKey, out var pfx, englishFallbackPrefix)) return false;

            return line.Trim().StartsWith(pfx, StringComparison.InvariantCultureIgnoreCase);
        }

        static bool TryGetLocalizedPrefix(string langKey, out string prefix, string englishFallback = null)
        {
            prefix = null;

            const double NUM = -123456789.0;
            const string STR = "\uE000\uE001__FG_PREFIX__\uE002";

            try
            {
                string loc = Vintagestory.API.Config.Lang.Get(langKey, NUM, STR, NUM, STR, NUM, STR);

                if (string.IsNullOrEmpty(loc) || loc == langKey)
                {
                    if (!string.IsNullOrEmpty(englishFallback))
                    {
                        prefix = englishFallback.Trim();
                        return true;
                    }

                    return false;
                }

                string numSent = NUM.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

        static void RemoveLines(StringBuilder dsc, Vintagestory.API.Common.Func<string, bool> predicate)
        {
            var lines = dsc.ToString().Replace("\r\n", "\n").Split('\n');
            bool anyRemoved = false;

            var sb = new StringBuilder(dsc.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (predicate(l))
                {
                    anyRemoved = true;
                    continue;
                }

                if (sb.Length > 0) sb.AppendLine();
                sb.Append(l);
            }

            if (!anyRemoved) return;

            dsc.Clear();
            dsc.Append(sb.ToString());
        }
    }
}
