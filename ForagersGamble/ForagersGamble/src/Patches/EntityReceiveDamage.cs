using System;
using ForagersGamble.Behaviors;
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ForagersGamble.Patches
{
    public static class EatPoisonInterceptor
    {
        public static bool Intercept(Entity entity, DamageSource damageSource, ref float damage)
        {
            try
            {
                if (entity?.WatchedAttributes?.GetBool("FG.InstantApply", false) == true)
                    return true;

                if (entity?.World?.Side != EnumAppSide.Server) return true;
                if (!ModConfig.Instance.Main.PoisonOnset) return true;
                if (entity is not EntityPlayer) return true;
                if (damageSource == null) return true;
                if (damageSource.Source != EnumDamageSource.Internal || damageSource.Type != EnumDamageType.Poison)
                    return true;

                string itemKey = entity.WatchedAttributes?.GetString("FG.LastEatItemKey", null);
                if (string.IsNullOrEmpty(itemKey)) return true;

                float durationSec = (float)damageSource.Duration.TotalSeconds;
                int   ticks       = damageSource.TicksPerDuration;

                var beh = entity.GetBehavior<EntityBehaviorDelayedPoison>();
                if (beh != null)
                {
                    beh.SchedulePoisonFromFood(
                        Math.Abs(damage), itemKey,
                        ModConfig.Instance.Main.PoisonOnsetMinHours,
                        ModConfig.Instance.Main.PoisonOnsetMaxHours,
                        durationSec, ticks
                    );
                }

                entity.WatchedAttributes?.SetString("FG.LastEatItemKey", null);
                entity.Attributes?.MarkPathDirty("FG.LastEatItemKey");

                damage = 0f;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage), new Type[] { typeof(DamageSource), typeof(float) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_Entity_ReceiveDamage
    {
        public static bool Prefix(Entity __instance, DamageSource damageSource, ref float damage)
            => EatPoisonInterceptor.Intercept(__instance, damageSource, ref damage);
    }

    [HarmonyPatch(typeof(EntityAgent), nameof(EntityAgent.ReceiveDamage), new Type[] { typeof(DamageSource), typeof(float) })]
    [HarmonyPriority(Priority.First)]
    public static class Patch_EntityAgent_ReceiveDamage
    {
        public static bool Prefix(EntityAgent __instance, DamageSource damageSource, ref float damage)
            => EatPoisonInterceptor.Intercept(__instance, damageSource, ref damage);
    }
}
