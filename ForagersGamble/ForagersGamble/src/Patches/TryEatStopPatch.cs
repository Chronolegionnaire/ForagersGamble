using System;
using ForagersGamble;
using ForagersGamble.Behaviors;
using ForagersGamble.Compat;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop", new Type[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_CollectibleObject_TryEatStop
    {
        public struct State
        {
            public string ItemKey;
            public bool HandledNibble;
        }

        static bool Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, ref State __state)
        {
            __state = new State
            {
                ItemKey = Knowledge.ItemKey(slot?.Itemstack),
                HandledNibble = false
            };

            try
            {
                if (!(byEntity?.World is IServerWorldAccessor)) return true;
                if (secondsUsed < 0.95f) return true;
                if (slot?.Itemstack == null) return true;

                byEntity.WatchedAttributes?.SetString("FG.LastEatItemKey", __state.ItemKey ?? "");
                byEntity.Attributes?.MarkPathDirty("FG.LastEatItemKey");

                var wat  = byEntity.WatchedAttributes;
                var root = wat.GetTreeAttribute(NibbleKeys.AttrRoot);
                bool nibble = root?.GetBool(NibbleKeys.NibbleIntent, false) ?? false;
                if (root != null)
                {
                    root.SetBool(NibbleKeys.NibbleIntent, false);
                    wat.SetAttribute(NibbleKeys.AttrRoot, root);
                    byEntity.Attributes.MarkPathDirty(NibbleKeys.AttrRoot);
                }

                if (!nibble) return true;


                var nutrition = slot.Itemstack.Collectible.GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity);
                if (nutrition == null) return true;

                var transitionState = slot.Itemstack.Collectible.UpdateAndGetTransitionState(byEntity.Api.World, slot, EnumTransitionType.Perish);
                double spoilLevel = transitionState?.TransitionLevel ?? 0.0;
                float satMul = GlobalConstants.FoodSpoilageSatLossMul((float)spoilLevel, slot.Itemstack, byEntity);
                float hpMul  = GlobalConstants.FoodSpoilageHealthLossMul((float)spoilLevel, slot.Itemstack, byEntity);

                float nibbleFactor = ModConfig.Instance.Main.NibbleFactor;

                byEntity.ReceiveSaturation(nutrition.Satiety * satMul * nibbleFactor, nutrition.FoodCategory);
                try
                {
                    if (byEntity is EntityPlayer && byEntity.World?.Api?.Side == EnumAppSide.Server)
                    {
                        HodCompat.TryApplyHydration(byEntity, slot.Itemstack, nibbleFactor);
                    }
                }
                catch { }
                IPlayer player = (byEntity is EntityPlayer ep) ? byEntity.World.PlayerByUid(ep.PlayerUID) : null;
                slot.TakeOut(1);
                if (nutrition.EatenStack != null)
                {
                    var outStack = nutrition.EatenStack.ResolvedItemstack.Clone();
                    if (slot.Empty) slot.Itemstack = outStack;
                    else if (player == null || !player.InventoryManager.TryGiveItemstack(outStack, true))
                        byEntity.World.SpawnItemEntity(outStack, byEntity.SidedPos.XYZ);
                }
                float intox = byEntity.WatchedAttributes.GetFloat("intoxication", 0f);
                byEntity.WatchedAttributes.SetFloat("intoxication", Math.Min(1.1f, intox + nutrition.Intoxication * nibbleFactor));
                float healthTotal = nutrition.Health * hpMul * nibbleFactor;
                float durationSec = slot.Itemstack?.Collectible?.Attributes?["eatHealthEffectDurationSec"].AsFloat() ?? 0f;
                int   ticks       = slot.Itemstack?.Collectible?.Attributes?["eatHealthEffectTicks"].AsInt(1) ?? 1;

                if (Math.Abs(healthTotal) > 0f)
                {
                    bool isPoison     = (healthTotal < 0f);
                    bool onsetEnabled = ModConfig.Instance.Main.PoisonOnset && isPoison;

                    if (onsetEnabled)
                    {
                        var beh = byEntity.GetBehavior<EntityBehaviorDelayedPoison>();
                        if (beh != null)
                        {
                            beh.SchedulePoisonFromFood(
                                Math.Abs(healthTotal),
                                __state.ItemKey,
                                ModConfig.Instance.Main.PoisonOnsetMinHours,
                                ModConfig.Instance.Main.PoisonOnsetMaxHours,
                                durationSec,
                                ticks
                            );
                        }
                        else
                        {
                            var fallback = new DamageSource
                            {
                                Source = EnumDamageSource.Internal,
                                Type = EnumDamageType.Poison,
                                DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison
                            };
                            if (durationSec > 0 && ticks > 0)
                            {
                                fallback.Duration = TimeSpan.FromSeconds(durationSec);
                                fallback.TicksPerDuration = ticks;
                            }
                            byEntity.ReceiveDamage(fallback, Math.Abs(healthTotal));
                        }
                    }
                    else
                    {
                        var ds = new DamageSource
                        {
                            Source = EnumDamageSource.Internal,
                            Type = (healthTotal > 0f) ? EnumDamageType.Heal : EnumDamageType.Poison,
                            DamageOverTimeTypeEnum = (healthTotal > 0f) ? EnumDamageOverTimeEffectType.Unknown : EnumDamageOverTimeEffectType.Poison
                        };
                        if (durationSec > 0 && ticks > 0)
                        {
                            ds.Duration = TimeSpan.FromSeconds(durationSec);
                            ds.TicksPerDuration = ticks;
                        }
                        byEntity.ReceiveDamage(ds, Math.Abs(healthTotal));
                    }
                }
                if (!string.IsNullOrEmpty(__state.ItemKey))
                {
                    Knowledge.MarkKnown(byEntity, __state.ItemKey);
                }
                slot.MarkDirty();
                player?.InventoryManager.BroadcastHotbarSlot();
                byEntity.WatchedAttributes?.SetString("FG.LastEatItemKey", null);
                byEntity.Attributes?.MarkPathDirty("FG.LastEatItemKey");

                __state.HandledNibble = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        static void Postfix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, State __state)
        {
            try
            {
                if (!__state.HandledNibble && byEntity?.World is IServerWorldAccessor && secondsUsed >= 0.95f)
                {
                    string key = Knowledge.ItemKey(slot?.Itemstack);
                    if (!string.IsNullOrEmpty(key))
                    {
                        Knowledge.MarkKnown(byEntity, key);
                    }
                }
                byEntity?.WatchedAttributes?.SetString("FG.LastEatItemKey", null);
                byEntity?.Attributes?.MarkPathDirty("FG.LastEatItemKey");
            }
            catch { }
        }
    }
}
