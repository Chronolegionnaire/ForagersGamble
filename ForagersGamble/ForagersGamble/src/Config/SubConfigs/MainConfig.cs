using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ForagersGamble.Config.SubConfigs
{
    public class OnsetScale
    {
        [Range(0, 10)] [DefaultValue(1f)] public float MinMul { get; set; } = 1f;
        [Range(0, 10)] [DefaultValue(1f)] public float MaxMul { get; set; } = 1f;
        [Range(0, 240)] [DefaultValue(0f)] public float MinAdd { get; set; } = 0f;
        [Range(0, 240)] [DefaultValue(0f)] public float MaxAdd { get; set; } = 0f;
    }

    public class DamageClassBand
    {
        [DefaultValue(0f)]   public float MinDamage { get; set; } = 0f;
        [DefaultValue(0f)]   public float MaxDamage { get; set; } = 0f;
        [DefaultValue("moderate")] public string Class { get; set; } = "moderate";
    }

    public class MainConfig
    {
        [Category("Main")]
        [DefaultValue(false)]
        public bool UnknownAll { get; set; } = false;

        [Category("Main")] [DefaultValue(true)]  public bool UnknownMushrooms { get; set; } = true;
        [Category("Main")] [DefaultValue(true)]  public bool UnknownPlants   { get; set; } = true;
        [Category("Main")] [DefaultValue(true)]  public bool ForgetOnDeath   { get; set; } = true;
        [Category("Main")] [DefaultValue(true)]  public bool HideNutritionInfo { get; set; } = true;
        [Category("Main")] [DefaultValue(true)]  public bool HideCraftingInfo  { get; set; } = true;

        [Category("Main")]
        [Display(Name = "Hide Meal Safety")]
        [DefaultValue(true)]
        public bool HideMealSafety { get; set; } = true;

        [Category("Main")]
        [Display(Name = "Nibble Factor", Description = "Fraction of a full eat action applied when nibbling (0.0 - 1.0).")]
        [Range(0.0, 1.0)]
        [DefaultValue(0.1f)]
        public float NibbleFactor { get; set; } = 0.1f;

        [Category("Main")]
        [Display(Name = "Delayed Poison Onset")]
        [DefaultValue(true)]
        public bool PoisonOnset { get; set; } = true;

        [Category("Main")]
        [Display(Name = "Poison Onset Min (hours, in-game)")]
        [Range(0, 240)]
        [DefaultValue(12f)]
        public float PoisonOnsetMinHours { get; set; } = 12f;

        [Category("Main")]
        [Display(Name = "Poison Onset Max (hours, in-game)")]
        [Range(0, 240)]
        [DefaultValue(24f)]
        public float PoisonOnsetMaxHours { get; set; } = 24f;

        [Category("Main")]
        [Display(
            Name = "Instant Death Threshold",
            Description = "If total queued delayed-poison damage is at or above this value, apply it immediately."
        )]
        [Range(-1, 100000)]
        [DefaultValue(100f)]
        public float InstantDeathThreshhold { get; set; } = 30f;

        [Category("Poison")]
        [Display(Name = "Easter Egg Chance (≥50 dmg)", Description = "Chance (0-1) that 50+ damage uses the \"He's dead, Jim.\" line.")]
        [Range(0, 1)]
        [DefaultValue(0.001f)]
        public float PoisonDeadJimChance { get; set; } = 0.001f;

        [Category("Poison")]
        [Display(Name = "Onset Multipliers by Class", Description = "Per-class scaling for onset. min/max are applied to the base onset range, then clamped to 0..240 hours.")]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, OnsetScale> PoisonOnsetClassScales { get; set; } = new()
        {
            ["weak"]     = new OnsetScale { MinMul = 0.50f, MaxMul = 0.75f },
            ["moderate"] = new OnsetScale { MinMul = 1.00f, MaxMul = 1.00f },
            ["strong"]   = new OnsetScale { MinMul = 1.25f, MaxMul = 1.50f },
            ["severe"]   = new OnsetScale { MinMul = 1.75f, MaxMul = 2.00f },
            ["fatal"]    = new OnsetScale { MinMul = 2.50f, MaxMul = 3.00f },
            ["lethal"]   = new OnsetScale { MinMul = 3.50f, MaxMul = 4.00f },
        };

        [Category("Poison")]
        [Display(Name = "Class by Damage Bands", Description = "If no per-item class override is set, these bands map damage to a poison class.")]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<DamageClassBand> PoisonClassByDamage { get; set; } = new()
        {
            new DamageClassBand { MinDamage = 0f,     MaxDamage = 1f,          Class = "weak"     },
            new DamageClassBand { MinDamage = 1.01f,  MaxDamage = 5f,          Class = "moderate" },
            new DamageClassBand { MinDamage = 5.01f,  MaxDamage = 10f,         Class = "strong"   },
            new DamageClassBand { MinDamage = 10.01f, MaxDamage = 15f,         Class = "severe"   },
            new DamageClassBand { MinDamage = 15.01f, MaxDamage = 49.999f,     Class = "fatal"    },
            new DamageClassBand { MinDamage = 50f,    MaxDamage = float.MaxValue, Class = "lethal" },
        };

        [Category("Poison")]
        [Display(Name = "Per-Item Poison Class", Description = "Optional explicit mapping from itemKey to poison class (e.g. game:unknown-mushroom -> strong).")]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, string> PoisonClassByItemKey { get; set; } = new();
    }
}
