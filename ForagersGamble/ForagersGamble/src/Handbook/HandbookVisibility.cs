using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

internal static class HandbookVisibility
{
    private static readonly ConditionalWeakTable<object, ItemStack[]> stacksCache = new();

    static bool IsEdible(FoodNutritionProperties p) =>
        p != null && p.FoodCategory != EnumFoodCategory.Unknown && p.FoodCategory != EnumFoodCategory.NoNutrition;

    public static bool ShouldHidePage(GuiHandbookPage page, ICoreClientAPI capi, EntityPlayer player)
    {
        var cfg = ModConfig.Instance?.Main;
        if (cfg?.PreventHandbookOnUnidentified != true) return false;

        if (page == null || capi?.World == null || player == null) return false;
        if (player.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return false;

        var stacks = stacksCache.GetValue(page, ExtractStacks);
        if (stacks == null || stacks.Length == 0) return false;

        var world = capi.World;
        var idx = ForagersGamble.PlantKnowledgeIndex.Get(capi);

        foreach (var st in stacks)
        {
            if (st == null || st.Collectible == null) continue;

            if (st.Block is BlockLiquidContainerTopOpened) continue;

            var coll = st.Collectible;

            var hasLiquidPropsAttr = coll.Attributes?["waterTightContainerProps"]?.Exists == true;
            bool hasLiquidPropsHelper = false;
            try { hasLiquidPropsHelper = BlockLiquidContainerBase.GetContainableProps(st) != null; } catch { }

            var selfProps = coll.GetNutritionProperties(world, st, player);
            bool selfEdible = IsEdible(selfProps);

            if (hasLiquidPropsAttr || hasLiquidPropsHelper)
            {
                if (selfEdible)
                {
                    if (!ForagersGamble.Knowledge.IsKnown(player, st)) return true;
                    continue;
                }

                ItemStack parent = null;
                if (!ForagersGamble.PlantKnowledgeUtil.TryResolveBaseProduceFromItem(capi, st, out var baseProduce) || baseProduce == null)
                {
                    parent = TryResolveEdibleCounterpart(capi, idx, coll, st, player);
                }
                else parent = baseProduce;

                if (parent != null)
                {
                    if (!ForagersGamble.Knowledge.IsKnown(player, parent)) return true;
                    continue;
                }

                continue;
            }

            if (selfEdible)
            {
                if (!ForagersGamble.Knowledge.IsKnown(player, st)) return true;
                continue;
            }

            var asBlock = st.Block;
            if (asBlock != null && idx != null)
            {
                var bcode = asBlock.Code?.ToString() ?? "";
                if (!string.IsNullOrEmpty(bcode) && idx.IsKnowledgeGated(bcode))
                {
                    if (ForagersGamble.PlantKnowledgeUtil.TryResolveReferenceFruit(capi, asBlock, new ItemStack(asBlock), out var fruitRef))
                    {
                        var fprops = fruitRef?.Collectible?.GetNutritionProperties(world, fruitRef, player);
                        if (IsEdible(fprops) && !ForagersGamble.Knowledge.IsKnown(player, fruitRef))
                            return true;
                    }
                }
            }
        }

        return false;

        ItemStack[] ExtractStacks(object p)
        {
            var list = new List<ItemStack>();
            TryField("stack");
            TryField("itemstack");
            TryField("displayStack");
            TryField("forStack");
            TryField("primaryStack");
            TryField("outputStack");
            TryField("recipe");
            TryProp("Stack");
            TryProp("ItemStack");
            TryProp("DisplayStack");
            TryProp("OutputStack");
            foreach (var f in p.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(ItemStack)) Add((ItemStack)f.GetValue(p));
                else if (f.FieldType.Name == "JsonItemStack") TryJson(f.GetValue(p));
            }
            void TryField(string name)
            {
                var f = p.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (f == null) return;
                var v = f.GetValue(p);
                if (v is ItemStack st) Add(st);
                else if (v?.GetType().Name == "GridRecipe" || v?.GetType().Name.EndsWith("Recipe") == true)
                    TryRecipe(v);
                else if (v?.GetType().Name == "JsonItemStack")
                    TryJson(v);
            }
            void TryProp(string name)
            {
                var pr = p.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (pr == null) return;
                var v = pr.GetValue(p);
                if (v is ItemStack st) Add(st);
                else if (v?.GetType().Name == "JsonItemStack")
                    TryJson(v);
            }
            void TryJson(object jis)
            {
                var worldLocal = capi.World;
                var resolve = jis?.GetType().GetMethod("Resolve", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                resolve?.Invoke(jis, new object[] { worldLocal, "ForagersGamble handbook filter" });

                var prop = jis?.GetType().GetProperty("ResolvedItemstack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(jis) is ItemStack st) Add(st);
            }
            void TryRecipe(object recipe)
            {
                var outProp = recipe.GetType().GetProperty("Output", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                              ?? recipe.GetType().GetProperty("OutputStack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (outProp == null) return;

                var outVal = outProp.GetValue(recipe);
                if (outVal is ItemStack st) Add(st);
                else if (outVal?.GetType().Name == "JsonItemStack") TryJson(outVal);
            }
            void Add(ItemStack st)
            {
                if (st != null) list.Add(st.Clone());
            }

            return list.DistinctBy(s => s.Collectible?.Code?.ToString() ?? "").ToArray();
        }
    }
    private static ItemStack TryResolveEdibleCounterpart(ICoreClientAPI api, ForagersGamble.PlantKnowledgeIndex idx,
        CollectibleObject coll, ItemStack stack, EntityPlayer agent)
    {
        if (api?.World == null || coll?.Code == null) return null;
        var asBlock = stack.Block ?? coll as Block;
        if (asBlock != null)
        {
            if (ForagersGamble.PlantKnowledgeUtil.TryResolveReferenceFruit(api, asBlock, stack, out var fruitStack))
            {
                var p = fruitStack.Collectible.GetNutritionProperties(api.World, fruitStack, agent);
                if (IsEdible(p)) return fruitStack;
            }
        }

        var path = coll.Code.Path ?? "";
        if (string.IsNullOrWhiteSpace(path)) return null;

        var stageWords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        { "ripe","unripe","empty","flowering","flower","immature","mature","harvested","small","medium","large","stage","young","old","branch","foliage","leaves","leaf","trunk" };

        var colorWords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        { "white","black","gray","grey","lightgray","darkgray","red","orange","yellow","green","blue","teal","cyan","aqua","purple","violet","magenta","pink","brown","beige","tan" };

        var materialWords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        { "tile","claytile","brick","plank","wood","stone","granite","basalt","limestone","sandstone","metal","copper","tin","bronze","iron","steel","cloth","linen","wool","glass","paper" };

        var tokens = path.Split('-');
        foreach (var rawTok in tokens)
        {
            var tok = rawTok.Trim();
            if (tok.Length < 3 || stageWords.Contains(tok) || colorWords.Contains(tok) || materialWords.Contains(tok)) continue;
            if (tok.Any(char.IsDigit)) continue;

            var baseTok = tok.Replace("berries", "berry", System.StringComparison.OrdinalIgnoreCase)
                             .Trim('-', '_', '.');

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
}
