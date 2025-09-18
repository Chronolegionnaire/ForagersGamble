using System;
using System.Collections.Generic;
using System.Linq;
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
        static bool IsEdible(FoodNutritionProperties p)
        {
            return p != null &&
                   p.FoodCategory != EnumFoodCategory.Unknown &&
                   p.FoodCategory != EnumFoodCategory.NoNutrition;
        }

        static ItemStack TryResolveEdibleCounterpart(ICoreAPI api, PlantKnowledgeIndex idx, CollectibleObject coll,
            ItemStack stack, EntityPlayer agent)
        {
            if (api?.World == null || coll?.Code == null) return null;
            var keyFull = coll.Code.ToString();
            if (idx != null && idx.TryGetFruit(keyFull, out var fr))
            {
                if (fr.Type == EnumItemClass.Item)
                {
                    var it = api.World.GetItem(fr.Code);
                    if (it != null)
                    {
                        var test = new ItemStack(it);
                        var p = it.GetNutritionProperties(api.World, test, agent);
                        if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                            p.FoodCategory != EnumFoodCategory.NoNutrition)
                            return test;
                    }
                }
                else
                {
                    var bl = api.World.GetBlock(fr.Code);
                    if (bl != null)
                    {
                        var test = new ItemStack(bl);
                        var p = bl.GetNutritionProperties(api.World, test, agent);
                        if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                            p.FoodCategory != EnumFoodCategory.NoNutrition)
                            return test;
                    }
                }
            }

            var path = coll.Code.Path ?? "";
            if (string.IsNullOrWhiteSpace(path)) return null;

            var stageWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ripe", "unripe", "empty", "flowering", "flower", "immature", "mature", "harvested",
                "small", "medium", "large", "stage", "young", "old", "branch", "foliage", "leaves", "leaf", "trunk"
            };

            var colorWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "white", "black", "gray", "grey", "lightgray", "darkgray", "red", "orange", "yellow", "green",
                "blue", "teal", "cyan", "aqua", "purple", "violet", "magenta", "pink", "brown", "beige", "tan"
            };
            var materialWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tile", "claytile", "brick", "plank", "wood", "stone", "granite", "basalt", "limestone", "sandstone",
                "metal", "copper", "tin", "bronze", "iron", "steel", "cloth", "linen", "wool", "glass", "paper"
            };

            var tokens = path.Split('-');
            foreach (var rawTok in tokens)
            {
                var tok = rawTok.Trim();
                if (tok.Length < 3 || stageWords.Contains(tok)) continue;

                if (colorWords.Contains(tok)) continue;
                if (materialWords.Contains(tok)) continue;

                var baseTok = tok.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase)
                    .Trim('-', '_', '.');

                if (baseTok.Any(char.IsDigit)) continue;

                var candidate = new AssetLocation("game", "fruit-" + baseTok);

                var item = api.World.GetItem(candidate);
                if (item != null)
                {
                    var t = new ItemStack(item);
                    var p = item.GetNutritionProperties(api.World, t, agent);
                    if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                        p.FoodCategory != EnumFoodCategory.NoNutrition)
                        return t;
                }

                var block = api.World.GetBlock(candidate);
                if (block != null)
                {
                    var t = new ItemStack(block);
                    var p = block.GetNutritionProperties(api.World, t, agent);
                    if (p != null && p.FoodCategory != EnumFoodCategory.Unknown &&
                        p.FoodCategory != EnumFoodCategory.NoNutrition)
                        return t;
                }
            }

            return null;
        }

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
            if (IsEdible(itemProps))
            {
                if (gatePlants)
                {
                    if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(___api, itemStack, out var baseProduce) &&
                        baseProduce != null)
                    {
                        var baseProps = baseProduce.Collectible.GetNutritionProperties(world, baseProduce, agent);
                        var baseEdible = IsEdible(baseProps);
                        var thisKey = Knowledge.ItemKey(itemStack);
                        var baseKey = Knowledge.ItemKey(baseProduce);
                        var isBaseItem = string.Equals(thisKey, baseKey, StringComparison.Ordinal);

                        if (baseEdible)
                        {
                            if (isBaseItem && !Knowledge.IsKnown(agent, baseProduce))
                            {
                                switch (baseProps.FoodCategory)
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
                                    case EnumFoodCategory.Protein:
                                        __result = Lang.Get("foragersgamble:unknown-protein");
                                        return;
                                    case EnumFoodCategory.Dairy:
                                        __result = Lang.Get("foragersgamble:unknown-dairy");
                                        return;
                                    default:
                                        __result = Lang.Get("foragersgamble:unknown-food");
                                        return;
                                }
                            }
                            return;
                        }
                        else
                        {
                            if (!Knowledge.IsKnown(agent, itemStack))
                            {
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
                                    case EnumFoodCategory.Protein:
                                        __result = Lang.Get("foragersgamble:unknown-protein");
                                        return;
                                    case EnumFoodCategory.Dairy:
                                        __result = Lang.Get("foragersgamble:unknown-dairy");
                                        return;
                                    default:
                                        __result = Lang.Get("foragersgamble:unknown-food");
                                        return;
                                }
                            }
                            return;
                        }
                    }
                    return;
                }
            }

            if (gatePlants && (itemProps == null ||
                               itemProps.FoodCategory == EnumFoodCategory.Unknown ||
                               itemProps.FoodCategory == EnumFoodCategory.NoNutrition))
            {
                var edibleRef = TryResolveEdibleCounterpart(___api, idx, itemStack.Collectible, itemStack, agent);
                if (edibleRef != null)
                {
                    if (!Knowledge.IsKnown(agent, edibleRef))
                    {
                        var asBlock = itemStack.Block ?? __instance as Block;
                        if (asBlock != null)
                        {
                            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(asBlock));
                            return;
                        }
                        var fprops = edibleRef.Collectible.GetNutritionProperties(world, edibleRef, agent);
                        if (fprops != null)
                        {
                            __result = fprops.FoodCategory switch
                            {
                                EnumFoodCategory.Fruit     => Lang.Get("foragersgamble:unknown-fruit"),
                                EnumFoodCategory.Vegetable => Lang.Get("foragersgamble:unknown-vegetable"),
                                EnumFoodCategory.Grain     => Lang.Get("foragersgamble:unknown-grain"),
                                EnumFoodCategory.Protein   => Lang.Get("foragersgamble:unknown-protein"),
                                EnumFoodCategory.Dairy     => Lang.Get("foragersgamble:unknown-dairy"),
                                _                          => Lang.Get("foragersgamble:unknown-food")
                            };
                            return;
                        }
                    }
                }
                if (PlantKnowledgeUtil.TryResolveBaseProduceFromItem(___api, itemStack, out var derivedBase) &&
                    derivedBase != null)
                {
                    if (!Knowledge.IsKnown(agent, derivedBase))
                    {
                        var dprops = derivedBase.Collectible.GetNutritionProperties(world, derivedBase, agent);
                        if (dprops != null)
                        {
                            switch (dprops.FoodCategory)
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
                }
            }

            if (gatePlants)
            {
                var asBlock = itemStack.Block ?? __instance as Block;
                var key = asBlock?.Code != null ? Norm(asBlock.Code) : null;

                if (key != null && idx.IsKnowledgeGated(key))
                {
                    var fruitRef = TryResolveEdibleCounterpart(___api, idx, (CollectibleObject)asBlock ?? __instance, itemStack, agent);
                    if (fruitRef != null)
                    {
                        var fprops = fruitRef.Collectible.GetNutritionProperties(world, fruitRef, agent);
                        if (fprops != null && fprops.FoodCategory != EnumFoodCategory.Unknown && fprops.FoodCategory != EnumFoodCategory.NoNutrition)
                        {
                            if (!Knowledge.IsKnown(agent, fruitRef))
                            {
                                if (asBlock != null)
                                {
                                    __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(asBlock));
                                    return;
                                }
                                __result = Lang.Get("foragersgamble:unknown-plant");
                                return;
                            }
                        }
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
