using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools.Interop.Game.Helpers;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ShieldOnHP(ShieldOnHPConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ShieldOnHPTitle"),
        Description = OmniLoc.Get("ShieldOnHPDescription"),
        Category = ModuleCategory.Combat
    };

    private const string ParameterWidgetAddonName = "_ParameterWidget";
    private const uint ShieldBarNodeID = 32680;
    private const uint OverShieldBarNodeID = 32681;
    private const float BarBodyWidth = 148f;
    private FeatureLifetime? runtimeLifetime;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var showBar = config.ShowBar;
        var shouldSave = OmniControls.Checkbox(
            $"{OmniLoc.Get("Feature.ShieldOnHP.ShowBar")}##shieldOnHPShowBar",
            ref showBar);
        if (shouldSave)
        {
            config.ShowBar = showBar;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ShieldOnHP.ShowBar.Help"));
        ImGui.SameLine(0f, OmniTheme.ContentGap());
        using (ImRaii.Disabled(!config.ShowBar))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.ShieldOnHP.Color"));
            ImGui.SameLine();
            var color = config.Color;
            if (OmniControls.ColorEdit("##shieldOnHPColor", ref color))
            {
                config.Color = color;
            }

            shouldSave |= ImGui.IsItemDeactivatedAfterEdit();
        }

        return shouldSave;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(
                AddonEvent.PreFinalize,
                ParameterWidgetAddonName,
                OnParameterWidgetFinalize);

            if (!FrameworkManager.Instance().Reg(OnUpdate))
            {
                throw new InvalidOperationException("Shield-on-HP update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawShieldValue;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawShieldValue);
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            RemoveNodes(AddonHelper.GetByName(ParameterWidgetAddonName));
        }
    }

    private static void OnParameterWidgetFinalize(AddonEvent _, AddonArgs args) =>
        RemoveNodes((AtkUnitBase*)args.Addon.Address);

    private void OnUpdate(IFramework _)
    {
        if (!TryGetHpBarComponent(out var hpBar, out var hpNineGrid))
        {
            return;
        }

        var shieldBar = FindNode<AtkNineGridNode>(
            &hpBar->Component->UldManager,
            ShieldBarNodeID,
            NodeType.NineGrid);
        var overShieldBar = FindNode<AtkNineGridNode>(
            &hpBar->Component->UldManager,
            OverShieldBarNodeID,
            NodeType.NineGrid);
        if (!config.ShowBar || !TryGetShieldMetrics(out var metrics))
        {
            HideBars(shieldBar, overShieldBar);
            return;
        }

        if (shieldBar == null)
        {
            shieldBar = CreateShieldBarNode(hpBar, hpNineGrid, ShieldBarNodeID);
        }

        if (overShieldBar == null)
        {
            overShieldBar = CreateShieldBarNode(hpBar, hpNineGrid, OverShieldBarNodeID);
        }

        if (shieldBar == null || overShieldBar == null)
        {
            HideBars(shieldBar, overShieldBar);
            return;
        }

        shieldBar->AtkResNode.X = metrics.HpPercentage * BarBodyWidth;
        shieldBar->AtkResNode.SetWidth(ToBarWidth(metrics.ShieldPercentage));
        shieldBar->AtkResNode.DrawFlags |= 1;
        shieldBar->AtkResNode.ToggleVisibility(metrics.ShieldPercentage > 0f);

        overShieldBar->AtkResNode.X = 0f;
        overShieldBar->AtkResNode.SetWidth(ToBarWidth(metrics.OverShieldPercentage));
        overShieldBar->AtkResNode.DrawFlags |= 1;
        overShieldBar->AtkResNode.ToggleVisibility(metrics.OverShieldPercentage > 0f);
        ApplyBarColor(shieldBar, overShieldBar, config.Color);
    }

    private static void DrawShieldValue()
    {
        if (!TryGetShieldMetrics(out var metrics) ||
            !TryGetHpBarComponent(out var hpBar, out _) ||
            metrics.ShieldAmount == 0)
        {
            return;
        }

        var scale = GetNodeScale(&hpBar->AtkResNode);
        using var font = FontManager.Instance().MiedingerMidFont140.Push();
        var text = metrics.ShieldAmount.ToString();
        var textSize = ImGui.CalcTextSize(text);
        var position = new Vector2(
            hpBar->AtkResNode.ScreenX + 64f * scale.X,
            hpBar->AtkResNode.ScreenY - textSize.Y + 5f * scale.Y);
        var drawList = ImGui.GetBackgroundDrawList();
        drawList.AddText(position + Vector2.One, 0x9D00A2FF, text);
        drawList.AddText(position, 0xFFFFFFFF, text);
    }

    private static bool TryGetHpBarComponent(
        out AtkComponentNode* hpBar,
        out AtkNineGridNode* hpNineGrid)
    {
        hpBar = null;
        hpNineGrid = null;
        if (!AddonHelper.TryGetByName(ParameterWidgetAddonName, out AtkUnitBase* parameterWidget) ||
            parameterWidget->UldManager.LoadedState != AtkLoadState.Loaded ||
            !parameterWidget->IsVisible)
        {
            return false;
        }

        var hpBarNode = parameterWidget->GetNodeById(3);
        if (hpBarNode == null)
        {
            return false;
        }

        hpBar = hpBarNode->GetAsAtkComponentNode();
        if (hpBar == null || hpBar->Component == null || !hpBar->AtkResNode.IsVisible())
        {
            hpBar = null;
            return false;
        }

        hpNineGrid = FindNode<AtkNineGridNode>(
            &hpBar->Component->UldManager,
            5,
            NodeType.NineGrid);
        return hpNineGrid != null;
    }

    private static bool TryGetShieldMetrics(out ShieldMetrics metrics)
    {
        metrics = default;
        var localPlayer = (Character*)Control.GetLocalPlayer();
        if (localPlayer == null)
        {
            return false;
        }

        var data = &localPlayer->CharacterData;
        if (data->Health == 0 || data->MaxHealth == 0 || data->ShieldValue == 0)
        {
            return false;
        }

        var shieldPercentage = data->ShieldValue / 100f;
        var hpPercentage = Math.Clamp(
            (float)data->Health / data->MaxHealth,
            0f,
            1f);
        var overShieldPercentage = Math.Clamp(shieldPercentage - (1f - hpPercentage), 0f, 1f);
        metrics = new(
            hpPercentage,
            Math.Clamp(shieldPercentage - overShieldPercentage, 0f, 1f),
            overShieldPercentage,
            (uint)MathF.Round(
                data->Health * shieldPercentage,
                MidpointRounding.AwayFromZero));
        return true;
    }

    private static AtkNineGridNode* CreateShieldBarNode(
        AtkComponentNode* hpBar,
        AtkNineGridNode* hpNineGrid,
        uint nodeID)
    {
        if (hpNineGrid->AtkResNode.ParentNode == null)
        {
            return null;
        }

        var bar = IMemorySpace.GetUISpace()->Create<AtkNineGridNode>();
        if (bar == null)
        {
            return null;
        }

        bar->AtkResNode.Type = NodeType.NineGrid;
        bar->AtkResNode.NodeId = nodeID;
        bar->PartsList = hpNineGrid->PartsList;
        bar->TopOffset = 0;
        bar->BottomOffset = 0;
        bar->LeftOffset = 7;
        bar->RightOffset = 7;
        bar->BlendMode = 0;
        bar->PartsTypeRenderType = 0;
        bar->PartId = 2;
        bar->AtkResNode.MultiplyRed = 255;
        bar->AtkResNode.MultiplyGreen = 255;
        bar->AtkResNode.MultiplyBlue = 0;
        bar->AtkResNode.AddRed = -1;
        bar->AtkResNode.AddGreen = -1;
        bar->AtkResNode.AddBlue = -1;
        bar->AtkResNode.DrawFlags |= 1;
        bar->AtkResNode.SetHeight(20);
        bar->AtkResNode.SetWidth(0);
        bar->AtkResNode.SetScale(1f, 1f);
        bar->AtkResNode.ToggleVisibility(false);
        LinkNodeAfterTarget(&bar->AtkResNode, hpBar, &hpNineGrid->AtkResNode);
        return bar;
    }

    private static void LinkNodeAfterTarget(
        AtkResNode* node,
        AtkComponentNode* parent,
        AtkResNode* target)
    {
        node->ParentNode = target->ParentNode;
        if (target->PrevSiblingNode != null)
        {
            target->PrevSiblingNode->NextSiblingNode = node;
            node->PrevSiblingNode = target->PrevSiblingNode;
        }

        target->PrevSiblingNode = node;
        node->NextSiblingNode = target;
        parent->Component->UldManager.UpdateDrawNodeList();
    }

    private static void HideBars(
        AtkNineGridNode* shieldBar,
        AtkNineGridNode* overShieldBar)
    {
        if (shieldBar != null)
        {
            shieldBar->AtkResNode.X = 0f;
            shieldBar->AtkResNode.SetWidth(0);
            shieldBar->AtkResNode.ToggleVisibility(false);
        }

        if (overShieldBar != null)
        {
            overShieldBar->AtkResNode.X = 0f;
            overShieldBar->AtkResNode.SetWidth(0);
            overShieldBar->AtkResNode.ToggleVisibility(false);
        }
    }

    private static void ApplyBarColor(
        AtkNineGridNode* shieldBar,
        AtkNineGridNode* overShieldBar,
        Vector3 color)
    {
        var red = ToColorByte(color.X);
        var green = ToColorByte(color.Y);
        var blue = ToColorByte(color.Z);
        shieldBar->AtkResNode.MultiplyRed = red;
        shieldBar->AtkResNode.MultiplyGreen = green;
        shieldBar->AtkResNode.MultiplyBlue = blue;
        overShieldBar->AtkResNode.MultiplyRed = red;
        overShieldBar->AtkResNode.MultiplyGreen = green;
        overShieldBar->AtkResNode.MultiplyBlue = blue;
    }

    private static void RemoveNodes(AtkUnitBase* parameterWidget)
    {
        if (parameterWidget == null)
        {
            return;
        }

        var hpBarNode = parameterWidget->GetNodeById(3);
        if (hpBarNode == null)
        {
            return;
        }

        var hpBar = hpBarNode->GetAsAtkComponentNode();
        if (hpBar == null || hpBar->Component == null)
        {
            return;
        }

        var manager = &hpBar->Component->UldManager;
        var shieldBar = FindNode<AtkNineGridNode>(manager, ShieldBarNodeID, NodeType.NineGrid);
        var overShieldBar = FindNode<AtkNineGridNode>(manager, OverShieldBarNodeID, NodeType.NineGrid);
        if (shieldBar == null && overShieldBar == null)
        {
            return;
        }

        if (shieldBar != null)
        {
            UnlinkNode(&shieldBar->AtkResNode);
        }

        if (overShieldBar != null)
        {
            UnlinkNode(&overShieldBar->AtkResNode);
        }

        manager->UpdateDrawNodeList();
        if (shieldBar != null)
        {
            shieldBar->AtkResNode.Destroy(true);
        }

        if (overShieldBar != null)
        {
            overShieldBar->AtkResNode.Destroy(true);
        }
    }

    private static void UnlinkNode(AtkResNode* node)
    {
        if (node->ParentNode != null && node->ParentNode->ChildNode == node)
        {
            node->ParentNode->ChildNode = node->PrevSiblingNode != null
                ? node->PrevSiblingNode
                : node->NextSiblingNode;
        }

        if (node->PrevSiblingNode != null)
        {
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;
        }

        if (node->NextSiblingNode != null)
        {
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;
        }

        node->ParentNode = null;
        node->PrevSiblingNode = null;
        node->NextSiblingNode = null;
    }

    private static T* FindNode<T>(
        AtkUldManager* manager,
        uint nodeID,
        NodeType nodeType)
        where T : unmanaged
    {
        if (manager == null || manager->NodeList == null)
        {
            return null;
        }

        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if (node != null && node->NodeId == nodeID && node->Type == nodeType)
            {
                return (T*)node;
            }
        }

        return null;
    }

    private static ushort ToBarWidth(float percentage) =>
        percentage > 0f
            ? (ushort)Math.Clamp(
                (int)MathF.Round(percentage * BarBodyWidth + 12f),
                0,
                ushort.MaxValue)
            : (ushort)0;

    private static byte ToColorByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);

    private static Vector2 GetNodeScale(AtkResNode* node)
    {
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        for (var parent = node->ParentNode; parent != null; parent = parent->ParentNode)
        {
            scale *= new Vector2(parent->ScaleX, parent->ScaleY);
        }

        return scale;
    }

    private readonly struct ShieldMetrics(
        float hpPercentage,
        float shieldPercentage,
        float overShieldPercentage,
        uint shieldAmount)
    {
        public float HpPercentage { get; } = hpPercentage;

        public float ShieldPercentage { get; } = shieldPercentage;

        public float OverShieldPercentage { get; } = overShieldPercentage;

        public uint ShieldAmount { get; } = shieldAmount;
    }
}

[Serializable]
public sealed class ShieldOnHPConfig
{
    public bool ShowBar { get; set; } = true;
    public Vector3 Color { get; set; } = new(1f, 1f, 0f);
}
