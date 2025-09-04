
using ForagersGamble.Config;
using HarmonyLib;
using Vintagestory.API.Common;

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

	public override void Dispose()
	{
		ConfigManager.UnloadModConfig();
		harmony?.UnpatchAll(HarmonyID);
		base.Dispose();
	}
}