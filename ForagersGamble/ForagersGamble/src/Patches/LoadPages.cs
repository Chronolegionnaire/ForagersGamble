using HarmonyLib;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(GuiDialogHandbook), "LoadPages_Async")]
    internal static class Patch_GuiDialogHandbook_LoadPages_Async
    {
        static void Prefix()
        {
            NameMaskingScope.Enter();
        }
        static void Finalizer(System.Exception __exception)
        {
            NameMaskingScope.Exit();
        }
    }
}