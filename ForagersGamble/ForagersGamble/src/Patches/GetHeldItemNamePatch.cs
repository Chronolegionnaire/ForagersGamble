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
    internal static class NameMaskingScope
    {
        [ThreadStatic] private static int _depth;
        public static bool IsActive => _depth > 0;

        public static void Enter() { _depth++; }
        public static void Exit()  { if (_depth > 0) _depth--; }
        public static IDisposable Push() => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() => Exit(); public Scope() { Enter(); } }
    }

    [HarmonyPatch(typeof(CollectibleObject), "GetHeldItemName")]
    public static class Patch_CollectibleObject_GetHeldItemName
    {
        static bool IsEdible(FoodNutritionProperties p)
        {
            return p != null &&
                   p.FoodCategory != EnumFoodCategory.Unknown &&
                   p.FoodCategory != EnumFoodCategory.NoNutrition;
        }
        public static ItemStack TryResolveEdibleCounterpart(
            ICoreAPI api,
            PlantKnowledgeIndex idx,
            CollectibleObject coll,
            ItemStack stack,
            EntityPlayer agent)
        {
            if (api?.World == null || coll?.Code == null) return null;
            var keyFull = coll.Code.ToString();
            if (stack?.Block != null && PlantKnowledgeUtil.IsClipping(stack.Block))
            {
                if (PlantKnowledgeUtil.TryResolveBushFromClipping(api, stack.Block, out var bush))
                {
                    var bushKey = bush.Code?.ToString();
                    if (!string.IsNullOrEmpty(bushKey) && idx != null && idx.TryGetFruit(bushKey, out var fr2))
                    {
                        if (fr2.Type == EnumItemClass.Item)
                        {
                            var it2 = api.World.GetItem(fr2.Code);
                            if (it2 != null)
                            {
                                var t2 = new ItemStack(it2);
                                var p2 = it2.GetNutritionProperties(api.World, t2, agent);
                                if (IsEdible(p2)) return t2;
                            }
                        }
                        else
                        {
                            var bl2 = api.World.GetBlock(fr2.Code);
                            if (bl2 != null)
                            {
                                var t2 = new ItemStack(bl2);
                                var p2 = bl2.GetNutritionProperties(api.World, t2, agent);
                                if (IsEdible(p2)) return t2;
                            }
                        }
                    }
                    if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bush, new ItemStack(bush), out var viaBushFruit))
                    {
                        var p3 = viaBushFruit.Collectible.GetNutritionProperties(api.World, viaBushFruit, agent);
                        if (IsEdible(p3)) return viaBushFruit;
                    }
                }
            }
            if (PlantKnowledgeUtil.IsClipping(coll) && PlantKnowledgeUtil.TryResolveBushFromClipping(api, coll, out var bush2))
            {
                var bushKey2 = bush2.Code?.ToString();
                if (!string.IsNullOrEmpty(bushKey2) && idx != null && idx.TryGetFruit(bushKey2, out var frb))
                {
                    var test = frb.Type == EnumItemClass.Item
                        ? new ItemStack(api.World.GetItem(frb.Code))
                        : new ItemStack(api.World.GetBlock(frb.Code));
                    if (test?.Collectible != null &&
                        IsEdible(test.Collectible.GetNutritionProperties(api.World, test, agent))) return test;
                }
                if (PlantKnowledgeUtil.TryResolveReferenceFruit(api, bush2, new ItemStack(bush2), out var viaBushFruit))
                {
                    if (IsEdible(viaBushFruit.Collectible.GetNutritionProperties(api.World, viaBushFruit, agent)))
                        return viaBushFruit;
                }
            }
            if (idx != null && idx.TryGetFruit(keyFull, out var fr))
            {
                if (fr.Type == EnumItemClass.Item)
                {
                    var it = api.World.GetItem(fr.Code);
                    if (it != null)
                    {
                        var test = new ItemStack(it);
                        var p = it.GetNutritionProperties(api.World, test, agent);
                        if (IsEdible(p)) return test;
                    }
                }
                else
                {
                    var bl = api.World.GetBlock(fr.Code);
                    if (bl != null)
                    {
                        var test = new ItemStack(bl);
                        var p = bl.GetNutritionProperties(api.World, test, agent);
                        if (IsEdible(p)) return test;
                    }
                }
            }
            var path = coll.Code.Path ?? "";
            if (string.IsNullOrWhiteSpace(path)) return null;
            var tokens = path.Split('-');
            foreach (var rawTok in tokens)
            {
                var tok = rawTok.Trim();
                if (tok.Length < 3 || PlantKnowledgeUtil.StageWords.Contains(tok)) continue;
                if (PlantKnowledgeUtil.ColorWords.Contains(tok)) continue;
                if (PlantKnowledgeUtil.MaterialWords.Contains(tok)) continue;

                var baseTok = tok.Replace("berries", "berry", StringComparison.OrdinalIgnoreCase)
                                 .Trim('-', '_', '.');

                if (baseTok.Any(char.IsDigit)) continue;

                IEnumerable<string> DomainsToTry()
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add("game");
                    var d1 = coll.Code?.Domain;
                    if (!string.IsNullOrEmpty(d1)) set.Add(d1);
                    var d2 = stack?.Block?.Code?.Domain;
                    if (!string.IsNullOrEmpty(d2)) set.Add(d2);
                    return set;
                }

                foreach (var dom in DomainsToTry())
                {
                    var candidate = new AssetLocation(dom, "fruit-" + baseTok);

                    var item = api.World.GetItem(candidate);
                    if (item != null)
                    {
                        var t = new ItemStack(item);
                        if (IsEdible(item.GetNutritionProperties(api.World, t, agent)))
                            return t;
                    }

                    var block = api.World.GetBlock(candidate);
                    if (block != null)
                    {
                        var t = new ItemStack(block);
                        if (IsEdible(block.GetNutritionProperties(api.World, t, agent)))
                            return t;
                    }
                }
            }

            return null;
        }

        static void Postfix(CollectibleObject __instance, ItemStack itemStack, ref string __result, ICoreAPI ___api)
        {
            if (NameMaskingScope.IsActive) return;

            var cfg = ModConfig.Instance?.Main;
            if (itemStack == null || ___api?.World == null || cfg == null) return;

            var world = ___api.World;
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (Knowledge.IsKnown(agent, itemStack)) return;

            var idx = PlantKnowledgeIndex.Get(___api);
            if (idx == null) return;
            PlantKnowledgeUtil.TryResolveBaseProduceFromItem(___api, itemStack, out var baseProduce);

            var attrsLiquid = __instance?.Attributes;
            var hasLiquidPropsAttr = attrsLiquid?["waterTightContainerProps"]?.Exists == true;
            bool hasLiquidPropsHelper = false;
            try { hasLiquidPropsHelper = BlockLiquidContainerBase.GetContainableProps(itemStack) != null; } catch { }
            if ((hasLiquidPropsAttr || hasLiquidPropsHelper)
                && (cfg.UnknownAll == true || cfg.UnknownPlants || cfg.UnknownMushrooms))
            {
                var selfProps = __instance.GetNutritionProperties(world, itemStack, agent);
                bool selfEdible = IsEdible(selfProps);
                ItemStack parent = baseProduce ??
                                   TryResolveEdibleCounterpart(___api, idx, __instance, itemStack, agent);
                if (selfEdible && !Knowledge.IsKnown(agent, itemStack))
                {
                    __result = Lang.Get("foragersgamble:unknown-liquid");
                    return;
                }
                if (parent != null && !Knowledge.IsKnown(agent, parent))
                {
                    __result = Lang.Get("foragersgamble:unknown-liquid");
                    return;
                }
            }
            if (baseProduce != null)
            {
                var baseKey = Knowledge.ItemKey(baseProduce);
                if (!string.IsNullOrEmpty(baseKey) && idx.IsMushroom(baseKey) &&
                    (cfg.UnknownMushrooms || cfg.UnknownAll == true))
                {
                    if (!Knowledge.IsKnown(agent, baseProduce))
                    {
                        __result = Lang.Get("foragersgamble:unknown-mushroom");
                        return;
                    }
                }
            }

            var thisCode = Knowledge.ItemKey(itemStack);
            if (!string.IsNullOrEmpty(thisCode) && idx.IsMushroom(thisCode) &&
                (cfg.UnknownMushrooms || cfg.UnknownAll == true))
            {
                __result = Lang.Get("foragersgamble:unknown-mushroom");
                return;
            }

            bool gatePlants = cfg.UnknownPlants || cfg.UnknownAll == true;
            var itemProps = __instance.GetNutritionProperties(world, itemStack, agent);
            if (IsEdible(itemProps))
            {
                if (itemStack.Block != null && PlantKnowledgeUtil.IsClipping(itemStack.Block))
                {
                    if (PlantKnowledgeUtil.TryResolveBushFromClipping(___api, itemStack.Block, out var bush))
                    {
                        var bushCode = bush?.Code?.ToString();
                        if (!string.IsNullOrWhiteSpace(bushCode) && !Knowledge.IsKnown(agent, bushCode))
                        {
                            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(itemStack.Block));
                            return;
                        }
                    }
                }
                if (PlantKnowledgeUtil.IsClipping(__instance) &&
                    PlantKnowledgeUtil.TryResolveBushFromClipping(___api, __instance, out var bush3))
                {
                    var bushCode = bush3?.Code?.ToString();
                    if (!string.IsNullOrWhiteSpace(bushCode) && !Knowledge.IsKnown(agent, bushCode))
                    {
                        __result = Lang.Get("foragersgamble:unknown-berrybush");
                        return;
                    }
                }
                if (gatePlants && baseProduce != null)
                {
                    var baseProps = baseProduce.Collectible.GetNutritionProperties(world, baseProduce, agent);
                    var baseEdible = IsEdible(baseProps);

                    if (baseEdible && !Knowledge.IsKnown(agent, baseProduce))
                    {
                        __result = baseProps.FoodCategory switch
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

                    if (!Knowledge.IsKnown(agent, itemStack))
                    {
                        __result = itemProps.FoodCategory switch
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
            if (gatePlants &&
                (itemProps == null ||
                 itemProps.FoodCategory == EnumFoodCategory.Unknown ||
                 itemProps.FoodCategory == EnumFoodCategory.NoNutrition))
            {
                if (itemStack.Block != null && PlantKnowledgeUtil.IsClipping(itemStack.Block))
                {
                    if (PlantKnowledgeUtil.TryResolveBushFromClipping(___api, itemStack.Block, out var bush))
                    {
                        var bushCode = bush?.Code?.ToString();
                        if (!string.IsNullOrWhiteSpace(bushCode) && !Knowledge.IsKnown(agent, bushCode))
                        {
                            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(itemStack.Block));
                            return;
                        }
                    }
                }
                if (PlantKnowledgeUtil.IsClipping(__instance) &&
                    PlantKnowledgeUtil.TryResolveBushFromClipping(___api, __instance, out var bush3))
                {
                    var bushCode = bush3?.Code?.ToString();
                    if (!string.IsNullOrWhiteSpace(bushCode) && !Knowledge.IsKnown(agent, bushCode))
                    {
                        __result = Lang.Get("foragersgamble:unknown-berrybush");
                        return;
                    }
                }
                var edibleRef = TryResolveEdibleCounterpart(___api, idx, itemStack.Collectible, itemStack, agent);
                if (edibleRef != null && !Knowledge.IsKnown(agent, edibleRef))
                {
                    var asBlock = itemStack.Block ?? __instance as Block;
                    if (asBlock != null)
                    {
                        __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(asBlock));
                        return;
                    }
                    if (PlantKnowledgeUtil.IsClipping(itemStack.Collectible) ||
                        PlantKnowledgeUtil.IsClipping(__instance))
                    {
                        __result = Lang.Get("foragersgamble:unknown-berrybush");
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

                if (baseProduce != null && !Knowledge.IsKnown(agent, baseProduce))
                {
                    var dprops = baseProduce.Collectible.GetNutritionProperties(world, baseProduce, agent);
                    if (dprops != null)
                    {
                        __result = dprops.FoodCategory switch
                        {
                            EnumFoodCategory.Fruit     => Lang.Get("foragersgamble:unknown-fruit"),
                            EnumFoodCategory.Vegetable => Lang.Get("foragersgamble:unknown-vegetable"),
                            EnumFoodCategory.Grain     => Lang.Get("foragersgamble:unknown-grain"),
                            _                          => Lang.Get("foragersgamble:unknown-plant")
                        };
                        return;
                    }
                }
            }
            if (cfg.UnknownAll == true)
            {
                var props = itemProps ?? __instance.GetNutritionProperties(world, itemStack, agent);
                if (props == null) return;
                if (props.FoodCategory == EnumFoodCategory.Unknown ||
                    props.FoodCategory == EnumFoodCategory.NoNutrition)
                    return;

                __result = props.FoodCategory switch
                {
                    EnumFoodCategory.Fruit     => Lang.Get("foragersgamble:unknown-fruit"),
                    EnumFoodCategory.Vegetable => Lang.Get("foragersgamble:unknown-vegetable"),
                    EnumFoodCategory.Protein   => Lang.Get("foragersgamble:unknown-protein"),
                    EnumFoodCategory.Grain     => Lang.Get("foragersgamble:unknown-grain"),
                    EnumFoodCategory.Dairy     => Lang.Get("foragersgamble:unknown-dairy"),
                    _                          => Lang.Get("foragersgamble:unknown-food")
                };
            }
        }
    }
}
