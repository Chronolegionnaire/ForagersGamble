using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ForagersGamble;

public sealed class FoodKnowledgeIndex
{
    public enum KnowledgeGroup
    {
        None = 0,
        Fruit,
        Vegetable,
        Grain,
        NutOrLegume,
        Mushroom,
        Plant
    }

    public readonly struct Entry
    {
        public readonly string ParentCode;
        public readonly Knowledge.UnknownNameCategory Category;
        public readonly KnowledgeGroup Group;
        public readonly bool IsEdible;
        public readonly bool IsLiquid;

        public Entry(
            string parentCode,
            Knowledge.UnknownNameCategory category,
            KnowledgeGroup group,
            bool isEdible,
            bool isLiquid)
        {
            ParentCode = parentCode;
            Category = category;
            Group = group;
            IsEdible = isEdible;
            IsLiquid = isLiquid;
        }
    }

    private readonly Dictionary<CollectibleObject, Entry> byCollectible = new();
    private readonly Dictionary<string, Entry> byCode =
        new(StringComparer.OrdinalIgnoreCase);

    private FoodKnowledgeIndex()
    {
    }

    public bool TryGet(ItemStack stack, out Entry entry)
    {
        if (stack?.Collectible == null)
        {
            entry = default;
            return false;
        }

        return byCollectible.TryGetValue(stack.Collectible, out entry);
    }

    public bool TryGet(CollectibleObject coll, out Entry entry)
    {
        if (coll == null)
        {
            entry = default;
            return false;
        }

        return byCollectible.TryGetValue(coll, out entry);
    }

