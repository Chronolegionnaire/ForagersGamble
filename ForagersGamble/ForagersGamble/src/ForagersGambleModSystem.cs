
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;
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
	public override void AssetsFinalize(ICoreAPI api)
	{
		base.AssetsFinalize(api);
		var idx = PlantKnowledgeIndex.Build(api);
		PlantKnowledgeIndex.Put(api, idx);
	}
	public override void StartServerSide(ICoreServerAPI sapi)
	{
		base.StartServerSide(sapi);

		sapi.Event.PlayerDeath += (player, damageSource) =>
		{
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