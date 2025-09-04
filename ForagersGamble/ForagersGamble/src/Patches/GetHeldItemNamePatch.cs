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

            var world = ___api.World;
            var agent = (world as IClientWorldAccessor)?.Player?.Entity as EntityPlayer;
            if (agent?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

            if (Knowledge.IsKnown(agent, itemStack)) return;

            bool isMushroom =
                __instance is BlockMushroom ||
                itemStack.Block is BlockMushroom;

            if (isMushroom && cfg.UnknownMushrooms)
            {
                __result = Lang.Get("foragersgamble:unknown-mushroom");
                return;
            }

            if (cfg.HideName != true) return;

            var foodProps = __instance.GetNutritionProperties(world, itemStack, agent);
            if (foodProps == null) return;

            switch (foodProps.FoodCategory)
            {
                case EnumFoodCategory.Fruit:
                    __result = Lang.Get("foragersgamble:unknown-fruit");
                    break;
                case EnumFoodCategory.Vegetable:
                    __result = Lang.Get("foragersgamble:unknown-vegetable");
                    break;
                case EnumFoodCategory.Protein:
                    __result = Lang.Get("foragersgamble:unknown-protein");
                    break;
                case EnumFoodCategory.Grain:
                    __result = Lang.Get("foragersgamble:unknown-grain");
                    break;
                case EnumFoodCategory.Dairy:
                    __result = Lang.Get("foragersgamble:unknown-dairy");
                    break;
                case EnumFoodCategory.NoNutrition:
                case EnumFoodCategory.Unknown:
                default:
                    __result = Lang.Get("foragersgamble:unknown-food");
                    break;
            }
        }
    }
}
