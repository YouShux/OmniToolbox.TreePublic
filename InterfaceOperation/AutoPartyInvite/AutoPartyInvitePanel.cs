using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class AutoPartyInvitePanel
{
    private static readonly string[] DigitRuns =
    [
        "000", "111", "222", "333", "444", "555", "666", "777", "888", "999"
    ];

    private static readonly TerritorySelector TerritorySelector = new("autoPartyInviteTerritory");
    private static string customTriggerInput = string.Empty;

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
        var addLabel = OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Add");
        var addButtonSize = OmniControls.CompactButtonSize(addLabel);
        ImGui.SetNextItemWidth(MathF.Max(
            1f,
            ImGui.GetContentRegionAvail().X - addButtonSize.X - ImGui.GetStyle().ItemSpacing.X -
            OmniTheme.BorderThickness() -
            OmniTheme.Scale(MathF.Max(0f, OmniTheme.Tokens.ShadowOffset))));
        OmniControls.InputTextWithHint(
            "##autoPartyInviteTriggerInput",
            OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Hint"),
            ref customTriggerInput,
            128);
        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{addLabel}##autoPartyInviteTriggerAdd",
                false,
                addButtonSize))
        {
            changed |= AddCustomTrigger(config);
        }

        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##autoPartyInviteTriggers",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                ImGui.GetFrameHeightWithSpacing() * 6.35f));
        if (!table)
        {
            return changed;
        }

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
        OmniControls.ScrollableTableHeadersRow();

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
            if (OmniControls.Checkbox("##enabled", ref enabled))
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
            if (OmniControls.Checkbox("##enabled", ref enabled))
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
            if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Delete")}##delete", false))
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

    private static bool DrawTerritories(AutoPartyInviteConfig config) =>
        TerritorySelector.Draw(
            config.AllowedTerritoryIds,
            OmniLoc.Get("Feature.AutoPartyInvite.Territory.EmptySelection"));

    private static bool DrawChannels(AutoPartyInviteConfig config)
    {
        var changed = EnsureChannelsInitialized(config);
        if (!OmniControls.CollapsingHeader(
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

        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.SelectAll")}##channelSelectAll", false))
        {
            for (var index = 0; index < AutoPartyInviteFeature.ChannelOptions.Length; index++)
            {
                changed |= config.EnabledChannels.Add((ushort)AutoPartyInviteFeature.ChannelOptions[index].Type);
            }
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.Clear")}##channelClear", false))
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
                    if (OmniControls.Checkbox($"{GetChannelLabel(option)}##autoPartyInviteChannel{channelId}", ref enabled))
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
}
