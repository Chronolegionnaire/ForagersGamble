using System;
using System.Collections.Generic;
using System.Linq;
using ForagersGamble.Config;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ForagersGamble.Randomize
{
    public class Randomizer
    {
        private sealed class Slot
        {
            public CollectibleObject Obj { get; init; }
            public bool IsLiquid { get; init; }
            public string GroupKey { get; init; }
            public FoodNutritionProperties TargetFoodProps { get; init; }
            public JObject LiquidPerLitreNode { get; init; }
            public bool HadHealthField { get; init; }
            public string FamilyId { get; init; }
            public string FormKey { get; init; }
            public string CookState { get; init; }
            public bool IsBase { get; init; }
        }
        private static readonly string[] CookStates = { "partbaked", "perfect", "charred" };

        private static bool TryParseFamily(AssetLocation code, out string familyId, out string formKey,
            out string cookState, out bool isBase)
        {
            familyId = formKey = cookState = null;
            isBase = false;
            if (code == null || string.IsNullOrWhiteSpace(code.Path)) return false;

            var domain = code.Domain ?? "game";
            var path = code.Path;
            static string NormTok(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return s;
                s = s.Trim('-', '_', '.');
                if (s.Equals("tomatoes", StringComparison.OrdinalIgnoreCase)) return "tomato";
                if (s.Equals("potatoes", StringComparison.OrdinalIgnoreCase)) return "potato";
                s = s.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase);
                if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
                    !s.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(0, s.Length - 1);
                return s;
            }
            if (path.StartsWith("mushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"mushroom:{name}";
                    isBase = path.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0 || segs.Length == 2;
                    formKey = isBase ? "raw" : "raw";
                    cookState = null;
                    return true;
                }
            }
            if (path.StartsWith("cookedmushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 3)
                {
                    var name = NormTok(segs[1]);
                    var last = segs[^1];
                    cookState = CookStates.Any(s => s.Equals(last, StringComparison.OrdinalIgnoreCase)) ? last : null;
                    familyId = $"mushroom:{name}";
                    formKey = "cooked";
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("choppedmushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"mushroom:{name}";
                    formKey = "chopped";
                    cookState = null;
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("cookedchoppedmushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 3)
                {
                    var name = NormTok(segs[1]);
                    var last = segs[^1];
                    cookState = CookStates.Any(s => s.Equals(last, StringComparison.OrdinalIgnoreCase)) ? last : null;
                    familyId = $"mushroom:{name}";
                    formKey = "cookedchopped";
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("vegetable-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"vegetable:{name}";
                    formKey = "raw";
                    isBase = true;
                    cookState = null;
                    return true;
                }
            }

            if (path.StartsWith("cookedveggie-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 3)
                {
                    var name = NormTok(segs[1]);
                    var last = segs[^1];
                    cookState = CookStates.Any(s => s.Equals(last, StringComparison.OrdinalIgnoreCase)) ? last : null;
                    familyId = $"vegetable:{name}";
                    formKey = "cooked";
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("choppedveggie-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"vegetable:{name}";
                    formKey = "chopped";
                    cookState = null;
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("cookedchoppedveggie-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 3)
                {
                    var name = NormTok(segs[1]);
                    var last = segs[^1];
                    cookState = CookStates.Any(s => s.Equals(last, StringComparison.OrdinalIgnoreCase)) ? last : null;
                    familyId = $"vegetable:{name}";
                    formKey = "cookedchopped";
                    isBase = false;
                    return true;
                }
            }
            if (path.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"fruit:{name}";
                    formKey = "raw";
                    isBase = true;
                    cookState = null;
                    return true;
                }
            }
            if (path.StartsWith("dryfruit-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("candiedfruit-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("dehydratedfruit-", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("pressedmash-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    string root = segs[0];
                    string name = NormTok(segs[1]);
                    familyId = $"fruit:{name}";
                    formKey = root switch
                    {
                        "dryfruit" => "dry",
                        "candiedfruit" => "candied",
                        "dehydratedfruit" => "dehydrated",
                        "pressedmash" => "pressedmash",
                        _ => root
                    };
                    cookState = null;
                    isBase = false;
                    return true;
                }
            }

            static string NormalizeFormRoot(string root) => root switch
            {
                "juiceportion" => "juice",
                "vegetablejuiceportion" => "juice",
                "ciderportion" => "cider",
                "wineportion" => "wine",
                "fruitsyrupportion" => "syrup",
                "potentwineportion" => "wine-potent",
                "potentspiritportion" => "spirit-potent",
                "strongspiritportion" => "spirit-strong",
                "strongwineportion" => "wine-strong",
                "foodoilportion" => "oil",
                _ => root
            };
            string[] fruitLiquidRoots =
            {
                "juice", "cider", "wine", "juiceportion", "ciderportion", "wineportion",
                "fruitsyrupportion", "yogurt"
            };
            string[] vegLiquidRoots = { "vegetablejuiceportion" };
            string[] grainLeading = { "mash", "wort", "beer" };
            string[] grainProcess = { "dough", "porridge", "flour" };
            bool LeadingRoot(string[] roots, out string root, out string token)
            {
                root = token = null;
                foreach (var r in roots)
                {
                    var prefix = r + "-";
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        root = r;
                        token = path.Substring(prefix.Length).Trim('-', '_', '.');
                        return !string.IsNullOrWhiteSpace(token);
                    }
                }

                return false;
            }
            bool TrailingRoot(string[] roots, out string root, out string token)
            {
                root = token = null;
                foreach (var r in roots)
                {
                    if (path.EndsWith(r, StringComparison.OrdinalIgnoreCase))
                    {
                        root = r;
                        token = path.Substring(0, path.Length - r.Length).Trim('-', '_', '.');
                        return !string.IsNullOrWhiteSpace(token);
                    }
                }

                return false;
            }
            if (LeadingRoot(fruitLiquidRoots, out var rootF, out var tokF) ||
                TrailingRoot(fruitLiquidRoots, out rootF, out tokF))
            {
                var name = NormTok(tokF);
                familyId = $"fruit:{name}";
                formKey = NormalizeFormRoot(rootF);
                cookState = null;
                isBase = false;
                return true;
            }
            if (LeadingRoot(vegLiquidRoots, out var rootV, out var tokV) ||
                TrailingRoot(vegLiquidRoots, out rootV, out tokV))
            {
                var name = NormTok(tokV);
                familyId = $"vegetable:{name}";
                formKey = NormalizeFormRoot(rootV);
                cookState = null;
                isBase = false;
                return true;
            }
            if (path.StartsWith("grain-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"grain:{name}";
                    formKey = "raw";
                    cookState = null;
                    isBase = true;
                    return true;
                }
            }
            if (LeadingRoot(grainLeading, out var rootG, out var tokG))
            {
                var name = NormTok(tokG);
                familyId = $"grain:{name}";
                formKey = NormalizeFormRoot(rootG);
                cookState = null;
                isBase = false;
                return true;
            }
            if (LeadingRoot(grainProcess, out var rootGp, out var tokGp))
            {
                var name = NormTok(tokGp);
                familyId = $"grain:{name}";
                formKey = NormalizeFormRoot(rootGp);
                cookState = null;
                isBase = false;
                return true;
            }


            return false;
        }

        private static string VariantKey(string formKey, string cookState)
            => string.IsNullOrEmpty(cookState) ? formKey ?? "" : $"{formKey}:{cookState}";

        bool randomizeHealing = ModConfig.Instance?.Main?.RandomizeHealingItems == true;
        static bool ShouldIgnore(AssetLocation code) =>
            code != null && string.Equals(code.Domain, "hydrateordiedrate", StringComparison.OrdinalIgnoreCase);
        public void RandomizeFoodHealth(ICoreAPI api)
        {
            if (api?.World?.Collectibles == null) return;

            int seed32 = (int)(api.World.Seed & 0x7FFFFFFF);
            var candidatesByFamily = new Dictionary<string, List<Slot>>(StringComparer.OrdinalIgnoreCase);

            var candidatesByGroup = new Dictionary<string, List<Slot>>();
            var healthCountsByGroup = new Dictionary<string, Dictionary<float, int>>();
            var vanillaByFamily = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);


            int nonLiquidCount = 0, liquidCount = 0;
            int nonZeroFound = 0;
            static string GroupKeyFor(AssetLocation code)
            {
                if (code == null) return "unknown";
                var path = code.Path ?? "unknown";
                int dash = path.IndexOf('-');
                return dash >= 0 ? path[..dash] : path;
            }

            static void AddToBag(Dictionary<string, Dictionary<float, int>> bagByGroup, string g, float val)
            {
                if (!bagByGroup.TryGetValue(g, out var bag))
                {
                    bag = new Dictionary<float, int>();
                    bagByGroup[g] = bag;
                }
                if (!bag.TryAdd(val, 1)) bag[val]++;
            }

            foreach (var obj in api.World.Collectibles)
            {
                if (obj?.Code == null) continue;
                if (ShouldIgnore(obj.Code)) continue;
                string famId, formKey, cookState;
                bool isBase;
                bool inFamily = TryParseFamily(obj.Code, out famId, out formKey, out cookState, out isBase);

                var group = GroupKeyFor(obj.Code);
                var list = GetList(candidatesByGroup, group);
                if (obj.NutritionProps != null)
                {
                    float h = obj.NutritionProps.Health;
                    bool hadHealth = Math.Abs(h) > float.Epsilon;
                    bool isHealing = h > 0f;
                    bool eligible = !isHealing || randomizeHealing;

                    if (hadHealth && eligible)
                    {
                        AddToBag(healthCountsByGroup, group, h);
                        if (inFamily)
                        {
                            var vmap = vanillaByFamily.TryGetValue(famId, out var m)
                                ? m
                                : (vanillaByFamily[famId] = new());
                            vmap[VariantKey(formKey, cookState)] = h;
                            if (isBase)
                                vmap["raw"] = h;
                        }

                        obj.NutritionProps.Health = 0f;
                        nonZeroFound++;
                    }

                    if (eligible || !hadHealth)
                    {
                        var slot = new Slot
                        {
                            Obj = obj,
                            IsLiquid = false,
                            GroupKey = group,
                            TargetFoodProps = obj.NutritionProps,
                            HadHealthField = hadHealth,
                            FamilyId = inFamily ? famId : null,
                            FormKey = inFamily ? formKey : null,
                            CookState = inFamily ? cookState : null,
                            IsBase = inFamily && isBase
                        };

                        list.Add(slot);
                        if (inFamily)
                        {
                            var flist = GetList(candidatesByFamily, famId);
                            flist.Add(slot);
                        }

                        nonLiquidCount++;
                    }
                }
                var attrObj = obj.Attributes?.Token as JObject;
                var perLitre = attrObj?["waterTightContainerProps"]?["nutritionPropsPerLitre"] as JObject;
                if (perLitre != null)
                {
                    var tok = perLitre["health"];
                    bool hadHealth = tok != null && (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float);
                    float h = hadHealth ? (float)tok : 0f;
                    bool isHealing = hadHealth && h > 0f;
                    bool eligible = !isHealing || randomizeHealing;

                    if (hadHealth && eligible)
                    {
                        if (Math.Abs(h) > float.Epsilon)
                        {
                            AddToBag(healthCountsByGroup, group, h);

                            if (inFamily)
                            {
                                var vmap = vanillaByFamily.TryGetValue(famId, out var m)
                                    ? m
                                    : (vanillaByFamily[famId] = new());
                                vmap[VariantKey(formKey, cookState)] = h;
                                if (isBase) vmap["raw"] = h;
                            }

                            nonZeroFound++;
                        }

                        perLitre["health"] = 0f;
                    }

                    var target = obj.NutritionProps ?? new FoodNutritionProperties();
                    if (obj.NutritionProps == null) obj.NutritionProps = target;
                    if (eligible) target.Health = 0f;

                    if (eligible || !hadHealth)
                    {
                        var slot = new Slot
                        {
                            Obj = obj,
                            IsLiquid = true,
                            GroupKey = group,
                            TargetFoodProps = target,
                            LiquidPerLitreNode = perLitre,
                            HadHealthField = hadHealth,
                            FamilyId = inFamily ? famId : null,
                            FormKey = inFamily ? formKey : null,
                            CookState = inFamily ? cookState : null,
                            IsBase = inFamily && isBase
                        };

                        list.Add(slot);
                        if (inFamily)
                        {
                            var flist = GetList(candidatesByFamily, famId);
                            flist.Add(slot);
                        }

                        liquidCount++;
                    }
                }
            }
            
            int totalCandidates = candidatesByGroup.Values.Sum(l => l.Count);
            int totalDistinctGroups = candidatesByGroup.Count;

            if (totalCandidates == 0 || nonZeroFound == 0) return;

            var rngA = new Random(seed32 ^ 0x5F3759DF);
            var rngB = new Random(seed32 ^ unchecked((int)0x9E3779B9));
            int assignedTotal = 0, assignedLiquids = 0, assignedNonLiquids = 0;

            var familiesByGroup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var familyGroupKey  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (famId, slots) in candidatesByFamily)
            {
                var g = slots.FirstOrDefault()?.GroupKey ?? "unknown";
                if (!familiesByGroup.TryGetValue(g, out var fl))
                {
                    fl = new List<string>();
                    familiesByGroup[g] = fl;
                }
                if (!fl.Contains(famId)) fl.Add(famId);
                familyGroupKey[famId] = g;
            }

            foreach (var (group, recFamList) in familiesByGroup)
            {
                var donorFamList = vanillaByFamily.Keys
                    .Where(fid =>
                        familyGroupKey.TryGetValue(fid, out var g) &&
                        string.Equals(g, group, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (donorFamList.Count == 0) continue;
                FisherYates(donorFamList, rngA);
                for (int i = 0; i < recFamList.Count; i++)
                {
                    var recFamId = recFamList[i];
                    if (!candidatesByFamily.TryGetValue(recFamId, out var slots) || slots == null || slots.Count == 0)
                        continue;

                    var donorFamId = donorFamList[i % donorFamList.Count];
                    if (!vanillaByFamily.TryGetValue(donorFamId, out var donorMap) || donorMap == null ||
                        donorMap.Count == 0)
                        continue;

                    int gAssigned = 0;

                    foreach (var slot in slots)
                    {
                        var vkey = VariantKey(slot.FormKey, slot.CookState);

                        if (!string.IsNullOrEmpty(vkey) && donorMap.TryGetValue(vkey, out var donorVal))
                        {
                            if (slot.TargetFoodProps != null)
                            {
                                slot.TargetFoodProps.Health = donorVal;
                                if (!ReferenceEquals(slot.Obj.NutritionProps, slot.TargetFoodProps))
                                    slot.Obj.NutritionProps = slot.TargetFoodProps;
                            }

                            if (slot.IsLiquid && slot.LiquidPerLitreNode != null)
                                slot.LiquidPerLitreNode["health"] = donorVal;

                            gAssigned++;
                            if (slot.IsLiquid) assignedLiquids++;
                            else assignedNonLiquids++;
                        }
                    }

                    assignedTotal += gAssigned;
                }
            }

            foreach (var (group, slots) in candidatesByGroup)
            {
                var leftovers = slots.Where(s => string.IsNullOrEmpty(s.FamilyId)).ToList();
                if (leftovers.Count == 0) continue;

                if (!healthCountsByGroup.TryGetValue(group, out var countsForGroup) ||
                    countsForGroup.Count == 0) continue;

                var valueBag = new List<float>(countsForGroup.Sum(kv => kv.Value));
                foreach (var (v, c) in countsForGroup)
                    for (int i = 0; i < c; i++)
                        valueBag.Add(v);
                FisherYates(valueBag, rngB);

                int n = Math.Min(leftovers.Count, valueBag.Count);
                for (int i = 0; i < n; i++)
                {
                    var slot = leftovers[i];
                    var val = valueBag[i];

                    if (slot.TargetFoodProps != null)
                    {
                        slot.TargetFoodProps.Health = val;
                        if (!ReferenceEquals(slot.Obj.NutritionProps, slot.TargetFoodProps))
                            slot.Obj.NutritionProps = slot.TargetFoodProps;
                    }

                    if (slot.IsLiquid && slot.LiquidPerLitreNode != null)
                        slot.LiquidPerLitreNode["health"] = val;

                    assignedTotal++;
                    if (slot.IsLiquid) assignedLiquids++;
                    else assignedNonLiquids++;
                }
            }
        }

        private static List<T> GetList<K, T>(Dictionary<K, List<T>> dict, K key)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<T>();
                dict[key] = list;
            }
            return list;
        }

        private static void FisherYates<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
