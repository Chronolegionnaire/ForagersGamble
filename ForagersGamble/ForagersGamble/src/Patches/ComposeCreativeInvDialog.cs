using HarmonyLib;

namespace ForagersGamble.Patches;

public class ComposeCreativeInvDialog
{
    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.GuiDialogInventory), "ComposeCreativeInvDialog")]
    internal static class Patch_GuiDialogInventory_ComposeCreativeInvDialog
    {
        static void Prefix()    => NameMaskingScope.Enter();
        static void Finalizer() => NameMaskingScope.Exit();
    }

}