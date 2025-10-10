using System;
using System.Collections.Generic;
using System.Linq;
using ForagersGamble.Behaviors;
using ForagersGamble.Config;
using ForagersGamble.KnowledgeBooks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ForagersGamble;

public class ForagersGambleModSystem : ModSystem
{
	public const string HarmonyID = "com.chronolegionnaire.foragersgamble";
	private Harmony harmony;
	ICoreClientAPI capi;
	public override void StartPre(ICoreAPI api)
	{
		base.StartPre(api);
		ConfigManager.EnsureModConfigLoaded(api);

		if (!Harmony.HasAnyPatches(HarmonyID))
		{
			harmony = new Harmony(HarmonyID);
            
			harmony.PatchAllUncategorized();
		}
	}
	public override void Start(ICoreAPI api)
	{
		base.Start(api);
		api.RegisterEntityBehaviorClass("fgDelayedPoison", typeof(EntityBehaviorDelayedPoison));
		api.RegisterItemClass("ItemKnowledgeBook", typeof(ItemKnowledgeBook));
	}
	public override void AssetsFinalize(ICoreAPI api)
	{
		base.AssetsFinalize(api);
		var idx = PlantKnowledgeIndex.Build(api);
		PlantKnowledgeIndex.Put(api, idx);
		if (api.Side == EnumAppSide.Client) return;

		if (Config.ModConfig.Instance.Main.PoisonOnset)
		{
			var playerEntity = api.World.GetEntityType(new AssetLocation("game", "player"));

			var fgBehaviors = new List<JsonObject>(1)
			{
				new(new JObject { ["code"] = "fgDelayedPoison" })
			};

			playerEntity.Server.BehaviorsAsJsonObj = [
				..playerEntity.Server.BehaviorsAsJsonObj,
				..fgBehaviors
			];
			playerEntity.Client.BehaviorsAsJsonObj = [
				..playerEntity.Client.BehaviorsAsJsonObj,
				..fgBehaviors
			];
		}
		if (Config.ModConfig.Instance?.Main?.RandomizeDamagingItems == true)
		{
			new ForagersGamble.Randomize.Randomizer().RandomizeFoodHealth(api);
		}
	}
	public override void StartServerSide(ICoreServerAPI sapi)
	{
		base.StartServerSide(sapi);

		sapi.Event.PlayerDeath += (player, damageSource) =>
		{
			var beh = player?.Entity?.GetBehavior<EntityBehaviorDelayedPoison>();
			beh?.ClearAll();
			if (ModConfig.Instance?.Main?.ForgetOnDeath == true)
			{
				Knowledge.ForgetAll(player);
			}
		};
	}

	
	public override void StartClientSide(ICoreClientAPI capi)
	{
		this.capi = capi;

		if (ModConfig.Instance.Main.PreventHandbookOnUnidentified)
		{
			capi.Input.AddHotkeyListener(OnAnyHotkey);
		}
		if(capi.ModLoader.IsModEnabled("configlib")) ConfigLibCompatibility.Init(capi);
	}

	private void OnAnyHotkey(string hotkeyCode, KeyCombination comb)
	{
		if (hotkeyCode != "handbook") return;
		if (ModConfig.Instance?.Main?.PreventHandbookOnUnidentified != true) return;

		var player = capi.World?.Player;
		if (player == null) return;
		var gm = player.WorldData?.CurrentGameMode ?? EnumGameMode.Survival;
		if (gm != EnumGameMode.Survival) return;

		var slot = player.InventoryManager?.CurrentHoveredSlot;
		if (slot == null || slot.Empty) return;

		var stack = slot.Itemstack;
		var coll  = stack.Collectible;
		var world = capi.World;
		var agent = player.Entity as EntityAgent;
		bool isEdible = false;
		{
			var props = coll?.GetNutritionProperties(world, stack, agent as EntityPlayer);
			isEdible = props != null
			           && props.FoodCategory != EnumFoodCategory.Unknown
			           && props.FoodCategory != EnumFoodCategory.NoNutrition;
		}

		bool isGatedPlant = false;
		{
			var asBlock = stack.Block ?? coll as Block;
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
		if (!isEdible && !isGatedPlant) return;
		var codeKey = ForagersGamble.Knowledge.ItemKey(stack);
		var known   = ForagersGamble.Knowledge.IsKnown(player.Entity, codeKey);
		if (known) return;
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
	public override void Dispose()
	{
		ConfigManager.UnloadModConfig();
		harmony?.UnpatchAll(HarmonyID);
		base.Dispose();
	}
}