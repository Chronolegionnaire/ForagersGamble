using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches;

// Put somewhere shared (e.g. ForagersGamble.Patches namespace)
static class IngredientMasking
{
    public static string MaskedIngredientLabel(ICoreAPI api, EntityPlayer agent, ItemStack stack)
    {
        if (api?.World == null || agent == null || stack?.Collectible == null) return stack?.GetName() ?? "";

        // Is this ingredient edible?
        var props = stack.Collectible.GetNutritionProperties(api.World, stack, agent);
        bool edible = props != null &&
                      props.FoodCategory != EnumFoodCategory.Unknown &&
                      props.FoodCategory != EnumFoodCategory.NoNutrition;

        if (!edible) return stack.GetName(); // non-food, keep vanilla name

        // Map to a base produce and check knowledge
        if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, stack, out var baseProduce) && baseProduce != null)
        {
            if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(baseProduce)))
                return CategoryPlaceholder(props.FoodCategory);
        }

        // If we couldn't resolve base produce, fall back to the ingredient itself
        if (!Knowledge.IsKnown(agent, Knowledge.ItemKey(stack)))
            return CategoryPlaceholder(props.FoodCategory);

        return stack.GetName();
    }

    static string CategoryPlaceholder(EnumFoodCategory cat)
    {
        // Add these to your lang file, or replace with hard-coded English strings
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
