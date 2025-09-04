using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    public static class Patch_CollectibleObject_TryEatStop
    {
        static void Prefix(ItemSlot slot, ref string __state)
        {
            try
            {
                __state = Knowledge.ItemKey(slot?.Itemstack);
            }
            catch
            {
                __state = null;
            }
        }

        static void Postfix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, string __state)
        {
            try
            {
                if (byEntity?.World is IServerWorldAccessor && secondsUsed >= 0.95f)
                {
                    if (slot?.Itemstack != null)
                    {
                        Knowledge.MarkKnown(byEntity, slot.Itemstack);
                    }
                    else if (!string.IsNullOrEmpty(__state))
                    {
                        Knowledge.MarkKnown(byEntity, __state);
                    }
                }
            }
            catch { }
        }
    }
}