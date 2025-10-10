using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches;

static class IngredientMasking
{
    public static string MaskedIngredientLabel(ICoreAPI api, EntityPlayer agent, ItemStack stack)
    {
        if (api?.World == null || agent == null || stack?.Collectible == null) return stack?.GetName() ?? "";
        var props = stack.Collectible.GetNutritionProperties(api.World, stack, agent);
        bool edible = props != null &&
                      props.FoodCategory != EnumFoodCategory.Unknown &&
                      props.FoodCategory != EnumFoodCategory.NoNutrition;

        if (!edible) return stack.GetName();
        if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce) && baseProduce != null)
        {
            if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(baseProduce)))
                return CategoryPlaceholder(props.FoodCategory);
        }
        if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(stack)))
            return CategoryPlaceholder(props.FoodCategory);

        return stack.GetName();
    }

    static string CategoryPlaceholder(EnumFoodCategory cat)
    {
        return cat switch
        {
            EnumFoodCategory.Fruit     => Lang.Get("foragersgamble:unknown-fruit"),
            EnumFoodCategory.Vegetable => Lang.Get("foragersgamble:unknown-vegetable"),
            EnumFoodCategory.Grain     => Lang.Get("foragersgamble:unknown-grain"),
            EnumFoodCategory.Protein   => Lang.Get("foragersgamble:unknown-protein"),
            EnumFoodCategory.Dairy     => Lang.Get("foragersgamble:unknown-dairy"),
            _                          => Lang.Get("foragersgamble:unknown-food")
        };
    }
}
