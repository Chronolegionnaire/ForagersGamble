using System;
using System.Linq;
using System.Text;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ForagersGamble.KnowledgeBooks
{
    public class ItemKnowledgeBook : Item
    {
        private ICoreClientAPI capi;
        private const string AttrRootPlayer = "foragersGamble";
        private const string KnownSet       = "knownFoods";
        private const string KnownHealthSet = "knownHealth";
        private const string BookRoot         = "fgBook";
        private const string BookFoodsKey     = "foods";
        private const string BookHealthKey    = "health";
        private const string BookSealedKey    = "sealed";
        private const string BookTitleKey     = "title";
        private const string BookSignedByKey  = "signedby";
        private const string BookCreatedAtKey = "createdAt";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            capi = api as ICoreClientAPI;
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            var title = itemStack?.Attributes?.GetString(BookTitleKey, null);
            return !string.IsNullOrEmpty(title) ? title : base.GetHeldItemName(itemStack);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var stack = inSlot?.Itemstack;
            if (stack == null) return;

            var root = stack.Attributes.GetTreeAttribute(BookRoot);
            if (root != null && root.GetBool(BookSealedKey))
            {
                var by   = stack.Attributes.GetString(BookSignedByKey, Lang.Get("foragersgamble:kb.author.unknown"));
                var when = stack.Attributes.GetString(BookCreatedAtKey, null);

                dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.inscribedby", by));
                if (!string.IsNullOrEmpty(when))
                    dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.inscribedon", when));

                var foodsLen  = (root[BookFoodsKey]  as StringArrayAttribute)?.value?.Length ?? 0;
                var healthLen = (root[BookHealthKey] as StringArrayAttribute)?.value?.Length ?? 0;
                if (foodsLen + healthLen > 0)
                    dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.containsnotes", foodsLen + healthLen));

                dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.studyhint"));
            }
            else
            {
                dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.blank"));
                dsc.AppendLine(Lang.Get("foragersgamble:kb.tooltip.inscribehint"));
            }
        }

        public override void OnHeldInteractStart(
            ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity == null || slot?.Itemstack == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            if (byEntity.Controls.ShiftKey)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            var player = (byEntity as EntityPlayer)?.Player;
            if (player == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;

            var stack = slot.Itemstack;
            var offhand = GetOtherHandSlot(byEntity, slot);

            if (IsSealed(stack))
            {
                if (IsWritingTool(offhand))
                {
                    Feedback(player, Lang.Get("foragersgamble:kb.msg.cannotstudywithquill"));
                    return;
                }

                ApplyBookKnowledgeToPlayer(stack, byEntity);
                if (ModConfig.Instance.Main.JournalConsumeOnLearn)
                {
                    ConsumeOne(slot);
                }
                Feedback(player, Lang.Get("foragersgamble:kb.msg.studied"));
                return;
            }

            if (!IsWritingTool(offhand))
            {
                Feedback(player, Lang.Get("foragersgamble:kb.msg.needquill"));
                return;
            }

            var pname = player.PlayerName ?? Lang.Get("foragersgamble:kb.author.unknown");
            var title = Lang.Get("foragersgamble:kb.title.format", pname);

            WritePayloadFromPlayer(stack, player);
            stack.Attributes.SetString(BookTitleKey, title);
            stack.Attributes.SetString(BookSignedByKey, pname);
            Seal(stack);

            slot.MarkDirty();
            Feedback(player, Lang.Get("foragersgamble:kb.msg.inscribed"));
        }
        private static ItemSlot GetOtherHandSlot(EntityAgent entity, ItemSlot thisSlot)
        {
            var ep  = entity as EntityPlayer;
            var inv = ep?.Player?.InventoryManager;
            if (inv == null) return null;

            var right = inv.ActiveHotbarSlot;
            var left  = entity.LeftHandItemSlot;

            if (right == thisSlot) return left;
            if (left  == thisSlot) return right;
            return left ?? right;
        }

        private static bool IsWritingTool(ItemSlot slot)
        {
            var it = slot?.Itemstack;
            if (it == null) return false;
            var attrs = it.Collectible?.Attributes;
            return attrs != null && attrs.IsTrue("writingTool");
        }

        private static bool IsSealed(ItemStack book)
        {
            var root = book?.Attributes?.GetTreeAttribute(BookRoot);
            return root != null && root.GetBool(BookSealedKey, false);
        }

        private static void Seal(ItemStack book)
        {
            var root = book.Attributes.GetOrAddTreeAttribute(BookRoot);
            root.SetBool(BookSealedKey, true);

            if (book.Attributes.GetString(BookSignedByKey, null) == null)
            {
                book.Attributes.SetString(BookSignedByKey, "");
            }
        }

        private void WritePayloadFromPlayer(ItemStack book, IPlayer player)
        {
            var foods  = ReadPlayerSet(player, KnownSet);
            var health = ReadPlayerSet(player, KnownHealthSet);

            var root = book.Attributes.GetOrAddTreeAttribute(BookRoot);
            root[BookFoodsKey]  = new StringArrayAttribute(foods);
            root[BookHealthKey] = new StringArrayAttribute(health);
            root.SetBool(BookSealedKey, true);
        }

        private static string[] ReadPlayerSet(IPlayer player, string key)
        {
            var root = player?.Entity?.WatchedAttributes?.GetTreeAttribute(AttrRootPlayer);
            var arr  = root?[key] as StringArrayAttribute;
            return (arr?.value ?? Array.Empty<string>()).ToArray();
        }

        private void ApplyBookKnowledgeToPlayer(ItemStack book, EntityAgent entity)
        {
            var root = book.Attributes.GetTreeAttribute(BookRoot);
            if (root == null) return;

            var foods  = (root[BookFoodsKey]  as StringArrayAttribute)?.value ?? Array.Empty<string>();
            var health = (root[BookHealthKey] as StringArrayAttribute)?.value ?? Array.Empty<string>();

            foreach (var code in foods)  ForagersGamble.Knowledge.MarkKnown(entity, code);
            foreach (var code in health) ForagersGamble.Knowledge.MarkHealthKnown(entity, code);
        }

        private static void ConsumeOne(ItemSlot slot)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        private void Feedback(IPlayer player, string msg)
        {
            if (api.Side == EnumAppSide.Server)
            {
                var sapi = api as ICoreServerAPI;
                sapi?.SendMessage(player, GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
            }
            else
            {
                capi?.TriggerIngameError(this, "fgbookinfo", msg);
            }
        }
    }
}
