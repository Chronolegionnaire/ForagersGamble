using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using ForagersGamble.Compat;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ForagersGamble.Patches
{
    public static class NibbleLiquidKeys
    {
        public const string AttrRoot       = "foragersGamble";
        public const string NibbleIntent   = "nibbleIntent";
        public const string LastEatItemKey = "FG.LastEatItemKey";
    }
    [HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    public static class Patch_LiquidContainer_TryEatStop_ScaleOnSneak
    {
        public static float ApplyNibble(EntityAgent ent, ItemSlot slot, float value)
        {
            try
            {
                if (ent?.Controls?.Sneak == true)
                {
                    return value * ModConfig.Instance.Main.NibbleFactor;
                }
            }
            catch { }
            return value;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instr);
            var miReceiveSaturation = AccessTools.Method(
                typeof(EntityAgent),
                nameof(EntityAgent.ReceiveSaturation),
                new Type[] { typeof(float), typeof(EnumFoodCategory), typeof(float), typeof(float) });

            var miReceiveDamage = AccessTools.Method(
                typeof(EntityAgent),
                nameof(EntityAgent.ReceiveDamage),
                new Type[] { typeof(DamageSource), typeof(float) });

            var miApplyNibble = AccessTools.Method(
                typeof(Patch_LiquidContainer_TryEatStop_ScaleOnSneak),
                nameof(ApplyNibble));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(miReceiveSaturation))
                {
                    var lOne     = il.DeclareLocal(typeof(float));
                    var lTen     = il.DeclareLocal(typeof(float));
                    var lFoodCat = il.DeclareLocal(typeof(int));
                    var lSat     = il.DeclareLocal(typeof(float));
                    var lEnt     = il.DeclareLocal(typeof(EntityAgent));
                    var lWrapSat = il.DeclareLocal(typeof(float));

                    code.RemoveAt(i);

                    var block = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Stloc_S, lOne),
                        new CodeInstruction(OpCodes.Stloc_S, lTen),
                        new CodeInstruction(OpCodes.Stloc_S, lFoodCat),
                        new CodeInstruction(OpCodes.Stloc_S, lSat),
                        new CodeInstruction(OpCodes.Stloc_S, lEnt),
                        new CodeInstruction(OpCodes.Ldloc_S, lEnt),
                        new CodeInstruction(OpCodes.Ldarg_2),
                        new CodeInstruction(OpCodes.Ldloc_S, lSat),
                        new CodeInstruction(OpCodes.Call, miApplyNibble),
                        new CodeInstruction(OpCodes.Stloc_S, lWrapSat),
                        new CodeInstruction(OpCodes.Ldloc_S, lEnt),
                        new CodeInstruction(OpCodes.Ldloc_S, lWrapSat),
                        new CodeInstruction(OpCodes.Ldloc_S, lFoodCat),
                        new CodeInstruction(OpCodes.Ldloc_S, lTen),
                        new CodeInstruction(OpCodes.Ldloc_S, lOne),

                        new CodeInstruction(OpCodes.Callvirt, miReceiveSaturation)
                    };

                    code.InsertRange(i, block);
                    i += block.Count - 1;
                    continue;
                }
                if (code[i].Calls(miReceiveDamage))
                {
                    var lMag     = il.DeclareLocal(typeof(float));
                    var lDs      = il.DeclareLocal(typeof(DamageSource));
                    var lEnt     = il.DeclareLocal(typeof(EntityAgent));
                    var lWrapMag = il.DeclareLocal(typeof(float));

                    code.RemoveAt(i);

                    var block = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Stloc_S, lMag),
                        new CodeInstruction(OpCodes.Stloc_S, lDs),
                        new CodeInstruction(OpCodes.Stloc_S, lEnt),

                        new CodeInstruction(OpCodes.Ldloc_S, lEnt),
                        new CodeInstruction(OpCodes.Ldarg_2),
                        new CodeInstruction(OpCodes.Ldloc_S, lMag),
                        new CodeInstruction(OpCodes.Call, miApplyNibble),
                        new CodeInstruction(OpCodes.Stloc_S, lWrapMag),

                        new CodeInstruction(OpCodes.Ldloc_S, lEnt),
                        new CodeInstruction(OpCodes.Ldloc_S, lDs),
                        new CodeInstruction(OpCodes.Ldloc_S, lWrapMag),

                        new CodeInstruction(OpCodes.Callvirt, miReceiveDamage)
                    };

                    code.InsertRange(i, block);
                    i += block.Count - 1;
                    continue;
                }
            }

            return code;
        }
    }
    [HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_LiquidContainer_TryEatStop_Capture
    {
        static void Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockLiquidContainerBase __instance)
        {
            try
            {
                if (byEntity == null) return;

                var wat = byEntity.WatchedAttributes;
                wat?.SetString(NibbleLiquidKeys.LastEatItemKey, null);
                byEntity.Attributes?.MarkPathDirty(NibbleLiquidKeys.LastEatItemKey);
                ItemStack consumedStack = null;

                if (slot?.Itemstack != null && __instance != null)
                {
                    consumedStack = __instance.GetContent(slot.Itemstack);
                }

                string key = null;

                if (consumedStack != null)
                    key = Knowledge.ItemKey(consumedStack);
                if (key == null && slot?.Itemstack != null)
                    key = Knowledge.ItemKey(slot.Itemstack);

                if (!string.IsNullOrEmpty(key))
                {
                    wat?.SetString(NibbleLiquidKeys.LastEatItemKey, key);
                    byEntity.Attributes?.MarkPathDirty(NibbleLiquidKeys.LastEatItemKey);
                }

                bool wantNibble = (byEntity.Controls?.Sneak ?? false)
                                  && consumedStack != null
                                  && !Knowledge.IsKnown(byEntity, consumedStack);

                var root = wat?.GetTreeAttribute(NibbleLiquidKeys.AttrRoot) ?? new TreeAttribute();
                root.SetBool(NibbleLiquidKeys.NibbleIntent, wantNibble);
                wat?.SetAttribute(NibbleLiquidKeys.AttrRoot, root);
                byEntity.Attributes?.MarkPathDirty(NibbleLiquidKeys.AttrRoot);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    [HarmonyPriority(HarmonyLib.Priority.First)]
    public static class Patch_LiquidContainer_TryEatStop_Knowledge
    {
        static void Postfix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockLiquidContainerBase __instance)
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
                    consumed = __instance.GetContent(slot.Itemstack);

                string key = wat?.GetString(NibbleLiquidKeys.LastEatItemKey, null);
                if (string.IsNullOrEmpty(key))
                {
                    if (consumed != null) key = Knowledge.ItemKey(consumed);
                    else if (slot?.Itemstack != null) key = Knowledge.ItemKey(slot.Itemstack);
                }

                var cfg = ModConfig.Instance?.Main;
                float amt = Math.Max(0f, Math.Min(1f, cfg?.LearnAmountPerEat ?? 0.20f));

                bool justDiscovered = false;

                if (!string.IsNullOrEmpty(key))
                {
                    bool isGated = Knowledge.IsInUnknownUniverse(key);
                    if (amt > 0f && isGated)
                    {
                        justDiscovered = Knowledge.AddProgress(byEntity, key, amt);
                        if (justDiscovered)
                            Knowledge.MarkDiscovered(byEntity, key);
                    }
                }

                if (consumed != null && __instance != null)
                {
                    var apiField = AccessTools.Field(typeof(Block), "api");
                    var api = apiField?.GetValue(__instance) as ICoreAPI;

                    if (api != null &&
                        PlantKnowledgeUtil.TryResolveBaseProduceFromItem(api, consumed, out var baseProduce) &&
                        baseProduce != null)
                    {
                        var parentKey = Knowledge.ItemKey(baseProduce);

                        if (!string.IsNullOrEmpty(parentKey) && Knowledge.IsInUnknownUniverse(parentKey) && amt > 0f)
                        {
                            var parentJustDiscovered = Knowledge.AddProgress(byEntity, parentKey, amt);
                            if (parentJustDiscovered)
                                Knowledge.MarkKnown(byEntity, baseProduce);
                        }
                        else if (!string.IsNullOrEmpty(parentKey) && Knowledge.IsInUnknownUniverse(parentKey) &&
                                 amt <= 0f)
                        {
                            Knowledge.MarkKnown(byEntity, baseProduce);
                        }
                    }
                }

                if (justDiscovered && !string.IsNullOrEmpty(key))
                {
                    Knowledge.MarkKnown(byEntity, key);
                }

                if (wasNibble && slot?.Itemstack != null)
                {
                    float factor = ModConfig.Instance.Main.EnableNibbling
                        ? ModConfig.Instance.Main.NibbleFactor
                        : 1f;
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
                catch
                {
                }
            }
        }
    }
}
