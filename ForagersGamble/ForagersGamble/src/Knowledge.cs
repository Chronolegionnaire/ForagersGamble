using System;
using System.Collections.Generic;
using ForagersGamble.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ForagersGamble
{
    public static class Knowledge
    {
        private const string AttrRoot        = "foragersGamble";
        private const string KnownSet        = "knownFoods";
        private const string KnownHealthSet  = "knownHealth";
        private const string ProgressTree    = "knowledgeProgress";
        private static readonly string[] CookStates = { "partbaked", "perfect", "charred" };
        public static float GetProgress(EntityAgent entity, ItemStack stack)
            => GetProgress(entity, ItemKey(stack));

        
        private static HashSet<string> s_unknownUniverse;

        public static void BuildUnknownUniverse(ICoreAPI api, PlantKnowledgeIndex idx)
        {
            var cfg = ModConfig.Instance?.Main;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (idx != null)
            {
                foreach (var bl in api.World.Blocks)
                {
                    if (bl?.Code == null) continue;
                    var code = bl.Code.ToString();
                    if (idx.IsMushroom(code))
                    {
                        set.Add(code);
                        set.Add(Norm(code));
                    }
                }
            }
            foreach (var bl in api.World.Blocks)
            {
                if (bl?.Code == null) continue;

                if (PlantKnowledgeUtil.IsKnowledgeGatedPlant(bl, api))
                {
                    var bcode = bl.Code.ToString();
                    set.Add(bcode);
                    set.Add(Norm(bcode));

                    if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bl, new ItemStack(bl), out var fruit))
                    {
                        var fcode = fruit.Collectible?.Code?.ToString();
                        if (!string.IsNullOrEmpty(fcode))
                        {
                            set.Add(fcode);
                            set.Add(Norm(fcode));
                        }
                    }
                }
            }
            foreach (var coll in api.World.Collectibles)
            {
                if (coll?.Code == null) continue;
                if (coll is BlockSapling) continue;
                if (coll is BlockLiquidContainerTopOpened) continue;
                if (coll is BlockLiquidContainerBase) continue;
                if (coll is BlockPlant flowerBlk)
                {
                    var p = flowerBlk.Code?.Path ?? "";
                    bool looksLikeFlower = p.StartsWith("flower-", StringComparison.OrdinalIgnoreCase)
                                           || p.Contains("-flower-", StringComparison.OrdinalIgnoreCase);
                    var attrs = flowerBlk.Attributes;
                    bool hasNutritionProps = (attrs?["NutritionProps"]?.Exists ?? false)
                                             || (attrs?["nutritionProps"]?.Exists ?? false);
                    if (looksLikeFlower && !hasNutritionProps)
                        continue;
                }

                ItemStack stack;
                try
                {
                    stack = new ItemStack(coll);
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce))
                    {
                        var selfCode = coll.Code.ToString();
                        set.Add(selfCode);
                        set.Add(Norm(selfCode));

                        var baseCode = baseProduce.Collectible?.Code?.ToString();
                        if (!string.IsNullOrEmpty(baseCode))
                        {
                            set.Add(baseCode);
                            set.Add(Norm(baseCode));
                        }
                    }
                }
                catch
                {
                }
            }
            if (cfg?.UnknownAll == true || cfg?.UnknownPlants == true || cfg?.UnknownMushrooms == true)
            {
                foreach (var coll in api.World.Collectibles)
                {
                    if (coll?.Code == null) continue;

                    var hasLiquidAttr = coll.Attributes?["waterTightContainerProps"]?.Exists == true;
                    if (!hasLiquidAttr) continue;

                    ItemStack stack;
                    try
                    {
                        stack = new ItemStack(coll);
                    }
                    catch
                    {
                        continue;
                    }

                    FoodNutritionProperties props = null;
                    try
                    {
                        props = coll.GetNutritionProperties(api.World, stack, null);
                    }
                    catch
                    {
                    }

                    if (props != null &&
                        props.FoodCategory != EnumFoodCategory.Unknown &&
                        props.FoodCategory != EnumFoodCategory.NoNutrition)
                    {
                        var selfCode = coll.Code.ToString();
                        set.Add(selfCode);
                        set.Add(Norm(selfCode));
                    }

                    try
                    {
                        if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce))
                        {
                            var baseCode = baseProduce.Collectible?.Code?.ToString();
                            if (!string.IsNullOrEmpty(baseCode))
                            {
                                set.Add(baseCode);
                                set.Add(Norm(baseCode));
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (cfg?.UnknownAll == true)
            {
                foreach (var coll in api.World.Collectibles)
                {
                    if (coll?.Code == null) continue;

                    ItemStack stack;
                    try
                    {
                        stack = new ItemStack(coll);
                    }
                    catch
                    {
                        continue;
                    }

                    FoodNutritionProperties props = null;
                    try
                    {
                        props = coll.GetNutritionProperties(api.World, stack, null);
                    }
                    catch
                    {
                    }

                    if (props != null &&
                        props.FoodCategory != EnumFoodCategory.Unknown &&
                        props.FoodCategory != EnumFoodCategory.NoNutrition)
                    {
                        var c = coll.Code.ToString();
                        set.Add(c);
                        set.Add(Norm(c));
                    }
                }
            }

            s_unknownUniverse = set;
        }

        private static string Norm(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            var slash = code.IndexOf('/');
            return slash > 0 ? code.Substring(0, slash) : code;
        }

        public static bool IsInUnknownUniverse(string code)
        {
            return !string.IsNullOrEmpty(code)
                   && s_unknownUniverse != null
                   && s_unknownUniverse.Contains(code);
        }
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

            float cur  = prog.GetFloat(code, 0f);
            float next = Math.Min(1f, cur + amount);
            if (Math.Abs(next - cur) < 0.0001f) return false;

            prog.SetFloat(code, next);
            root[ProgressTree] = prog;
            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
            if (next >= 1f) MarkKnown(entity, code);
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
        private static bool IsSaplingCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            int colon = code.IndexOf(':');
            var path = colon >= 0 ? code.Substring(colon + 1) : code;

            return path.StartsWith("sapling-", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsKnown(EntityAgent entity, ItemStack stack)
            => IsKnown(entity, ItemKey(stack));

        public static bool IsKnown(EntityAgent entity, string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;
            if (IsSaplingCode(code))
                return true;

            if (!IsInUnknownUniverse(code))
                return true;

            return GetProgress(entity, code) >= 1f;
        }
        
        public static void MarkKnown(EntityAgent entity, ItemStack stack)
        {
            if (entity == null || stack == null) return;
            var self = ItemKey(stack);
            MarkDiscovered(entity, self);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
            foreach (var code in FamilyCodesFrom(stack))
                if (seen.Add(code)) MarkDiscovered(entity, code);
        }

        public static void MarkKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrWhiteSpace(code)) return;

            MarkDiscovered(entity, code);

            if (TrySplitDomainPath(code, out var domain, out var path))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };
                foreach (var c in FamilyCodesFrom(domain, path))
                    if (seen.Add(c)) MarkDiscovered(entity, c);
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
        {
            if (entity == null || stack == null) return;
            var code = ItemKey(stack);
            MarkHealthKnown(entity, code);
        }
        public static void MarkHealthKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrWhiteSpace(code)) return;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return;

            var wat  = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = root[KnownHealthSet] as StringArrayAttribute;
            if (cur?.value != null)
            {
                foreach (var s in cur.value) if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
            }
            set.Add(code);
            if (TrySplitDomainPath(code, out var domain, out var path))
            {
                foreach (var c in FamilyCodesFrom(domain, path))
                {
                    if (!string.IsNullOrWhiteSpace(c)) set.Add(c);
                }
            }
            var newArr = new List<string>(set).ToArray();
            if (cur?.value == null || cur.value.Length != newArr.Length)
            {
                root[KnownHealthSet] = new StringArrayAttribute(newArr);
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

            var wat  = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();

            root[KnownSet]       = new StringArrayAttribute(System.Array.Empty<string>());
            root[KnownHealthSet] = new StringArrayAttribute(System.Array.Empty<string>());
            root[ProgressTree]   = new TreeAttribute();
            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
        }
        private static bool TrySplitDomainPath(string code, out string domain, out string path)
        {
            domain = "game"; path = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            int i = code.IndexOf(':');
            if (i >= 0) { domain = code.Substring(0, i); path = code.Substring(i + 1); }
            else { path = code; }
            return !string.IsNullOrWhiteSpace(path);
        }

        private static List<string> BuildMushroomFamily(string mush, string variantDomain)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(mush)) return list;
            list.Add($"game:mushroom-{mush}-normal");
            list.Add($"game:mushroom-{mush}-normal-north");
            foreach (var st in CookStates)
            {
                list.Add($"{variantDomain}:cookedmushroom-{mush}-{st}");
                list.Add($"{variantDomain}:choppedmushroom-{mush}-{st}");
                list.Add($"{variantDomain}:cookedchoppedmushroom-{mush}-{st}");
            }
            return list;
        }

        private static List<string> BuildVegFamily(string veg, string variantDomain)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(veg)) return list;

            list.Add($"game:vegetable-{veg}");
            foreach (var st in CookStates)
            {
                list.Add($"{variantDomain}:cookedveggie-{veg}-{st}");
                list.Add($"{variantDomain}:choppedveggie-{veg}-{st}");
                list.Add($"{variantDomain}:cookedchoppedveggie-{veg}-{st}");
            }
            return list;
        }

        private static List<string> BuildFruitFamily(string fruit, string variantDomain)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(fruit)) return list;

            list.Add($"game:fruit-{fruit}");
            list.Add($"{variantDomain}:dryfruit-{fruit}");
            list.Add($"{variantDomain}:candiedfruit-{fruit}");
            list.Add($"{variantDomain}:dehydratedfruit-{fruit}");
            list.Add($"{variantDomain}:pressedmash-{fruit}");
            return list;
        }
        private static IEnumerable<string> FamilyCodesFrom(string domain, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) yield break;
            var segs = path.Split('-');
            if (segs.Length == 0) yield break;
            if (path.StartsWith("mushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var mush = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildMushroomFamily(mush, domain)) yield return c;
                yield break;
            }

            if (path.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase))
            {
                var veg = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildVegFamily(veg, domain)) yield return c;
                yield break;
            }

            if (path.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase))
            {
                var fru = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildFruitFamily(fru, domain)) yield return c;
                yield break;
            }
            if (path.StartsWith("cookedmushroom-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("choppedmushroom-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("cookedchoppedmushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var mush = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildMushroomFamily(mush, domain)) yield return c;
                yield break;
            }
            if (path.StartsWith("cookedveggie-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("choppedveggie-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("cookedchoppedveggie-", StringComparison.OrdinalIgnoreCase))
            {
                var veg = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildVegFamily(veg, domain)) yield return c;
                yield break;
            }
            if (path.StartsWith("dryfruit-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("candiedfruit-", StringComparison.OrdinalIgnoreCase))
            {
                var fru = segs.Length > 1 ? segs[1] : null;
                foreach (var c in BuildFruitFamily(fru, domain)) yield return c;
                yield break;
            }
        }
        private static IEnumerable<string> FamilyCodesFrom(ItemStack stack)
        {
            var al = stack?.Collectible?.Code;
            if (al == null) yield break;

            var domain = al.Domain ?? "game";
            var path   = al.Path   ?? "";
            foreach (var c in FamilyCodesFrom(domain, path)) yield return c;
        }
    }
}
