using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ForagersGamble
{
    public static class Knowledge
    {
        private const string AttrRoot        = "foragersGamble";
        private const string KnownSet        = "knownFoods";
        private const string KnownHealthSet  = "knownHealth";

        public static bool IsKnown(EntityAgent entity, ItemStack stack)
        {
            if (entity == null || stack == null) return false;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return false;

            var root = player.Entity.WatchedAttributes.GetTreeAttribute(AttrRoot);
            if (root == null) return false;

            var known = root[KnownSet] as StringArrayAttribute;
            if (known == null) return false;

            var code = ItemKey(stack);
            foreach (var s in known.value)
            {
                if (s == code) return true;
            }
            return false;
        }
        public static void MarkKnown(EntityAgent entity, ItemStack stack)
        {
            if (entity == null || stack == null) return;
            var code = ItemKey(stack);
            MarkKnown(entity, code);
        }
        public static void MarkKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrEmpty(code)) return;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return;

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();

            var list = new List<string>();
            var cur = root[KnownSet] as StringArrayAttribute;
            if (cur != null) list.AddRange(cur.value);

            if (!list.Contains(code))
            {
                list.Add(code);
                root[KnownSet] = new StringArrayAttribute(list.ToArray());
                wat.SetAttribute(AttrRoot, root);
                player.Entity.Attributes.MarkPathDirty(AttrRoot);
            }
        }

        public static bool IsHealthKnown(EntityAgent entity, ItemStack stack)
            => IsHealthKnown(entity, ItemKey(stack));

        public static bool IsHealthKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrEmpty(code)) return false;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return false;

            var root = player.Entity.WatchedAttributes.GetTreeAttribute(AttrRoot);
            if (root == null) return false;

            var known = root[KnownHealthSet] as StringArrayAttribute;
            if (known == null || known.value == null) return false;

            foreach (var s in known.value)
            {
                if (s == code) return true;
            }
            return false;
        }

        public static void MarkHealthKnown(EntityAgent entity, ItemStack stack)
            => MarkHealthKnown(entity, ItemKey(stack));

        public static void MarkHealthKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrEmpty(code)) return;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return;

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();

            var list = new List<string>();
            var cur  = root[KnownHealthSet] as StringArrayAttribute;
            if (cur?.value != null) list.AddRange(cur.value);

            if (!list.Contains(code))
            {
                list.Add(code);
                root[KnownHealthSet] = new StringArrayAttribute(list.ToArray());
                wat.SetAttribute(AttrRoot, root);
                player.Entity.Attributes.MarkPathDirty(AttrRoot);
            }
        }

        public static string ItemKey(ItemStack stack)
        {
            return stack?.Collectible?.Code?.ToString() ?? "";
        }

        public static void ForgetAll(IPlayer player)
        {
            if (player?.Entity == null) return;

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot);
            if (root == null) return;

            root[KnownSet]       = new StringArrayAttribute(System.Array.Empty<string>());
            root[KnownHealthSet] = new StringArrayAttribute(System.Array.Empty<string>());
            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
        }
    }
}
