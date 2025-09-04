using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using ForagersGamble.Config.SubConfigs;


namespace ForagersGamble.Config;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class ModConfig
{
    public const string ConfigPath = "ForagersGambleConfig.json";

    public static ModConfig Instance { get; internal set; }
    
    /// <summary>
    /// The configuration for advanced user options
    /// </summary>
    public MainConfig Main { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JToken> LegacyData { get; set; }
}
