﻿using System.Collections.Generic;
using ForagersGamble.Behaviors;
using ForagersGamble.Config;
using ForagersGamble.KnowledgeBooks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ForagersGamble;

public class ForagersGambleModSystem : ModSystem
{
	public const string HarmonyID = "com.chronolegionnaire.foragersgamble";
	private Harmony harmony;
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
	public override void Dispose()
	{
		ConfigManager.UnloadModConfig();
		harmony?.UnpatchAll(HarmonyID);
		base.Dispose();
	}
}