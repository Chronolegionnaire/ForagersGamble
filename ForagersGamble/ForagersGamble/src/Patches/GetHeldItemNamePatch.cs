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

                var candidate = new AssetLocation("game", "fruit-" + baseTok);

                var item = api.World.GetItem(candidate);
                if (item != null)
                {
                    var t = new ItemStack(item);
                    var p = item.GetNutritionProperties(api.World, t, agent);
                    if (IsEdible(p)) return t;
                }

                var block = api.World.GetBlock(candidate);
                if (block != null)
                {
                    var t = new ItemStack(block);
                    var p = block.GetNutritionProperties(api.World, t, agent);
                    if (IsEdible(p)) return t;
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
                var edibleRef = TryResolveEdibleCounterpart(___api, idx, itemStack.Collectible, itemStack, agent);
                if (edibleRef != null && !Knowledge.IsKnown(agent, edibleRef))
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
