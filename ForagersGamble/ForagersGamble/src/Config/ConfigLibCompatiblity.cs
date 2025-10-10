using ImGuiNET;
using System;
using System.Collections.Generic;
using ConfigLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using ForagersGamble.Config.SubConfigs;

namespace ForagersGamble.Config
{
    public class ConfigLibCompatibility
    {
        // ===== Language keys =====
        private const string title = "foragersgamble:Config.Title";

        private const string sectionMain = "foragersgamble:Config.Section.Main";
        private const string sectionPoison = "foragersgamble:Config.Section.Poison";
        private const string sectionKnowledge = "foragersgamble:Config.Section.Knowledge";

        // Main settings
        private const string settingUnknownAll = "foragersgamble:Config.Setting.UnknownAll";
        private const string settingUnknownMushrooms = "foragersgamble:Config.Setting.UnknownMushrooms";
        private const string settingUnknownPlants = "foragersgamble:Config.Setting.UnknownPlants";
        private const string settingForgetOnDeath = "foragersgamble:Config.Setting.ForgetOnDeath";
        private const string settingHideNutritionInfo = "foragersgamble:Config.Setting.HideNutritionInfo";
        private const string settingHideCraftingInfo = "foragersgamble:Config.Setting.HideCraftingInfo";
        private const string settingHideMealSafety = "foragersgamble:Config.Setting.HideMealSafety";
        private const string settingNibbleFactor = "foragersgamble:Config.Setting.NibbleFactor";

        private const string settingPoisonOnset = "foragersgamble:Config.Setting.PoisonOnset";
        private const string settingPoisonOnsetMin = "foragersgamble:Config.Setting.PoisonOnsetMin";
        private const string settingPoisonOnsetMax = "foragersgamble:Config.Setting.PoisonOnsetMax";
        private const string settingInstantDeathThreshold = "foragersgamble:Config.Setting.InstantDeathThreshold";
        private const string settingShowFoodInWarning = "foragersgamble:Config.Setting.ShowFoodInWarning";

        private const string settingRandomizeDamagingItems = "foragersgamble:Config.Setting.RandomizeDamagingItems";
        private const string settingRandomizeHealingItems = "foragersgamble:Config.Setting.RandomizeHealingItems";

        // Poison
        private const string settingDeadJimChance = "foragersgamble:Config.Setting.DeadJimChance";
        private const string headerOnsetScales = "foragersgamble:Config.Header.OnsetClassScales";
        private const string headerDamageBands = "foragersgamble:Config.Header.DamageBands";
        private const string headerPerItem = "foragersgamble:Config.Header.PerItemClass";

        // Knowledge
        private const string settingLearnAmountPerEat = "foragersgamble:Config.Setting.LearnAmountPerEat";
        private const string settingJournalConsumeOnLearn = "foragersgamble:Config.Setting.JournalConsumeOnLearn";
        private const string settingPreventHandbookOnUnidentified = "foragersgamble:Config.Setting.PreventHandbookOnUnidentified";

        // UI helpers
        private const string uiAddClassScale = "foragersgamble:Config.UI.AddClassScale";
        private const string uiAddBand = "foragersgamble:Config.UI.AddBand";
        private const string uiAddMapping = "foragersgamble:Config.UI.AddMapping";
        private const string uiRemove = "foragersgamble:Config.UI.Remove";
        private const string uiClass = "foragersgamble:Config.UI.Class";
        private const string uiItemKey = "foragersgamble:Config.UI.ItemKey";
        private const string uiBand = "foragersgamble:Config.UI.Band";
        private const string uiMinDamage = "foragersgamble:Config.UI.MinDamage";
        private const string uiMaxDamage = "foragersgamble:Config.UI.MaxDamage";
        private const string uiMinMul = "foragersgamble:Config.UI.MinMul";
        private const string uiMaxMul = "foragersgamble:Config.UI.MaxMul";
        private const string uiMinAdd = "foragersgamble:Config.UI.MinAdd";
        private const string uiMaxAdd = "foragersgamble:Config.UI.MaxAdd";

        private int tempScaleCounter = 1;
        private int tempItemClassCounter = 1;

        private ConfigLibCompatibility() { }

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
            catch (Exception ex)
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

