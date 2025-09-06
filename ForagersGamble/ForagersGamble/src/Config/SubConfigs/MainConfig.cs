using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace ForagersGamble.Config.SubConfigs;

public class MainConfig
{
    [Category("Main")]
    [DefaultValue(false)]
    public bool UnknownAll { get; set; } = false;
    
    [Category("Main")]
    [DefaultValue(true)]
    public bool UnknownMushrooms { get; set; } = true;
    
    [Category("Main")]
    [DefaultValue(true)]
    public bool UnknownPlants { get; set; } = true;
    
    [Category("Main")]
    [DefaultValue(true)]
    public bool ForgetOnDeath { get; set; } = true;
    
    [Category("Main")]
    [DefaultValue(true)]
    public bool HideNutritionInfo { get; set; } = true;

    [Category("Main")]
    [DefaultValue(true)]
    public bool HideCraftingInfo { get; set; } = true;
}