using System;
using System.Linq;
using ForagersGamble.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ForagersGamble.Handbook;

public class HandbookBlock
{
	private readonly ICoreClientAPI capi;
	
	public HandbookBlock(ICoreClientAPI capi)
	{
		this.capi = capi;
	}

	public void OnAnyHotkey(string hotkeyCode, KeyCombination comb)
	{
		if (hotkeyCode != "handbook") return;

		var cfg = ModConfig.Instance?.Main;
		if (cfg?.PreventHandbookOnUnidentified != true) return;

		var player = capi?.World?.Player;
		if (player == null) return;
		if (player.WorldData?.CurrentGameMode != EnumGameMode.Survival) return;

		var slot = player.InventoryManager?.CurrentHoveredSlot;
		if (slot == null || slot.Empty) return;

		var stack = slot.Itemstack;
		if (stack == null) return;

		var world = capi.World;
		var agent = player.Entity as EntityAgent;
		var coll = stack.Collectible;
		bool IsEdibleProps(FoodNutritionProperties p) =>
			p != null && p.FoodCategory != EnumFoodCategory.Unknown && p.FoodCategory != EnumFoodCategory.NoNutrition;

		bool TryResolveFruitFromFruitTreeBlock(Block asBlock, ItemStack srcStack, out ItemStack fruitStack)
		{
			fruitStack = null;
			if (capi?.World == null || asBlock == null) return false;
			bool isFruitTreePart =
				asBlock is BlockFruitTreeBranch
				|| asBlock is BlockFruitTreeFoliage
				|| (asBlock.GetType().Name.IndexOf("FruitTree", StringComparison.OrdinalIgnoreCase) >= 0)
				|| (asBlock.Code?.Path?.IndexOf("fruittree", StringComparison.OrdinalIgnoreCase) >= 0);

			if (!isFruitTreePart) return false;

			var propsObj = asBlock.Attributes?["fruittreeProperties"];
			if (propsObj == null || !propsObj.Exists) return false;

			string type =
				srcStack?.Attributes?.GetString("type", null) ??
				srcStack?.Attributes?.GetString("fruitTreeType", null) ??
				asBlock.Variant?["type"];
			if (string.IsNullOrWhiteSpace(type))
			{
				var path = asBlock.Code?.Path ?? "";
				try
				{
					var jtok = Newtonsoft.Json.Linq.JToken.Parse(propsObj.ToString());
					if (jtok is Newtonsoft.Json.Linq.JObject dict)
					{
						foreach (var kv in dict)
						{
							var key = kv.Key;
							if (!string.IsNullOrWhiteSpace(key) &&
							    path.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
							{
								type = key;
								break;
							}
						}
					}
				}
				catch
				{
					/* ignore */
				}
			}

			if (string.IsNullOrWhiteSpace(type)) return false;

			var typeNode = propsObj[type];
			if (typeNode == null || !typeNode.Exists) return false;

			var stacksNode = typeNode["fruitStacks"];
			if (stacksNode == null || !stacksNode.Exists) return false;

			string produceCode = null;
			try
			{
				var arr = stacksNode.AsArray();
				if (arr != null && arr.Length > 0)
				{
					var codeStr = arr[0]["code"].AsString(null);
					if (!string.IsNullOrWhiteSpace(codeStr)) produceCode = codeStr.Trim();
				}
			}
			catch
			{
				/* ignore */
			}

			if (string.IsNullOrWhiteSpace(produceCode)) return false;

			var al = new AssetLocation(produceCode);

			var item = capi.World.GetItem(al);
			if (item != null)
			{
				var test = new ItemStack(item);
				var np = item.GetNutritionProperties(capi.World, test, null);
				if (IsEdibleProps(np))
				{
					fruitStack = test;
					return true;
				}
			}

			var block = capi.World.GetBlock(al);
			if (block != null)
			{
				var test = new ItemStack(block);
				var np = block.GetNutritionProperties(capi.World, test, null);
				if (IsEdibleProps(np))
				{
					fruitStack = test;
					return true;
				}
			}

			return false;
		}
		bool isEdible = false;
		{
			var props = coll?.GetNutritionProperties(world, stack, agent as EntityPlayer);
			isEdible = IsEdibleProps(props);
		}
		bool isGatedPlant = false;
		Block asBlock = null;
		{
			asBlock = stack.Block ?? coll as Block;
			if (asBlock != null)
			{
				var idx = PlantKnowledgeIndex.Get(capi);
				if (idx != null)
				{
					var code = asBlock.Code?.ToString() ?? "";
					isGatedPlant = idx.IsKnowledgeGated(code);
				}
				else
				{
					isGatedPlant = PlantKnowledgeUtil.IsKnowledgeGatedPlant(asBlock);
				}
			}
		}
		bool isMushroom = false;
		{
			var idx = PlantKnowledgeIndex.Get(capi);
			var key = ForagersGamble.Knowledge.ItemKey(stack);
			isMushroom = idx != null && !string.IsNullOrEmpty(key) && idx.IsMushroom(key);
		}
		bool guardPlants = cfg.UnknownAll == true || cfg.UnknownPlants;
		bool guardMushrooms = cfg.UnknownAll == true || cfg.UnknownMushrooms;
		bool guardEverything = cfg.UnknownAll == true;
		if (!guardPlants && !guardMushrooms && !guardEverything) return;

		bool shouldGateThis =
			guardEverything
			|| (guardPlants && (isEdible || isGatedPlant))
			|| (guardMushrooms && isMushroom);

		if (!shouldGateThis) return;
		var codeKey = ForagersGamble.Knowledge.ItemKey(stack);
		bool knownEnough = ForagersGamble.Knowledge.IsKnown(player.Entity, codeKey);
		if (!knownEnough && isGatedPlant && asBlock != null)
		{
			if (TryResolveFruitFromFruitTreeBlock(asBlock, stack, out var fruitFromAttrs) && fruitFromAttrs != null)
			{
				var fkey = ForagersGamble.Knowledge.ItemKey(fruitFromAttrs);
				if (!string.IsNullOrEmpty(fkey) && ForagersGamble.Knowledge.IsKnown(player.Entity, fkey))
				{
					knownEnough = true;
				}
			}
			if (!knownEnough)
			{
				var idx = PlantKnowledgeIndex.Get(capi);
				if (idx != null && idx.TryGetFruit(asBlock.Code?.ToString(), out var fr))
				{
					ItemStack fruitStack = null;
					if (fr.Type == EnumItemClass.Item)
					{
						var it = capi.World.GetItem(fr.Code);
						if (it != null) fruitStack = new ItemStack(it);
					}
					else
					{
						var bl = capi.World.GetBlock(fr.Code);
						if (bl != null) fruitStack = new ItemStack(bl);
					}

					if (fruitStack != null)
					{
						var fkey = ForagersGamble.Knowledge.ItemKey(fruitStack);
						if (!string.IsNullOrEmpty(fkey) && ForagersGamble.Knowledge.IsKnown(player.Entity, fkey))
							knownEnough = true;
					}
				}
			}
			if (!knownEnough)
			{
				if (PlantKnowledgeUtil.TryResolveReferenceFruit(capi, asBlock, stack, out var fruitRef) &&
				    fruitRef != null)
				{
					var fkey = ForagersGamble.Knowledge.ItemKey(fruitRef);
					if (!string.IsNullOrEmpty(fkey) && ForagersGamble.Knowledge.IsKnown(player.Entity, fkey))
						knownEnough = true;
				}
			}
		}
		if (knownEnough) return;
		CloseHandbookIfOpen();
		capi.Event.EnqueueMainThreadTask(() => CloseHandbookIfOpen(), "fg-close-handbook-1");
		capi.Event.EnqueueMainThreadTask(() => CloseHandbookIfOpen(), "fg-close-handbook-2");
		capi.TriggerIngameError(this, "notidentified", Lang.Get("foragersgamble:unidentified"));
	}

	private void CloseHandbookIfOpen()
	{
		var dlg = capi.Gui.LoadedGuis?.FirstOrDefault(d => d is Vintagestory.GameContent.GuiDialogHandbook);
		if (dlg != null && dlg.IsOpened())
		{
			dlg.TryClose();
		}
	}
}