        private static void DragFloatClamped(string label, ref float value, float speed, float min, float max)
        {
            ImGui.DragFloat(label, ref value, speed, min, max);
            if (value < min) value = min;
            if (value > max) value = max;
        }

        private static void DrawOnsetScale(string header, ref OnsetScale scale, string id)
        {
            if (ImGui.TreeNode($"{header}##onset-{header}-{id}"))
            {
                float minMul = scale.MinMul;
                ImGui.DragFloat(Lang.Get(uiMinMul) + $"##onset-minmul-{header}-{id}", ref minMul, 0.01f, 0f, 10f);
                scale.MinMul = minMul;

                float maxMul = scale.MaxMul;
                ImGui.DragFloat(Lang.Get(uiMaxMul) + $"##onset-maxmul-{header}-{id}", ref maxMul, 0.01f, 0f, 10f);
                scale.MaxMul = maxMul;

                float minAdd = scale.MinAdd;
                ImGui.DragFloat(Lang.Get(uiMinAdd) + $"##onset-minadd-{header}-{id}", ref minAdd, 0.1f, 0f, 240f);
                scale.MinAdd = minAdd;

                float maxAdd = scale.MaxAdd;
                ImGui.DragFloat(Lang.Get(uiMaxAdd) + $"##onset-maxadd-{header}-{id}", ref maxAdd, 0.1f, 0f, 240f);
                scale.MaxAdd = maxAdd;

                ImGui.TreePop();
            }
        }

        private static void DrawDamageBand(string header, DamageClassBand band, string id)
        {
            if (ImGui.TreeNode($"{header}##band-{header}-{id}"))
            {
                float min = band.MinDamage;
                ImGui.DragFloat(Lang.Get(uiMinDamage) + $"##band-mindmg-{header}-{id}", ref min, 0.1f, 0f, float.MaxValue);
                band.MinDamage = min < 0 ? 0 : min;

                float max = band.MaxDamage;
                ImGui.DragFloat(Lang.Get(uiMaxDamage) + $"##band-maxdmg-{header}-{id}", ref max, 0.1f, 0f, float.MaxValue);
                band.MaxDamage = max < 0 ? 0 : max;

                string cls = band.Class;
                ImGui.InputText(Lang.Get(uiClass) + $"##band-class-{header}-{id}", ref cls, 64);
                band.Class = string.IsNullOrWhiteSpace(cls) ? "moderate" : cls.Trim();

                // Ensure ordering makes sense
                if (band.MaxDamage < band.MinDamage)
                {
                    band.MaxDamage = band.MinDamage;
                }

                ImGui.TreePop();
            }
        }

