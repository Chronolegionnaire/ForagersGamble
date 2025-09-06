using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ForagersGamble.Patches
{
    internal static class NibbleKeys
    {
        public const string AttrRoot = "foragersGamble";
        public const string NibbleIntent = "nibbleIntent";
    }
    [HarmonyPatch(typeof(CollectibleObject), "tryEatBegin")]
    public static class Patch_CollectibleObject_TryEatBegin
    {
        static void Postfix(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling)
        {
            try
            {
                if (slot?.Itemstack == null || byEntity == null) return;
                bool wantNibble = (byEntity.Controls?.Sneak ?? false) && !Knowledge.IsKnown(byEntity, slot.Itemstack);
                
                var wat = byEntity.WatchedAttributes;
                var root = wat.GetTreeAttribute(NibbleKeys.AttrRoot) ?? new TreeAttribute();
                root.SetBool(NibbleKeys.NibbleIntent, wantNibble);
                wat.SetAttribute(NibbleKeys.AttrRoot, root);
                byEntity.Attributes.MarkPathDirty(NibbleKeys.AttrRoot);
            }
            catch
            {
            }
        }
    }
}