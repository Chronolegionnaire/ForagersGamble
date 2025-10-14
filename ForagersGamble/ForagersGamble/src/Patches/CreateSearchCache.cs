using System;
using HarmonyLib;
using Vintagestory.Common;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CreativeTabs), "CreateSearchCache")]
    internal static class Patch_CreativeTabs_CreateSearchCache
    {
        static void Prefix()
        {
            NameMaskingScope.Enter();
        }
        static void Finalizer(Exception __exception)
        {
            NameMaskingScope.Exit();
        }
    }
}