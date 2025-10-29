using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ForagersGamble
{
    public sealed class PlantKnowledgeIndex
    {
        public struct FruitRef
        {
            public AssetLocation Code;
            public EnumItemClass Type;
        }

        private readonly Dictionary<string, FruitRef> plantToFruit = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> noFruit = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> knowledgeGatedBlocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> mushroomCodes = new(StringComparer.OrdinalIgnoreCase);

        private PlantKnowledgeIndex()
        {
        }

        public static PlantKnowledgeIndex Build(ICoreAPI api)
        {
            var idx = new PlantKnowledgeIndex();

            foreach (var bl in api.World.Blocks)
            {
                if (bl?.Code == null) continue;
                var bcode = bl.Code.ToString();

                if (bl is BlockMushroom) idx.mushroomCodes.Add(bcode);
                if (PlantKnowledgeUtil.IsKnowledgeGatedPlant(bl)) idx.knowledgeGatedBlocks.Add(bcode);
            }

            foreach (var bl in api.World.Blocks)
            {
                if (bl?.Code == null) continue;
                var bcode = bl.Code.ToString();
                if (!idx.knowledgeGatedBlocks.Contains(bcode)) continue;

                if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bl, new ItemStack(bl), out var fruit))
                {
                    var code = fruit.Collectible?.Code;
                    if (code != null)
                    {
                        idx.plantToFruit[bcode] = new FruitRef
                        {
                            Code = code,
                            Type = fruit.Collectible is Item ? EnumItemClass.Item : EnumItemClass.Block
                        };
                    }
                    else
                    {
                        idx.noFruit.Add(bcode);
                    }
                }
                else
                {
                    idx.noFruit.Add(bcode);
                }
            }

            return idx;
        }

        public bool IsMushroom(string code) => !string.IsNullOrEmpty(code) && mushroomCodes.Contains(code);

        public bool IsKnowledgeGated(string blockCode) =>
            !string.IsNullOrEmpty(blockCode)
            && knowledgeGatedBlocks.Contains(blockCode)
            && !noFruit.Contains(blockCode);

        public bool TryGetFruit(string blockCode, out FruitRef fruit) =>
            plantToFruit.TryGetValue(blockCode ?? "", out fruit);

        public bool IsNoFruit(string blockCode) => noFruit.Contains(blockCode);

        public static PlantKnowledgeIndex Get(ICoreAPI api) =>
            api.ObjectCache.TryGetValue("ForagersGamble.PlantKnowledgeIndex", out var o)
                ? (PlantKnowledgeIndex)o
                : null;

        public static void Put(ICoreAPI api, PlantKnowledgeIndex idx) =>
            api.ObjectCache["ForagersGamble.PlantKnowledgeIndex"] = idx;
    }
    internal static class PlantKnowledgeUtil
    {
        private static readonly string[] CookStates = { "partbaked", "perfect", "charred" };
        public static bool TryResolveReferenceFruit(ICoreAPI api, CollectibleObject coll, ItemStack heldStack,
            out ItemStack fruitStack)
        {
            fruitStack = null;
            if (api?.World == null || coll == null) return false;

            var block = heldStack?.Block ?? coll as Block;
            if (block == null) return false;

            var attribs = block.Attributes;
            if (block is BlockPlant)
            {
                var p = block.Code?.Path ?? "";
                var looksLikeFlower = p.StartsWith("flower-", StringComparison.OrdinalIgnoreCase) || p.Contains("-flower-", StringComparison.OrdinalIgnoreCase);
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

        public static bool TryResolveBaseProduceFromItem(ICoreAPI api, ItemStack stack, out ItemStack baseProduce)
        {
            baseProduce = null;
            if (api?.World == null || stack?.Collectible == null) return false;
            var codePath = stack.Collectible.Code?.Path ?? "";
            if (TryResolveSeedDerivative(api, codePath, out baseProduce))
                return true;
            var props = stack.Collectible.GetNutritionProperties(api.World, stack, null);
            if (props == null ||
                props.FoodCategory == EnumFoodCategory.Unknown ||
                props.FoodCategory == EnumFoodCategory.NoNutrition)
            {
                return false;
            }
            if (codePath.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase) ||
                codePath.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase) ||
                codePath.StartsWith("grain-", StringComparison.OrdinalIgnoreCase))
            {
                baseProduce = stack;
                return true;
            }
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

            if (TryResolveMushroomDerivative(api, codePath, out baseProduce))
                return true;
            if (TryResolveVegFruitGrainDerivative(api, codePath, out baseProduce))
                return true;

            if (string.IsNullOrEmpty(variantTok))
            {
                var pathLower = codePath.ToLowerInvariant();
                string[] suffixes =
                {
                    "juiceportion", "ciderportion",
                    "wineportion",
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
                        variantTok = codePath.Substring(0, codePath.Length - suf.Length)
                            .Trim('-', '_', '.');
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
                    if (p2 != null && p2.FoodCategory != EnumFoodCategory.Unknown &&
                        p2.FoodCategory != EnumFoodCategory.NoNutrition)
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
                    if (p2 != null && p2.FoodCategory != EnumFoodCategory.Unknown &&
                        p2.FoodCategory != EnumFoodCategory.NoNutrition)
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
            var props = stack.Collectible.GetNutritionProperties(api.World, stack, agent);
            if (props == null) return false;
            return props.FoodCategory != EnumFoodCategory.Unknown &&
                   props.FoodCategory != EnumFoodCategory.NoNutrition;
        }
        private static readonly string[] FoodPrefixes = { "vegetable-", "herb-", "spice-", "grain-", "" };
        private static readonly string[] FoodSuffixes = { "", "-raw", "-fresh", "-leaf", "-leaves", "-sprig", "-sprigs", "-bulb", "-root" };

        private static bool TryFruitViaNutritionProps(ICoreAPI api, JsonObject attribs, out ItemStack stack)
        {
            stack = null;
            if (api?.World == null || attribs == null) return false;

            var tagsNode = attribs["NutritionProps"];
            if (tagsNode == null) return false;
            List<string> tags = new List<string>();
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
            catch
            {
            }

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

            var domain = block.Code.Domain ?? "game";
            var path = block.Code.Path ?? "";
            var stageWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "ripe", "unripe", "empty", "flowering", "flower", "immature", "mature", "harvested", "small", "medium", "large", "stage", "young", "old" };

            var tokens = path.Split('-');
            var candidates = new List<string>();

            foreach (var t in tokens)
            {
                var tok = t.Trim();
                if (tok.Length < 3) continue;
                if (stageWords.Contains(tok)) continue;
                candidates.Add(tok);
            }

            foreach (var tok in candidates)
            {
                var baseTok = tok.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase)
                    .Replace("berry", "", StringComparison.OrdinalIgnoreCase)
                    .Trim('-');

                var shapes = new List<string>();
                shapes.Add(tok);
                shapes.Add(baseTok);
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
                        if (IsFood(api, test)) { stack = test; return true; }
                    }
                    var codeDom = new AssetLocation(block.Code.Domain ?? "game", shape);
                    if (codeDom.Domain != "game")
                    {
                        var item2 = api.World.GetItem(codeDom);
                        if (item2 != null)
                        {
                            var test = new ItemStack(item2);
                            if (IsFood(api, test)) { stack = test; return true; }
                        }
                        var block2 = api.World.GetBlock(codeDom);
                        if (block2 != null)
                        {
                            var test = new ItemStack(block2);
                            if (IsFood(api, test)) { stack = test; return true; }
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
                    {
                        continue;
                    }
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
                    if (IsFood(api, test)) { stack = test; return true; }
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
            catch { /* ignored */ }

            return false;
        }

        private static bool TryMakeStackFromUnknown(ICoreAPI api, object val, out ItemStack stack)
        {
            stack = null;
            if (val == null) return false;

            if (val is AssetLocation al)
            {
                var it = api.World.GetItem(al);
                if (it != null) { var test = new ItemStack(it); if (IsFood(api, test)) { stack = test; return true; } }
                var bl = api.World.GetBlock(al);
                if (bl != null) { var test = new ItemStack(bl); if (IsFood(api, test)) { stack = test; return true; } }
            }

            if (val is string s && !string.IsNullOrWhiteSpace(s))
            {
                var aloc = new AssetLocation(s);
                var it = api.World.GetItem(aloc);
                if (it != null) { var test = new ItemStack(it); if (IsFood(api, test)) { stack = test; return true; } }
                var bl = api.World.GetBlock(aloc);
                if (bl != null) { var test = new ItemStack(bl); if (IsFood(api, test)) { stack = test; return true; } }
            }

            if (val is JsonItemStack jis)
            {
                jis.Resolve(api.World, "ForagersGamble reflection fruit");
                if (jis.ResolvedItemstack != null)
                {
                    var test = jis.ResolvedItemstack.Clone();
                    if (IsFood(api, test)) { stack = test; return true; }
                }
            }

            return false;
        }

        private static bool TryResolveMushroomDerivative(ICoreAPI api, string codePath, out ItemStack baseProduce)
        {
            baseProduce = null;
            if (string.IsNullOrEmpty(codePath)) return false;
            string[] prefixes =
            {
                "cookedmushroom-", "cookedmushroom-",
                "choppedmushroom-", "choppedmushroom-",
                "cookedchoppedmushroom-", "cookedchoppedmushroom-"
            };
            string[] states = { "partbaked", "perfect", "charred" };

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
            var last = segs[segs.Length - 1];
            foreach (var st in states)
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
                    if (p2 != null && p2.FoodCategory != EnumFoodCategory.Unknown &&
                        p2.FoodCategory != EnumFoodCategory.NoNutrition)
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
                    if (p2 != null && p2.FoodCategory != EnumFoodCategory.Unknown &&
                        p2.FoodCategory != EnumFoodCategory.NoNutrition)
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
                        if (last.Equals(st, StringComparison.OrdinalIgnoreCase)) { endExclusive--; break; }

                    var vegJoined = string.Join("-", parts, 1, Math.Max(0, endExclusive - 1));
                    vegJoined = StripLeadingProcessWord(vegJoined);

                    var veg = NormalizeProduceToken(vegJoined);
                    return TryMakeBase(api, "vegetable-", veg, out baseProduce);
                }
                return false;
            }
            if (codePath.StartsWith("dryfruit-", StringComparison.OrdinalIgnoreCase) || codePath.StartsWith("dehydratedfruit-", StringComparison.OrdinalIgnoreCase) || codePath.StartsWith("pressedmash-", StringComparison.OrdinalIgnoreCase) ||
                codePath.StartsWith("candiedfruit-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = codePath.Split('-');
                if (parts.Length >= 2)
                {
                    var fr = parts[1];
                    return TryMakeBase(api, "fruit-", fr, out baseProduce);
                }
                return false;
            }
            string[] fruitLiquidRoots  = { "juice", "cider", "wine", "juiceportion", "vegetablejuiceportion", "ciderportion", "fruitsyrupportion", "yogurt", "wineportion", "foodoilportion", "potentwineportion", "potentspiritportion", "strongspiritportion", "strongwineportion" };
            foreach (var root in fruitLiquidRoots)
            {
                if (codePath.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase))
                {
                    var token = codePath.Substring(root.Length).TrimStart('-').TrimEnd('-', '_', '.');
                    if (!string.IsNullOrWhiteSpace(token))
                        return TryMakeBase(api, "fruit-", token, out baseProduce);
                }
                if (codePath.EndsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    var token = codePath.Substring(0, codePath.Length - root.Length).TrimEnd('-', '_', '.');
                    if (!string.IsNullOrWhiteSpace(token))
                        return TryMakeBase(api, "fruit-", token, out baseProduce);
                }
            }
            string[] grainLeading = { "mash", "wort", "beer" };
            foreach (var lead in grainLeading)
            {
                if (codePath.StartsWith(lead + "-", StringComparison.OrdinalIgnoreCase))
                {
                    var token = codePath.Substring(lead.Length).TrimStart('-').TrimEnd('-', '_', '.');
                    if (!string.IsNullOrWhiteSpace(token))
                        return TryMakeBase(api, "grain-", token, out baseProduce);
                }
            }
            string[] grainProcess = { "dough", "porridge", "flour" };
            foreach (var gp in grainProcess)
            {
                if (codePath.StartsWith(gp + "-", StringComparison.OrdinalIgnoreCase))
                {
                    var token = codePath.Substring(gp.Length).TrimStart('-').TrimEnd('-', '_', '.');
                    if (!string.IsNullOrWhiteSpace(token))
                        return TryMakeBase(api, "grain-", token, out baseProduce);
                }
            }

            return false;
        }

        private static bool TryResolveSeedDerivative(ICoreAPI api, string codePath, out ItemStack baseProduce)
        {
            baseProduce = null;
            if (string.IsNullOrEmpty(codePath)) return false;
            if (!codePath.StartsWith("seeds-", StringComparison.OrdinalIgnoreCase)) return false;
            var type = codePath.Substring("seeds-".Length).Trim('-', '_', '.');
            if (string.IsNullOrWhiteSpace(type)) return false;
            var grains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "spelt", "rye", "rice", "amaranth" };

            var prefix = grains.Contains(type) ? "grain-" : "vegetable-";

            return TryMakeBase(api, prefix, type, out baseProduce);
        }
        private static bool TryMakeBase(ICoreAPI api, string familyPrefix, string token, out ItemStack stack)
        {
            stack = null;
            if (api?.World == null || string.IsNullOrWhiteSpace(familyPrefix) || string.IsNullOrWhiteSpace(token))
                return false;

            var al = new AssetLocation("game", familyPrefix + token);

            var it = api.World.GetItem(al);
            if (it != null)
            {
                var test = new ItemStack(it);
                var p = it.GetNutritionProperties(api.World, test, null);
                if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                    p.FoodCategory != EnumFoodCategory.NoNutrition)
                {
                    stack = test; return true;
                }
            }

            var bl = api.World.GetBlock(al);
            if (bl != null)
            {
                var test = new ItemStack(bl);
                var p = bl.GetNutritionProperties(api.World, test, null);
                if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                    p.FoodCategory != EnumFoodCategory.NoNutrition)
                {
                    stack = test; return true;
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

            var tname = block.GetType().Name;
            if (tname.Contains("BerryBush", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-berrybush";
            if (tname.Contains("FruitingVine", StringComparison.OrdinalIgnoreCase) || tname.Contains("FruitingVines", StringComparison.OrdinalIgnoreCase))
                return "foragersgamble:unknown-berrybush";
            if (tname.Contains("Herb", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-herb";
            if (tname.Contains("Plant", StringComparison.OrdinalIgnoreCase)) return "foragersgamble:unknown-plant";
            if (tname.Contains("Tree", StringComparison.OrdinalIgnoreCase) || tname.Contains("Foliage", StringComparison.OrdinalIgnoreCase) || tname.Contains("Branch", StringComparison.OrdinalIgnoreCase))
                return "foragersgamble:unknown-fruittree";
            return "foragersgamble:unknown-plant";
        }
        public static bool IsKnowledgeGatedPlant(Block block, ICoreAPI api = null)
        {
            if (block == null) return false;

            var tn = block.GetType().Name;

            if (tn.Contains("Coral", StringComparison.OrdinalIgnoreCase)) return false;
            if (tn.Contains("Kelp", StringComparison.OrdinalIgnoreCase)) return false;
            if (tn.Contains("Seaweed", StringComparison.OrdinalIgnoreCase)) return false;

            var path = block.Code?.Path ?? "";
            if ((path.Contains("flower", StringComparison.OrdinalIgnoreCase) ||
                 path.Contains("lily",   StringComparison.OrdinalIgnoreCase)) &&
                (block.Attributes?["NutritionProps"] == null))
            {
                return false;
            }
            if (block is BlockBerryBush || block is BlockPlant || block is BlockCrop ||
                block is BlockFruitTreeBranch || block is BlockFruitTreeFoliage)
            {
                if (api != null &&
                    !PlantKnowledgeUtil.TryResolveReferenceFruit(api, block, new ItemStack(block), out _))
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
                    !PlantKnowledgeUtil.TryResolveReferenceFruit(api, block, new ItemStack(block), out _))
                {
                    return false;
                }

                return true;
            }
            return false;
        }
        private static string NormalizeProduceToken(string token)
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
    }
}
