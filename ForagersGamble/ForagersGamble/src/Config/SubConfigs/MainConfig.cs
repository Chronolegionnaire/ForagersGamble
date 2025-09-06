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
    
    [Category("Main")]
    [Display(Name = "Hide Meal Safety")]
    [DefaultValue(true)]
    public bool HideMealSafety { get; set; } = true;
    
    [Category("Main")]
    [Display(Name = "Nibble Factor", Description = "Fraction of a full eat action applied when nibbling (0.0 - 1.0).")]
    [Range(0.0, 1.0)]
    [DefaultValue(0.1f)]
    public float NibbleFactor { get; set; } = 0.1f;
}