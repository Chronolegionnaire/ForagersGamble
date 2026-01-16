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
    [HarmonyPatch(typeof(BlockLiquidContainerTopOpened), nameof(BlockLiquidContainerTopOpened.GetContainedInfo))]
    public static class Patch_TopOpened_GetContainedInfo_UnknownLiquid
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

        static void Postfix(BlockLiquidContainerTopOpened __instance, ItemSlot inSlot, ref string __result)
        {
            if (NameMaskingScope.IsActive) return;
            if (inSlot?.Itemstack == null || string.IsNullOrEmpty(__result)) return;

            var apiField = AccessTools.Field(typeof(Block), "api");
            var api = apiField?.GetValue(__instance) as ICoreAPI;
            var world = api?.World;
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return;

            var mGetCurrentLitres = AccessTools.Method(
                typeof(BlockLiquidContainerBase),
                "GetCurrentLitres",
                new[] { typeof(ItemStack) }
            );
            var mGetContent = AccessTools.DeclaredMethod(
                typeof(BlockLiquidContainerBase),
                "GetContent",
                new[] { typeof(ItemStack) }
            );
            if (mGetCurrentLitres == null || mGetContent == null) return;

            float litres = (float)mGetCurrentLitres.Invoke(__instance, new object[] { inSlot.Itemstack });
            if (litres <= 0f) return;

            var content = (ItemStack)mGetContent.Invoke(__instance, new object[] { inSlot.Itemstack });
            if (content == null || !IsLiquid(content)) return;

            // Centralized unknown-liquid check
            if (!Knowledge.TryResolveUnknownLiquidName(
                    agent,
                    api,
                    content.Collectible,
                    content,
                    cfg.UnknownAll == true,
                    cfg.UnknownPlants,
                    cfg.UnknownMushrooms,
                    out var langKey))
            {
                return;
            }

            var containerName = inSlot.Itemstack.GetName();

            var mPerishContainer = AccessTools.Method(
                typeof(Block),
                "PerishableInfoCompactContainer",
                new[] { typeof(ICoreAPI), typeof(ItemSlot) }
            );
            string perish = mPerishContainer != null
                ? (string)mPerishContainer.Invoke(__instance, new object[] { api, inSlot })
                : BlockLiquidContainerBase.PerishableInfoCompact(api, inSlot, 0f, false);

            var contentName = Lang.Get(langKey);

            __result = Lang.Get("contained-liquidcontainer-compact", new object[]
            {
                containerName, litres, contentName, perish
            });
        }
    }
}
