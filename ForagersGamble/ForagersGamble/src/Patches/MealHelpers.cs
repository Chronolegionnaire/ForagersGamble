using ForagersGamble.Config.SubConfigs;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForagersGamble.Patches;

static class IngredientMasking
{
    public static string GetUnmaskedName(ItemStack stack)
    {
        if (stack == null)
            return "";

        using (NameMaskingScope.Push())
        {
            return stack.GetName();
        }
    }

    public static string MaskedIngredientLabel(
        ICoreAPI api,
        EntityPlayer agent,
        ItemStack stack,
        MainConfig cfg)
    {
        if (api?.World == null ||
            agent == null ||
            stack?.Collectible == null ||
            cfg == null)
        {
            return GetUnmaskedName(stack);
        }

        var idx = FoodKnowledgeIndex.Get(api);
        if (idx == null ||
            !idx.TryGet(stack, out var meta) ||
            !meta.IsEdible)
        {
            return GetUnmaskedName(stack);
        }

        bool gated = cfg.UnknownAll == true;

        if (!gated)
        {
            gated = meta.Group switch
            {
                FoodKnowledgeIndex.KnowledgeGroup.Fruit =>
                    cfg.UnknownFruits || cfg.UnknownPlants,

                FoodKnowledgeIndex.KnowledgeGroup.Vegetable =>
                    cfg.UnknownVegetables || cfg.UnknownPlants,

                FoodKnowledgeIndex.KnowledgeGroup.Grain =>
                    cfg.UnknownGrains || cfg.UnknownPlants,

                FoodKnowledgeIndex.KnowledgeGroup.NutOrLegume =>
                    cfg.UnknownPlants,

                FoodKnowledgeIndex.KnowledgeGroup.Mushroom =>
                    cfg.UnknownMushrooms,

                FoodKnowledgeIndex.KnowledgeGroup.Plant =>
                    cfg.UnknownPlants,

                _ => false
            };
        }

        if (!gated)
            return GetUnmaskedName(stack);
        if (Knowledge.IsKnown(agent, meta.ParentCode))
            return GetUnmaskedName(stack);

        return CategoryPlaceholder(meta.Category);
    }

    private static string CategoryPlaceholder(
        Knowledge.UnknownNameCategory category)
    {
        return category switch
        {
            Knowledge.UnknownNameCategory.Mushroom =>
                Lang.Get("foragersgamble:unknown-mushroom"),

            Knowledge.UnknownNameCategory.BerryBush =>
                Lang.Get("foragersgamble:unknown-berrybush"),

            Knowledge.UnknownNameCategory.Crop =>
                Lang.Get("foragersgamble:unknown-crop"),

            Knowledge.UnknownNameCategory.PlantGeneric =>
                Lang.Get("foragersgamble:unknown-plant"),

            Knowledge.UnknownNameCategory.Fruit =>
                Lang.Get("foragersgamble:unknown-fruit"),

            Knowledge.UnknownNameCategory.Vegetable =>
                Lang.Get("foragersgamble:unknown-vegetable"),

            Knowledge.UnknownNameCategory.Grain =>
                Lang.Get("foragersgamble:unknown-grain"),

            Knowledge.UnknownNameCategory.Protein =>
                Lang.Get("foragersgamble:unknown-protein"),

            Knowledge.UnknownNameCategory.Dairy =>
                Lang.Get("foragersgamble:unknown-dairy"),

            _ =>
                Lang.Get("foragersgamble:unknown-food")
        };
    }
}