        private void Edit(ModConfig config, string id)
        {
            var main = config.Main;
            ImGui.TextWrapped(Lang.Get(title));
            ImGui.Separator();

            // ============================
            // Main
            // ============================
            ImGui.SeparatorText(Lang.Get(sectionMain));

            bool unknownAll = main.UnknownAll;
            ImGui.Checkbox(Lang.Get(settingUnknownAll) + $"##unknownAll-{id}", ref unknownAll);
            main.UnknownAll = unknownAll;

            bool unknownMushrooms = main.UnknownMushrooms;
            ImGui.Checkbox(Lang.Get(settingUnknownMushrooms) + $"##unknownMushrooms-{id}", ref unknownMushrooms);
            main.UnknownMushrooms = unknownMushrooms;

            bool unknownPlants = main.UnknownPlants;
            ImGui.Checkbox(Lang.Get(settingUnknownPlants) + $"##unknownPlants-{id}", ref unknownPlants);
            main.UnknownPlants = unknownPlants;

            bool forgetOnDeath = main.ForgetOnDeath;
            ImGui.Checkbox(Lang.Get(settingForgetOnDeath) + $"##forgetOnDeath-{id}", ref forgetOnDeath);
            main.ForgetOnDeath = forgetOnDeath;

            bool hideNutritionInfo = main.HideNutritionInfo;
            ImGui.Checkbox(Lang.Get(settingHideNutritionInfo) + $"##hideNutritionInfo-{id}", ref hideNutritionInfo);
            main.HideNutritionInfo = hideNutritionInfo;

            bool hideCraftingInfo = main.HideCraftingInfo;
            ImGui.Checkbox(Lang.Get(settingHideCraftingInfo) + $"##hideCraftingInfo-{id}", ref hideCraftingInfo);
            main.HideCraftingInfo = hideCraftingInfo;

            bool hideMealSafety = main.HideMealSafety;
            ImGui.Checkbox(Lang.Get(settingHideMealSafety) + $"##hideMealSafety-{id}", ref hideMealSafety);
            main.HideMealSafety = hideMealSafety;

            float nibble = main.NibbleFactor;
            DragFloatClamped(Lang.Get(settingNibbleFactor) + $"##nibble-{id}", ref nibble, 0.01f, 0f, 1f);
            main.NibbleFactor = nibble;

            bool onset = main.PoisonOnset;
            ImGui.Checkbox(Lang.Get(settingPoisonOnset) + $"##onset-{id}", ref onset);
            main.PoisonOnset = onset;

            float onsetMin = main.PoisonOnsetMinHours;
            float onsetMax = main.PoisonOnsetMaxHours;
            DragFloatClamped(Lang.Get(settingPoisonOnsetMin) + $"##onset-min-{id}", ref onsetMin, 0.5f, 0f, 240f);
            DragFloatClamped(Lang.Get(settingPoisonOnsetMax) + $"##onset-max-{id}", ref onsetMax, 0.5f, 0f, 240f);
            if (onsetMax < onsetMin) onsetMax = onsetMin;
            main.PoisonOnsetMinHours = onsetMin;
            main.PoisonOnsetMaxHours = onsetMax;

            float instantDeath = main.InstantDeathThreshold;
            ImGui.DragFloat(Lang.Get(settingInstantDeathThreshold) + $"##instant-{id}", ref instantDeath, 1f, -1f, 100000f);
            main.InstantDeathThreshold = instantDeath;

            bool showFood = main.ShowFoodInWarning;
            ImGui.Checkbox(Lang.Get(settingShowFoodInWarning) + $"##showfood-{id}", ref showFood);
            main.ShowFoodInWarning = showFood;

            bool randomizeDamaging = main.RandomizeDamagingItems;
            ImGui.Checkbox(Lang.Get(settingRandomizeDamagingItems) + $"##randdmg-{id}", ref randomizeDamaging);
            main.RandomizeDamagingItems = randomizeDamaging;

            bool randomizeHealing = main.RandomizeHealingItems;
            ImGui.Checkbox(Lang.Get(settingRandomizeHealingItems) + $"##randheal-{id}", ref randomizeHealing);
            main.RandomizeHealingItems = randomizeHealing;

            ImGui.Spacing();

            // ============================
            // Poison
            // ============================
            ImGui.SeparatorText(Lang.Get(sectionPoison));

            float deadJimChance = main.PoisonDeadJimChance;
            DragFloatClamped(Lang.Get(settingDeadJimChance) + $"##deadjim-{id}", ref deadJimChance, 0.0005f, 0f, 1f);
            main.PoisonDeadJimChance = deadJimChance;

            if (ImGui.CollapsingHeader($"{Lang.Get(headerOnsetScales)}##class-onset-{id}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                var keys = new List<string>(main.PoisonOnsetClassScales.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    var scale = main.PoisonOnsetClassScales[key];

                    if (ImGui.BeginTable($"##onset-table-{key}-{id}", 2, ImGuiTableFlags.BordersInnerV))
                    {
                        ImGui.TableNextColumn();
                        string k = key;
                        ImGui.InputText(Lang.Get(uiClass) + $"##onset-class-{key}-{id}", ref k, 64);
                        if (!string.Equals(k, key, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(k))
                        {
                            if (!main.PoisonOnsetClassScales.ContainsKey(k))
                            {
                                // rename: replace key, keep same value
                                main.PoisonOnsetClassScales.Remove(key);
                                main.PoisonOnsetClassScales[k] = scale;
                                key = k;         // local rename
                                keys[i] = k;     // keep iteration list in sync
                            }
                        }

                        ImGui.TableNextColumn();
                        DrawOnsetScale($"{key}", ref scale, id);
                        main.PoisonOnsetClassScales[key] = scale;
                        ImGui.EndTable();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button(Lang.Get(uiRemove) + $"##onset-remove-{key}-{id}"))
                    {
                        main.PoisonOnsetClassScales.Remove(key);
                        keys.RemoveAt(i);
                        i--;
                        continue;
                    }
                }

                if (ImGui.Button(Lang.Get(uiAddClassScale) + $"##onset-add-{id}"))
                {
                    string newKey = $"newclass{tempScaleCounter++}";
                    if (!main.PoisonOnsetClassScales.ContainsKey(newKey))
                    {
                        main.PoisonOnsetClassScales[newKey] = new OnsetScale { MinMul = 1f, MaxMul = 1f, MinAdd = 0f, MaxAdd = 0f };
                    }
                }
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader($"{Lang.Get(headerDamageBands)}##bands-{id}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                for (int i = 0; i < main.PoisonClassByDamage.Count; i++)
                {
                    var band = main.PoisonClassByDamage[i];
                    DrawDamageBand($"{Lang.Get(uiBand)} {i + 1}", band, id);
                    ImGui.SameLine();
                    if (ImGui.Button(Lang.Get(uiRemove) + $"##band-remove-{i}-{id}"))
                    {
                        main.PoisonClassByDamage.RemoveAt(i);
                        i--;
                        continue;
                    }
                }
                if (ImGui.Button(Lang.Get(uiAddBand) + $"##band-add-{id}"))
                {
                    main.PoisonClassByDamage.Add(new DamageClassBand { MinDamage = 0f, MaxDamage = 0f, Class = "moderate" });
                }
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader($"{Lang.Get(headerPerItem)}##peritem-{id}"))
            {
                ImGui.Indent();
                var pairs = new List<KeyValuePair<string, string>>(main.PoisonClassByItemKey);
                for (int i = 0; i < pairs.Count; i++)
                {
                    string itemKey = pairs[i].Key;
                    string cls = pairs[i].Value;

                    ImGui.PushID($"itemclass-{i}-{id}");
                    ImGui.InputText(Lang.Get(uiItemKey), ref itemKey, 256);
                    ImGui.SameLine();
                    ImGui.InputText(Lang.Get(uiClass), ref cls, 64);
                    ImGui.SameLine();
                    if (ImGui.Button(Lang.Get(uiRemove)))
                    {
                        main.PoisonClassByItemKey.Remove(pairs[i].Key);
                        ImGui.PopID();
                        continue;
                    }
                    ImGui.PopID();

                    // apply edits (support rename)
                    if (!string.Equals(itemKey, pairs[i].Key, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(itemKey))
                        {
                            main.PoisonClassByItemKey.Remove(pairs[i].Key);
                            main.PoisonClassByItemKey[itemKey.Trim()] = string.IsNullOrWhiteSpace(cls) ? "moderate" : cls.Trim();
                        }
                    }
                    else
                    {
                        main.PoisonClassByItemKey[itemKey] = string.IsNullOrWhiteSpace(cls) ? "moderate" : cls.Trim();
                    }
                }
                if (ImGui.Button(Lang.Get(uiAddMapping) + $"##peritem-add-{id}"))
                {
                    string newKey = $"game:item-key-{tempItemClassCounter++}";
                    if (!main.PoisonClassByItemKey.ContainsKey(newKey))
                        main.PoisonClassByItemKey[newKey] = "moderate";
                }
                ImGui.Unindent();
            }

            ImGui.Spacing();

            // ============================
            // Knowledge
            // ============================
            ImGui.SeparatorText(Lang.Get(sectionKnowledge));

            float learnAmt = main.LearnAmountPerEat;
            DragFloatClamped(Lang.Get(settingLearnAmountPerEat) + $"##learn-{id}", ref learnAmt, 0.01f, 0f, 1f);
            main.LearnAmountPerEat = learnAmt;

            bool journalConsume = main.JournalConsumeOnLearn;
            ImGui.Checkbox(Lang.Get(settingJournalConsumeOnLearn) + $"##journal-{id}", ref journalConsume);
            main.JournalConsumeOnLearn = journalConsume;

            bool preventHandbook = main.PreventHandbookOnUnidentified;
            ImGui.Checkbox(Lang.Get(settingPreventHandbookOnUnidentified) + $"##preventhandbook-{id}", ref preventHandbook);
            main.PreventHandbookOnUnidentified = preventHandbook;
        }
    }
}
