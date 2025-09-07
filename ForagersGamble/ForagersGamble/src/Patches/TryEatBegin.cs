using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ForagersGamble.Patches
{
    internal static class NibbleKeys
    {
        public const string AttrRoot = "foragersGamble";
        public const string NibbleIntent = "nibbleIntent";
        public const string LastEatItemKey = "FG.LastEatItemKey";
    }

    [HarmonyPatch(typeof(CollectibleObject), "tryEatBegin")]
    [HarmonyPriority(Priority.First)]
    public static class Patch_CollectibleObject_TryEatBegin
    {
        static void Prefix(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling)
        {
            try
            {
                if (byEntity == null) return;

                string key = (slot?.Itemstack != null) ? Knowledge.ItemKey(slot.Itemstack) : null;
                byEntity.WatchedAttributes?.SetString(NibbleKeys.LastEatItemKey, key ?? "");
                byEntity.Attributes?.MarkPathDirty(NibbleKeys.LastEatItemKey);

                bool wantNibble = (byEntity.Controls?.Sneak ?? false)
                                  && (slot?.Itemstack != null)
                                  && !Knowledge.IsKnown(byEntity, slot.Itemstack);

                var wat  = byEntity.WatchedAttributes;
                var root = wat?.GetTreeAttribute(NibbleKeys.AttrRoot) ?? new TreeAttribute();
                root.SetBool(NibbleKeys.NibbleIntent, wantNibble);
                wat?.SetAttribute(NibbleKeys.AttrRoot, root);
                byEntity.Attributes?.MarkPathDirty(NibbleKeys.AttrRoot);
            }
            catch { }
        }
    }
}