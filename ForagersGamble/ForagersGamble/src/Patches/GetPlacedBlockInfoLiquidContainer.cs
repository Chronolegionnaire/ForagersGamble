using System;
using System.Text;
using ForagersGamble;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(
        typeof(BlockLiquidContainerBase),
        nameof(BlockLiquidContainerBase.GetPlacedBlockInfo)
    )]
    public static class Patch_Base_GetPlacedBlockInfo_UnknownLiquid
    {
        static void Postfix(
            BlockLiquidContainerBase __instance,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer forPlayer,
            ref string __result)
        {
            var apiField = AccessTools.Field(typeof(Block), "api");
            var api = apiField?.GetValue(__instance) as ICoreAPI;

            var agent =
                (api?.World as IClientWorldAccessor)?
                .Player?
                .Entity as EntityPlayer;

            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival)
                return;

            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return;

            var becontainer =
                world.BlockAccessor.GetBlockEntity(pos)
                as BlockEntityContainer;

            if (becontainer == null)
                return;

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

            if (mGetCurrentLitres == null ||
                mGetContainerSlotId == null)
            {
                return;
            }

            float litres = (float)mGetCurrentLitres.Invoke(
                __instance,
                new object[] { pos }
            );

            if (litres <= 0f)
                return;

            int slotId = (int)mGetContainerSlotId.Invoke(
                __instance,
                new object[] { pos }
            );

            var slot = becontainer.Inventory[slotId];
            var content = slot?.Itemstack;

            if (content == null)
                return;

            if (!Knowledge.TryResolveUnknownLiquidName(
                    agent,
                    api,
                    content.Collectible,
                    content,
                    cfg.UnknownAll == true,
                    cfg.UnknownPlants,
                    cfg.UnknownMushrooms,
                    cfg.UnknownFruits,
                    cfg.UnknownVegetables,
                    cfg.UnknownGrains,
                    out var langKey))
            {
                return;
            }
            var sb = new StringBuilder();

            sb.AppendLine(
                Lang.Get("Contents:", Array.Empty<object>())
            );

            sb.AppendLine(
                " " + Lang.Get(
                    "{0} litres of {1}",
                    new object[]
                    {
                        litres,
                        Lang.Get(langKey)
                    }
                )
            );

            var header =
                Lang.Get("Contents:", Array.Empty<object>());

            var original = __result ?? "";

            int idx = original.IndexOf(
                header,
                StringComparison.Ordinal
            );

            if (idx >= 0)
            {
                int afterHeader = idx + header.Length;

                int nextBlank = original.IndexOf(
                    "\n\n",
                    afterHeader,
                    StringComparison.Ordinal
                );

                string before = original
                    .Substring(0, idx)
                    .TrimEnd();

                string tail = nextBlank >= 0
                    ? original.Substring(nextBlank + 2)
                    : "";

                var result = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(before))
                {
                    result.AppendLine(before);
                }

                result.Append(sb);

                if (!string.IsNullOrWhiteSpace(tail))
                {
                    result.AppendLine();
                    result.Append(tail);
                }

                __result = result
                    .ToString()
                    .TrimEnd();
            }
            else
            {
                __result =
                    sb.ToString().TrimEnd();
            }
        }
    }
}