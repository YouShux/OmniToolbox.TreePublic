using Dalamud.Utility;
using OmenTools.ImGuiOm;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Tooltips;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class DesynthesisEnhancement
(
    DesynthesisEnhancementConfig config,
    HookRegistry                 hookRegistry,
    Action<bool, bool>           setDesynthesisEnabled
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Item/DesynthesisEnhancement-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DesynthesisEnhancementTitle",
            ["Description"] = "DesynthesisEnhancementDescription",
            ["Feature.DesynthesisEnhancement.Add"] = "Feature.DesynthesisEnhancement.Add",
            ["Feature.DesynthesisEnhancement.Column.Skill"] = "Feature.DesynthesisEnhancement.Column.Skill",
            ["Feature.DesynthesisEnhancement.ConfirmWhenManual"] = "Feature.DesynthesisEnhancement.ConfirmWhenManual",
            ["Feature.DesynthesisEnhancement.GuaranteeNQ"] = "Feature.DesynthesisEnhancement.GuaranteeNQ",
            ["Feature.DesynthesisEnhancement.LockCustomItems"] = "Feature.DesynthesisEnhancement.LockCustomItems",
            ["Feature.DesynthesisEnhancement.LockCustomItems.Help"] = "Feature.DesynthesisEnhancement.LockCustomItems.Help",
            ["Feature.DesynthesisEnhancement.LockGearsetItems"] = "Feature.DesynthesisEnhancement.LockGearsetItems",
            ["Feature.DesynthesisEnhancement.NoCandidates"] = "Feature.DesynthesisEnhancement.NoCandidates",
            ["Feature.DesynthesisEnhancement.SkipHQ"] = "Feature.DesynthesisEnhancement.SkipHQ",
            ["Feature.DesynthesisEnhancement.Start"] = "Feature.DesynthesisEnhancement.Start",
            ["Feature.DesynthesisEnhancement.Stop"] = "Feature.DesynthesisEnhancement.Stop",
            ["Feature.DesynthesisEnhancement.Tooltip"] = "Feature.DesynthesisEnhancement.Tooltip",
            ["Feature.DesynthesisEnhancement.TooltipPrefix"] = "Feature.DesynthesisEnhancement.TooltipPrefix",
            ["Feature.DesynthesisEnhancement.TooltipText"] = "Feature.DesynthesisEnhancement.TooltipText",
            ["Feature.DesynthesisEnhancement.TooltipText.Help"] = "Feature.DesynthesisEnhancement.TooltipText.Help",
            ["Feature.DesynthesisEnhancement.TooltipValue"] = "Feature.DesynthesisEnhancement.TooltipValue",
            ["Feature.DesynthesisEnhancement.TooltipValueWithoutJob"] = "Feature.DesynthesisEnhancement.TooltipValueWithoutJob"
        });

    public override string ModuleName => nameof(DesynthesisEnhancement);

    private readonly DesynthesisEnhancementPanel panel    = new();
    private DesynthesisEnhancementNativeUI?      nativeUI;

    protected override void OnStart(FeatureLifetime activation)
    {
        nativeUI = new(activation, config, hookRegistry, () => SaveHostConfig?.Invoke());
        activation.Add(nativeUI);
        setDesynthesisEnabled(true, config.ShowTooltipText);
    }

    protected override void OnStop()
    {
        try
        {
            setDesynthesisEnabled(false, config.ShowTooltipText);
        }
        finally
        {
            nativeUI = null;
        }
    }

    protected override bool OnInterruptAutomation() => nativeUI?.CancelBatch() == true;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!panel.Draw(config))
        {
            return false;
        }

        nativeUI?.RefreshSettings();
        setDesynthesisEnabled(IsEnabled && !Activation.IsStopping, config.ShowTooltipText);
        return true;
    }
}
