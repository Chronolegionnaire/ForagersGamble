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

        private static bool TryParseFamily(
            AssetLocation code,
            out string familyId,
            out string formKey,
            out string cookState,
            out bool isBase)
        {
            familyId = formKey = cookState = null;
            isBase = false;
            if (code == null || string.IsNullOrWhiteSpace(code.Path)) return false;

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
            static bool IsCookState(string tok) =>
                !string.IsNullOrWhiteSpace(tok) &&
                CookStates.Any(s => s.Equals(tok, StringComparison.OrdinalIgnoreCase));
            if (path.StartsWith("mushroom-", StringComparison.OrdinalIgnoreCase))
            {
                var segs = path.Split('-');
                if (segs.Length >= 2)
                {
                    var name = NormTok(segs[1]);
                    familyId = $"mushroom:{name}";
                    isBase = path.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0 || segs.Length == 2;
                    formKey = "raw";
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
                    cookState = IsCookState(last) ? last : null;
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
                    var last = segs[^1];
                    cookState = IsCookState(last) ? last : null;
                    var name = NormTok(segs[1]);
                    familyId = $"mushroom:{name}";
                    formKey = "chopped";
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
                    cookState = IsCookState(last) ? last : null;
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
                    cookState = IsCookState(last) ? last : null;
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
                    var last = segs[^1];
                    cookState = IsCookState(last) ? last : null;
                    var name = NormTok(segs[1]);
                    familyId = $"vegetable:{name}";
                    formKey = "chopped";
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
                    cookState = IsCookState(last) ? last : null;
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

        bool randomizeHealing = ModConfig.Instance?.Main?.ShuffleHealingItems == true;
        static bool ShouldIgnore(AssetLocation code) =>
            code != null && string.Equals(code.Domain, "hydrateordiedrate", StringComparison.OrdinalIgnoreCase);

        public void RandomizeFoodHealth(ICoreAPI api)
        {
            if (api?.World?.Collectibles == null) return;

            int seed32 = (int)(api.World.Seed & 0x7FFFFFFF);
            var candidatesByFamily = new Dictionary<string, List<Slot>>(StringComparer.OrdinalIgnoreCase);
            var candidatesByGroup = new Dictionary<string, List<Slot>>(StringComparer.OrdinalIgnoreCase);
            var vanillaByFamily = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

            int nonZeroFound = 0;

            static string GroupKeyFor(AssetLocation code)
            {
                if (code == null) return "unknown";
                var path = code.Path ?? "unknown";
                int dash = path.IndexOf('-');
                return dash >= 0 ? path[..dash] : path;
            }

            foreach (var obj in api.World.Collectibles)
            {
                if (obj?.Code == null) continue;
                if (ShouldIgnore(obj.Code)) continue;

                string famId, formKey, cookState;
                bool isBase;
                bool inFamily = TryParseFamily(obj.Code, out famId, out formKey, out cookState, out isBase);

                var group = GroupKeyFor(obj.Code);
                var groupList = GetList(candidatesByGroup, group);
                if (obj.NutritionProps != null)
                {
                    float h = obj.NutritionProps.Health;
                    if (inFamily)
                    {
                        bool includeInPattern = h <= 0f || randomizeHealing;
                        if (includeInPattern)
                        {
                            var vmap = vanillaByFamily.TryGetValue(famId, out var m)
                                ? m
                                : (vanillaByFamily[famId] =
                                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase));

                            vmap[VariantKey(formKey, cookState)] = h;
                            if (isBase) vmap["raw"] = h;
                        }
                    }
                    bool hadNonZeroHealth = Math.Abs(h) > float.Epsilon;
                    bool isHealing = h > 0f;
                    bool eligible = !isHealing || randomizeHealing;
                    if (hadNonZeroHealth && eligible)
                    {
                        obj.NutritionProps.Health = 0f;
                        nonZeroFound++;
                    }
                    if (eligible || !hadNonZeroHealth)
                    {
                        var slot = new Slot
                        {
                            Obj = obj,
                            IsLiquid = false,
                            GroupKey = group,
                            TargetFoodProps = obj.NutritionProps,
                            HadHealthField = hadNonZeroHealth,
                            FamilyId = inFamily ? famId : null,
                            FormKey = inFamily ? formKey : null,
                            CookState = inFamily ? cookState : null,
                            IsBase = inFamily && isBase
                        };

                        groupList.Add(slot);
                        if (inFamily)
                        {
                            var flist = GetList(candidatesByFamily, famId);
                            flist.Add(slot);
                        }
                    }
                }
                var attrObj = obj.Attributes?.Token as JObject;
                var perLitre = attrObj?["waterTightContainerProps"]?["nutritionPropsPerLitre"] as JObject;
                if (perLitre != null)
                {
                    var tok = perLitre["health"];
                    bool hadField =
                        tok != null && (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float);
                    float h = hadField ? (float)tok : 0f;

                    if (inFamily)
                    {
                        bool includeInPattern = h <= 0f || randomizeHealing;
                        if (includeInPattern)
                        {
                            var vmap = vanillaByFamily.TryGetValue(famId, out var m)
                                ? m
                                : (vanillaByFamily[famId] =
                                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase));

                            vmap[VariantKey(formKey, cookState)] = h;
                            if (isBase) vmap["raw"] = h;
                        }
                    }
                    bool isHealing = hadField && h > 0f;
                    bool eligible = !isHealing || randomizeHealing;
                    if (hadField && eligible && Math.Abs(h) > float.Epsilon)
                    {
                        perLitre["health"] = 0f;
                        nonZeroFound++;
                    }

                    var target = obj.NutritionProps ?? new FoodNutritionProperties();
                    if (obj.NutritionProps == null) obj.NutritionProps = target;
                    if (eligible) target.Health = 0f;

                    if (eligible || !hadField)
                    {
                        var slot = new Slot
                        {
                            Obj = obj,
                            IsLiquid = true,
                            GroupKey = group,
                            TargetFoodProps = target,
                            LiquidPerLitreNode = perLitre,
                            HadHealthField = hadField,
                            FamilyId = inFamily ? famId : null,
                            FormKey = inFamily ? formKey : null,
                            CookState = inFamily ? cookState : null,
                            IsBase = inFamily && isBase
                        };

                        groupList.Add(slot);
                        if (inFamily)
                        {
                            var flist = GetList(candidatesByFamily, famId);
                            flist.Add(slot);
                        }
                    }
                }
            }

            int totalCandidates = candidatesByGroup.Values.Sum(l => l.Count);
            if (totalCandidates == 0 || nonZeroFound == 0) return;

            var rngA = new Random(seed32 ^ 0x5F3759DF);

            static string FamilyGroup(string famId)
            {
                if (string.IsNullOrEmpty(famId)) return "unknown";
                int i = famId.IndexOf(':');
                return i > 0 ? famId[..i] : famId;
            }

            var familiesByGroup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (famId, _slots) in candidatesByFamily)
            {
                var g = FamilyGroup(famId);
                if (!familiesByGroup.TryGetValue(g, out var fl))
                    familiesByGroup[g] = fl = new List<string>();
                fl.Add(famId);
            }

            foreach (var (group, famListInGroup) in familiesByGroup)
            {
                var donors = famListInGroup
                    .Where(fid => vanillaByFamily.TryGetValue(fid, out var v) && v != null && v.Count > 0)
                    .ToList();
                if (donors.Count < 2) continue;
                var candidates = famListInGroup
                    .Where(fid =>
                        candidatesByFamily.TryGetValue(fid, out var slots) && slots != null && slots.Count > 0)
                    .ToList();

                if (candidates.Count == 0) continue;

                List<string> participants;

                if (candidates.Count <= donors.Count)
                {
                    participants = new List<string>(donors);
                }
                else
                {
                    participants = new List<string>(candidates);
                    FisherYates(participants, rngA);
                    participants = participants.Take(donors.Count).ToList();
                }
                var donorOrder = new List<string>(donors);
                FisherYates(donorOrder, rngA);
                for (int i = 0; i < donors.Count && i < participants.Count; i++)
                {
                    if (string.Equals(participants[i], donorOrder[i], StringComparison.OrdinalIgnoreCase))
                    {
                        int j = (i + 1) % donors.Count;
                        (donorOrder[i], donorOrder[j]) = (donorOrder[j], donorOrder[i]);
                    }
                }
                for (int i = 0; i < donors.Count && i < participants.Count; i++)
                {
                    var recFamId = participants[i];
                    var donorFamId = donorOrder[i];

                    if (!candidatesByFamily.TryGetValue(recFamId, out var recSlots) ||
                        recSlots == null || recSlots.Count == 0)
                        continue;

                    if (!vanillaByFamily.TryGetValue(donorFamId, out var donorMap) ||
                        donorMap == null || donorMap.Count == 0)
                        continue;

                    foreach (var slot in recSlots)
                    {
                        var vkey = VariantKey(slot.FormKey, slot.CookState);
                        if (string.IsNullOrEmpty(vkey)) continue;

                        if (!donorMap.TryGetValue(vkey, out var donorVal))
                            continue;

                        if (slot.TargetFoodProps != null)
                        {
                            slot.TargetFoodProps.Health = donorVal;
                            if (!ReferenceEquals(slot.Obj.NutritionProps, slot.TargetFoodProps))
                                slot.Obj.NutritionProps = slot.TargetFoodProps;
                        }

                        if (slot.IsLiquid && slot.LiquidPerLitreNode != null)
                            slot.LiquidPerLitreNode["health"] = donorVal;
                    }
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
