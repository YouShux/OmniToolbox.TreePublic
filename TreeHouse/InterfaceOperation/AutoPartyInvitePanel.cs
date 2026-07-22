using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using TinyPinyin;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class AutoPartyInvitePanel
{
    private static readonly string[] DigitRuns =
    [
        "000", "111", "222", "333", "444", "555", "666", "777", "888", "999"
    ];

    private static string customTriggerInput = string.Empty;
    private static string territorySearch = string.Empty;
    private static List<TerritoryOption>? territoryOptions;
    private static HashSet<uint>? validTerritoryIds;

    public static bool Draw(AutoPartyInviteConfig config)
    {
        var changed = DrawTriggers(config);
        ImGui.Spacing();
        changed |= DrawTerritories(config);
        ImGui.Spacing();
        changed |= DrawChannels(config);
        return changed;
    }

    internal static bool NormalizeConfig(AutoPartyInviteConfig config)
    {
        var changed = false;
        config.EnabledChannels ??= [];
        config.EnabledDigits ??= [];
        config.CustomTriggers ??= [];
        config.AllowedTerritoryIds ??= [];

        changed |= config.EnabledDigits.RemoveWhere(static digit => digit is < 0 or > 9) > 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = config.CustomTriggers.Count - 1; index >= 0; index--)
        {
            var trigger = config.CustomTriggers[index];
            var text = trigger?.Text.Trim() ?? string.Empty;
            if (trigger is null || text.Length == 0 || IsDigitsOnly(text) || !seen.Add(text))
            {
                config.CustomTriggers.RemoveAt(index);
                changed = true;
                continue;
            }

            if (!string.Equals(trigger.Text, text, StringComparison.Ordinal))
            {
                trigger.Text = text;
                changed = true;
            }
        }

        for (var index = 1; index < config.CustomTriggers.Count; index++)
        {
            if (string.CompareOrdinal(config.CustomTriggers[index - 1].Text, config.CustomTriggers[index].Text) <= 0)
            {
                continue;
            }

            config.CustomTriggers.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
            changed = true;
            break;
        }

        return changed;
    }

    private static bool DrawTriggers(AutoPartyInviteConfig config)
    {
        var changed = false;
        var digitToggleMask = 0;
        AutoPartyInviteTrigger? toggledTrigger = null;
        var toggledTriggerEnabled = false;
        if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Add")}##autoPartyInviteTriggerAdd"))
        {
            changed |= AddCustomTrigger(config);
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint(
            "##autoPartyInviteTriggerInput",
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Hint"),
            ref customTriggerInput,
            128);

        ImGui.Spacing();
        var rowCount = 10 + config.CustomTriggers.Count;
        using var table = ImRaii.Table(
            "##autoPartyInviteTriggers",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                ImGui.GetFrameHeightWithSpacing() * (Math.Clamp(rowCount, 1, 6) + 1.35f)));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Enabled"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(56f));
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Content"),
            ImGuiTableColumnFlags.WidthStretch,
            1.45f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type"),
            ImGuiTableColumnFlags.WidthStretch,
            0.8f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Action"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(86f));
        ImGui.TableHeadersRow();

        changed |= DrawTriggerState(
            config,
            true,
            ref digitToggleMask,
            ref toggledTrigger,
            ref toggledTriggerEnabled);
        changed |= DrawTriggerState(
            config,
            false,
            ref digitToggleMask,
            ref toggledTrigger,
            ref toggledTriggerEnabled);
        for (var digit = 0; digit < DigitRuns.Length; digit++)
        {
            if ((digitToggleMask & (1 << digit)) == 0)
            {
                continue;
            }

            if (!config.EnabledDigits.Remove(digit))
            {
                config.EnabledDigits.Add(digit);
            }
        }

        if (toggledTrigger is not null)
        {
            toggledTrigger.Enabled = toggledTriggerEnabled;
        }

        return changed;
    }

    private static bool DrawTriggerState(
        AutoPartyInviteConfig config,
        bool enabledState,
        ref int digitToggleMask,
        ref AutoPartyInviteTrigger? toggledTrigger,
        ref bool toggledTriggerEnabled)
    {
        var changed = false;
        for (var digit = 0; digit < DigitRuns.Length; digit++)
        {
            if (config.EnabledDigits.Contains(digit) != enabledState)
            {
                continue;
            }

            ImGui.PushID($"autoPartyInviteDigit{digit}");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = enabledState;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                digitToggleMask |= 1 << digit;
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.Format(
                OmniLoc.Get("Feature.AutoPartyInvite.Trigger.DigitFormat"),
                digit,
                DigitRuns[digit]));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Digit"));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Fixed"));
            ImGui.PopID();
        }

        for (var index = 0; index < config.CustomTriggers.Count; index++)
        {
            var trigger = config.CustomTriggers[index];
            if (trigger.Enabled != enabledState)
            {
                continue;
            }

            ImGui.PushID($"autoPartyInviteCustom{index}");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = trigger.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                toggledTrigger = trigger;
                toggledTriggerEnabled = enabled;
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(trigger.Text);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Exact"));
            ImGui.TableNextColumn();
            if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Delete")}##delete"))
            {
                config.CustomTriggers.RemoveAt(index);
                index--;
                changed = true;
            }

            ImGui.PopID();
        }

        return changed;
    }

    private static bool AddCustomTrigger(AutoPartyInviteConfig config)
    {
        var text = customTriggerInput.Trim();
        if (text.Length == 0 || IsDigitsOnly(text))
        {
            return false;
        }

        for (var index = 0; index < config.CustomTriggers.Count; index++)
        {
            if (string.Equals(config.CustomTriggers[index].Text, text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        config.CustomTriggers.Add(new()
        {
            Text = text,
            Enabled = true
        });
        config.CustomTriggers.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
        customTriggerInput = string.Empty;
        return true;
    }

    private static bool DrawTerritories(AutoPartyInviteConfig config)
    {
        EnsureTerritoryOptions();
        var changed = false;
        if (territoryOptions!.Count > 0)
        {
            changed |= config.AllowedTerritoryIds.RemoveWhere(id => !validTerritoryIds!.Contains(id)) > 0;
        }

        if (!ImGui.CollapsingHeader(
                string.Format(
                    OmniLoc.Get("Feature.AutoPartyInvite.Territory.Title"),
                    config.AllowedTerritoryIds.Count),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            if (config.AllowedTerritoryIds.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Territory.EmptySelection"));
            }

            return changed;
        }

        var services = DService.Instance();
        var currentTerritoryId = services.ClientState.IsLoggedIn
            ? (uint)services.ClientState.TerritoryType
            : 0;
        using (var controls = ImRaii.Table(
                   "##autoPartyInviteTerritoryControls",
                   4,
                   ImGuiTableFlags.SizingStretchProp,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (controls)
            {
                ImGui.TableSetupColumn("##selectAll", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##clear", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##addCurrent", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##current", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.SelectAll")}##selectAll"))
                {
                    for (var index = 0; index < territoryOptions.Count; index++)
                    {
                        changed |= config.AllowedTerritoryIds.Add(territoryOptions[index].Id);
                    }
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.Clear")}##clear"))
                {
                    changed |= config.AllowedTerritoryIds.Count > 0;
                    config.AllowedTerritoryIds.Clear();
                }

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(currentTerritoryId == 0 || config.AllowedTerritoryIds.Contains(currentTerritoryId)))
                {
                    if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.AddCurrent")}##addCurrent"))
                    {
                        changed |= config.AllowedTerritoryIds.Add(currentTerritoryId);
                    }
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled(currentTerritoryId == 0
                    ? OmniLoc.Get("Feature.AutoPartyInvite.Territory.LoggedOut")
                    : string.Format(
                        OmniLoc.Get("Feature.AutoPartyInvite.Territory.Current"),
                        GetTerritoryName(currentTerritoryId),
                        currentTerritoryId));
            }
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint(
            "##autoPartyInviteTerritorySearch",
            OmniLoc.Get("Feature.AutoPartyInvite.Territory.SearchHint"),
            ref territorySearch,
            128);

        changed |= DrawTerritoryTable(config);
        if (config.AllowedTerritoryIds.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Territory.EmptySelection"));
        }

        return changed;
    }

    private static bool DrawTerritoryTable(AutoPartyInviteConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##autoPartyInviteTerritories",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                MathF.Min(
                    OmniTheme.Scale(360f),
                    MathF.Max(
                        OmniTheme.Scale(180f),
                        ImGui.GetTextLineHeightWithSpacing() * 12f + ImGui.GetFrameHeightWithSpacing()))));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##selected", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Territory.Column.Map"),
            ImGuiTableColumnFlags.WidthStretch,
            1.35f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Territory.Column.Region"),
            ImGuiTableColumnFlags.WidthStretch,
            1f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.AutoPartyInvite.Territory.Column.Id"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(74f));
        ImGui.TableHeadersRow();

        var query = territorySearch.Trim();
        var hasExactId = uint.TryParse(query, out var exactId);
        var anyVisible = false;
        var toggledTerritoryId = 0u;
        var toggledTerritorySelected = false;
        changed |= DrawTerritoryRows(
            config,
            true,
            query,
            hasExactId,
            exactId,
            ref anyVisible,
            ref toggledTerritoryId,
            ref toggledTerritorySelected);
        changed |= DrawTerritoryRows(
            config,
            false,
            query,
            hasExactId,
            exactId,
            ref anyVisible,
            ref toggledTerritoryId,
            ref toggledTerritorySelected);
        if (toggledTerritoryId != 0)
        {
            if (toggledTerritorySelected)
            {
                config.AllowedTerritoryIds.Add(toggledTerritoryId);
            }
            else
            {
                config.AllowedTerritoryIds.Remove(toggledTerritoryId);
            }
        }

        if (!anyVisible)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Territory.NoMatches"));
        }

        return changed;
    }

    private static bool DrawTerritoryRows(
        AutoPartyInviteConfig config,
        bool selectedState,
        string query,
        bool hasExactId,
        uint exactId,
        ref bool anyVisible,
        ref uint toggledTerritoryId,
        ref bool toggledTerritorySelected)
    {
        var changed = false;
        for (var index = 0; index < territoryOptions!.Count; index++)
        {
            var option = territoryOptions[index];
            if (config.AllowedTerritoryIds.Contains(option.Id) != selectedState ||
                hasExactId && option.Id != exactId ||
                !hasExactId && query.Length > 0 &&
                !option.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            anyVisible = true;
            ImGui.PushID((int)option.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var selected = selectedState;
            if (ImGui.Checkbox("##selected", ref selected))
            {
                toggledTerritoryId = option.Id;
                toggledTerritorySelected = selected;
                changed = true;
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(option.Name);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(option.Location);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(option.Id.ToString());
            ImGui.PopID();
        }

        return changed;
    }

    private static bool DrawChannels(AutoPartyInviteConfig config)
    {
        var changed = EnsureChannelsInitialized(config);
        if (!ImGui.CollapsingHeader(
                string.Format(
                    OmniLoc.Get("Feature.AutoPartyInvite.Channel.Title"),
                    config.EnabledChannels.Count),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            if (config.EnabledChannels.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Channel.EmptySelection"));
            }

            return changed;
        }

        if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.SelectAll")}##channelSelectAll"))
        {
            for (var index = 0; index < AutoPartyInviteFeature.ChannelOptions.Length; index++)
            {
                changed |= config.EnabledChannels.Add((ushort)AutoPartyInviteFeature.ChannelOptions[index].Type);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.Clear")}##channelClear"))
        {
            changed |= config.EnabledChannels.Count > 0;
            config.EnabledChannels.Clear();
        }

        ImGui.Spacing();
        var columnCount = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / OmniTheme.Scale(150f)), 1, 4);
        using (var table = ImRaii.Table(
                   "##autoPartyInviteChannels",
                   columnCount,
                   ImGuiTableFlags.SizingStretchSame,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                for (var index = 0; index < AutoPartyInviteFeature.ChannelOptions.Length; index++)
                {
                    if (index % columnCount == 0)
                    {
                        ImGui.TableNextRow();
                    }

                    var option = AutoPartyInviteFeature.ChannelOptions[index];
                    ImGui.TableSetColumnIndex(index % columnCount);
                    var channelId = (ushort)option.Type;
                    var enabled = config.EnabledChannels.Contains(channelId);
                    if (ImGui.Checkbox($"{GetChannelLabel(option)}##autoPartyInviteChannel{channelId}", ref enabled))
                    {
                        if (enabled)
                        {
                            config.EnabledChannels.Add(channelId);
                        }
                        else
                        {
                            config.EnabledChannels.Remove(channelId);
                        }

                        changed = true;
                    }
                }
            }
        }

        if (config.EnabledChannels.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Channel.EmptySelection"));
        }

        return changed;
    }

    private static bool EnsureChannelsInitialized(AutoPartyInviteConfig config)
    {
        if (config.ChannelsInitialized)
        {
            return false;
        }

        config.EnabledChannels.Clear();
        for (var index = 0; index < AutoPartyInviteFeature.ChannelOptions.Length; index++)
        {
            config.EnabledChannels.Add((ushort)AutoPartyInviteFeature.ChannelOptions[index].Type);
        }

        config.ChannelsInitialized = true;
        return true;
    }

    private static string GetChannelLabel(AutoPartyInviteChannel option) =>
        option.Number == 0
            ? OmniLoc.Get(option.LabelKey)
            : string.Format(OmniLoc.Get(option.LabelKey), option.Number);

    private static void EnsureTerritoryOptions()
    {
        if (territoryOptions is not null)
        {
            return;
        }

        territoryOptions = [];
        validTerritoryIds = [];
        foreach (var territory in LuminaGetter.Get<TerritoryType>())
        {
            if (territory.RowId == 0 || !TryGetTerritoryName(territory, out var name))
            {
                continue;
            }

            var regionName = territory.PlaceNameRegion.ValueNullable?.Name.ToString() ?? string.Empty;
            var zoneName = territory.PlaceNameZone.ValueNullable?.Name.ToString() ?? string.Empty;
            var contentName = territory.ContentFinderCondition.ValueNullable?.Name.ToString() ?? string.Empty;
            var location = BuildLocation(regionName, zoneName);
            var searchText = string.Join(
                '\n',
                territory.RowId.ToString(),
                name,
                territory.Name.ToString(),
                location,
                regionName,
                zoneName,
                contentName);
            territoryOptions.Add(new(
                territory.RowId,
                name,
                location,
                $"{searchText}\n{PinyinHelper.GetPinyin(searchText, string.Empty)}\n{BuildPinyinInitials(searchText)}"));
            validTerritoryIds.Add(territory.RowId);
        }

        territoryOptions.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }

    private static string BuildPinyinInitials(string text)
    {
        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        var length = 0;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            var pinyin = PinyinHelper.GetPinyin(character.ToString(), string.Empty);
            if (pinyin.Length > 0)
            {
                buffer[length++] = char.ToLowerInvariant(pinyin[0]);
            }
        }

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }

    private static string GetTerritoryName(uint territoryId)
    {
        for (var index = 0; index < territoryOptions!.Count; index++)
        {
            if (territoryOptions[index].Id == territoryId)
            {
                return territoryOptions[index].Name;
            }
        }

        return string.Format(OmniLoc.Get("Feature.AutoPartyInvite.Territory.Fallback"), territoryId);
    }

    private static bool TryGetTerritoryName(TerritoryType territory, out string name)
    {
        name = territory.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
        if (name.Length > 0)
        {
            return true;
        }

        name = territory.Name.ToString();
        return name.Length > 0;
    }

    private static string BuildLocation(string regionName, string zoneName)
    {
        if (regionName.Length == 0)
        {
            return zoneName;
        }

        if (zoneName.Length == 0 || string.Equals(regionName, zoneName, StringComparison.Ordinal))
        {
            return regionName;
        }

        return $"{regionName} / {zoneName}";
    }

    private static bool IsDigitsOnly(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsDigit(text[index]))
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private readonly record struct TerritoryOption(uint Id, string Name, string Location, string SearchText);
}
