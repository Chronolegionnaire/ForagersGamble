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
        }
        bool randomizeHealing = ModConfig.Instance?.Main?.RandomizeHealingItems == true;
        static bool ShouldIgnore(AssetLocation code) =>
            code != null && string.Equals(code.Domain, "hydrateordiedrate", StringComparison.OrdinalIgnoreCase);
        public void RandomizeFoodHealth(ICoreAPI api)
        {
            if (api?.World?.Collectibles == null) return;

            int seed32 = (int)(api.World.Seed & 0x7FFFFFFF);
            var candidatesByGroup = new Dictionary<string, List<Slot>>();
            var healthCountsByGroup = new Dictionary<string, Dictionary<float, int>>();

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
                        nonZeroFound++;
                        obj.NutritionProps.Health = 0f;
                    }
                    if (eligible || !hadHealth)
                    {
                        list.Add(new Slot
                        {
                            Obj = obj,
                            IsLiquid = false,
                            GroupKey = group,
                            TargetFoodProps = obj.NutritionProps,
                            HadHealthField = hadHealth
                        });
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
                            nonZeroFound++;
                        }
                        perLitre["health"] = 0f;
                    }
                    var target = obj.NutritionProps ?? new FoodNutritionProperties();
                    if (obj.NutritionProps == null)
                    {
                        obj.NutritionProps = target;
                    }
                    if (eligible)
                    {
                        target.Health = 0f;
                    }
                    if (eligible || !hadHealth)
                    {
                        list.Add(new Slot
                        {
                            Obj = obj,
                            IsLiquid = true,
                            GroupKey = group,
                            TargetFoodProps = target,
                            LiquidPerLitreNode = perLitre,
                            HadHealthField = hadHealth
                        });
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

            foreach (var (group, slots) in candidatesByGroup)
            {
                if (!healthCountsByGroup.TryGetValue(group, out var countsForGroup) || countsForGroup.Count == 0)
                    continue;
                var valueBag = new List<float>(countsForGroup.Sum(kv => kv.Value));
                foreach (var (v, c) in countsForGroup)
                    for (int i = 0; i < c; i++) valueBag.Add(v);

                if (valueBag.Count == 0) continue;
                var liquids = slots.Where(s => s.IsLiquid).ToList();
                var nonLiquids = slots.Where(s => !s.IsLiquid).ToList();
                FisherYates(liquids, rngA);
                FisherYates(nonLiquids, rngA);

                var interleaved = new List<Slot>(slots.Count);
                int li = 0, ni = 0;
                while (li < liquids.Count || ni < nonLiquids.Count)
                {
                    if (ni < nonLiquids.Count) interleaved.Add(nonLiquids[ni++]);
                    if (li < liquids.Count) interleaved.Add(liquids[li++]);
                }
                FisherYates(valueBag, rngB);

                int n = Math.Min(interleaved.Count, valueBag.Count);
                int gAssigned = 0, gLiquids = 0, gNonLiquids = 0;

                for (int i = 0; i < n; i++)
                {
                    var slot = interleaved[i];
                    float val = valueBag[i];

                    if (slot.TargetFoodProps != null)
                    {
                        slot.TargetFoodProps.Health = val;
                        if (!ReferenceEquals(slot.Obj.NutritionProps, slot.TargetFoodProps))
                            slot.Obj.NutritionProps = slot.TargetFoodProps;
                    }

                    if (slot.IsLiquid && slot.LiquidPerLitreNode != null)
                    {
                        slot.LiquidPerLitreNode["health"] = val;
                    }

                    gAssigned++;
                    if (slot.IsLiquid) gLiquids++; else gNonLiquids++;
                }
                assignedTotal += gAssigned;
                assignedLiquids += gLiquids;
                assignedNonLiquids += gNonLiquids;
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
