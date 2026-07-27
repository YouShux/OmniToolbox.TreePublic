using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;

namespace OmniToolbox.TreeHouse.General;

internal static class FoodReminderPanel
{
    private static readonly List<TerritoryOption> TerritoryOptions = [];
    private static readonly HashSet<uint> TerritoryOptionIds = [];
    private static readonly List<TerritoryOption> VisibleTerritoryOptions = [];
    private static string territorySearch = string.Empty;
    private static bool territoryOptionsLoaded;

    public static bool Draw(FoodReminderConfig config, System.Action sendTestReminder)
    {
        var changed = NormalizeConfig(config);
        changed |= DrawNameMode(config);
        changed |= DrawThreshold(config);
        changed |= DrawPartyMessage(config, sendTestReminder);
        changed |= DrawTerritorySelector(config);
        return changed;
    }

    private static bool NormalizeConfig(FoodReminderConfig config)
    {
        var changed = false;
        config.AllowedTerritoryIds ??= [];
        if (config.TargetNameMode is < 0 or > 1)
        {
            config.TargetNameMode = Math.Clamp(config.TargetNameMode, 0, 1);
            changed = true;
        }

        var threshold = Math.Clamp(config.ThresholdSeconds, 0, 7200);
        if (threshold != config.ThresholdSeconds)
        {
            config.ThresholdSeconds = threshold;
            changed = true;
        }

        return changed;
    }

    private static bool DrawNameMode(FoodReminderConfig config)
    {
        var changed = false;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.TargetNameMode"));
        ImGui.SameLine();
        var characterName = config.TargetNameMode == 0;
        if (ImGui.RadioButton(
                OmniLoc.Get("Feature.FoodReminder.TargetNameMode.CharacterName"),
                characterName))
        {
            config.TargetNameMode = 0;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(
                OmniLoc.Get("Feature.FoodReminder.TargetNameMode.JobName"),
                !characterName))
        {
            config.TargetNameMode = 1;
            changed = true;
        }

        ImGui.TextDisabled(OmniLoc.Get("Feature.FoodReminder.TargetNameMode.Help"));
        return changed;
    }

    private static bool DrawThreshold(FoodReminderConfig config)
    {
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.Threshold"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(110f));
        var threshold = config.ThresholdSeconds;
        if (OmniControls.InputInt("##foodReminderThreshold", ref threshold, 0, 0))
        {
            config.ThresholdSeconds = Math.Clamp(threshold, 0, 7200);
        }

        var changed = ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.Seconds"));
        ImGui.TextDisabled(OmniLoc.Get("Feature.FoodReminder.Threshold.Help"));
        return changed;
    }

    private static bool DrawPartyMessage(FoodReminderConfig config, System.Action sendTestReminder)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.PartyMessage"));
        var message = config.PartyMessage ?? OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage");
        var inputSize = new Vector2(MathF.Max(OmniTheme.Scale(240f), ImGui.GetContentRegionAvail().X), OmniTheme.Scale(72f));
        if (OmniControls.InputTextMultiline(
                "##foodReminderPartyMessage",
                ref message,
                1024,
                inputSize))
        {
            config.PartyMessage = string.IsNullOrWhiteSpace(message)
                ? OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage")
                : message;
        }

        var changed = ImGui.IsItemDeactivatedAfterEdit();

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.Reset"), false))
        {
            config.PartyMessage = OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage");
            changed = true;
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.Test"), false))
        {
            sendTestReminder();
        }

        return changed;
    }

