using ForagersGamble.Config.SubConfigs;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches;

static class IngredientMasking
{
    static bool IsEdible(FoodNutritionProperties p) =>
        p != null && p.FoodCategory != EnumFoodCategory.Unknown && p.FoodCategory != EnumFoodCategory.NoNutrition;

    public static string MaskedIngredientLabel(
        ICoreAPI api,
        EntityPlayer agent,
        ItemStack stack,
        MainConfig cfg,
        PlantKnowledgeIndex idx
    )
    {
        if (api?.World == null || agent == null || stack?.Collectible == null || cfg == null)
            return stack?.GetName() ?? "";

        var props = stack.Collectible.GetNutritionProperties(api.World, stack, agent);
        var edible = IsEdible(props);
        if (!edible) return stack.GetName();
        if (cfg.UnknownAll == true)
        {
            if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(stack)))
                return CategoryPlaceholder(props.FoodCategory);

            if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce) && baseProduce != null)
            {
                if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(baseProduce)))
                    return CategoryPlaceholder(props.FoodCategory);
            }
            return stack.GetName();
        }
        bool gatePlants = cfg.UnknownPlants;
        bool gateMushrooms = cfg.UnknownMushrooms;
        if (gatePlants && PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProd) && baseProd != null)
        {
            var baseProps = baseProd.Collectible.GetNutritionProperties(api.World, baseProd, agent);
            if (IsEdible(baseProps) && !Knowledge.IsKnown(agent, Knowledge.ItemKey(baseProd)))
                return CategoryPlaceholder(baseProps.FoodCategory);
        }
        if (gateMushrooms && idx != null)
        {
            var key = Knowledge.ItemKey(stack);
            if (!string.IsNullOrEmpty(key) && idx.IsMushroom(key) && !Knowledge.IsKnown(agent, key))
                return Lang.Get("foragersgamble:unknown-mushroom");
        }
        if (gatePlants && !Knowledge.IsKnown(agent, Knowledge.ItemKey(stack)))
            return CategoryPlaceholder(props.FoodCategory);

        return stack.GetName();
    }

    static string CategoryPlaceholder(EnumFoodCategory cat) => cat switch
    {
        EnumFoodCategory.Fruit     => Lang.Get("foragersgamble:unknown-fruit"),
        EnumFoodCategory.Vegetable => Lang.Get("foragersgamble:unknown-vegetable"),
        EnumFoodCategory.Grain     => Lang.Get("foragersgamble:unknown-grain"),
        EnumFoodCategory.Protein   => Lang.Get("foragersgamble:unknown-protein"),
        EnumFoodCategory.Dairy     => Lang.Get("foragersgamble:unknown-dairy"),
        _                          => Lang.Get("foragersgamble:unknown-food")
    };
}
