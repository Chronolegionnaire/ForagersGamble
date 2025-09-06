using System;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CollectibleObject), "GetHeldItemName")]
    public static class Patch_CollectibleObject_GetHeldItemName
    {
        static void Postfix(CollectibleObject __instance, ItemStack itemStack, ref string __result, ICoreAPI ___api)
        {
            var cfg = ModConfig.Instance?.Main;
            if (itemStack == null || ___api?.World == null || cfg == null) return;
            if (itemStack.Block is BlockLiquidContainerTopOpened container) return;
            if (itemStack.Block is BlockPlant flowerBlk)
            {
                var p = flowerBlk.Code?.Path ?? "";
                bool looksLikeFlower = p.StartsWith("flower-", StringComparison.OrdinalIgnoreCase) ||
                                       p.Contains("-flower-", StringComparison.OrdinalIgnoreCase);
                var attrs = flowerBlk.Attributes;
                bool hasNutritionProps = (attrs?["NutritionProps"]?.Exists ?? false) ||
                                         (attrs?["nutritionProps"]?.Exists ?? false);
                if (looksLikeFlower && !hasNutritionProps) return;
            }

            var world = ___api.World;
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (Knowledge.IsKnown(agent, itemStack)) return;
            var idx = PlantKnowledgeIndex.Get(___api);
            if (idx == null) return;
            var thisCode = Knowledge.ItemKey(itemStack);
            if (!string.IsNullOrEmpty(thisCode) && idx.IsMushroom(thisCode) &&
                (cfg.UnknownMushrooms || cfg.UnknownAll == true))
            {
                __result = Lang.Get("foragersgamble:unknown-mushroom");
                return;
            }
            static string Norm(AssetLocation al)
            {
                if (al == null) return null;
                var s = al.ToString();
                var slash = s.IndexOf('/');
                return slash > 0 ? s.Substring(0, slash) : s;
            }
            bool gatePlants = cfg.UnknownPlants || cfg.UnknownAll == true;
            var itemProps = __instance.GetNutritionProperties(world, itemStack, agent);
            if (itemProps != null &&
                itemProps.FoodCategory != EnumFoodCategory.Unknown &&
                itemProps.FoodCategory != EnumFoodCategory.NoNutrition)
            {
                if (gatePlants)
                {
                    if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(___api, itemStack, out var baseProduce) &&
                        baseProduce != null && Knowledge.IsKnown(agent, baseProduce))
                    {
                        return;
                    }

                    switch (itemProps.FoodCategory)
                    {
                        case EnumFoodCategory.Fruit:
                            __result = Lang.Get("foragersgamble:unknown-fruit");
                            return;
                        case EnumFoodCategory.Vegetable:
                            __result = Lang.Get("foragersgamble:unknown-vegetable");
                            return;
                        case EnumFoodCategory.Grain:
                            __result = Lang.Get("foragersgamble:unknown-grain");
                            return;
                    }
                }
            }
            if (gatePlants && (itemProps == null ||
                itemProps.FoodCategory == EnumFoodCategory.Unknown ||
                itemProps.FoodCategory == EnumFoodCategory.NoNutrition))
            {
                if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(___api, itemStack, out var derivedBase) && derivedBase != null)
                {
                    if (!Knowledge.IsKnown(agent, derivedBase))
                    {
                        var dprops = derivedBase.Collectible.GetNutritionProperties(world, derivedBase, agent);
                        if (dprops != null)
                        {
                            switch (dprops.FoodCategory)
                            {
                                case EnumFoodCategory.Fruit: __result = Lang.Get("foragersgamble:unknown-fruit"); return;
                                case EnumFoodCategory.Vegetable: __result = Lang.Get("foragersgamble:unknown-vegetable"); return;
                                case EnumFoodCategory.Grain: __result = Lang.Get("foragersgamble:unknown-grain"); return;
                            }
                        }
                    }
                }
            }
            if (gatePlants)
            {
                var asBlock = itemStack.Block ?? __instance as Block;
                var key = asBlock?.Code != null ? Norm(asBlock.Code) : null;

                if (key != null && idx.IsKnowledgeGated(key))
                {
                    if (idx.TryGetFruit(key, out var fr))
                    {
                        ItemStack fruitRef = null;
                        if (fr.Type == EnumItemClass.Item)
                        {
                            var it = ___api.World.GetItem(fr.Code);
                            if (it != null) fruitRef = new ItemStack(it);
                        }
                        else
                        {
                            var bl = ___api.World.GetBlock(fr.Code);
                            if (bl != null) fruitRef = new ItemStack(bl);
                        }

                        if (fruitRef != null)
                        {
                            var fprops = fruitRef.Collectible.GetNutritionProperties(world, fruitRef, agent);
                            bool isEdible = fprops != null &&
                                            fprops.FoodCategory != EnumFoodCategory.Unknown &&
                                            fprops.FoodCategory != EnumFoodCategory.NoNutrition;
                            if (isEdible)
                            {
                                if (!Knowledge.IsKnown(agent, fruitRef))
                                {
                                    __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(asBlock));
                                    return;
                                }

                                return;
                            }

                            return;
                        }
                        return;
                    }
                    return;
                }
            }
            if (cfg.UnknownAll == true)
            {
                var props = itemProps ?? __instance.GetNutritionProperties(world, itemStack, agent);
                if (props == null) return;

                if (props.FoodCategory == EnumFoodCategory.Unknown ||
                    props.FoodCategory == EnumFoodCategory.NoNutrition)
                {
                    return;
                }

                switch (props.FoodCategory)
                {
                    case EnumFoodCategory.Fruit: __result = Lang.Get("foragersgamble:unknown-fruit"); break;
                    case EnumFoodCategory.Vegetable: __result = Lang.Get("foragersgamble:unknown-vegetable"); break;
                    case EnumFoodCategory.Protein: __result = Lang.Get("foragersgamble:unknown-protein"); break;
                    case EnumFoodCategory.Grain: __result = Lang.Get("foragersgamble:unknown-grain"); break;
                    case EnumFoodCategory.Dairy: __result = Lang.Get("foragersgamble:unknown-dairy"); break;
                    default: __result = Lang.Get("foragersgamble:unknown-food"); break;
                }
            }
        }
    }
}
