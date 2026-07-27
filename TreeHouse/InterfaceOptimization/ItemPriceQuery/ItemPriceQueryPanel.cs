using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPriceQuery;

internal sealed class ItemPriceQueryPanel(MarketConfig config)
{
    private static readonly (MarketPriceScope Scope, string LabelKey)[] Scopes =
    [
        (MarketPriceScope.Automatic, "Settings.Market.Scope.Automatic"),
        (MarketPriceScope.World, "Settings.Market.Scope.World"),
        (MarketPriceScope.DataCenter, "Settings.Market.Scope.DataCenter"),
        (MarketPriceScope.Region, "Settings.Market.Scope.Region")
    ];

    private static readonly (MarketMetricScope Scope, string LabelKey)[] MetricScopes =
    [
        (MarketMetricScope.Hidden, "Settings.Market.Scope.Hidden"),
        (MarketMetricScope.World, "Settings.Market.Scope.World"),
        (MarketMetricScope.DataCenter, "Settings.Market.Scope.DataCenter"),
        (MarketMetricScope.Region, "Settings.Market.Scope.Region")
    ];

    public bool Draw()
    {
        using var table = ImRaii.Table(
            "##itemPriceQuerySettingsTable",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        var changed = DrawQualitySettings();
        changed |= DrawCompactScope();
        changed |= DrawTooltipSettings();
        var averageSalePriceScope = config.AverageSalePriceScope;
        if (DrawMetricScope(
                "Settings.Market.Tooltip.AverageSalePrice",
                "##averageSalePriceScope",
                ref averageSalePriceScope))
        {
            config.AverageSalePriceScope = averageSalePriceScope;
            changed = true;
        }

        var dailySaleVelocityScope = config.DailySaleVelocityScope;
        if (DrawMetricScope(
                "Settings.Market.Tooltip.DailySaleVelocity",
                "##dailySaleVelocityScope",
                ref dailySaleVelocityScope))
        {
            config.DailySaleVelocityScope = dailySaleVelocityScope;
            changed = true;
        }

        return changed;
    }

    private bool DrawQualitySettings()
    {
        var changed = false;
        var showNqPrice = config.ShowNqPrice;
        if (DrawCheckboxRow("Settings.Market.ShowNqPrice", "##showNqPrice", ref showNqPrice))
        {
            config.ShowNqPrice = showNqPrice;
            changed = true;
        }

        var showHqPrice = config.ShowHqPrice;
        if (DrawCheckboxRow("Settings.Market.ShowHqPrice", "##showHqPrice", ref showHqPrice))
        {
            config.ShowHqPrice = showHqPrice;
            changed = true;
        }

        return changed;
    }

    private bool DrawCompactScope()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.Market.CompactScope"));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                "##compactPriceScope",
                OmniLoc.Get(GetScopeLabelKey(config.CompactPriceScope)),
                ImGui.GetContentRegionAvail().X,
                ImGuiComboFlags.HeightLarge))
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < Scopes.Length; index++)
        {
            var selected = config.CompactPriceScope == Scopes[index].Scope;
            if (ImGui.Selectable(OmniLoc.Get(Scopes[index].LabelKey), selected) && !selected)
            {
                config.CompactPriceScope = Scopes[index].Scope;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private bool DrawTooltipSettings()
    {
        var changed = false;
        var showWorld = config.ShowWorldPriceInTooltip;
        if (DrawCheckboxRow("Settings.Market.Tooltip.World", "##showWorldPrice", ref showWorld))
        {
            config.ShowWorldPriceInTooltip = showWorld;
            changed = true;
        }

        var showDataCenter = config.ShowDataCenterPriceInTooltip;
        if (DrawCheckboxRow("Settings.Market.Tooltip.DataCenter", "##showDataCenterPrice", ref showDataCenter))
        {
            config.ShowDataCenterPriceInTooltip = showDataCenter;
            changed = true;
        }

        var showRegion = config.ShowRegionPriceInTooltip;
        if (DrawCheckboxRow("Settings.Market.Tooltip.Region", "##showRegionPrice", ref showRegion))
        {
            config.ShowRegionPriceInTooltip = showRegion;
            changed = true;
        }

        var showWorldHistory = config.ShowWorldRecentPurchaseInTooltip;
        if (DrawCheckboxRow("Settings.Market.Tooltip.History.World", "##showWorldHistory", ref showWorldHistory))
        {
            config.ShowWorldRecentPurchaseInTooltip = showWorldHistory;
            changed = true;
        }

        var showDataCenterHistory = config.ShowDataCenterRecentPurchaseInTooltip;
        if (DrawCheckboxRow(
                "Settings.Market.Tooltip.History.DataCenter",
                "##showDataCenterHistory",
                ref showDataCenterHistory))
        {
            config.ShowDataCenterRecentPurchaseInTooltip = showDataCenterHistory;
            changed = true;
        }

        var showRegionHistory = config.ShowRegionRecentPurchaseInTooltip;
        if (DrawCheckboxRow("Settings.Market.Tooltip.History.Region", "##showRegionHistory", ref showRegionHistory))
        {
            config.ShowRegionRecentPurchaseInTooltip = showRegionHistory;
            changed = true;
        }

        return changed;
    }

    private static bool DrawMetricScope(
        string labelKey,
        string id,
        ref MarketMetricScope scope)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                id,
                OmniLoc.Get(GetMetricScopeLabelKey(scope)),
                ImGui.GetContentRegionAvail().X,
                ImGuiComboFlags.HeightLarge))
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < MetricScopes.Length; index++)
        {
            var selected = scope == MetricScopes[index].Scope;
            if (ImGui.Selectable(OmniLoc.Get(MetricScopes[index].LabelKey), selected) && !selected)
            {
                scope = MetricScopes[index].Scope;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawCheckboxRow(string labelKey, string id, ref bool value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        return OmniControls.Checkbox(id, ref value);
    }

    private static string GetScopeLabelKey(MarketPriceScope scope) => scope switch
    {
        MarketPriceScope.World => "Settings.Market.Scope.World",
        MarketPriceScope.DataCenter => "Settings.Market.Scope.DataCenter",
        MarketPriceScope.Region => "Settings.Market.Scope.Region",
        _ => "Settings.Market.Scope.Automatic"
    };

    private static string GetMetricScopeLabelKey(MarketMetricScope scope) => scope switch
    {
        MarketMetricScope.World => "Settings.Market.Scope.World",
        MarketMetricScope.DataCenter => "Settings.Market.Scope.DataCenter",
        MarketMetricScope.Region => "Settings.Market.Scope.Region",
        _ => "Settings.Market.Scope.Hidden"
    };
}
