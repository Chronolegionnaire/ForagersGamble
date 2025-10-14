using System.Collections.Generic;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(GuiDialogHandbook), "OpenDetailPageFor")]
static class Patch_Handbook_OpenDetail
{
    static bool Prefix(GuiDialogHandbook __instance, string pageCode, ref bool __result)
    {
        var cfg = ModConfig.Instance?.Main;
        if (cfg?.PreventHandbookOnUnidentified != true) return true;

        var capi = (ICoreClientAPI)AccessTools.Field(typeof(GuiDialogHandbook), "capi").GetValue(__instance);
        var agent = (capi?.World as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
        if (agent == null) return true;
        if (agent.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return true;
        var map = (Dictionary<string, int>)AccessTools.Field(typeof(GuiDialogHandbook), "pageNumberByPageCode").GetValue(__instance);
        var all = (List<GuiHandbookPage>)AccessTools.Field(typeof(GuiDialogHandbook), "allHandbookPages").GetValue(__instance);
        if (map == null || all == null) return true;
        if (!map.TryGetValue(pageCode, out var idx)) return true;
        if (idx < 0 || idx >= all.Count) return true;
        var page = all[idx];
        if (HandbookVisibility.ShouldHidePage(page, capi, agent))
        {
            __result = false;
            return false;
        }
        return true;
    }
}