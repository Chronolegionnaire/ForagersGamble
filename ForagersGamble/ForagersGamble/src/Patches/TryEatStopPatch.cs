using System;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ForagersGamble.Patches
{
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
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
                var wat = byEntity.WatchedAttributes;
                var root = wat.GetTreeAttribute(NibbleKeys.AttrRoot);
                bool nibble = root?.GetBool(NibbleKeys.NibbleIntent, false) ?? false;
                if (root != null)
                {
                    root.SetBool(NibbleKeys.NibbleIntent, false);
                    wat.SetAttribute(NibbleKeys.AttrRoot, root);
                    byEntity.Attributes.MarkPathDirty(NibbleKeys.AttrRoot);
                }
                if (!nibble || slot?.Itemstack == null) return true;
                var nutrition = slot.Itemstack.Collectible.GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity);
                if (nutrition == null) return true;

                var transitionState = slot.Itemstack.Collectible.UpdateAndGetTransitionState(byEntity.Api.World, slot, EnumTransitionType.Perish);
                double spoilLevel = transitionState?.TransitionLevel ?? 0.0;
                float satMul = GlobalConstants.FoodSpoilageSatLossMul((float)spoilLevel, slot.Itemstack, byEntity);
                float hpMul  = GlobalConstants.FoodSpoilageHealthLossMul((float)spoilLevel, slot.Itemstack, byEntity);

                float nibbleFactor = ModConfig.Instance.Main.NibbleFactor;
                byEntity.ReceiveSaturation(nutrition.Satiety * satMul * nibbleFactor, nutrition.FoodCategory);
                IPlayer player = (byEntity is EntityPlayer ep) ? byEntity.World.PlayerByUid(ep.PlayerUID) : null;
                slot.TakeOut(1);
                if (nutrition.EatenStack != null)
                {
                    var outStack = nutrition.EatenStack.ResolvedItemstack.Clone();
                    if (slot.Empty)
                    {
                        slot.Itemstack = outStack;
                    }
                    else if (player == null || !player.InventoryManager.TryGiveItemstack(outStack, true))
                    {
                        byEntity.World.SpawnItemEntity(outStack, byEntity.SidedPos.XYZ);
                    }
                }
                float intox = byEntity.WatchedAttributes.GetFloat("intoxication", 0f);
                byEntity.WatchedAttributes.SetFloat("intoxication", Math.Min(1.1f, intox + nutrition.Intoxication * nibbleFactor));
                float healthTotal = nutrition.Health * hpMul * nibbleFactor;
                if (Math.Abs(healthTotal) > 0f)
                {
                    float durationSec = slot.Itemstack?.Collectible?.Attributes?["eatHealthEffectDurationSec"].AsFloat() ?? 0f;
                    int ticks = slot.Itemstack?.Collectible?.Attributes?["eatHealthEffectTicks"].AsInt(1) ?? 1;

                    var ds = new DamageSource
                    {
                        Source = EnumDamageSource.Internal,
                        Type = (healthTotal > 0f) ? EnumDamageType.Heal : EnumDamageType.Poison,
                        Duration = TimeSpan.FromSeconds(durationSec),
                        TicksPerDuration = ticks,
                        DamageOverTimeTypeEnum = (healthTotal > 0f) ? EnumDamageOverTimeEffectType.Unknown : EnumDamageOverTimeEffectType.Poison
                    };

                    byEntity.ReceiveDamage(ds, Math.Abs(healthTotal));
                }
                if (slot?.Itemstack != null)
                    Knowledge.MarkKnown(byEntity, slot.Itemstack);
                else if (!string.IsNullOrEmpty(__state.ItemKey))
                    Knowledge.MarkKnown(byEntity, __state.ItemKey);

                slot.MarkDirty();
                player?.InventoryManager.BroadcastHotbarSlot();

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
                if (__state.HandledNibble) return;
                if (byEntity?.World is IServerWorldAccessor && secondsUsed >= 0.95f)
                {
                    if (slot?.Itemstack != null)
                    {
                        Knowledge.MarkKnown(byEntity, slot.Itemstack);
                    }
                    else if (!string.IsNullOrEmpty(__state.ItemKey))
                    {
                        Knowledge.MarkKnown(byEntity, __state.ItemKey);
                    }
                }
            }
            catch { }
        }
    }
}