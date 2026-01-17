using System;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(ItemPressedMash), nameof(ItemPressedMash.GetHeldItemName))]
    public static class Patch_ItemPressedMash_GetHeldItemName
    {
        static void Postfix(ItemPressedMash __instance, ItemStack itemStack, ref string __result, ICoreAPI ___api)
        {
            if (NameMaskingScope.IsActive) return;

            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return;
            if (itemStack == null || ___api?.World == null) return;

            if (___api.World is not IClientWorldAccessor cwa) return;
            if (cwa.Player?.Entity is not EntityPlayer agent) return;
            if (agent.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (Knowledge.TryGetMaskedHeldItemName(agent, ___api, __instance, itemStack, cfg, out var langKey))
            {
                __result = Lang.Get(langKey);
            }
        }
    }
}