    private static bool DrawTerritorySelector(FoodReminderConfig config)
    {
        EnsureTerritoryOptions();
        var changed = false;
        var currentTerritory = DService.Instance().ClientState.TerritoryType;
        if (TerritoryOptionIds.Count > 0)
        {
            changed |= config.AllowedTerritoryIds.RemoveWhere(id => !TerritoryOptionIds.Contains(id)) > 0;
        }

        var header = $"{OmniLoc.Get("Feature.FoodReminder.TerritorySelector")} ({config.AllowedTerritoryIds.Count})";
        if (!OmniControls.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (config.AllowedTerritoryIds.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.FoodReminder.TerritorySelector.Empty"));
            }

            return changed;
        }

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.SelectAll"), false))
        {
            foreach (var option in TerritoryOptions)
            {
                changed |= config.AllowedTerritoryIds.Add(option.Id);
            }
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.Clear"), false))
        {
            if (config.AllowedTerritoryIds.Count > 0)
            {
                config.AllowedTerritoryIds.Clear();
                changed = true;
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(currentTerritory == 0 || config.AllowedTerritoryIds.Contains(currentTerritory)))
        {
            if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.AddCurrent"), false) &&
                currentTerritory != 0)
            {
                changed |= config.AllowedTerritoryIds.Add(currentTerritory);
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled(currentTerritory == 0
            ? OmniLoc.Get("Feature.FoodReminder.NotLoggedIn")
            : string.Format(
                OmniLoc.Get("Feature.FoodReminder.CurrentMap"),
                GetTerritoryName(currentTerritory),
                currentTerritory));

        OmniControls.InputTextWithHint(
            "##foodReminderTerritorySearch",
            OmniLoc.Get("Feature.FoodReminder.Search"),
            ref territorySearch,
            128,
            ImGui.GetContentRegionAvail().X);
        changed |= DrawTerritoryTable(config);

        if (config.AllowedTerritoryIds.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.FoodReminder.TerritorySelector.Empty"));
        }

        return changed;
    }

    private static bool DrawTerritoryTable(FoodReminderConfig config)
    {
        var query = territorySearch.Trim();
        var rows = GetVisibleTerritories(config.AllowedTerritoryIds, query);
        if (rows.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.FoodReminder.NoMatches"));
            return false;
        }

        var tableHeight = MathF.Min(
            OmniTheme.Scale(360f),
            MathF.Max(OmniTheme.Scale(180f), ImGui.GetTextLineHeightWithSpacing() * 12f + ImGui.GetFrameHeightWithSpacing()));
        using var table = ImRaii.Table(
            "##foodReminderTerritoryTable",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
            new Vector2(0f, tableHeight));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##foodReminderTerritoryCheck", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.FoodReminder.Column.Map"), ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.FoodReminder.Column.Region"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.FoodReminder.Column.Id"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(74f));
        OmniControls.ScrollableTableHeadersRow();

        var changed = false;
        foreach (var option in rows)
        {
            ImGui.PushID((int)option.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var selected = config.AllowedTerritoryIds.Contains(option.Id);
            if (OmniControls.Checkbox("##selected", ref selected))
            {
                changed |= selected
                    ? config.AllowedTerritoryIds.Add(option.Id)
                    : config.AllowedTerritoryIds.Remove(option.Id);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(option.Name);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(option.Region);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(option.Id.ToString());
            ImGui.PopID();
        }

        return changed;
    }

    private static IReadOnlyList<TerritoryOption> GetVisibleTerritories(HashSet<uint> selected, string query)
    {
        VisibleTerritoryOptions.Clear();
        var exactId = uint.TryParse(query, out var parsedId) ? parsedId : 0;
        foreach (var option in TerritoryOptions)
        {
            if (exactId != 0
                ? option.Id != exactId
                : query.Length > 0 && !option.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleTerritoryOptions.Add(option);
        }

        VisibleTerritoryOptions.Sort((left, right) =>
        {
            var selectedComparison = selected.Contains(right.Id).CompareTo(selected.Contains(left.Id));
            return selectedComparison != 0 ? selectedComparison : left.Id.CompareTo(right.Id);
        });
        return VisibleTerritoryOptions;
    }

    private static void EnsureTerritoryOptions()
    {
        if (territoryOptionsLoaded)
        {
            return;
        }

        territoryOptionsLoaded = true;
        var sheet = DService.Instance().Data.GetExcelSheet<TerritoryType>();
        if (sheet is null)
        {
            return;
        }

        foreach (var territory in sheet)
        {
            if (territory.RowId == 0)
            {
                continue;
            }

            var name = territory.PlaceName.ValueNullable?.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = territory.Name.ToString().Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var region = BuildRegionText(
                territory.PlaceNameRegion.ValueNullable?.Name.ToString(),
                territory.PlaceNameZone.ValueNullable?.Name.ToString());
            var contentName = territory.ContentFinderCondition.ValueNullable?.Name.ToString() ?? string.Empty;
            TerritoryOptions.Add(new(
                territory.RowId,
                name,
                region,
                OmniSearchText.Build(
                    territory.RowId.ToString(),
                    name,
                    territory.Name.ToString(),
                    region,
                    contentName)));
            TerritoryOptionIds.Add(territory.RowId);
        }

        TerritoryOptions.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }

    private static string GetTerritoryName(uint id)
    {
        EnsureTerritoryOptions();
        foreach (var option in TerritoryOptions)
        {
            if (option.Id == id)
            {
                return option.Name;
            }
        }

        return string.Format(OmniLoc.Get("Feature.FoodReminder.TerritoryFallback"), id);
    }

    private static string BuildRegionText(string? region, string? zone)
    {
        region = region?.Trim() ?? string.Empty;
        zone = zone?.Trim() ?? string.Empty;
        if (region.Length == 0)
        {
            return zone;
        }

        return zone.Length == 0 || string.Equals(region, zone, StringComparison.Ordinal)
            ? region
            : $"{region} / {zone}";
    }

    private readonly record struct TerritoryOption(uint Id, string Name, string Region, string SearchText);
}
