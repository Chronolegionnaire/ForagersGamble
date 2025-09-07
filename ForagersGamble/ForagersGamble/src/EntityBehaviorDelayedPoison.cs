using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForagersGamble.Behaviors
{
    public class EntityBehaviorDelayedPoison : EntityBehavior
    {
        private const string AttrRoot = "foragersGamble.delayedPoison";
        private const string ListKey  = "queue";
        private const float CheckIntervalSec = 10f;
        private float checkTimerSec = 0f;
        private bool _applyingInstant;
        private static readonly string FlagInstant = "FG.InstantApply";
        private class PendingPoison
        {
            public float Damage;
            public string ItemKey;
            public double TriggerAtHours;
            public int? Ticks;
            public float? DurationSec;
        }

        private readonly List<PendingPoison> queue = new();

        public EntityBehaviorDelayedPoison(Entity entity) : base(entity) { }

        public override string PropertyName() => "fgDelayedPoison";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            Load();
        }

        private float TotalQueuedDamage()
        {
            float sum = 0f;
            for (int i = 0; i < queue.Count; i++) sum += queue[i].Damage;
            return sum;
        }
        private string DeterminePoisonClass(float damage, string itemKey)
        {
            var mc = Config.ModConfig.Instance.Main;

            if (!string.IsNullOrEmpty(itemKey) && mc.PoisonClassByItemKey != null
                                               && mc.PoisonClassByItemKey.TryGetValue(itemKey, out string forced))
            {
                return forced;
            }

            if (mc.PoisonClassByDamage != null)
            {
                for (int i = 0; i < mc.PoisonClassByDamage.Count; i++)
                {
                    var band = mc.PoisonClassByDamage[i];
                    if (damage >= band.MinDamage && damage <= band.MaxDamage) return band.Class;
                }
            }

            return "moderate";
        }

        private void TryApplyInstantIfOverThreshold()
        {
            if (entity.World.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer ep) return;
            if (ep.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (!entity.Alive) return;
            if (!Config.ModConfig.Instance.Main.PoisonOnset) return;

            float thresh = Config.ModConfig.Instance.Main.InstantDeathThreshhold;
            if (thresh <= 0f) return;

            float total = TotalQueuedDamage();
            if (total < thresh) return;

            var ds = new DamageSource
            {
                Source = EnumDamageSource.Unknown,
                Type   = EnumDamageType.Poison,
                DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison
            };

            for (int i = 0; i < queue.Count; i++)
            {
                var key = queue[i].ItemKey;
                if (!string.IsNullOrEmpty(key)) Knowledge.MarkHealthKnown(ep, key);
            }

            _applyingInstant = true;
            var wat = entity.WatchedAttributes;
            wat?.SetBool(FlagInstant, true);
            entity.Attributes.MarkPathDirty(FlagInstant);

            try
            {
                SendSeverityWarning(total);
                entity.ReceiveDamage(ds, total);
            }
            finally
            {
                _applyingInstant = false;
                wat?.SetBool(FlagInstant, false);
                entity.Attributes.MarkPathDirty(FlagInstant);
            }

            queue.Clear();
            Save();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (_applyingInstant) return;

            if (entity.World.Side != EnumAppSide.Server) return;
            if (!Config.ModConfig.Instance.Main.PoisonOnset) return;
            if (damage <= 0) return;

            if (damageSource != null
                && damageSource.Type == EnumDamageType.Poison
                && damageSource.Source == EnumDamageSource.Internal)
            {
                var wat = entity.WatchedAttributes;
                string itemKey = wat.GetString("FG.LastEatItemKey", null);
                if (string.IsNullOrEmpty(itemKey)) return;

                float baseMinH = Config.ModConfig.Instance.Main.PoisonOnsetMinHours;
                float baseMaxH = Config.ModConfig.Instance.Main.PoisonOnsetMaxHours;

                string pclass = DeterminePoisonClass(damage, itemKey);
                GetOnsetRangeScaled(baseMinH, baseMaxH, pclass, out float minH, out float maxH);

                SchedulePoisonFromFood(damage, itemKey, minH, maxH, durationSec: 0f, ticks: 0);
                damage = 0f;
                wat.SetBool("FG.PendingReveal", true);
                entity.Attributes.MarkPathDirty("FG.PendingReveal");
            }
        }

        public override void OnGameTick(float dt)
        {
            checkTimerSec += dt;
            if (checkTimerSec < CheckIntervalSec) return;
            checkTimerSec = 0f;

            if (entity.World.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer ep) return;
            if (ep.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (!entity.Alive) return;
            if (!Config.ModConfig.Instance.Main.PoisonOnset) return;
            if (queue.Count == 0) return;

            TryApplyInstantIfOverThreshold();
            if (queue.Count == 0) return;

            double nowHours = entity.World.Calendar.TotalHours;

            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var p = queue[i];
                double remHours = p.TriggerAtHours - nowHours;
                if (remHours > 0) continue;

                var ds = new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type   = EnumDamageType.Poison,
                    DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison,
                };

                if (p.DurationSec.HasValue && p.Ticks.HasValue && p.DurationSec.Value > 0 && p.Ticks.Value > 0)
                {
                    ds.Duration = TimeSpan.FromSeconds(p.DurationSec.Value);
                    ds.TicksPerDuration = p.Ticks.Value;
                }
                SendSeverityWarning(p.Damage);

                entity.ReceiveDamage(ds, p.Damage);

                if (!string.IsNullOrEmpty(p.ItemKey))
                {
                    Knowledge.MarkHealthKnown(ep, p.ItemKey);
                }

                queue.RemoveAt(i);
                Save();
            }
        }

        public void SchedulePoisonFromFood(float damage, string itemKey, float minHours, float maxHours, float durationSec, int ticks)
        {
            if (damage <= 0) return;
            if (maxHours < minHours) (minHours, maxHours) = (maxHours, minHours);

            double delayHours = minHours;
            if (maxHours > minHours)
            {
                double t = entity.World.Rand.NextDouble();
                delayHours = minHours + (maxHours - minHours) * t;
            }

            double triggerAt = entity.World.Calendar.TotalHours + delayHours;

            var pp = new PendingPoison
            {
                Damage = damage,
                ItemKey = itemKey,
                TriggerAtHours = triggerAt,
                DurationSec = durationSec > 0 ? durationSec : null,
                Ticks = ticks > 0 ? ticks : null
            };

            queue.Add(pp);
            Save();
            TryApplyInstantIfOverThreshold();
        }
        
        private void GetOnsetRangeScaled(float baseMin, float baseMax, string pclass, out float minH, out float maxH)
        {
            minH = baseMin;
            maxH = baseMax;

            var mc = Config.ModConfig.Instance.Main;
            if (mc.PoisonOnsetClassScales != null
                && !string.IsNullOrEmpty(pclass)
                && mc.PoisonOnsetClassScales.TryGetValue(pclass, out var scale)
                && scale != null)
            {
                minH = baseMin * scale.MinMul + scale.MinAdd;
                maxH = baseMax * scale.MaxMul + scale.MaxAdd;
            }

            minH = GameMath.Clamp(minH, 0f, 240f);
            maxH = GameMath.Clamp(Math.Max(minH, maxH), 0f, 240f);
        }

        private void SendSeverityWarning(float damage)
        {
            if (entity.World.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer ep) return;
            if (ep.Player is not IServerPlayer sp) return;

            var mc = Config.ModConfig.Instance.Main;

            if (damage >= 50f && entity.World.Rand.NextDouble() < mc.PoisonDeadJimChance)
            {
                sp.SendIngameError("poison", Lang.Get("foragersgamble:poison.warn.deadjim"));
                return;
            }

            string key =
                (damage < 1f)  ? "foragersgamble:poison.warn.lt1"   :
                (damage < 5f)  ? "foragersgamble:poison.warn.1to5"  :
                (damage < 10f) ? "foragersgamble:poison.warn.5to10" :
                (damage < 15f) ? "foragersgamble:poison.warn.10to14":
                (damage < 50f) ? "foragersgamble:poison.warn.15to49":
                "foragersgamble:poison.warn.50plus";

            sp.SendIngameError("poison", Lang.Get(key));
        }

        private void Save()
        {
            if (entity?.WatchedAttributes == null) return;

            var root = entity.WatchedAttributes.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var listRoot = new TreeAttribute();

            listRoot.SetInt("count", queue.Count);
            for (int i = 0; i < queue.Count; i++)
            {
                var q = queue[i];
                var t = new TreeAttribute();
                t.SetFloat("damage", q.Damage);
                t.SetString("itemKey", q.ItemKey ?? "");
                t.SetDouble("triggerAtHours", q.TriggerAtHours);
                if (q.Ticks.HasValue)       t.SetInt("ticks", q.Ticks.Value);
                if (q.DurationSec.HasValue) t.SetFloat("durSec", q.DurationSec.Value);

                listRoot[i.ToString()] = t;
            }

            root[ListKey] = listRoot;

            entity.WatchedAttributes.SetAttribute(AttrRoot, root);
            entity.Attributes.MarkPathDirty(AttrRoot);
        }

        private void Load()
        {
            queue.Clear();

            var root = entity.WatchedAttributes?.GetTreeAttribute(AttrRoot);
            if (root == null) return;

            var listRoot = root.GetTreeAttribute(ListKey);
            if (listRoot == null) return;

            int count = listRoot.GetInt("count", 0);
            double nowHours = entity.World.Calendar.TotalHours;

            for (int i = 0; i < count; i++)
            {
                var t = listRoot.GetTreeAttribute(i.ToString());
                if (t == null) continue;

                double triggerAt;
                if (t.HasAttribute("triggerAtHours"))
                {
                    triggerAt = t.GetDouble("triggerAtHours", nowHours);
                }
                else if (t.HasAttribute("gameSecLeft"))
                {
                    double secLeft = t.GetDouble("gameSecLeft", 0d);
                    triggerAt = nowHours + Math.Max(0d, secLeft) / 3600.0;
                }
                else
                {
                    continue;
                }

                var pp = new PendingPoison
                {
                    Damage         = t.GetFloat("damage", 0f),
                    ItemKey        = t.GetString("itemKey", null),
                    TriggerAtHours = triggerAt,
                    Ticks          = t.HasAttribute("ticks")  ? t.GetInt("ticks")   : (int?)null,
                    DurationSec    = t.HasAttribute("durSec") ? t.GetFloat("durSec") : (float?)null
                };

                if (pp.Damage > 0)
                    queue.Add(pp);
            }
        }

        public void ClearAll(bool persist = true)
        {
            queue.Clear();

            if (!persist) return;
            var wat  = entity?.WatchedAttributes;
            if (wat == null) return;

            var root = wat.GetTreeAttribute(AttrRoot) ?? new TreeAttribute();
            var empty = new TreeAttribute();
            empty.SetInt("count", 0);
            root[ListKey] = empty;

            wat.SetAttribute(AttrRoot, root);
            entity.Attributes.MarkPathDirty(AttrRoot);
        }
    }
}
