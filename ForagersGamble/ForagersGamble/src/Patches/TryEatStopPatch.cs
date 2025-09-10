using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using ForagersGamble.Compat;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ForagersGamble.Patches
{
    public static class NibbleKeys
    {
        public const string AttrRoot       = "foragersGamble";
        public const string NibbleIntent   = "nibbleIntent";
        public const string LastEatItemKey = "FG.LastEatItemKey";
    }
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    public static class Patch_CollectibleObject_TryEatStop_ScaleOnSneak
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

            var miReceiveSaturation = AccessTools.Method(typeof(EntityAgent), nameof(EntityAgent.ReceiveSaturation),
                new Type[] { typeof(float), typeof(EnumFoodCategory), typeof(float), typeof(float) });

            var miReceiveDamage = AccessTools.Method(typeof(EntityAgent), nameof(EntityAgent.ReceiveDamage),
                new Type[] { typeof(DamageSource), typeof(float) });

            var miApplyNibble = AccessTools.Method(typeof(Patch_CollectibleObject_TryEatStop_ScaleOnSneak),
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

    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_CollectibleObject_TryEatStop_Capture
    {
        static void Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, CollectibleObject __instance)
        {
            try
            {
                if (byEntity == null) return;

                var wat = byEntity.WatchedAttributes;
                wat?.SetString(NibbleKeys.LastEatItemKey, null);
                byEntity.Attributes?.MarkPathDirty(NibbleKeys.LastEatItemKey);

                string key = null;

                if (slot?.Itemstack != null)
                    key = Knowledge.ItemKey(slot.Itemstack);

                if (key == null && __instance != null)
                {
                    try { key = Knowledge.ItemKey(new ItemStack(__instance)); } catch { }
                }

                if (!string.IsNullOrEmpty(key))
                {
                    wat?.SetString(NibbleKeys.LastEatItemKey, key);
                    byEntity.Attributes?.MarkPathDirty(NibbleKeys.LastEatItemKey);
                }

                bool wantNibble = (byEntity.Controls?.Sneak ?? false)
                                  && (slot?.Itemstack != null)
                                  && !Knowledge.IsKnown(byEntity, slot?.Itemstack);

                var root = wat?.GetTreeAttribute(NibbleKeys.AttrRoot) ?? new TreeAttribute();
                root.SetBool(NibbleKeys.NibbleIntent, wantNibble);
                wat?.SetAttribute(NibbleKeys.AttrRoot, root);
                byEntity.Attributes?.MarkPathDirty(NibbleKeys.AttrRoot);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop",
        new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_CollectibleObject_TryEatStop_Knowledge
    {
        static void Postfix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, CollectibleObject __instance)
        {
            try
            {
                bool shouldProcess = byEntity?.World is IServerWorldAccessor && secondsUsed >= 0.95f;

                if (shouldProcess)
                {
                    var wat = byEntity.WatchedAttributes;
                    var root = wat?.GetTreeAttribute(NibbleKeys.AttrRoot);
                    bool wasNibble = root?.GetBool(NibbleKeys.NibbleIntent, false) ?? false;

                    // Read the captured key; treat empty as missing so we can fall back
                    string key = wat?.GetString(NibbleKeys.LastEatItemKey, null);
                    if (string.IsNullOrEmpty(key))
                    {
                        key = slot?.Itemstack != null ? Knowledge.ItemKey(slot.Itemstack) : null;
                        if (string.IsNullOrEmpty(key) && __instance != null)
                        {
                            try
                            {
                                key = Knowledge.ItemKey(new ItemStack(__instance));
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(key))
                    {
                        Knowledge.MarkKnown(byEntity, key);
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
            }
            catch
            {
            }
            finally
            {
                // Always clear
                try
                {
                    var wat = byEntity?.WatchedAttributes;
                    var root = wat?.GetTreeAttribute(NibbleKeys.AttrRoot);
                    if (root != null)
                    {
                        root.SetBool(NibbleKeys.NibbleIntent, false);
                        wat.SetAttribute(NibbleKeys.AttrRoot, root);
                    }

                    // Clear the cached key
                    wat?.SetString(NibbleKeys.LastEatItemKey, null);

                    byEntity?.Attributes?.MarkPathDirty(NibbleKeys.AttrRoot);
                    byEntity?.Attributes?.MarkPathDirty(NibbleKeys.LastEatItemKey);
                }
                catch
                {
                }
            }
        }
    }
}
