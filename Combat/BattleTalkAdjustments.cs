using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreePublic;

public sealed unsafe class BattleTalkAdjustments : ModuleBase
{
    private const string AddonName = "_BattleTalk";

    private readonly BattleTalkAdjustmentsConfig config;
    private readonly Stopwatch previewTimer = new();
    private FeatureLifetime? runtimeLifetime;

    public BattleTalkAdjustments(BattleTalkAdjustmentsConfig config)
    {
        this.config = config;
        config.Scale = Math.Clamp(config.Scale <= 0f ? 1f : config.Scale, 0.01f, 3f);
    }

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BattleTalkAdjustmentsTitle"),
        Description = OmniLoc.Get("BattleTalkAdjustmentsDescription"),
        Category = ModuleCategory.Combat
    };

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var settingsChanged = false;
        using var table = ImRaii.Table(
            "##battleTalkAdjustmentsSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##battleTalkAdjustmentsScaleColumn", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##battleTalkAdjustmentsOffsetColumn", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##battleTalkAdjustmentsEmptyColumn1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##battleTalkAdjustmentsEmptyColumn2", ImGuiTableColumnFlags.WidthStretch, 1f);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
        ImGui.TableNextColumn();
        var scaleLabel = OmniLoc.Get("Feature.BattleTalkAdjustments.Scale");
        var scaleWidth = MathF.Max(
            1f,
            ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemInnerSpacing.X - ImGui.CalcTextSize(scaleLabel).X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(scaleLabel);
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        var scale = config.Scale;
        if (OmniControls.SliderFloat(
                "##battleTalkAdjustmentsScale",
                ref scale,
                0.01f,
                3f,
                "%.2fx",
                scaleWidth))
        {
            config.Scale = scale;
            settingsChanged = true;
            changed = true;
        }

        ImGui.TableNextColumn();
        var offsetLabel = OmniLoc.Get("Feature.BattleTalkAdjustments.Offset");
        var offsetXLabel = OmniLoc.Get("Feature.BattleTalkAdjustments.OffsetX");
        var offsetYLabel = OmniLoc.Get("Feature.BattleTalkAdjustments.OffsetY");
        var offsetInputWidth = MathF.Max(
            1f,
            (ImGui.GetContentRegionAvail().X -
             ImGui.CalcTextSize(offsetLabel).X -
             ImGui.CalcTextSize(offsetXLabel).X -
             ImGui.CalcTextSize(offsetYLabel).X -
             ImGui.GetStyle().ItemInnerSpacing.X * 5f) / 2f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(offsetLabel);
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextUnformatted(offsetXLabel);
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        var offsetX = config.OffsetX;
        if (OmniControls.InputInt(
                "##battleTalkAdjustmentsOffsetX",
                ref offsetX,
                offsetInputWidth))
        {
            config.OffsetX = offsetX;
            settingsChanged = true;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(offsetYLabel);
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        var offsetY = config.OffsetY;
        if (OmniControls.InputInt(
                "##battleTalkAdjustmentsOffsetY",
                ref offsetY,
                offsetInputWidth))
        {
            config.OffsetY = offsetY;
            settingsChanged = true;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        if (settingsChanged && (!previewTimer.IsRunning || previewTimer.ElapsedMilliseconds > 10000))
        {
            var battleTalk = Addons.BattleTalk;
            if (battleTalk == null || !battleTalk->IsVisible)
            {
                UIModule.Instance()->ShowBattleTalk(
                    OmniLoc.Get("Feature.BattleTalkAdjustments.PreviewSpeaker"),
                    OmniLoc.Get("Feature.BattleTalkAdjustments.PreviewText"),
                    10f,
                    1);
                previewTimer.Restart();
            }
        }

        return changed;
    }

    protected override void OnEnable()
    {
        runtimeLifetime = new();
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonName, OnPreDraw);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, AddonName, OnPostUpdate);
        runtimeLifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreDraw,
            AddonName,
            OnPreDraw));
        runtimeLifetime.Add(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostUpdate,
            AddonName,
            OnPostUpdate));
    }

    protected override void OnDisable()
    {
        runtimeLifetime?.Dispose();
        runtimeLifetime = null;
        Restore(Addons.BattleTalk);
    }

    private void OnPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible || addon->RootNode == null)
        {
            return;
        }

        addon->RootNode->SetPosition(addon->X + config.OffsetX, addon->Y + config.OffsetY);
        addon->RootNode->SetScale(config.Scale, config.Scale);
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args) => Restore((AtkUnitBase*)args.Addon.Address);

    private static void Restore(AtkUnitBase* addon)
    {
        if (addon == null || addon->RootNode == null)
        {
            return;
        }

        addon->RootNode->SetPosition(addon->X, addon->Y);
        addon->RootNode->SetScale(1f, 1f);
    }
}

[Serializable]
public sealed class BattleTalkAdjustmentsConfig
{
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public float Scale { get; set; } = 1f;
}