    public bool TryGet(string code, out Entry entry)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            entry = default;
            return false;
        }

        return byCode.TryGetValue(code, out entry);
    }

    private void Add(CollectibleObject coll, Entry entry)
    {
        byCollectible[coll] = entry;

        var code = coll.Code?.ToString();
        if (!string.IsNullOrWhiteSpace(code))
        {
            byCode[code] = entry;
        }
    }

    public static FoodKnowledgeIndex Build(
        ICoreAPI api,
        PlantKnowledgeIndex plantIndex)
    {
        var idx = new FoodKnowledgeIndex();

        foreach (var coll in api.World.Collectibles)
        {
            if (coll?.Code == null)
                continue;

            ItemStack stack;

            try
            {
                stack = new ItemStack(coll);
            }
            catch
            {
                continue;
            }

            FoodNutritionProperties selfProps = null;

            try
            {
                selfProps = coll.GetNutritionProperties(
                    api.World,
                    stack,
                    null
                );
            }
            catch
            {
            }

            bool selfEdible = IsEdible(selfProps);

            ItemStack parentStack = null;
            if (PlantKnowledgeUtil.IsClipping(coll))
            {
                try
                {
                    if (PlantKnowledgeUtil.TryResolveBushFromClipping(
                            api,
                            coll,
                            out var bush) &&
                        bush != null &&
                        PlantKnowledgeUtil.TryResolveReferenceFruit(
                            api,
                            bush,
                            new ItemStack(bush),
                            out var fruit))
                    {
                        parentStack = fruit;
                    }
                }
                catch
                {
                }
            }
            if (parentStack == null && coll is Block block)
            {
                try
                {
                    if (PlantKnowledgeUtil.IsKnowledgeGatedPlant(block, api) &&
                        PlantKnowledgeUtil.TryResolveReferenceFruit(
                            api,
                            block,
                            stack,
                            out var fruit))
                    {
                        parentStack = fruit;
                    }
                }
                catch
                {
                }
            }
            if (parentStack == null)
            {
                try
                {
                    if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(
                            api,
                            stack,
                            out var baseProduce))
                    {
                        parentStack = baseProduce;
                    }
                }
                catch
                {
                }
            }
            if (parentStack == null && selfEdible)
            {
                parentStack = stack;
            }

            if (parentStack?.Collectible?.Code == null)
                continue;

            var parentCode = parentStack.Collectible.Code.ToString();

            var group = DetermineGroup(
                parentCode,
                plantIndex
            );

            var category = DetermineCategory(
                api,
                coll,
                parentStack,
                selfProps,
                group
            );

            bool isLiquid =
                coll.Attributes?["waterTightContainerProps"]?.Exists == true;

            idx.Add(
                coll,
                new Entry(
                    parentCode,
                    category,
                    group,
                    selfEdible,
                    isLiquid
                )
            );
        }

        return idx;
    }

    private static KnowledgeGroup DetermineGroup(
        string parentCode,
        PlantKnowledgeIndex plantIndex)
    {
        if (string.IsNullOrWhiteSpace(parentCode))
            return KnowledgeGroup.None;

        if (plantIndex != null &&
            plantIndex.IsMushroom(parentCode))
        {
            return KnowledgeGroup.Mushroom;
        }

        int colon = parentCode.IndexOf(':');

        var path = colon >= 0
            ? parentCode.Substring(colon + 1)
            : parentCode;

        if (path.StartsWith(
                "mushroom-",
                StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeGroup.Mushroom;
        }

        if (path.StartsWith(
                "fruit-",
                StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeGroup.Fruit;
        }

        if (path.StartsWith(
                "vegetable-",
                StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeGroup.Vegetable;
        }

        if (path.StartsWith(
                "grain-",
                StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeGroup.Grain;
        }

        if (path.StartsWith(
                "nut-",
                StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                "legume-",
                StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeGroup.NutOrLegume;
        }

        return KnowledgeGroup.None;
    }

    private static Knowledge.UnknownNameCategory DetermineCategory(
        ICoreAPI api,
        CollectibleObject coll,
        ItemStack parentStack,
        FoodNutritionProperties selfProps,
        KnowledgeGroup group)
    {
        if (group == KnowledgeGroup.Mushroom)
        {
            return Knowledge.UnknownNameCategory.Mushroom;
        }

        var path = coll.Code?.Path ?? "";

        if (PlantKnowledgeUtil.IsClipping(coll) ||
            path.StartsWith(
                "fruitingbush-",
                StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                "fruitingbushcutting-",
                StringComparison.OrdinalIgnoreCase) ||
            coll is BlockBerryBush)
        {
            return Knowledge.UnknownNameCategory.BerryBush;
        }

        if (coll is BlockCrop)
            return Knowledge.UnknownNameCategory.Crop;

        if (coll is BlockPlant)
            return Knowledge.UnknownNameCategory.PlantGeneric;

        FoodNutritionProperties props = null;

        try
        {
            if (parentStack?.Collectible != null)
            {
                props = parentStack.Collectible.GetNutritionProperties(
                    api.World,
                    parentStack,
                    null
                );
            }
        }
        catch
        {
        }

        props ??= selfProps;

        return props?.FoodCategory switch
        {
            EnumFoodCategory.Fruit =>
                Knowledge.UnknownNameCategory.Fruit,

            EnumFoodCategory.Vegetable =>
                Knowledge.UnknownNameCategory.Vegetable,

            EnumFoodCategory.Grain =>
                Knowledge.UnknownNameCategory.Grain,

            EnumFoodCategory.Protein =>
                Knowledge.UnknownNameCategory.Protein,

            EnumFoodCategory.Dairy =>
                Knowledge.UnknownNameCategory.Dairy,

            _ =>
                Knowledge.UnknownNameCategory.FoodGeneric
        };
    }

    private static bool IsEdible(FoodNutritionProperties props)
    {
        return props != null &&
               props.FoodCategory != EnumFoodCategory.Unknown &&
               props.FoodCategory != EnumFoodCategory.NoNutrition;
    }

    public static FoodKnowledgeIndex Get(ICoreAPI api)
    {
        if (api?.ObjectCache == null)
            return null;

        return api.ObjectCache.TryGetValue(
            "ForagersGamble.FoodKnowledgeIndex",
            out var obj)
            ? obj as FoodKnowledgeIndex
            : null;
    }

    public static void Put(
        ICoreAPI api,
        FoodKnowledgeIndex idx)
    {
        api.ObjectCache["ForagersGamble.FoodKnowledgeIndex"] = idx;
    }
}