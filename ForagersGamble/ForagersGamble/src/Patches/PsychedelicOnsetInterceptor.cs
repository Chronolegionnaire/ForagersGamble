using System;
using ForagersGamble.Behaviors;
using ForagersGamble.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ForagersGamble.Patches
{
    public static class PsychedelicOnsetInterceptor
    {
        public static void CaptureBefore(EntityAgent byEntity, string beforeKey)
        {
            try
            {
                if (byEntity?.WatchedAttributes == null) return;

                float current = byEntity.WatchedAttributes.GetFloat("psychedelic", 0f);
                byEntity.WatchedAttributes.SetFloat(beforeKey, current);
                byEntity.Attributes?.MarkPathDirty(beforeKey);
            }
            catch
            {
            }
        }

        public static void ProcessAfterEat(EntityAgent byEntity, string beforeKey, string itemKey)
        {
            try
            {
                if (byEntity?.WatchedAttributes == null) return;
                if (byEntity.World?.Side != EnumAppSide.Server) return;

                float before = byEntity.WatchedAttributes.GetFloat(beforeKey, 0f);
                float after  = byEntity.WatchedAttributes.GetFloat("psychedelic", 0f);

                if (after <= before) return;

                var root = byEntity.WatchedAttributes?.GetTreeAttribute(NibbleKeys.AttrRoot)
                           ?? byEntity.WatchedAttributes?.GetTreeAttribute(NibbleLiquidKeys.AttrRoot);

                bool wasNibble = root?.GetBool("nibbleIntent", false) ?? false;

                if (wasNibble && ModConfig.Instance.Main.EnableNibbling)
                {
                    after *= ModConfig.Instance.Main.NibbleFactor;
                }

                if (!ModConfig.Instance.Main.PsychedelicOnset)
                    return;

                byEntity.WatchedAttributes.SetFloat("psychedelic", before);
                byEntity.Attributes?.MarkPathDirty("psychedelic");

                var beh = byEntity.GetBehavior<EntityBehaviorDelayedPsychedelic>();
                if (beh != null)
                {
                    beh.ScheduleFromFood(
                        after,
                        itemKey,
                        ModConfig.Instance.Main.PsychedelicOnsetMinHours,
                        ModConfig.Instance.Main.PsychedelicOnsetMaxHours
                    );
                }
                else
                {
                    byEntity.WatchedAttributes.SetFloat("psychedelic", after);
                    byEntity.Attributes?.MarkPathDirty("psychedelic");
                }
            }
            catch
            {
            }
        }

        public static void ClearCapture(EntityAgent byEntity, string beforeKey)
        {
            try
            {
                if (byEntity?.WatchedAttributes == null) return;
                byEntity.WatchedAttributes.SetFloat(beforeKey, 0f);
                byEntity.Attributes?.MarkPathDirty(beforeKey);
            }
            catch
            {
            }
        }
    }
}