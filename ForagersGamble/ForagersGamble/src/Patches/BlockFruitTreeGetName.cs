using System;
using System.Linq;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    internal static class FruitTreeGatingHelpers
    {
        public static bool ShouldGate(IWorldAccessor world)
        {
            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return false;

            bool gatePlants = cfg.UnknownPlants || cfg.UnknownAll == true;
            if (!gatePlants) return false;

            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            return agent?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Survival;
        }

        public static bool TryGetFruitFromTreeAtPos(Block block, IWorldAccessor world, BlockPos pos, out ItemStack fruitRef)
        {
            fruitRef = null;

            var bePart = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityFruitTreePart;
            var treeType = bePart?.TreeType;
            if (string.IsNullOrEmpty(treeType)) return false;

            BlockFruitTreeBranch branchBlock = null;

            if (block is BlockFruitTreeBranch br)
            {
                branchBlock = br;
            }
            else if (block is BlockFruitTreeFoliage fol)
            {
                var branchCode = fol.Attributes?["branchBlock"]?.AsString(null);
                if (branchCode != null)
                {
                    branchBlock = world.GetBlock(AssetLocation.Create(branchCode, block.Code.Domain)) as BlockFruitTreeBranch;
                }
            }

            if (branchBlock?.TypeProps == null) return false;
            if (!branchBlock.TypeProps.TryGetValue(treeType, out var typeProps) ||
                typeProps?.FruitStacks == null || typeProps.FruitStacks.Length == 0) return false;

            foreach (var ds in typeProps.FruitStacks)
            {
                if (ds.ResolvedItemstack == null)
                {
                    ds.Resolve(world, "fruit tree FruitStacks (FG gating)", block.Code);
                }
            }

            var resolved = typeProps.FruitStacks.Select(s => s.ResolvedItemstack).FirstOrDefault(s => s != null);
            if (resolved == null) return false;

            fruitRef = resolved.Clone();
            return true;
        }

        public static bool IsFruitKnown(IWorldAccessor world, ItemStack fruitRef)
        {
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent == null || fruitRef == null) return true;

            var fprops = fruitRef.Collectible.GetNutritionProperties(world, fruitRef, agent);
            bool isEdible = fprops != null &&
                            fprops.FoodCategory != EnumFoodCategory.Unknown &&
                            fprops.FoodCategory != EnumFoodCategory.NoNutrition;
            if (!isEdible) return true;

            return Knowledge.IsKnown(agent, fruitRef);
        }
    }

    [HarmonyPatch(typeof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch.GetPlacedBlockName))]
    public static class Patch_BlockFruitTreeBranch_GetPlacedBlockName
    {
        static void Postfix(BlockFruitTreeBranch __instance, IWorldAccessor world, BlockPos pos, ref string __result)
        {
            if (!FruitTreeGatingHelpers.ShouldGate(world)) return;
            if (!FruitTreeGatingHelpers.TryGetFruitFromTreeAtPos(__instance, world, pos, out var fruitRef)) return;
            if (FruitTreeGatingHelpers.IsFruitKnown(world, fruitRef)) return;

            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(__instance));
        }
    }

    [HarmonyPatch(typeof(BlockFruitTreeFoliage), nameof(BlockFruitTreeFoliage.GetPlacedBlockName))]
    public static class Patch_BlockFruitTreeFoliage_GetPlacedBlockName
    {
        static void Postfix(BlockFruitTreeFoliage __instance, IWorldAccessor world, BlockPos pos, ref string __result)
        {
            if (!FruitTreeGatingHelpers.ShouldGate(world)) return;
            if (!FruitTreeGatingHelpers.TryGetFruitFromTreeAtPos(__instance, world, pos, out var fruitRef)) return;
            if (FruitTreeGatingHelpers.IsFruitKnown(world, fruitRef)) return;

            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(__instance));
        }
    }
    [HarmonyPatch(typeof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch.GetHeldItemName))]
    public static class Patch_BlockFruitTreeBranch_GetHeldItemName
    {
        static void Postfix(BlockFruitTreeBranch __instance, ItemStack itemStack, ref string __result, ICoreAPI ___api)
        {
            var cfg = ModConfig.Instance?.Main;
            if (cfg == null) return;

            bool gatePlants = cfg.UnknownPlants || cfg.UnknownAll == true;
            if (!gatePlants) return;

            var world = ___api?.World;
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            var treeType = itemStack?.Attributes?.GetString("type", null);
            if (string.IsNullOrEmpty(treeType)) return;
            var typeProps = __instance.TypeProps;
            if (typeProps == null || !typeProps.TryGetValue(treeType, out var props) || props?.FruitStacks == null) return;
            foreach (var ds in props.FruitStacks)
            {
                if (ds.ResolvedItemstack == null)
                {
                    ds.Resolve(world, "fruit tree FruitStacks (FG gating)", __instance.Code);
                }
            }
            var fruitRef = props.FruitStacks.Select(s => s.ResolvedItemstack).FirstOrDefault(s => s != null)?.Clone();
            if (fruitRef == null) return;
            var fprops = fruitRef.Collectible.GetNutritionProperties(world, fruitRef, agent);
            bool edible = fprops != null &&
                          fprops.FoodCategory != EnumFoodCategory.Unknown &&
                          fprops.FoodCategory != EnumFoodCategory.NoNutrition;

            if (!edible) return;
            if (Knowledge.IsKnown(agent, fruitRef)) return;
            __result = Lang.Get(PlantKnowledgeUtil.ClassifyUnknownKey(__instance));
        }
    }
}
