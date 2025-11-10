using System;
using System.Reflection;
using ForagersGamble.Compat;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using ForagersGamble.Config;
using ForagersGamble.Patches;

namespace ForagersGamble.Patches
{
    public static class CulinaryArtilleryCompat
    {
        public const string ModID = "aculinaryartillery";

        public static void TryApplyHarmony(ICoreAPI api, Harmony harmony)
        {
            if (api == null || harmony == null) return;
            if (!api.ModLoader.IsModEnabled(ModID)) return;
            var bottleType = AccessTools.TypeByName("ACulinaryArtillery.BlockBottle");
            if (bottleType == null) return;
            var targetMethod = AccessTools.Method(bottleType, "tryEatStop",
                new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) });

            if (targetMethod == null) return;
            var prefix = new HarmonyMethod(
                typeof(Patch_LiquidContainer_TryEatStop_Capture)
                    .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            );
            var transpiler = new HarmonyMethod(
                typeof(Patch_LiquidContainer_TryEatStop_ScaleOnSneak)
                    .GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            );
            var postfix = new HarmonyMethod(
                typeof(CulinaryArtilleryCompat)
                    .GetMethod(nameof(BlockBottle_TryEatStop_Postfix), BindingFlags.Static | BindingFlags.NonPublic)
            );

            harmony.Patch(targetMethod, prefix, postfix, transpiler);
        }
        private static void BlockBottle_TryEatStop_Postfix(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            object __instance
        )
        {
            try
            {
                if (byEntity?.World is not IServerWorldAccessor) return;
                if (secondsUsed < 0.95f) return;

                var wat = byEntity.WatchedAttributes;
                var root = wat?.GetTreeAttribute(NibbleLiquidKeys.AttrRoot);
                bool wasNibble = root?.GetBool(NibbleLiquidKeys.NibbleIntent, false) ?? false;
                ItemStack consumed = null;
                if (slot?.Itemstack != null && __instance != null)
                {
                    var miGetContent = AccessTools.Method(__instance.GetType(), "GetContent", new Type[] { typeof(ItemStack) });
                    if (miGetContent != null)
                    {
                        consumed = miGetContent.Invoke(__instance, new object[] { slot.Itemstack }) as ItemStack;
                    }
                }
                string key = wat?.GetString(NibbleLiquidKeys.LastEatItemKey, null);
                if (string.IsNullOrEmpty(key))
                {
                    if (consumed != null)
                        key = Knowledge.ItemKey(consumed);
                    else if (slot?.Itemstack != null)
                        key = Knowledge.ItemKey(slot.Itemstack);
                }

                bool justDiscovered = false;

                if (!string.IsNullOrEmpty(key))
                {
                    var cfg = ModConfig.Instance?.Main;
                    float baseAmt = Math.Max(0f, Math.Min(1f, cfg?.LearnAmountPerEat ?? 0.20f));
                    float amt = baseAmt * 0.25f;

                    bool isGated = Knowledge.IsInUnknownUniverse(key);

                    if (amt > 0f && isGated)
                    {
                        justDiscovered = Knowledge.AddProgress(byEntity, key, amt);
                        if (justDiscovered)
                        {
                            Knowledge.MarkDiscovered(byEntity, key);
                        }
                    }
                    else
                    {
                        _ = Knowledge.IsKnown(byEntity, key);
                    }
                }
                if (justDiscovered)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        Knowledge.MarkKnown(byEntity, key);
                    }

                    if (consumed != null && __instance != null)
                    {
                        var apiField = AccessTools.Field(typeof(Block), "api");
                        var api = apiField?.GetValue(__instance) as ICoreAPI;

                        if (api != null &&
                            PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, consumed, out var baseProduce) &&
                            baseProduce != null)
                        {
                            Knowledge.MarkKnown(byEntity, baseProduce);
                        }
                    }
                }
                if (wasNibble && slot?.Itemstack != null)
                {
                    float factor = ModConfig.Instance.Main.NibbleFactor;
                    float deltaMul = factor - 1f;
                    if (Math.Abs(deltaMul) > 0f)
                    {
                        HodCompat.TryApplyHydration(byEntity, slot.Itemstack, deltaMul);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    var wat = byEntity?.WatchedAttributes;
                    var root = wat?.GetTreeAttribute(NibbleLiquidKeys.AttrRoot);
                    if (root != null)
                    {
                        root.SetBool(NibbleLiquidKeys.NibbleIntent, false);
                        wat.SetAttribute(NibbleLiquidKeys.AttrRoot, root);
                    }

                    wat?.SetString(NibbleLiquidKeys.LastEatItemKey, null);

                    byEntity?.Attributes?.MarkPathDirty(NibbleLiquidKeys.AttrRoot);
                    byEntity?.Attributes?.MarkPathDirty(NibbleLiquidKeys.LastEatItemKey);
                }
                catch { }
            }
        }
    }
}
