using System.Diagnostics;
using System.Numerics;
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
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BattleTalkAdjustmentsTitle"),
        Description = OmniLoc.Get("BattleTalkAdjustmentsDescription"),
        Category = ModuleCategory.Combat
    };

    private const string AddonName = "_BattleTalk";

    private readonly BattleTalkAdjustmentsConfig config;
    private readonly Stopwatch previewTimer = new();
    private Vector2 nativeScale = Vector2.One;
    private bool hasNativeScale;
    private FeatureLifetime? runtimeLifetime;

    public BattleTalkAdjustments(BattleTalkAdjustmentsConfig config)
    {
        this.config = config;
        config.Scale = Math.Clamp(config.Scale <= 0f ? 1f : config.Scale, 0.01f, 3f);
    }

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
        var scaleWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(scaleLabel).X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(scaleLabel);
        ImGui.SameLine();
        var scale = config.Scale;
        if (OmniControls.DragFloat(
                "##battleTalkAdjustmentsScale",
                ref scale,
                0.05f,
                0.01f,
                3f,
                "%.2fx",
                MathF.Max(1f, scaleWidth),
                ImGuiSliderFlags.AlwaysClamp))
        {
            config.Scale = scale;
            settingsChanged = true;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.TableNextColumn();
        var offsetLabel = OmniLoc.Get("Feature.BattleTalkAdjustments.Offset");
        var offsetWidth = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(offsetLabel).X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(offsetLabel);
        ImGui.SameLine();
        var offset = new Vector2(config.OffsetX, config.OffsetY);
        if (OmniControls.DragFloat2(
                "##battleTalkAdjustmentsOffset",
                ref offset,
                1f,
                -1500f,
                1500f,
                "%.0f",
                MathF.Max(1f, offsetWidth)))
        {
            config.OffsetX = (int)MathF.Round(offset.X);
            config.OffsetY = (int)MathF.Round(offset.Y);
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

        if (!hasNativeScale)
        {
            nativeScale = new(addon->RootNode->ScaleX, addon->RootNode->ScaleY);
            hasNativeScale = true;
        }

        addon->RootNode->SetPosition(addon->X + config.OffsetX, addon->Y + config.OffsetY);
        addon->RootNode->SetScale(nativeScale.X * config.Scale, nativeScale.Y * config.Scale);
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args) => Restore((AtkUnitBase*)args.Addon.Address);

    private void Restore(AtkUnitBase* addon)
    {
        if (addon == null || addon->RootNode == null)
        {
            hasNativeScale = false;
            return;
        }

        addon->RootNode->SetPosition(addon->X, addon->Y);
        var scale = hasNativeScale ? nativeScale : Vector2.One;
        addon->RootNode->SetScale(scale.X, scale.Y);
        hasNativeScale = false;
    }
}

[Serializable]
public sealed class BattleTalkAdjustmentsConfig
{
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public float Scale { get; set; } = 1f;
}
