using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(GuiDialogHandbook), "FilterItems")]
static class Patch_Handbook_FilterItems
{
    static void Postfix(GuiDialogHandbook __instance)
    {
        var capiField = AccessTools.Field(typeof(GuiDialogHandbook), "capi");
        var capi = capiField.GetValue(__instance) as ICoreClientAPI;
        var player = (capi?.World as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
        if (player == null || capi == null) return;
        var shownField = AccessTools.Field(typeof(GuiDialogHandbook), "shownHandbookPages");
        var list = shownField.GetValue(__instance) as List<IFlatListItem>;
        if (list == null) return;
        list.RemoveAll(item =>
        {
            var page = item as GuiHandbookPage;
            return page != null && HandbookVisibility.ShouldHidePage(page, capi, player);
        });
        var overviewField = AccessTools.Field(typeof(GuiDialogHandbook), "overviewGui");
        var overview = overviewField.GetValue(__instance) as GuiComposer;
        var stacklist = overview?.GetFlatList("stacklist");
        var scrollbar = overview?.GetScrollbar("scrollbar");
        if (stacklist != null && scrollbar != null)
        {
            stacklist.CalcTotalHeight();
            var listHeightObj = AccessTools.Field(typeof(GuiDialogHandbook), "listHeight").GetValue(__instance);
            float listHeight = Convert.ToSingle(listHeightObj);
            float insideHeight = (float)stacklist.insideBounds.fixedHeight;

            scrollbar.SetHeights(listHeight, insideHeight);
        }
    }
}
