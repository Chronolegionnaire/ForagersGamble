using ImGuiNET;
using System;
using ConfigLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ForagersGamble.Config;

public class ConfigLibCompatibility
{
    private ConfigLibCompatibility()
    {
    }

    /// <summary>
    /// A copy of the ModConfig since we don't want to mutate the live config before we are sure everything is valid
    /// </summary>
    public ModConfig EditInstance { get; private set; }

    private static ModConfig LoadFromDisk(ICoreAPI api)
    {
        try
        {
            return api.LoadModConfig<ModConfig>(ModConfig.ConfigPath) ?? new ModConfig();
        }
        catch(Exception ex)
        {
            api.Logger.Error(ex);
            return new ModConfig();
        }
    }

    internal static void Init(ICoreClientAPI api)
    {
        var container = new ConfigLibCompatibility();
        api.ModLoader.GetModSystem<ConfigLibModSystem>().RegisterCustomConfig("foragersgamble", (id, buttons) =>
        {
            container.EditConfig(id, buttons, api);
            
            return new ControlButtons
            {
                Save = true,
                Restore = true,
                Defaults = true,
                Reload = api.IsSinglePlayer //There currently isn't any logic for re-sending configs to server and connected clients
            };
        });
    }

    private void EditConfig(string id, ControlButtons buttons, ICoreClientAPI api)
    {
        //Ensure we have a copy of the config ready (late initialized because we only need this if the editor was opened)
        EditInstance ??= ModConfig.Instance.JsonCopy();
        
        Edit(EditInstance, id);

        if (buttons.Save) ConfigManager.SaveModConfig(api, EditInstance);
        else if (buttons.Restore) EditInstance = LoadFromDisk(api);
        else if (buttons.Defaults) EditInstance = new ModConfig();
        else if (buttons.Reload) //Reload is for hot reloading config values
        {
            if (api.IsSinglePlayer)
            {
                ModConfig.Instance = EditInstance;
                EditInstance = null;
                ConfigManager.StoreModConfigToWorldConfig(api);
            }
            else
            {
                //TODO: maybe support reloading (at least part of) the config
            }
            
        }
    }

    private static void Edit(ModConfig config, string id)
    {
        ImGui.TextWrapped("Foragers Gamble Settings");
    }
}
