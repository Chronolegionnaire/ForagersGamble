// File: PlantKnowledgeUtil.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ForagersGamble;

public static class PlantKnowledgeUtil
{
    private static readonly string[] CookStates = { "partbaked", "perfect", "charred" };

    internal static readonly HashSet<string> StageWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ripe", "unripe", "empty", "flowering", "flower", "immature", "mature", "harvested",
        "small", "medium", "large", "stage", "young", "old", "branch", "foliage", "leaves", "leaf", "trunk",
        "unstable", "permanent",
        "slow", "fast",
        "rust", "fire", "water", "wind", "earth", "lightning", "frost", "nature", "arcane",
        "free", "snow",
        "land", "normal", "tallplant"
    };

    internal static readonly HashSet<string> ColorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "white", "black", "gray", "grey", "lightgray", "darkgray", "red", "orange", "yellow", "green",
        "blue", "teal", "cyan", "aqua", "purple", "violet", "magenta", "pink", "brown", "beige", "tan"
    };

    internal static readonly HashSet<string> MaterialWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tile", "claytile", "brick", "plank", "wood", "stone", "granite", "basalt", "limestone", "sandstone",
        "metal", "copper", "tin", "bronze", "iron", "steel", "cloth", "linen", "wool", "glass", "paper"
    };

    public static bool TryResolveReferenceFruit(ICoreAPI api, CollectibleObject coll, ItemStack heldStack,
        out ItemStack fruitStack)
    {
        fruitStack = null;
        if (api?.World == null || coll == null) return false;

        var block = heldStack?.Block ?? coll as Block;
        if (block == null) return false;

        var attribs = block.Attributes;
        if (IsClipping(coll) && TryResolveBushFromClipping(api, coll, out var bushFromItem))
        {
            return TryResolveReferenceFruit(api, bushFromItem, new ItemStack(bushFromItem), out fruitStack);
        }

        if (IsClipping(block) && TryResolveBushFromClipping(api, block, out var bushBlk))
        {
            return TryResolveReferenceFruit(api, bushBlk, new ItemStack(bushBlk), out fruitStack);
        }

        if (block is BlockPlant)
        {
            var p = block.Code?.Path ?? "";
            var looksLikeFlower = p.StartsWith("flower-", StringComparison.OrdinalIgnoreCase) ||
                                  p.Contains("-flower-", StringComparison.OrdinalIgnoreCase);
            var hasNutritionProps = attribs?["NutritionProps"] != null;

            if (looksLikeFlower && !hasNutritionProps)
            {
                return false;
            }
        }

        if (TryFruitViaReflection(api, block, out fruitStack)) return true;
        if (TryFruitViaNutritionProps(api, attribs, out fruitStack)) return true;
        if (TryGuessFruitFromCode(api, block, out fruitStack)) return true;
        return false;
    }

    // File: PlantKnowledgeUtil.cs
    public static bool TryResolveBaseProduceFromItem(ICoreAPI api, ItemStack stack, out ItemStack baseProduce)
    {
        baseProduce = null;
        if (api?.World == null || stack?.Collectible == null) return false;

        var preferredDomain = stack.Collectible.Code?.Domain;

        // keep your sapling exclusion
        if (stack.Block is BlockSapling)
            return false;

        var codePath = stack.Collectible.Code?.Path ?? "";

        // 1) seeds first (unchanged)
        if (TryResolveSeedDerivative(api, codePath, out baseProduce, preferredDomain))
            return true;

        // 2) FIX: glued "pickled{family}-" derivatives must resolve to the REAL base produce,
        // not rewrite codePath and then fall into "baseProduce = stack".
        {
            var lower = codePath.ToLowerInvariant();

            (string glued, string baseFamilyPrefix)[] gluedPickles =
            {
                ("pickledvegetable-", "vegetable-"),
                ("pickledfruit-", "fruit-"),
                ("pickledgrain-", "grain-"),
                ("picklednut-", "nut-")
            };

            for (int i = 0; i < gluedPickles.Length; i++)
            {
                var (glued, baseFamilyPrefix) = gluedPickles[i];
                if (lower.StartsWith(glued, StringComparison.OrdinalIgnoreCase))
                {
                    var token = codePath.Substring(glued.Length).Trim('-', '_', '.');
                    token = NormalizeProduceToken(token);

                    if (string.IsNullOrWhiteSpace(token))
                        return false;

                    // Resolve actual base produce stack; try "game" then preferred domain.
                    return TryMakeBase(api, baseFamilyPrefix, token, out baseProduce, preferredDomain);
                }
            }
        }

        // 3) NEW: resolve known derivative prefixes BEFORE nutrition gate
        // This allows non-edible derivatives like pressedmash-* to still resolve to their base produce.
        if (TryResolveMushroomDerivative(api, codePath, out baseProduce))
            return true;

        if (TryResolveVegFruitGrainDerivative(api, codePath, out baseProduce))
            return true;

        // 4) nutrition gate (unchanged)
        FoodNutritionProperties props = null;
        try
        {
            props = stack.Collectible.GetNutritionProperties(api.World, stack, null);
        }
        catch
        {
            props = null;
        }

        if (props == null ||
            props.FoodCategory == EnumFoodCategory.Unknown ||
            props.FoodCategory == EnumFoodCategory.NoNutrition)
        {
            return false;
        }

        // 5) base produce direct items (IMPORTANT: only if the item's REAL codePath starts with these)
        if (codePath.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("grain-", StringComparison.OrdinalIgnoreCase))
        {
            baseProduce = stack;
            return true;
        }

        // 6) existing token extraction logic (unchanged)
        string variantTok = null;
        {
            int dash = codePath.IndexOf('-');
            if (dash >= 0 && dash + 1 < codePath.Length)
            {
                var after = codePath.Substring(dash + 1);
                int dash2 = after.IndexOfAny(new[] { '-', '_', '.' });
                variantTok = (dash2 >= 0 ? after.Substring(0, dash2) : after).Trim();
            }
        }

        if (string.IsNullOrEmpty(variantTok))
        {
            var pathLower = codePath.ToLowerInvariant();
            string[] suffixes =
            {
                "juiceportion", "ciderportion", "wineportion",
                "juice", "cider", "wine",
                "jam", "jelly", "compote", "chutney", "sauce",
                "slices", "slice", "chopped", "diced",
                "puree", "paste", "mash",
                "dried", "candied", "stewed", "baked", "roasted", "fermented", "pickled"
            };

            foreach (var suf in suffixes)
            {
                if (pathLower.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                {
                    variantTok = codePath.Substring(0, codePath.Length - suf.Length).Trim('-', '_', '.');
                    break;
                }
            }

            if (string.IsNullOrEmpty(variantTok))
            {
                string[] leading = { "juiceportion", "ciderportion", "wineportion", "juice", "cider", "wine" };
                foreach (var lead in leading)
                {
                    if (pathLower.StartsWith(lead))
                    {
                        variantTok = codePath.Substring(lead.Length).Trim('-', '_', '.');
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(variantTok)) return false;
        variantTok = variantTok.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase);

        string familyPrefix;
        switch (props.FoodCategory)
        {
            case EnumFoodCategory.Fruit: familyPrefix = "fruit-"; break;
            case EnumFoodCategory.Grain: familyPrefix = "grain-"; break;
            case EnumFoodCategory.Vegetable: familyPrefix = "vegetable-"; break;
            default: return false;
        }

        bool TryResolve(string token, out ItemStack result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(token)) return false;

            var al = new AssetLocation("game", familyPrefix + token);

            var it = api.World.GetItem(al);
            if (it != null)
            {
                var test = new ItemStack(it);
                var p2 = it.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p2))
                {
                    result = test;
                    return true;
                }
            }

            var bl = api.World.GetBlock(al);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                var p2 = bl.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p2))
                {
                    result = test;
                    return true;
                }
            }

            return false;
        }

        if (TryResolve(variantTok, out baseProduce)) return true;
        if (variantTok.EndsWith("berry", StringComparison.OrdinalIgnoreCase))
        {
            var baseTok = variantTok.Substring(0, variantTok.Length - "berry".Length).Trim('-', '_', '.');
            if (baseTok.Length >= 3 && TryResolve(baseTok, out baseProduce)) return true;
        }

        return false;
    }

    private static bool IsFood(ICoreAPI api, ItemStack stack, EntityAgent agent = null)
    {
        if (stack?.Collectible == null || api?.World == null) return false;
        FoodNutritionProperties props = null;
        try { props = stack.Collectible.GetNutritionProperties(api.World, stack, agent); }
        catch { props = null; }
        return IsEdible(props);
    }

    private static readonly string[] FoodPrefixes = { "vegetable-", "herb-", "spice-", "grain-", "" };
    private static readonly string[] FoodSuffixes = { "", "-raw", "-fresh", "-leaf", "-leaves", "-sprig", "-sprigs", "-bulb", "-root" };

    private static bool TryFruitViaNutritionProps(ICoreAPI api, JsonObject attribs, out ItemStack stack)
    {
        stack = null;
        if (api?.World == null || attribs == null) return false;

        var tagsNode = attribs["NutritionProps"];
        if (tagsNode == null) return false;

        List<string> tags = new();
        try
        {
            if (tagsNode.IsArray())
            {
                foreach (var e in tagsNode.AsArray())
                {
                    var t = e.AsString();
                    if (!string.IsNullOrWhiteSpace(t)) tags.Add(t.Trim());
                }
            }
            else
            {
                var t = tagsNode.AsString();
                if (!string.IsNullOrWhiteSpace(t)) tags.Add(t.Trim());
            }
        }
        catch { /* ignore */ }

        if (tags.Count == 0) return false;

        foreach (var raw in tags)
        {
            var tag = raw.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase).Trim();
            foreach (var pre in FoodPrefixes)
            foreach (var suf in FoodSuffixes)
            {
                var code = new AssetLocation("game", pre + tag + suf);
                var item = api.World.GetItem(code);
                if (item != null)
                {
                    var test = new ItemStack(item);
                    if (IsFood(api, test))
                    {
                        stack = test;
                        return true;
                    }
                }

                var block = api.World.GetBlock(code);
                if (block != null)
                {
                    var test = new ItemStack(block);
                    if (IsFood(api, test))
                    {
                        stack = test;
                        return true;
                    }
                }
            }
        }

        foreach (var it in api.World.Items)
        {
            if (it?.Code?.Path == null) continue;
            var p = it.Code.Path;

            foreach (var raw in tags)
            {
                var tag = raw.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase).Trim();
                foreach (var pre in FoodPrefixes)
                {
                    if (p.IndexOf(pre + tag, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var test = new ItemStack(it);
                        if (IsFood(api, test))
                        {
                            stack = test;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool TryGuessFruitFromCode(ICoreAPI api, Block block, out ItemStack stack)
    {
        stack = null;
        if (api?.World == null || block?.Code == null) return false;

        var path = block.Code.Path ?? "";
        var tokens = path.Split('-');
        var candidates = new List<string>();

        foreach (var t in tokens)
        {
            var tok = t.Trim();
            if (tok.Length < 3) continue;
            if (StageWords.Contains(tok)) continue;
            if (ColorWords.Contains(tok)) continue;
            if (MaterialWords.Contains(tok)) continue;
            candidates.Add(tok);
        }

        foreach (var tok in candidates)
        {
            var baseTok = tok
                .Replace("berries", "berry", StringComparison.OrdinalIgnoreCase)
                .Replace("berry", "", StringComparison.OrdinalIgnoreCase)
                .Trim('-');

            var shapes = new List<string> { tok, baseTok };

            foreach (var pre in FoodPrefixes)
            foreach (var suf in FoodSuffixes)
            {
                if (!string.IsNullOrEmpty(pre) || !string.IsNullOrEmpty(suf))
                    shapes.Add(pre + baseTok + suf);
            }

            foreach (var suf in FoodSuffixes)
                shapes.Add(baseTok + "berry" + suf);

            foreach (var shape in shapes)
            {
                var codeGame = new AssetLocation("game", shape);
                var item = api.World.GetItem(codeGame);
                if (item != null)
                {
                    var test = new ItemStack(item);
                    if (IsFood(api, test))
                    {
                        stack = test;
                        return true;
                    }
                }

                var codeDom = new AssetLocation(block.Code.Domain ?? "game", shape);
                if (!codeDom.Domain.Equals("game", StringComparison.OrdinalIgnoreCase))
                {
                    var item2 = api.World.GetItem(codeDom);
                    if (item2 != null)
                    {
                        var test = new ItemStack(item2);
                        if (IsFood(api, test))
                        {
                            stack = test;
                            return true;
                        }
                    }

                    var block2 = api.World.GetBlock(codeDom);
                    if (block2 != null)
                    {
                        var test = new ItemStack(block2);
                        if (IsFood(api, test))
                        {
                            stack = test;
                            return true;
                        }
                    }
                }
            }
        }

        foreach (var it in api.World.Items)
        {
            if (it?.Code?.Path == null) continue;
            var ipath = it.Code.Path;
            var isegs = ipath.Split('-');

            foreach (var tok in candidates)
            {
                if (tok.Length < 3) continue;
                if (tok.Equals("land", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("free", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("tallplant", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool tokenMatch = false;
                for (int i = 0; i < isegs.Length; i++)
                {
                    if (string.Equals(isegs[i], tok, StringComparison.OrdinalIgnoreCase))
                    {
                        tokenMatch = true;
                        break;
                    }
                }

                if (!tokenMatch) continue;

                var test = new ItemStack(it);
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFruitViaReflection(ICoreAPI api, Block block, out ItemStack stack)
    {
        stack = null;
        try
        {
            var t = block.GetType();
            var names = new[] { "fruit", "fruitCode", "berry", "berryCode", "produce", "product", "yieldItem", "harvestItem" };

            foreach (var n in names)
            {
                var prop = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    var val = prop.GetValue(block);
                    if (TryMakeStackFromUnknown(api, val, out stack)) return true;
                }

                var fld = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (fld != null)
                {
                    var val = fld.GetValue(block);
                    if (TryMakeStackFromUnknown(api, val, out stack)) return true;
                }
            }
        }
        catch
        {
            /* ignored */
        }

        return false;
    }

    private static bool TryMakeStackFromUnknown(ICoreAPI api, object val, out ItemStack stack)
    {
        stack = null;
        if (val == null) return false;

        if (val is AssetLocation al)
        {
            var it = api.World.GetItem(al);
            if (it != null)
            {
                var test = new ItemStack(it);
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }

            var bl = api.World.GetBlock(al);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }
        }

        if (val is string s && !string.IsNullOrWhiteSpace(s))
        {
            var aloc = new AssetLocation(s);
            var it = api.World.GetItem(aloc);
            if (it != null)
            {
                var test = new ItemStack(it);
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }

            var bl = api.World.GetBlock(aloc);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }
        }

        if (val is JsonItemStack jis)
        {
            jis.Resolve(api.World, "ForagersGamble reflection fruit");
            if (jis.ResolvedItemstack != null)
            {
                var test = jis.ResolvedItemstack.Clone();
                if (IsFood(api, test))
                {
                    stack = test;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveMushroomDerivative(ICoreAPI api, string codePath, out ItemStack baseProduce)
    {
        baseProduce = null;
        if (string.IsNullOrEmpty(codePath)) return false;

        // de-duplicated prefixes
        string[] prefixes =
        {
            "cookedmushroom-",
            "choppedmushroom-",
            "cookedchoppedmushroom-"
        };

        string matchedPrefix = null;
        foreach (var pre in prefixes)
        {
            if (codePath.StartsWith(pre, StringComparison.OrdinalIgnoreCase))
            {
                matchedPrefix = pre;
                break;
            }
        }

        if (matchedPrefix == null) return false;

        var after = codePath.Substring(matchedPrefix.Length);
        var segs = after.Split('-');
        if (segs.Length == 0) return false;

        int endExclusive = segs.Length;
        var last = segs[^1];
        foreach (var st in CookStates)
        {
            if (last.Equals(st, StringComparison.OrdinalIgnoreCase))
            {
                endExclusive = segs.Length - 1;
                break;
            }
        }

        if (endExclusive <= 0) return false;
        var mush = string.Join("-", segs, 0, endExclusive);
        mush = NormalizeProduceToken(mush);
        if (string.IsNullOrWhiteSpace(mush)) return false;

        var candidates = new[]
        {
            new AssetLocation("game", $"mushroom-{mush}-normal"),
            new AssetLocation("game", $"mushroom-{mush}-normal-north"),
            new AssetLocation("game", $"mushroom-{mush}")
        };

        foreach (var al in candidates)
        {
            var bl = api.World.GetBlock(al);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                var p2 = bl.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p2))
                {
                    baseProduce = test;
                    return true;
                }
            }

            var it = api.World.GetItem(al);
            if (it != null)
            {
                var test = new ItemStack(it);
                var p2 = it.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p2))
                {
                    baseProduce = test;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveVegFruitGrainDerivative(ICoreAPI api, string codePath, out ItemStack baseProduce)
    {
        baseProduce = null;
        if (string.IsNullOrEmpty(codePath)) return false;

        if (codePath.StartsWith("cookedveggie-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("choppedveggie-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("cookedchoppedveggie-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("cookedvegetable-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("choppedvegetable-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("cookedchoppedvegetable-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = codePath.Split('-');
            if (parts.Length >= 2)
            {
                var endExclusive = parts.Length;

                var last = parts[^1];
                foreach (var st in CookStates)
                    if (last.Equals(st, StringComparison.OrdinalIgnoreCase))
                    {
                        endExclusive--;
                        break;
                    }

                var vegJoined = string.Join("-", parts, 1, Math.Max(0, endExclusive - 1));
                vegJoined = StripLeadingProcessWord(vegJoined);

                var veg = NormalizeProduceToken(vegJoined);
                return TryMakeBase(api, "vegetable-", veg, out baseProduce);
            }

            return false;
        }

        // fruit-ish processed
        if (codePath.StartsWith("dryfruit-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("dehydratedfruit-", StringComparison.OrdinalIgnoreCase) ||
            codePath.StartsWith("candiedfruit-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = codePath.Split('-');
            if (parts.Length >= 2)
            {
                var fr = NormalizeProduceToken(parts[1]);
                return TryMakeBase(api, "fruit-", fr, out baseProduce);
            }

            return false;
        }

        // NEW/IMPROVED: pressedmash- can be fruit OR vegetable OR grain and token may contain dashes
        if (codePath.StartsWith("pressedmash-", StringComparison.OrdinalIgnoreCase))
        {
            var after = codePath.Substring("pressedmash-".Length).Trim('-', '_', '.');
            if (string.IsNullOrWhiteSpace(after)) return false;

            // If you later discover pressedmash has known tail markers, strip them here.
            // For now we take whole remainder (supports multi-dash produce like sugar-beet).
            var tok = NormalizeProduceToken(after);

            if (TryMakeBase(api, "vegetable-", tok, out baseProduce)) return true;
            if (TryMakeBase(api, "fruit-", tok, out baseProduce)) return true;
            if (TryMakeBase(api, "grain-", tok, out baseProduce)) return true;

            return false;
        }

        // liquid roots that imply fruit bases (existing behavior, kept)
        string[] fruitLiquidRoots =
        {
            "juice", "cider", "wine", "juiceportion", "vegetablejuiceportion", "ciderportion", "fruitsyrupportion",
            "yogurt", "wineportion", "foodoilportion", "potentwineportion", "potentspiritportion",
            "strongspiritportion", "strongwineportion"
        };
        foreach (var root in fruitLiquidRoots)
        {
            if (codePath.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase))
            {
                var token = codePath.Substring(root.Length).TrimStart('-').TrimEnd('-', '_', '.');
                if (!string.IsNullOrWhiteSpace(token))
                    return TryMakeBase(api, "fruit-", NormalizeProduceToken(token), out baseProduce);
            }

            if (codePath.EndsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var token = codePath.Substring(0, codePath.Length - root.Length).TrimEnd('-', '_', '.');
                if (!string.IsNullOrWhiteSpace(token))
                    return TryMakeBase(api, "fruit-", NormalizeProduceToken(token), out baseProduce);
            }
        }

        // grain leading/process (existing behavior, kept)
        string[] grainLeading = { "mash", "wort", "beer" };
        foreach (var lead in grainLeading)
        {
            if (codePath.StartsWith(lead + "-", StringComparison.OrdinalIgnoreCase))
            {
                var token = codePath.Substring(lead.Length).TrimStart('-').TrimEnd('-', '_', '.');
                if (!string.IsNullOrWhiteSpace(token))
                    return TryMakeBase(api, "grain-", NormalizeProduceToken(token), out baseProduce);
            }
        }

        string[] grainProcess = { "dough", "porridge", "flour" };
        foreach (var gp in grainProcess)
        {
            if (codePath.StartsWith(gp + "-", StringComparison.OrdinalIgnoreCase))
            {
                var token = codePath.Substring(gp.Length).TrimStart('-').TrimEnd('-', '_', '.');
                if (!string.IsNullOrWhiteSpace(token))
                    return TryMakeBase(api, "grain-", NormalizeProduceToken(token), out baseProduce);
            }
        }

        return false;
    }

    private static bool TryResolveSeedDerivative(ICoreAPI api, string codePath, out ItemStack baseProduce, string preferredDomain = null)
    {
        baseProduce = null;
        if (string.IsNullOrEmpty(codePath)) return false;

        string rest;
        if (codePath.StartsWith("seeds-", StringComparison.OrdinalIgnoreCase))
            rest = codePath.Substring("seeds-".Length);
        else if (codePath.StartsWith("seed-", StringComparison.OrdinalIgnoreCase))
            rest = codePath.Substring("seed-".Length);
        else if (codePath.StartsWith("melonseeds-", StringComparison.OrdinalIgnoreCase))
            rest = codePath.Substring("melonseeds-".Length);
        else
            return false;

        rest = rest.Trim('-', '_', '.');
        if (string.IsNullOrWhiteSpace(rest)) return false;

        var segs = rest.Split('-');
        if (segs.Length > 1 && StageWords.Contains(segs[^1]))
            rest = string.Join("-", segs, 0, segs.Length - 1);

        rest = NormalizeProduceToken(rest);
        if (string.IsNullOrWhiteSpace(rest)) return false;

        var candidates = new List<string> { rest };

        if (!rest.EndsWith("berry", StringComparison.OrdinalIgnoreCase))
            candidates.Add(rest + "berry");

        if (rest.EndsWith("berry", StringComparison.OrdinalIgnoreCase))
        {
            var noBerry = rest.Substring(0, rest.Length - "berry".Length).Trim('-', '_', '.');
            if (noBerry.Length >= 3) candidates.Add(noBerry);
        }

        var prefixes = new[] { "fruit-", "vegetable-", "grain-", "nut-" };

        foreach (var tok in candidates)
        foreach (var pre in prefixes)
        {
            if (TryMakeBase(api, pre, tok, out baseProduce, preferredDomain))
                return true;
        }

        return false;
    }

    private static bool TryMakeBase(ICoreAPI api, string familyPrefix, string token, out ItemStack stack, string preferredDomain = null)
    {
        stack = null;
        if (api?.World == null || string.IsNullOrWhiteSpace(familyPrefix) || string.IsNullOrWhiteSpace(token))
            return false;

        var domains = new List<string> { "game" };
        if (!string.IsNullOrWhiteSpace(preferredDomain) &&
            !preferredDomain.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            domains.Add(preferredDomain);
        }

        foreach (var dom in domains)
        {
            var al = new AssetLocation(dom, familyPrefix + token);

            var it = api.World.GetItem(al);
            if (it != null)
            {
                var test = new ItemStack(it);
                var p = it.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p))
                {
                    stack = test;
                    return true;
                }
            }

            var bl = api.World.GetBlock(al);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                var p = bl.GetNutritionProperties(api.World, test, null);
                if (IsEdible(p))
                {
                    stack = test;
                    return true;
                }
            }
        }

        return false;
    }

    public static string ClassifyUnknownKey(Block block)
    {
        if (block is BlockBerryBush) return "foragersgamble:unknown-berrybush";
        if (block is BlockCrop) return "foragersgamble:unknown-crop";
        if (block is BlockPlant) return "foragersgamble:unknown-plant";
        if (block is BlockFruitTreeBranch || block is BlockFruitTreeFoliage) return "foragersgamble:unknown-fruittree";

        var path = block.Code?.Path ?? "";
        var tname = block.GetType().Name;
        if (path.StartsWith("clipping-", StringComparison.OrdinalIgnoreCase) ||
            tname.Contains("Clipping", StringComparison.OrdinalIgnoreCase))
            return "foragersgamble:unknown-berrybush";
        if (tname.Contains("BerryBush", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-berrybush";
        if (tname.Contains("FruitingVine", StringComparison.OrdinalIgnoreCase) ||
            tname.Contains("FruitingVines", StringComparison.OrdinalIgnoreCase))
            return "foragersgamble:unknown-berrybush";
        if (tname.Contains("Herb", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-herb";
        if (tname.Contains("Plant", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-plant";
        if (tname.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
            tname.Contains("Foliage", StringComparison.OrdinalIgnoreCase) ||
            tname.Contains("Branch", StringComparison.OrdinalIgnoreCase))
            return "foragersgamble:unknown-fruittree";
        return "foragersgamble:unknown-plant";
    }

    public static bool IsKnowledgeGatedPlant(Block block, ICoreAPI api = null)
    {
        if (block == null) return false;

        var tn = block.GetType().Name;
        var path = block.Code?.Path ?? "";

        if (tn.Contains("Coral", StringComparison.OrdinalIgnoreCase)) return false;
        if (tn.Contains("Kelp", StringComparison.OrdinalIgnoreCase)) return false;
        if (tn.Contains("Seaweed", StringComparison.OrdinalIgnoreCase)) return false;

        if ((path.Contains("flower", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("lily", StringComparison.OrdinalIgnoreCase)) &&
            (block.Attributes?["NutritionProps"] == null))
        {
            return false;
        }

        bool looksLikeClipping =
            path.StartsWith("clipping-", StringComparison.OrdinalIgnoreCase) ||
            tn.Contains("Clipping", StringComparison.OrdinalIgnoreCase);

        if (looksLikeClipping)
        {
            if (api != null)
            {
                if (TryResolveBushFromClipping(api, block, out _)) return true;
                if (TryResolveReferenceFruit(api, block, new ItemStack(block), out _)) return true;
                return false;
            }

            return true;
        }

        if (block is BlockBerryBush || block is BlockPlant || block is BlockCrop ||
            block is BlockFruitTreeBranch || block is BlockFruitTreeFoliage)
        {
            if (api != null &&
                !TryResolveReferenceFruit(api, block, new ItemStack(block), out _))
            {
                return false;
            }

            return true;
        }

        var n = block.GetType().Name;
        bool specialPlant = n == "BlockFruitingVines"
                            || n == "GroundBerryPlant"
                            || n == "PricklyBerryBush"
                            || n == "HerbariumBerryBush"
                            || n == "HerbPlant"
                            || n == "StoneBerryPlant"
                            || n == "WaterHerb";

        if (specialPlant)
        {
            if (api != null &&
                !TryResolveReferenceFruit(api, block, new ItemStack(block), out _))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public static string NormalizeProduceToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        token = token.Trim('-', '_', '.');
        if (token.Equals("tomatoes", StringComparison.OrdinalIgnoreCase)) return "tomato";
        if (token.Equals("potatoes", StringComparison.OrdinalIgnoreCase)) return "potato";
        token = token.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase);

        if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !token.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(0, token.Length - 1);
        }

        return token;
    }

    private static readonly string[] ProcessLeads =
    {
        "pickled", "candied", "dried", "stewed", "baked", "roasted",
        "fermented", "puree", "pureed", "paste", "mash", "sliced", "slice",
        "chopped", "diced", "cooked", "raw", "fresh"
    };

    private static string StripLeadingProcessWord(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        token = token.Trim('-', '_', '.');
        foreach (var lead in ProcessLeads)
        {
            if (token.StartsWith(lead, StringComparison.OrdinalIgnoreCase))
            {
                var rest = token.Substring(lead.Length);
                return string.IsNullOrWhiteSpace(rest) ? token : rest.Trim('-', '_', '.');
            }
        }

        return token;
    }

    public static bool IsClipping(CollectibleObject coll)
    {
        if (coll?.Code?.Path == null) return false;
        var path = coll.Code.Path;
        var tname = coll.GetType().Name;
        return path.StartsWith("clipping-", StringComparison.OrdinalIgnoreCase)
               || tname.Contains("Clipping", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryResolveBushFromClipping(ICoreAPI api, CollectibleObject clipping, out Block bushBlock)
    {
        bushBlock = null;
        if (api?.World == null || clipping == null || !IsClipping(clipping)) return false;

        var bushCodeStr = clipping.Attributes?["bushCode"]?.AsString(null);
        if (string.IsNullOrWhiteSpace(bushCodeStr)) return false;

        var al = new AssetLocation(bushCodeStr.Trim());
        bushBlock = api.World.GetBlock(al);
        if (bushBlock != null) return true;

        if (string.IsNullOrEmpty(al.Domain) || al.Domain.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            var al2 = new AssetLocation(clipping.Code.Domain, al.Path);
            bushBlock = api.World.GetBlock(al2);
            if (bushBlock != null) return true;
        }

        return false;
    }

    private static bool IsEdible(FoodNutritionProperties p)
    {
        return p != null &&
               p.FoodCategory != EnumFoodCategory.Unknown &&
               p.FoodCategory != EnumFoodCategory.NoNutrition;
    }

    public static ItemStack TryResolveEdibleCounterpart(
        ICoreAPI api,
        PlantKnowledgeIndex idx,
        CollectibleObject coll,
        ItemStack stack,
        EntityPlayer agent)
    {
        if (api?.World == null || coll?.Code == null) return null;

        var keyFull = coll.Code.ToString();

        if (stack?.Block != null && IsClipping(stack.Block))
        {
            if (TryResolveBushFromClipping(api, stack.Block, out var bush))
            {
                var bushKey = bush.Code?.ToString();
                if (!string.IsNullOrEmpty(bushKey) && idx != null && idx.TryGetFruit(bushKey, out var fr2))
                {
                    if (fr2.Type == EnumItemClass.Item)
                    {
                        var it2 = api.World.GetItem(fr2.Code);
                        if (it2 != null)
                        {
                            var t2 = new ItemStack(it2);
                            if (IsEdible(it2.GetNutritionProperties(api.World, t2, agent))) return t2;
                        }
                    }
                    else
                    {
                        var bl2 = api.World.GetBlock(fr2.Code);
                        if (bl2 != null)
                        {
                            var t2 = new ItemStack(bl2);
                            if (IsEdible(bl2.GetNutritionProperties(api.World, t2, agent))) return t2;
                        }
                    }
                }

                if (TryResolveReferenceFruit(api, bush, new ItemStack(bush), out var viaBushFruit))
                {
                    if (IsEdible(viaBushFruit.Collectible.GetNutritionProperties(api.World, viaBushFruit, agent)))
                        return viaBushFruit;
                }
            }
        }

        if (IsClipping(coll) && TryResolveBushFromClipping(api, coll, out var bush2))
        {
            var bushKey2 = bush2.Code?.ToString();
            if (!string.IsNullOrEmpty(bushKey2) && idx != null && idx.TryGetFruit(bushKey2, out var frb))
            {
                var test = frb.Type == EnumItemClass.Item
                    ? new ItemStack(api.World.GetItem(frb.Code))
                    : new ItemStack(api.World.GetBlock(frb.Code));

                if (test?.Collectible != null &&
                    IsEdible(test.Collectible.GetNutritionProperties(api.World, test, agent)))
                {
                    return test;
                }
            }

            if (TryResolveReferenceFruit(api, bush2, new ItemStack(bush2), out var viaBushFruit))
            {
                if (IsEdible(viaBushFruit.Collectible.GetNutritionProperties(api.World, viaBushFruit, agent)))
                    return viaBushFruit;
            }
        }

        if (idx != null && idx.TryGetFruit(keyFull, out var fr))
        {
            if (fr.Type == EnumItemClass.Item)
            {
                var it = api.World.GetItem(fr.Code);
                if (it != null)
                {
                    var test = new ItemStack(it);
                    if (IsEdible(it.GetNutritionProperties(api.World, test, agent))) return test;
                }
            }
            else
            {
                var bl = api.World.GetBlock(fr.Code);
                if (bl != null)
                {
                    var test = new ItemStack(bl);
                    if (IsEdible(bl.GetNutritionProperties(api.World, test, agent))) return test;
                }
            }
        }

        var path = coll.Code.Path ?? "";
        if (string.IsNullOrWhiteSpace(path)) return null;

        var tokens = path.Split('-');
        foreach (var rawTok in tokens)
        {
            var tok = rawTok.Trim();
            if (tok.Length < 3 || StageWords.Contains(tok)) continue;
            if (ColorWords.Contains(tok)) continue;
            if (MaterialWords.Contains(tok)) continue;

            var baseTok = tok.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase).Trim('-', '_', '.');
            if (baseTok.Any(char.IsDigit)) continue;

            IEnumerable<string> DomainsToTry()
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game" };
                var d1 = coll.Code?.Domain;
                if (!string.IsNullOrEmpty(d1)) set.Add(d1);
                var d2 = stack?.Block?.Code?.Domain;
                if (!string.IsNullOrEmpty(d2)) set.Add(d2);
                return set;
            }

            foreach (var dom in DomainsToTry())
            {
                var candidate = new AssetLocation(dom, "fruit-" + baseTok);

                var item = api.World.GetItem(candidate);
                if (item != null)
                {
                    var t = new ItemStack(item);
                    if (IsEdible(item.GetNutritionProperties(api.World, t, agent)))
                        return t;
                }

                var block = api.World.GetBlock(candidate);
                if (block != null)
                {
                    var t = new ItemStack(block);
                    if (IsEdible(block.GetNutritionProperties(api.World, t, agent)))
                        return t;
                }
            }
        }

        return null;
    }
}
