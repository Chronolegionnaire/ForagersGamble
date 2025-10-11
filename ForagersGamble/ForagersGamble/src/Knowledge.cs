using System;
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
        private const string ProgressTree    = "knowledgeProgress";

        public static float GetProgress(EntityAgent entity, ItemStack stack)
            => GetProgress(entity, ItemKey(stack));

        public static float GetProgress(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrEmpty(code)) return 0f;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return 0f;

            var root = player.Entity.WatchedAttributes.GetTreeAttribute(AttrRoot);
            if (root == null) return 0f;

            var legacy = root[KnownSet] as StringArrayAttribute;
            if (legacy?.value != null)
            {
                for (int i = 0; i < legacy.value.Length; i++)
                    if (legacy.value[i] == code) return 1f;
            }

            var prog = root.GetTreeAttribute(ProgressTree);
            return prog?.GetFloat(code, 0f) ?? 0f;
        }

        public static bool AddProgress(EntityAgent entity, string code, float amount)
        {
            if (entity == null || string.IsNullOrEmpty(code) || amount <= 0f) return false;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return false;

            var wat  = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var prog = root.GetTreeAttribute(ProgressTree) ?? new TreeAttribute();

            float cur = prog.GetFloat(code, 0f);
            float next = Math.Min(1f, cur + amount);
            if (Math.Abs(next - cur) < 0.0001f) return false;

            prog.SetFloat(code, next);
            root[ProgressTree] = prog;
            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
            return next >= 1f;
        }
        public static void MarkDiscovered(EntityAgent entity, string code)
        {
            if (string.IsNullOrEmpty(code) || entity == null) return;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return;

            var wat  = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var prog = root.GetTreeAttribute(ProgressTree) ?? new TreeAttribute();

            prog.SetFloat(code, 1f);
            root[ProgressTree] = prog;
            var list = new List<string>();
            var cur  = root[KnownSet] as StringArrayAttribute;
            if (cur?.value != null) list.AddRange(cur.value);
            if (!list.Contains(code)) list.Add(code);
            root[KnownSet] = new StringArrayAttribute(list.ToArray());

            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
        }
        public static bool IsKnown(EntityAgent entity, ItemStack stack)
            => IsKnown(entity, ItemKey(stack));

        public static bool IsKnown(EntityAgent entity, string code)
            => GetProgress(entity, code) >= 1f;

        public static void MarkKnown(EntityAgent entity, ItemStack stack)
        {
            if (entity == null || stack == null) return;
            MarkDiscovered(entity, ItemKey(stack));
        }

        public static void MarkKnown(EntityAgent entity, string code)
            => MarkDiscovered(entity, code);

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
