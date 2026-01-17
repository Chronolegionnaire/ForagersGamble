// File: Knowledge.cs
using System;
using System.Collections.Generic;
using System.Linq;
using ForagersGamble.Config;
using ForagersGamble.Config.SubConfigs;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ForagersGamble
{
    public static class Knowledge
    {
        private const string AttrRoot = "foragersGamble";
        private const string KnownSet = "knownFoods";
        private const string KnownHealthSet = "knownHealth";
        private const string ProgressTree = "knowledgeProgress";
        private static readonly string[] CookStates = { "partbaked", "perfect", "charred" };
        private static Dictionary<string, string> s_derivativeToBase; // derivative -> base produce code
        private static Dictionary<string, HashSet<string>> s_baseToDerivatives;
        // -----------------------------
        // Unknown naming categories
        // -----------------------------
        public enum UnknownNameCategory
        {
            None = 0,

            // plant-ish
            Mushroom,
            BerryBush,
            Crop,
            PlantGeneric,

            // food-ish
            Fruit,
            Vegetable,
            Grain,
            Protein,
            Dairy,
            FoodGeneric,
            PressedMash
        }

        private static Dictionary<string, UnknownNameCategory> s_codeToUnknownNameCategory;
        private static HashSet<string> s_liquidContainerUniverse;
        private static HashSet<string> s_unknownUniverse;

        // -----------------------------
        // Caches for "smarter" masking
        // -----------------------------
        private static readonly object s_cacheLock = new();

        // item code -> base produce code (or "" if not resolvable)
        private static readonly Dictionary<string, string> s_baseProduceCodeCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] SeedPrefixes = { "seeds-", "seed-", "melonseeds-" };

        public static void ClearNameMaskingCaches()
        {
            lock (s_cacheLock)
            {
                s_baseProduceCodeCache.Clear();
            }
        }

        public static bool TryGetMaskedHeldItemName(
            EntityPlayer agent,
            ICoreAPI api,
            CollectibleObject coll,
            ItemStack stack,
            MainConfig cfg,
            out string langKey)
        {
            langKey = null;
            if (agent == null || api?.World == null || coll == null || stack?.Collectible?.Code == null || cfg == null)
                return false;

            var code = ItemKey(stack);
            if (string.IsNullOrWhiteSpace(code)) return false;

            bool unknownAll = cfg.UnknownAll == true;
            bool unknownPlants = cfg.UnknownPlants == true;
            bool unknownMushrooms = cfg.UnknownMushrooms == true;

            // Special-case seeds: show "unknown-seeds" if parent produce isn't known
            if (unknownAll || unknownPlants)
            {
                var path = stack.Collectible.Code?.Path ?? "";
                if (TryExtractSeedToken(path, out _))
                {
                    // Resolve the base produce for the seed item and gate on that
                    if (!TryResolveBaseProduceCodeCached(api, stack, out var parentCode))
                        return false; // can't resolve -> don't alter display

                    if (!IsKnown(agent, parentCode))
                    {
                        langKey = "foragersgamble:unknown-seeds";
                        return true;
                    }

                    // known parent -> do not mask
                    return false;
                }
            }

            // Only mask things in our unknown universe
            if (!IsInUnknownUniverse(code)) return false;

            // If already known, show real name
            if (IsKnown(agent, code)) return false;

            // Liquid container special-case
            if (IsLiquidContainer(code))
            {
                if (TryResolveUnknownLiquidName(
                        agent,
                        api,
                        coll,
                        stack,
                        unknownAll,
                        unknownPlants,
                        unknownMushrooms,
                        out var liquidKey))
                {
                    langKey = liquidKey;
                    return true;
                }
            }

            // General unknown category-based label
            if (TryResolveUnknownName(code, out var generalKey))
            {
                langKey = generalKey;
                return true;
            }

            // ---------------------------------------------------------
            // NEW: fallback — if the derivative itself has no category
            // (common for pressedmash-* due to custom naming / no nutrition),
            // try to resolve its base produce and use THAT category.
            // ---------------------------------------------------------
            try
            {
                if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce) &&
                    baseProduce?.Collectible?.Code != null)
                {
                    var baseCode = baseProduce.Collectible.Code.ToString();

                    // If base is not known, mask derivative using base's category label
                    // (even if the derivative has no entry in s_codeToUnknownNameCategory)
                    if (!string.IsNullOrWhiteSpace(baseCode) && !IsKnown(agent, baseCode))
                    {
                        if (TryResolveUnknownName(baseCode, out var baseKey))
                        {
                            langKey = baseKey;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore and fall through
            }

            return false;
        }

        public static bool TryExtractSeedToken(string path, out string token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            string pre = null;
            for (int i = 0; i < SeedPrefixes.Length; i++)
            {
                if (path.StartsWith(SeedPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    pre = SeedPrefixes[i];
                    break;
                }
            }
            if (pre == null) return false;

            token = path.Substring(pre.Length).Trim('-', '_', '.');
            if (string.IsNullOrWhiteSpace(token)) return false;

            var segs = token.Split('-');
            if (segs.Length > 1 && PlantKnowledgeUtil.StageWords.Contains(segs[^1]))
                token = string.Join("-", segs, 0, segs.Length - 1);

            token = PlantKnowledgeUtil.NormalizeProduceToken(token);
            return !string.IsNullOrWhiteSpace(token);
        }

        public static bool TryResolveBaseProduceCodeCached(ICoreAPI api, ItemStack stack, out string baseCode)
        {
            baseCode = null;
            if (api?.World == null || stack?.Collectible?.Code == null) return false;

            var selfCode = stack.Collectible.Code.ToString();
            if (string.IsNullOrEmpty(selfCode)) return false;

            lock (s_cacheLock)
            {
                if (s_baseProduceCodeCache.TryGetValue(selfCode, out var cached))
                {
                    baseCode = cached;
                    return !string.IsNullOrEmpty(baseCode);
                }
            }

            string resolved = "";
            try
            {
                if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var bp) &&
                    bp?.Collectible?.Code != null)
                {
                    resolved = bp.Collectible.Code.ToString();
                }
            }
            catch
            {
                resolved = "";
            }

            lock (s_cacheLock)
            {
                s_baseProduceCodeCache[selfCode] = resolved;
            }

            baseCode = resolved;
            return !string.IsNullOrEmpty(baseCode);
        }

        // File: Knowledge.cs (inside BuildUnknownUniverse)
        public static void BuildUnknownUniverse(ICoreAPI api, PlantKnowledgeIndex idx)
        {
            ClearNameMaskingCaches();

            var cfg = ModConfig.Instance?.Main;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // NEW: derivative/base indices
            var derivativeToBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var baseToDerivatives = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void AddDerivEdge(string deriv, string @base)
            {
                if (string.IsNullOrWhiteSpace(deriv) || string.IsNullOrWhiteSpace(@base)) return;

                derivativeToBase[deriv] = @base;

                if (!baseToDerivatives.TryGetValue(@base, out var hs))
                    baseToDerivatives[@base] = hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                hs.Add(deriv);
            }

            bool IsPlantProduceCode(string fullCode)
            {
                if (string.IsNullOrWhiteSpace(fullCode)) return false;
                int colon = fullCode.IndexOf(':');
                var path = colon >= 0 ? fullCode.Substring(colon + 1) : fullCode;
                return path.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("grain-", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("nut-", StringComparison.OrdinalIgnoreCase);
            }

            bool IsEdibleCollectible(CollectibleObject coll, ItemStack st)
            {
                try
                {
                    var p = coll.GetNutritionProperties(api.World, st, null);
                    return p != null
                           && p.FoodCategory != EnumFoodCategory.Unknown
                           && p.FoodCategory != EnumFoodCategory.NoNutrition;
                }
                catch
                {
                    return false;
                }
            }

            // mushrooms (from index)
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

            // knowledge-gated plants + their reference fruits
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

            // clippings + related
            foreach (var coll in api.World.Collectibles)
            {
                if (coll?.Code == null) continue;
                if (!PlantKnowledgeUtil.IsClipping(coll)) continue;

                var selfCode = coll.Code.ToString();
                var selfNorm = Norm(selfCode);
                set.Add(selfCode);
                set.Add(selfNorm);

                if (PlantKnowledgeUtil.TryResolveBushFromClipping(api, coll, out var bush))
                {
                    var bcode = bush?.Code?.ToString();
                    if (!string.IsNullOrEmpty(bcode))
                    {
                        set.Add(bcode);
                        set.Add(Norm(bcode));
                    }

                    if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bush, new ItemStack(bush), out var fruit))
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

            // liquids (existing logic)
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
                        /* ignore */
                    }

                    if (props != null &&
                        props.FoodCategory != EnumFoodCategory.Unknown &&
                        props.FoodCategory != EnumFoodCategory.NoNutrition)
                    {
                        var selfCode = coll.Code.ToString();
                        var selfNorm = Norm(selfCode);
                        set.Add(selfCode);
                        set.Add(selfNorm);

                        try
                        {
                            if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce))
                            {
                                var baseCode = baseProduce?.Collectible?.Code?.ToString();
                                if (!string.IsNullOrEmpty(baseCode))
                                {
                                    var baseNorm = Norm(baseCode);
                                    set.Add(baseCode);
                                    set.Add(baseNorm);

                                    AddDerivEdge(selfCode, baseCode);
                                    AddDerivEdge(selfNorm, baseNorm);
                                    AddDerivEdge(selfCode, baseNorm);
                                    AddDerivEdge(selfNorm, baseCode);
                                }
                            }
                        }
                        catch
                        {
                            /* ignore */
                        }
                    }
                }
            }

            // direct plant produce (existing logic)
            if (cfg?.UnknownPlants == true || cfg?.UnknownAll == true)
            {
                foreach (var coll in api.World.Collectibles)
                {
                    if (coll?.Code == null) continue;

                    var path = coll.Code.Path ?? "";

                    bool isPlantProduce =
                        path.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("grain-", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("nut-", StringComparison.OrdinalIgnoreCase);

                    if (!isPlantProduce) continue;

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
                        /* ignore */
                    }

                    if (props == null ||
                        props.FoodCategory == EnumFoodCategory.Unknown ||
                        props.FoodCategory == EnumFoodCategory.NoNutrition)
                    {
                        continue;
                    }

                    var selfCode = coll.Code.ToString();
                    set.Add(selfCode);
                    set.Add(Norm(selfCode));
                }

                // NEW: include *processed* foods that resolve to plant produce (pickled/pressedmash/etc)
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

                    // CHANGE: allow pressedmash-* even if not edible
                    var path = coll.Code.Path ?? "";
                    bool allowNonEdibleDerivative =
                        path.StartsWith("pressedmash-", StringComparison.OrdinalIgnoreCase);

                    if (!allowNonEdibleDerivative && !IsEdibleCollectible(coll, stack))
                        continue;

                    try
                    {
                        if (!PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce))
                            continue;

                        var baseCode = baseProduce?.Collectible?.Code?.ToString();
                        if (string.IsNullOrEmpty(baseCode)) continue;

                        // Only pull in derivatives of plant produce for UnknownPlants
                        if (!(cfg.UnknownAll == true) && !IsPlantProduceCode(baseCode))
                            continue;

                        var selfCode = coll.Code.ToString();
                        var selfNorm = Norm(selfCode);
                        var baseNorm = Norm(baseCode);

                        set.Add(selfCode);
                        set.Add(selfNorm);
                        set.Add(baseCode);
                        set.Add(baseNorm);

                        AddDerivEdge(selfCode, baseCode);
                        AddDerivEdge(selfNorm, baseNorm);
                        AddDerivEdge(selfCode, baseNorm);
                        AddDerivEdge(selfNorm, baseCode);
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }

            // unknown all foods (existing logic)
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
                        /* ignore */
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

            // build maps
            var nameMap = new Dictionary<string, UnknownNameCategory>(StringComparer.OrdinalIgnoreCase);
            var liquidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var coll in api.World.Collectibles)
            {
                if (coll?.Code == null) continue;
                var fullCode = coll.Code.ToString();
                var normCode = Norm(fullCode);

                if (!set.Contains(fullCode) && !set.Contains(normCode)) continue;

                UnknownNameCategory cat = UnknownNameCategory.None;

                if (idx != null && idx.IsMushroom(fullCode))
                {
                    cat = UnknownNameCategory.Mushroom;
                }
                else
                {
                    if (PlantKnowledgeUtil.IsClipping(coll)) cat = UnknownNameCategory.BerryBush;
                    else if (coll is BlockBerryBush) cat = UnknownNameCategory.BerryBush;
                    else if (coll is BlockCrop) cat = UnknownNameCategory.Crop;
                    else if (coll is BlockPlant) cat = UnknownNameCategory.PlantGeneric;
                }

                ItemStack tmpStack = null;
                try
                {
                    tmpStack = new ItemStack(coll);
                }
                catch
                {
                    tmpStack = null;
                }

                if (tmpStack != null)
                {
                    FoodNutritionProperties props = null;
                    try
                    {
                        props = coll.GetNutritionProperties(api.World, tmpStack, null);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    var fcat = props?.FoodCategory ?? EnumFoodCategory.Unknown;

                    var hasLiquidProps = coll.Attributes?["waterTightContainerProps"]?.Exists == true;
                    if (hasLiquidProps &&
                        fcat != EnumFoodCategory.Unknown &&
                        fcat != EnumFoodCategory.NoNutrition)
                    {
                        liquidSet.Add(fullCode);
                        liquidSet.Add(normCode);
                    }

                    if (cat == UnknownNameCategory.None || cat == UnknownNameCategory.PlantGeneric)
                    {
                        switch (fcat)
                        {
                            case EnumFoodCategory.Fruit: cat = UnknownNameCategory.Fruit; break;
                            case EnumFoodCategory.Vegetable: cat = UnknownNameCategory.Vegetable; break;
                            case EnumFoodCategory.Grain: cat = UnknownNameCategory.Grain; break;
                            case EnumFoodCategory.Protein: cat = UnknownNameCategory.Protein; break;
                            case EnumFoodCategory.Dairy: cat = UnknownNameCategory.Dairy; break;
                            default:
                                if (fcat != EnumFoodCategory.Unknown &&
                                    fcat != EnumFoodCategory.NoNutrition &&
                                    cat == UnknownNameCategory.None)
                                {
                                    cat = UnknownNameCategory.FoodGeneric;
                                }

                                break;
                        }
                    }
                }

                if (cat != UnknownNameCategory.None)
                {
                    nameMap[fullCode] = cat;
                    nameMap[normCode] = cat;
                }
            }

            s_unknownUniverse = set;
            s_codeToUnknownNameCategory = nameMap;
            s_liquidContainerUniverse = liquidSet;

            // NEW: store indices
            s_derivativeToBase = derivativeToBase;
            s_baseToDerivatives = baseToDerivatives;
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

        public static bool IsLiquidContainer(string code)
        {
            return !string.IsNullOrEmpty(code)
                   && s_liquidContainerUniverse != null
                   && s_liquidContainerUniverse.Contains(code);
        }

        public static bool TryResolveUnknownName(string code, out string langKey)
        {
            langKey = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            if (s_codeToUnknownNameCategory == null) return false;

            if (!s_codeToUnknownNameCategory.TryGetValue(code, out var cat) ||
                cat == UnknownNameCategory.None)
            {
                return false;
            }

            switch (cat)
            {
                case UnknownNameCategory.Mushroom: langKey = "foragersgamble:unknown-mushroom"; break;
                case UnknownNameCategory.BerryBush: langKey = "foragersgamble:unknown-berrybush"; break;
                case UnknownNameCategory.Crop: langKey = "foragersgamble:unknown-crop"; break;
                case UnknownNameCategory.PlantGeneric: langKey = "foragersgamble:unknown-plant"; break;
                
                case UnknownNameCategory.Fruit: langKey = "foragersgamble:unknown-fruit"; break;
                case UnknownNameCategory.Vegetable: langKey = "foragersgamble:unknown-vegetable"; break;
                case UnknownNameCategory.Grain: langKey = "foragersgamble:unknown-grain"; break;
                case UnknownNameCategory.Protein: langKey = "foragersgamble:unknown-protein"; break;
                case UnknownNameCategory.Dairy: langKey = "foragersgamble:unknown-dairy"; break;
                case UnknownNameCategory.FoodGeneric: langKey = "foragersgamble:unknown-food"; break;
                case UnknownNameCategory.PressedMash: langKey = "foragersgamble:unknown-mash"; break;
                default: return false;
            }

            return true;
        }

        // -----------------------------
        // Progress / known logic
        // -----------------------------
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
                    if (legacy.value[i] == code)
                        return 1f;
            }

            var prog = root.GetTreeAttribute(ProgressTree);
            return prog?.GetFloat(code, 0f) ?? 0f;
        }

        public static bool AddProgress(EntityAgent entity, string code, float amount)
        {
            if (entity == null || string.IsNullOrEmpty(code) || amount <= 0f) return false;
            var player = (entity as EntityPlayer)?.Player;
            if (player == null) return false;

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var prog = root.GetTreeAttribute(ProgressTree) ?? new TreeAttribute();

            float cur = prog.GetFloat(code, 0f);
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

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var prog = root.GetTreeAttribute(ProgressTree) ?? new TreeAttribute();

            prog.SetFloat(code, 1f);
            root[ProgressTree] = prog;
            var list = new List<string>();
            var cur = root[KnownSet] as StringArrayAttribute;
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

            // NEW: derivative inherits base knowledge
            if (s_derivativeToBase != null && s_derivativeToBase.TryGetValue(code, out var baseCode))
            {
                if (!string.IsNullOrEmpty(baseCode) && GetProgress(entity, baseCode) >= 1f)
                    return true;
            }

            return GetProgress(entity, code) >= 1f;
        }

        public static void MarkKnown(EntityAgent entity, ItemStack stack)
        {
            if (entity == null || stack == null) return;
            var self = ItemKey(stack);
            MarkDiscovered(entity, self);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
            foreach (var code in FamilyCodesFrom(stack))
                if (seen.Add(code))
                    MarkDiscovered(entity, code);
        }

        public static void MarkKnown(EntityAgent entity, string code)
        {
            if (entity == null || string.IsNullOrWhiteSpace(code)) return;

            MarkDiscovered(entity, code);

            // NEW: if this is a base produce, unlock all derivatives too
            if (s_baseToDerivatives != null && s_baseToDerivatives.TryGetValue(code, out var derivs))
            {
                foreach (var d in derivs)
                    MarkDiscovered(entity, d);
            }

            // also do it for normalized base if your universe uses Norm forms
            var norm = Norm(code);
            if (!string.IsNullOrEmpty(norm) && !norm.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                if (s_baseToDerivatives != null && s_baseToDerivatives.TryGetValue(norm, out var derivs2))
                {
                    foreach (var d in derivs2)
                        MarkDiscovered(entity, d);
                }
            }

            if (TrySplitDomainPath(code, out var domain, out var path))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };
                foreach (var c in FamilyCodesFrom(domain, path))
                    if (seen.Add(c))
                        MarkDiscovered(entity, c);
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

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = root[KnownHealthSet] as StringArrayAttribute;
            if (cur?.value != null)
            {
                foreach (var s in cur.value)
                    if (!string.IsNullOrWhiteSpace(s))
                        set.Add(s.Trim());
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

            var wat = player.Entity.WatchedAttributes;
            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();

            root[KnownSet] = new StringArrayAttribute(Array.Empty<string>());
            root[KnownHealthSet] = new StringArrayAttribute(Array.Empty<string>());
            root[ProgressTree] = new TreeAttribute();
            wat.SetAttribute(AttrRoot, root);
            player.Entity.Attributes.MarkPathDirty(AttrRoot);
        }

        private static bool TrySplitDomainPath(string code, out string domain, out string path)
        {
            domain = "game";
            path = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            int i = code.IndexOf(':');
            if (i >= 0)
            {
                domain = code.Substring(0, i);
                path = code.Substring(i + 1);
            }
            else
            {
                path = code;
            }

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

            list.Add($"{variantDomain}:pressedmash-{veg}");

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

            var pathPart = path;

            var colon = path.IndexOf(':');
            if (colon > 0)
            {
                domain = path.Substring(0, colon);
                pathPart = path.Substring(colon + 1);
            }

            var seedPrefixes = new[] { "seeds-", "seed-", "melonseeds-" };
            var matchedPrefix = seedPrefixes.FirstOrDefault(p =>
                pathPart.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (matchedPrefix != null)
            {
                var rest = pathPart.Substring(matchedPrefix.Length);
                rest = rest.Trim('-', '_', '.');

                var segs2 = rest.Split('-');
                if (segs2.Length > 1 && PlantKnowledgeUtil.StageWords.Contains(segs2[^1]))
                {
                    rest = string.Join("-", segs2, 0, segs2.Length - 1);
                }

                if (!string.IsNullOrWhiteSpace(rest))
                {
                    var grains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "spelt", "rye", "rice", "amaranth" };

                    var prefix = grains.Contains(rest) ? "grain-" : "vegetable-";

                    yield return $"{domain}:{prefix}{rest}";
                    yield return $"{domain}:seeds-{rest}";
                }

                yield break;
            }
        }

        private static IEnumerable<string> FamilyCodesFrom(ItemStack stack)
        {
            var al = stack?.Collectible?.Code;
            if (al == null) yield break;

            var domain = al.Domain ?? "game";
            var path = al.Path ?? "";
            foreach (var c in FamilyCodesFrom(domain, path)) yield return c;
        }

        // -----------------------------
        // Liquid masking logic
        // -----------------------------
        public static bool TryResolveUnknownLiquidName(
            EntityPlayer agent,
            ICoreAPI api,
            CollectibleObject coll,
            ItemStack stack,
            bool unknownAll,
            bool unknownPlants,
            bool unknownMushrooms,
            out string langKey)
        {
            langKey = null;
            if (agent == null || api?.World == null || coll == null || stack == null)
                return false;

            if (!(unknownAll || unknownPlants || unknownMushrooms))
                return false;

            var world = api.World;

            FoodNutritionProperties selfProps = null;
            try
            {
                selfProps = coll.GetNutritionProperties(world, stack, agent);
            }
            catch
            {
                // ignore
            }

            bool selfEdible = selfProps != null &&
                              selfProps.FoodCategory != EnumFoodCategory.Unknown &&
                              selfProps.FoodCategory != EnumFoodCategory.NoNutrition;

            if (!selfEdible)
                return false;

            ItemStack baseProduce = null;
            try
            {
                PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out baseProduce);
            }
            catch { /* ignore */ }

            var idx = PlantKnowledgeIndex.Get(api);
            ItemStack edibleCounterpart = null;
            try
            {
                edibleCounterpart = PlantKnowledgeUtil.TryResolveEdibleCounterpart(api, idx, coll, stack, agent);
            }
            catch { /* ignore */ }

            var parent = baseProduce ?? edibleCounterpart;

            if (!IsKnown(agent, stack))
            {
                langKey = "foragersgamble:unknown-liquid";
                return true;
            }

            if (parent != null && !IsKnown(agent, parent))
            {
                langKey = "foragersgamble:unknown-liquid";
                return true;
            }

            return false;
        }
    }
}
