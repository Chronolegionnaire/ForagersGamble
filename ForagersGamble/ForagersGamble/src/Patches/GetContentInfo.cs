using System.Text;
using HarmonyLib;
using ForagersGamble;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.GetContentInfo))]
    public static class Patch_Base_GetContentInfo_UnknownLiquid
    {
        static bool IsLiquid(ItemStack s)
        {
            if (s?.Collectible == null) return false;
            if (s.Collectible.Attributes?["waterTightContainerProps"]?.Exists == true) return true;
            try
            {
                return BlockLiquidContainerBase.GetContainableProps(s) != null;
            }
            catch
            {
                return false;
            }
        }

        static bool Prefix(BlockLiquidContainerBase __instance, ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
        {
            if (NameMaskingScope.IsActive) return true;
            if (inSlot?.Itemstack == null) return true;

            var apiField = AccessTools.Field(typeof(Block), "api");
            var api = apiField?.GetValue(__instance) as ICoreAPI;
            var worldAcc = api?.World;
            var agent = (worldAcc as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return true;

            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return true;

            var mGetCurrentLitres = AccessTools.DeclaredMethod(
                typeof(BlockLiquidContainerBase),
                "GetCurrentLitres",
                new[] { typeof(ItemStack) });
            var mGetContent = AccessTools.DeclaredMethod(
                typeof(BlockLiquidContainerBase),
                "GetContent",
                new[] { typeof(ItemStack) });

            var mGetContentInDummySlot = AccessTools.Method(
                typeof(BlockLiquidContainerBase),
                "GetContentInDummySlot",
                new[] { typeof(ItemSlot), typeof(ItemStack) });
            var mAppendPerishableInfoText = AccessTools.Method(
                typeof(BlockLiquidContainerBase),
                "AppendPerishableInfoText",
                new[]
                {
                    typeof(ItemSlot), typeof(StringBuilder),
                    typeof(IWorldAccessor), typeof(TransitionState), typeof(bool)
                });

            if (mGetCurrentLitres == null || mGetContent == null ||
                mGetContentInDummySlot == null || mAppendPerishableInfoText == null)
                return true;

            float litres = (float)mGetCurrentLitres.Invoke(__instance, new object[] { inSlot.Itemstack });
            ItemStack content = (ItemStack)mGetContent.Invoke(__instance, new object[] { inSlot.Itemstack });

            if (litres <= 0f)
            {
                dsc.AppendLine(Lang.Get("Empty"));
                return false;
            }

            if (content == null) return true;

            string nameToken;

            if (IsLiquid(content))
            {
                // Centralized unknown-liquid decision
                if (Knowledge.TryResolveUnknownLiquidName(
                        agent,
                        api,
                        content.Collectible,
                        content,
                        cfg.UnknownAll == true,
                        cfg.UnknownPlants,
                        cfg.UnknownMushrooms,
                        out var langKey))
                {
                    nameToken = Lang.Get(langKey);
                }
                else
                {
                    nameToken = Lang.Get(
                        content.Collectible.Code.Domain + ":incontainer-" +
                        content.Class.ToString().ToLowerInvariant() + "-" +
                        content.Collectible.Code.Path
                    );
                }
            }
            else
            {
                nameToken = Lang.Get(
                    content.Collectible.Code.Domain + ":incontainer-" +
                    content.Class.ToString().ToLowerInvariant() + "-" +
                    content.Collectible.Code.Path
                );
            }

            dsc.AppendLine(Lang.Get("{0} litres of {1}", new object[] { litres, nameToken }));

            ItemSlot dummySlot = (ItemSlot)mGetContentInDummySlot.Invoke(
                __instance,
                new object[] { inSlot, content });

            TransitionState[] states = content.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);

            if (states != null && !dummySlot.Empty)
            {
                bool nowSpoiling = false;
                foreach (var state in states)
                {
                    var ret = mAppendPerishableInfoText.Invoke(
                        __instance,
                        new object[] { dummySlot, dsc, world, state, nowSpoiling });

                    if (ret is float f && f > 0f) nowSpoiling = true;
                }
            }

            return false;
        }
    }
}
