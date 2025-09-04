using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace ForagersGamble.Config.SubConfigs;

public class MainConfig
{
    [Category("Main")]
    [DefaultValue(false)]
    public bool HideName { get; set; } = false;
    
    [Category("Main")]
    [DefaultValue(true)]
    public bool UnknownMushrooms { get; set; } = true;
}