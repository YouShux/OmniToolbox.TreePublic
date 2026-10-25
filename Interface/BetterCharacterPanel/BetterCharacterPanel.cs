using TreeTools.Lifecycle;
using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class BetterCharacterPanel(BetterCharacterPanelConfig config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Interface/BetterCharacterPanel-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BetterCharacterPanelTitle",
            ["Description"] = "BetterCharacterPanelDescription",
            ["Feature.BetterCharacterPanel.AdjustEquipmentPositions"] = "Feature.BetterCharacterPanel.AdjustEquipmentPositions",
            ["Feature.BetterCharacterPanel.AlternateGcd.FireIII"] = "Feature.BetterCharacterPanel.AlternateGcd.FireIII",
            ["Feature.BetterCharacterPanel.AlternateGcd.Frostbite"] = "Feature.BetterCharacterPanel.AlternateGcd.Frostbite",
            ["Feature.BetterCharacterPanel.AlternateGcd.RubyRite"] = "Feature.BetterCharacterPanel.AlternateGcd.RubyRite",
            ["Feature.BetterCharacterPanel.GcdModifier.ArmysPaeon"] = "Feature.BetterCharacterPanel.GcdModifier.ArmysPaeon",
            ["Feature.BetterCharacterPanel.GcdModifier.AstralPrism"] = "Feature.BetterCharacterPanel.GcdModifier.AstralPrism",
            ["Feature.BetterCharacterPanel.GcdModifier.GreasedLightning"] = "Feature.BetterCharacterPanel.GcdModifier.GreasedLightning",
            ["Feature.BetterCharacterPanel.GcdModifier.Huton"] = "Feature.BetterCharacterPanel.GcdModifier.Huton",
            ["Feature.BetterCharacterPanel.GcdModifier.LeyLines"] = "Feature.BetterCharacterPanel.GcdModifier.LeyLines",
            ["Feature.BetterCharacterPanel.GcdModifier.PresenceOfMind"] = "Feature.BetterCharacterPanel.GcdModifier.PresenceOfMind",
            ["Feature.BetterCharacterPanel.GcdModifier.Shifu"] = "Feature.BetterCharacterPanel.GcdModifier.Shifu",
            ["Feature.BetterCharacterPanel.GcdModifier.Swiftscaled"] = "Feature.BetterCharacterPanel.GcdModifier.Swiftscaled",
            ["Feature.BetterCharacterPanel.GearSet.MoveDown"] = "Feature.BetterCharacterPanel.GearSet.MoveDown",
            ["Feature.BetterCharacterPanel.GearSet.MoveUp"] = "Feature.BetterCharacterPanel.GearSet.MoveUp",
            ["Feature.BetterCharacterPanel.ReverseCharacterPanel"] = "Feature.BetterCharacterPanel.ReverseCharacterPanel",
            ["Feature.BetterCharacterPanel.Row.CraftingPoints"] = "Feature.BetterCharacterPanel.Row.CraftingPoints",
            ["Feature.BetterCharacterPanel.Row.CriticalChance"] = "Feature.BetterCharacterPanel.Row.CriticalChance",
            ["Feature.BetterCharacterPanel.Row.CriticalDamage"] = "Feature.BetterCharacterPanel.Row.CriticalDamage",
            ["Feature.BetterCharacterPanel.Row.CriticalExpected"] = "Feature.BetterCharacterPanel.Row.CriticalExpected",
            ["Feature.BetterCharacterPanel.Row.DeterminationBonus"] = "Feature.BetterCharacterPanel.Row.DeterminationBonus",
            ["Feature.BetterCharacterPanel.Row.DirectHitChance"] = "Feature.BetterCharacterPanel.Row.DirectHitChance",
            ["Feature.BetterCharacterPanel.Row.DirectHitDamage"] = "Feature.BetterCharacterPanel.Row.DirectHitDamage",
            ["Feature.BetterCharacterPanel.Row.ExpectedDamage"] = "Feature.BetterCharacterPanel.Row.ExpectedDamage",
            ["Feature.BetterCharacterPanel.Row.ExpectedHeal"] = "Feature.BetterCharacterPanel.Row.ExpectedHeal",
            ["Feature.BetterCharacterPanel.Row.GatheringPoints"] = "Feature.BetterCharacterPanel.Row.GatheringPoints",
            ["Feature.BetterCharacterPanel.Row.Gcd"] = "Feature.BetterCharacterPanel.Row.Gcd",
            ["Feature.BetterCharacterPanel.Row.MagicMitigation"] = "Feature.BetterCharacterPanel.Row.MagicMitigation",
            ["Feature.BetterCharacterPanel.Row.ManaPerTick"] = "Feature.BetterCharacterPanel.Row.ManaPerTick",
            ["Feature.BetterCharacterPanel.Row.PhysicalMitigation"] = "Feature.BetterCharacterPanel.Row.PhysicalMitigation",
            ["Feature.BetterCharacterPanel.Row.SkillSpeedBonus"] = "Feature.BetterCharacterPanel.Row.SkillSpeedBonus",
            ["Feature.BetterCharacterPanel.Row.SpeedHeader"] = "Feature.BetterCharacterPanel.Row.SpeedHeader",
            ["Feature.BetterCharacterPanel.Row.SpellSpeedBonus"] = "Feature.BetterCharacterPanel.Row.SpellSpeedBonus",
            ["Feature.BetterCharacterPanel.Row.TenacityDamage"] = "Feature.BetterCharacterPanel.Row.TenacityDamage",
            ["Feature.BetterCharacterPanel.Row.TenacityMitigation"] = "Feature.BetterCharacterPanel.Row.TenacityMitigation",
            ["Feature.BetterCharacterPanel.ShowGearSetReorderButtons"] = "Feature.BetterCharacterPanel.ShowGearSetReorderButtons",
            ["Feature.BetterCharacterPanel.ShowTargetItemLevel"] = "Feature.BetterCharacterPanel.ShowTargetItemLevel",
            ["Feature.BetterCharacterPanel.ShowTargetItemLevel.Help"] = "Feature.BetterCharacterPanel.ShowTargetItemLevel.Help",
            ["Feature.BetterCharacterPanel.ShowUsefulStats"] = "Feature.BetterCharacterPanel.ShowUsefulStats",
            ["Feature.BetterCharacterPanel.ShowUsefulStats.Help"] = "Feature.BetterCharacterPanel.ShowUsefulStats.Help",
            ["Feature.BetterCharacterPanel.SoulstoneAboveOffhand"] = "Feature.BetterCharacterPanel.SoulstoneAboveOffhand",
            ["Feature.BetterCharacterPanel.Tooltip.CriticalHit.Body"] = "Feature.BetterCharacterPanel.Tooltip.CriticalHit.Body",
            ["Feature.BetterCharacterPanel.Tooltip.CriticalHit.Title"] = "Feature.BetterCharacterPanel.Tooltip.CriticalHit.Title",
            ["Feature.BetterCharacterPanel.Tooltip.Defense.Body"] = "Feature.BetterCharacterPanel.Tooltip.Defense.Body",
            ["Feature.BetterCharacterPanel.Tooltip.Defense.Title"] = "Feature.BetterCharacterPanel.Tooltip.Defense.Title",
            ["Feature.BetterCharacterPanel.Tooltip.Determination.Body"] = "Feature.BetterCharacterPanel.Tooltip.Determination.Body",
            ["Feature.BetterCharacterPanel.Tooltip.Determination.Title"] = "Feature.BetterCharacterPanel.Tooltip.Determination.Title",
            ["Feature.BetterCharacterPanel.Tooltip.DirectHit.Body"] = "Feature.BetterCharacterPanel.Tooltip.DirectHit.Body",
            ["Feature.BetterCharacterPanel.Tooltip.DirectHit.Title"] = "Feature.BetterCharacterPanel.Tooltip.DirectHit.Title",
            ["Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Body"] = "Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Body",
            ["Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Title"] = "Feature.BetterCharacterPanel.Tooltip.ExpectedDamage.Title",
            ["Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Body"] = "Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Body",
            ["Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Title"] = "Feature.BetterCharacterPanel.Tooltip.ExpectedHeal.Title",
            ["Feature.BetterCharacterPanel.Tooltip.MagicDefense.Body"] = "Feature.BetterCharacterPanel.Tooltip.MagicDefense.Body",
            ["Feature.BetterCharacterPanel.Tooltip.MagicDefense.Title"] = "Feature.BetterCharacterPanel.Tooltip.MagicDefense.Title",
            ["Feature.BetterCharacterPanel.Tooltip.MainStat"] = "Feature.BetterCharacterPanel.Tooltip.MainStat",
            ["Feature.BetterCharacterPanel.Tooltip.Piety.Body"] = "Feature.BetterCharacterPanel.Tooltip.Piety.Body",
            ["Feature.BetterCharacterPanel.Tooltip.Piety.Title"] = "Feature.BetterCharacterPanel.Tooltip.Piety.Title",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.Alternate"] = "Feature.BetterCharacterPanel.Tooltip.Speed.Alternate",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.Body"] = "Feature.BetterCharacterPanel.Tooltip.Speed.Body",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.DotHot"] = "Feature.BetterCharacterPanel.Tooltip.Speed.DotHot",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.Modifier"] = "Feature.BetterCharacterPanel.Tooltip.Speed.Modifier",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.SkillTitle"] = "Feature.BetterCharacterPanel.Tooltip.Speed.SkillTitle",
            ["Feature.BetterCharacterPanel.Tooltip.Speed.SpellTitle"] = "Feature.BetterCharacterPanel.Tooltip.Speed.SpellTitle",
            ["Feature.BetterCharacterPanel.Tooltip.Tenacity.Damage"] = "Feature.BetterCharacterPanel.Tooltip.Tenacity.Damage",
            ["Feature.BetterCharacterPanel.Tooltip.Tenacity.Mitigation"] = "Feature.BetterCharacterPanel.Tooltip.Tenacity.Mitigation",
            ["Feature.BetterCharacterPanel.Tooltip.Tenacity.Title"] = "Feature.BetterCharacterPanel.Tooltip.Tenacity.Title",
            ["Feature.BetterCharacterPanel.Tooltip.Tier.Next"] = "Feature.BetterCharacterPanel.Tooltip.Tier.Next",
            ["Feature.BetterCharacterPanel.Tooltip.Tier.Suffix"] = "Feature.BetterCharacterPanel.Tooltip.Tier.Suffix",
            ["Feature.BetterCharacterPanel.Tooltip.Tier.Wasted"] = "Feature.BetterCharacterPanel.Tooltip.Tier.Wasted",
            ["Feature.BetterCharacterPanel.Tooltip.Vitality"] = "Feature.BetterCharacterPanel.Tooltip.Vitality",
            ["Feature.BetterCharacterPanel.Value.Seconds"] = "Feature.BetterCharacterPanel.Value.Seconds"
        });

    public override bool HasSettings => true;

    private BetterCharacterPanelNativeUI? nativeUI;

    protected override void OnStart(FeatureLifetime activation)
    {
        nativeUI = new(config);
        activation.Add(nativeUI);
    }

    protected override void OnStop()
    {
        nativeUI = null;
    }

    public override bool DrawSettings()
    {
        if (!BetterCharacterPanelPanel.Draw(config))
        {
            return false;
        }

        nativeUI?.RefreshSettings();
        return true;
    }
}
