using System;
using System.Text;
using HarmonyLib;
using ForagersGamble;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.GetPlacedBlockInfo))]
    public static class Patch_Base_GetPlacedBlockInfo_UnknownLiquid
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

        static void Postfix(
            BlockLiquidContainerBase __instance,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer forPlayer,
            ref string __result)
        {
            var apiField = AccessTools.Field(typeof(Block), "api");
            var api = apiField?.GetValue(__instance) as ICoreAPI;
            var worldAcc = api?.World;
            var agent = (worldAcc as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return;

            var becontainer = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityContainer;
            if (becontainer == null) return;

            var mGetCurrentLitres = AccessTools.DeclaredMethod(
                typeof(BlockLiquidContainerBase),
                "GetCurrentLitres",
                new[] { typeof(BlockPos) }
            );
            var mGetContainerSlotId = AccessTools.DeclaredMethod(
                typeof(BlockLiquidContainerBase),
                "GetContainerSlotId",
                new[] { typeof(BlockPos) }
            );
            if (mGetCurrentLitres == null || mGetContainerSlotId == null) return;

            float litres = (float)mGetCurrentLitres.Invoke(__instance, new object[] { pos });
            if (litres <= 0f) return;

            int slotId = (int)mGetContainerSlotId.Invoke(__instance, new object[] { pos });
            var slot = becontainer.Inventory[slotId];
            var content = slot?.Itemstack;
            if (content == null || !IsLiquid(content)) return;

            // Ask Knowledge if this should be masked as unknown-liquid
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
                // either the config doesn’t care or the player already knows it
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(Lang.Get("Contents:", Array.Empty<object>()));
            sb.AppendLine(" " + Lang.Get("{0} litres of {1}", new object[]
            {
                litres,
                Lang.Get(langKey)
            }));

            var perishableInfo = BlockLiquidContainerBase.PerishableInfoCompact(api, slot, 0f, false);
            if (perishableInfo.Length > 2) sb.AppendLine(perishableInfo.Substring(2));

            var header = Lang.Get("Contents:", Array.Empty<object>());
            var original = __result ?? "";
            int idx = original.IndexOf(header, StringComparison.Ordinal);
            if (idx >= 0)
            {
                int afterHeader = idx + header.Length;
                int nextBlank = original.IndexOf("\n\n", afterHeader, StringComparison.Ordinal);
                string tail = nextBlank >= 0 ? original.Substring(nextBlank + 2) : "";
                __result = sb + tail;
            }
            else
            {
                __result = sb + original;
            }
        }
    }
}
