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
    public class EntityBehaviorDelayedPsychedelic : EntityBehavior
    {
        private const string AttrRoot = "foragersGamble.delayedPsychedelic";
        private const string ListKey = "queue";
        private const float CheckIntervalSec = 2f;

        private float checkTimerSec = 0f;

        private class PendingPsychedelic
        {
            public float Amount;
            public string ItemKey;
            public double TriggerAtHours;
        }
        private readonly List<PendingPsychedelic> queue = new();

        public EntityBehaviorDelayedPsychedelic(Entity entity) : base(entity) { }

        public override string PropertyName() => "fgDelayedPsychedelic";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            Load();
            if (queue.Count > 1)
            {
                KeepOnlyHighestQueued();
                Save();
            }
        }

        private void KeepOnlyHighestQueued()
        {
            if (queue.Count <= 1) return;

            PendingPsychedelic highest = queue[0];
            for (int i = 1; i < queue.Count; i++)
            {
                var cur = queue[i];
                if (cur.Amount > highest.Amount)
                {
                    highest = cur;
                }
            }

            queue.Clear();
            queue.Add(highest);
        }

        public void ScheduleFromFood(float amount, string itemKey, float minHours, float maxHours)
        {
            if (amount <= 0f) return;
            if (maxHours < minHours) (minHours, maxHours) = (maxHours, minHours);

            double delayHours = minHours;
            if (maxHours > minHours)
            {
                double t = entity.World.Rand.NextDouble();
                delayHours = minHours + (maxHours - minHours) * t;
            }

            var pending = new PendingPsychedelic
            {
                Amount = amount,
                ItemKey = itemKey,
                TriggerAtHours = entity.World.Calendar.TotalHours + delayHours
            };
            if (queue.Count == 0)
            {
                queue.Add(pending);
                Save();
                return;
            }

            var existing = queue[0];
            if (pending.Amount <= existing.Amount)
            {
                return;
            }
            queue[0] = pending;
            Save();
        }

        public override void OnGameTick(float dt)
        {
            if (entity is not EntityPlayer ep) return;
            if (ep.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;
            if (!entity.Alive) return;

            checkTimerSec += dt;
            if (checkTimerSec < CheckIntervalSec) return;
            checkTimerSec = 0f;

            if (entity.World.Side != EnumAppSide.Server) return;
            if (!Config.ModConfig.Instance.Main.PsychedelicOnset) return;
            if (queue.Count == 0) return;

            double nowHours = entity.World.Calendar.TotalHours;
            var p = queue[0];

            if (p.TriggerAtHours > nowHours) return;

            float current = entity.WatchedAttributes.GetFloat("psychedelic", 0f);

            SendOnsetWarning(p.Amount, p.ItemKey);

            if (p.Amount > current)
            {
                entity.WatchedAttributes.SetFloat("psychedelic", p.Amount);
                entity.Attributes.MarkPathDirty("psychedelic");
            }

            queue.Clear();
            Save();
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
                t.SetFloat("amount", q.Amount);
                t.SetString("itemKey", q.ItemKey ?? "");
                t.SetDouble("triggerAtHours", q.TriggerAtHours);
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
            var listRoot = root?.GetTreeAttribute(ListKey);
            if (listRoot == null) return;

            int count = listRoot.GetInt("count", 0);
            double nowHours = entity.World.Calendar.TotalHours;

            for (int i = 0; i < count; i++)
            {
                var t = listRoot.GetTreeAttribute(i.ToString());
                if (t == null) continue;

                var pp = new PendingPsychedelic
                {
                    Amount = t.GetFloat("amount", 0f),
                    ItemKey = t.GetString("itemKey", null),
                    TriggerAtHours = t.GetDouble("triggerAtHours", nowHours)
                };

                if (pp.Amount > 0f)
                    queue.Add(pp);
            }
        }

        private string ResolveItemName(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey))
                return Lang.Get("foragersgamble:unknown-food");

            try
            {
                var loc = AssetLocation.Create(itemKey);
                CollectibleObject col =
                    entity.World.GetItem(loc) ??
                    (entity.World.GetBlock(loc) as CollectibleObject);

                if (col != null)
                {
                    var stack = new ItemStack(col);
                    return col.GetHeldItemName(stack);
                }

                return Lang.Get(itemKey);
            }
            catch
            {
                return Lang.Get("foragersgamble:unknown-food");
            }
        }

        private void SendOnsetWarning(float amount, string itemKey = null)
        {
            if (entity.World.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer ep) return;
            if (ep.Player is not IServerPlayer sp) return;

            var mc = Config.ModConfig.Instance.Main;
            bool showFood = mc.ShowFoodInWarning;

            string baseKey =
                (amount < 0.25f) ? "foragersgamble:psychedelic.warn.low" :
                (amount < 0.75f) ? "foragersgamble:psychedelic.warn.medium" :
                "foragersgamble:psychedelic.warn.high";

            string key = showFood ? baseKey : (baseKey + ".plain");

            if (showFood)
            {
                string foodName = "(unknown)";
                if (!string.IsNullOrEmpty(itemKey))
                {
                    bool discovered = Knowledge.IsKnown(ep, itemKey);
                    if (discovered) foodName = ResolveItemName(itemKey);
                }

                sp.SendIngameError("psychedelic", Lang.Get(key, foodName));
            }
            else
            {
                sp.SendIngameError("psychedelic", Lang.Get(key));
            }
        }

        public void ClearAll(bool persist = true)
        {
            queue.Clear();

            if (!persist) return;
            var wat = entity?.WatchedAttributes;
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