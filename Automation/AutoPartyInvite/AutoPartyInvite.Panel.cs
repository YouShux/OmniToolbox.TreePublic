using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AutoPartyInvite
{
    private sealed class Panel
    {
        private static readonly string[] DigitRuns =
        [
            "000", "111", "222", "333", "444", "555", "666", "777", "888", "999"
        ];

        private static readonly TerritorySelector TerritorySelector = new("autoPartyInviteTerritory");
        private string CustomTriggerInput = string.Empty;

        public bool Draw(AutoPartyInviteConfig config)
        {
            var changed = DrawTriggers(config);
            ImGui.Spacing();
            changed |= DrawTerritorySelector(config);
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
            config.BlacklistTerritoryIds ??= [];
            config.WhitelistTerritoryIds ??= [];

            if (!config.ChannelsInitialized)
            {
                if (config.EnabledChannels.Count == 0)
                {
                    foreach (var option in ChannelOptions)
                    {
                        config.EnabledChannels.Add((ushort)option.Type);
                    }
                }

                config.ChannelsInitialized = true;
                changed = true;
            }

            if (config.AllowedTerritoryIds.Count > 0)
            {
                config.WhitelistTerritoryIds.UnionWith(config.AllowedTerritoryIds);
                config.AllowedTerritoryIds.Clear();
                config.UseWhitelist = true;
                changed = true;
            }

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

        private bool DrawTerritorySelector(AutoPartyInviteConfig config)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoPartyInvite.Territory.WorkMode"));
            ImGui.SameLine();
            var changed = false;
            if (OmniControls.RadioButton(
                    $"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.Blacklist")}##autoPartyInviteBlacklist",
                    !config.UseWhitelist))
            {
                config.UseWhitelist = false;
                changed = true;
            }

            ImGui.SameLine();
            if (OmniControls.RadioButton(
                    $"{OmniLoc.Get("Feature.AutoPartyInvite.Territory.Whitelist")}##autoPartyInviteWhitelist",
                    config.UseWhitelist))
            {
                config.UseWhitelist = true;
                changed = true;
            }

            ImGui.SameLine();
            OmniControls.HelpIcon(OmniLoc.Get("Feature.AutoPartyInvite.Territory.WorkMode.Help"));
            ImGui.Spacing();
            changed |= TerritorySelector.Draw(
                config.UseWhitelist ? config.WhitelistTerritoryIds : config.BlacklistTerritoryIds,
                string.Empty);
            return changed;
        }

        private bool DrawTriggers(AutoPartyInviteConfig config)
        {
            var changed = false;
            var digitToggleMask = 0;
            Trigger? toggledTrigger = null;
            var toggledTriggerEnabled = false;
            var addLabel = OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Add");
            var addButtonSize = OmniControls.CompactButtonSize(addLabel);
            var inputWidth = MathF.Max(
                ImGui.GetFontSize() * 8f,
                ImGui.GetContentRegionAvail().X - addButtonSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetNextItemWidth(
                MathF.Max(
                    1f,
                    MathF.Min(inputWidth, ImGui.GetContentRegionAvail().X) -
                    OmniTheme.BorderThickness() -
                    OmniTheme.Scale(MathF.Max(0f, OmniTheme.Tokens.ShadowOffset))));
            OmniControls.InputTextWithHint(
                "##autoPartyInviteTriggerInput",
                OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Hint"),
                ref CustomTriggerInput,
                128);
            OmniControls.SameLineOrWrap(addButtonSize.X);
            if (OmniControls.SmallButton(
                    $"{addLabel}##autoPartyInviteTriggerAdd",
                    false,
                    addButtonSize))
            {
                changed |= AddCustomTrigger(config);
            }

            ImGui.Spacing();
            var enabledWidth = OmniControls.MeasureCheckbox(string.Empty).X;
            var deleteWidth = OmniControls.CompactButtonSize(
                OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Delete")).X;
            using var table = OmniControls.DataTable(
                "##autoPartyInviteTriggers",
                [
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Enabled"),
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Content"),
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type"),
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Action")
                ],
                [enabledWidth, ImGui.GetFontSize() * 6f, ImGui.GetFontSize() * 4f, deleteWidth],
                [
                    MathF.Max(enabledWidth, OmniTheme.Scale(56f)),
                    OmniTheme.Scale(200f),
                    OmniTheme.Scale(110f),
                    MathF.Max(deleteWidth, OmniTheme.Scale(86f))
                ],
                out _,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(
                    ImGui.GetContentRegionAvail().X,
                    ImGui.GetFrameHeightWithSpacing() * 6.35f),
                stretchColumn: 1);
            if (!table)
            {
                return changed;
            }

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

        private bool DrawTriggerState(
            AutoPartyInviteConfig config,
            bool enabledState,
            ref int digitToggleMask,
            ref Trigger? toggledTrigger,
            ref bool toggledTriggerEnabled)
        {
            var changed = false;
            using var wrap = ImRaii.TextWrapPos(0f);
            var detailLayout = ImGui.TableGetColumnCount() == 1;
            for (var digit = 0; digit < DigitRuns.Length; digit++)
            {
                if (config.EnabledDigits.Contains(digit) != enabledState)
                {
                    continue;
                }

                ImGui.PushID($"autoPartyInviteDigit{digit}");
                ImGui.TableNextRow();
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Enabled"),
                    detailLayout);
                var enabled = enabledState;
                if (OmniControls.Checkbox("##enabled", ref enabled))
                {
                    digitToggleMask |= 1 << digit;
                    changed = true;
                }

                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Content"),
                    detailLayout);
                ImGui.TextUnformatted(
                    string.Format(
                        OmniLoc.Get("Feature.AutoPartyInvite.Trigger.DigitFormat"),
                        digit,
                        DigitRuns[digit]));
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type"),
                    detailLayout);
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Digit"));
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Action"),
                    detailLayout);
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
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Enabled"),
                    detailLayout);
                var enabled = trigger.Enabled;
                if (OmniControls.Checkbox("##enabled", ref enabled))
                {
                    toggledTrigger = trigger;
                    toggledTriggerEnabled = enabled;
                    changed = true;
                }

                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Content"),
                    detailLayout);
                ImGui.TextWrapped(trigger.Text);
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type"),
                    detailLayout);
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Type.Exact"));
                OmniControls.NextTableField(
                    OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Action"),
                    detailLayout);
                if (OmniControls.SmallButton(
                        $"{OmniLoc.Get("Feature.AutoPartyInvite.Trigger.Delete")}##delete",
                        false))
                {
                    config.CustomTriggers.RemoveAt(index);
                    index--;
                    changed = true;
                }

                ImGui.PopID();
            }

            return changed;
        }

        private bool AddCustomTrigger(AutoPartyInviteConfig config)
        {
            var text = CustomTriggerInput.Trim();
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

            config.CustomTriggers.Add(
                new()
                {
                    Text = text,
                    Enabled = true
                });
            config.CustomTriggers.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
            CustomTriggerInput = string.Empty;
            return true;
        }

        private bool DrawChannels(AutoPartyInviteConfig config)
        {
            var changed = false;
            if (!OmniControls.CollapsingHeader(
                    string.Format(
                        OmniLoc.Get("Feature.AutoPartyInvite.Channel.Title"),
                        config.EnabledChannels.Count),
                    ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                return changed;
            }

            if (OmniControls.SmallButton(
                    $"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.SelectAll")}##channelSelectAll",
                    false))
            {
                for (var index = 0; index < ChannelOptions.Length; index++)
                {
                    changed |= config.EnabledChannels.Add((ushort)ChannelOptions[index].Type);
                }
            }

            ImGui.SameLine();
            if (OmniControls.SmallButton(
                    $"{OmniLoc.Get("Feature.AutoPartyInvite.Channel.Clear")}##channelClear",
                    false))
            {
                changed |= config.EnabledChannels.Count > 0;
                config.EnabledChannels.Clear();
            }

            ImGui.Spacing();
            var minimumWidth = 0f;
            foreach (var option in ChannelOptions)
            {
                minimumWidth = MathF.Max(minimumWidth, OmniControls.MeasureCheckbox(GetChannelLabel(option)).X);
            }

            var columnCount = OmniControls.ColumnsThatFit(4, minimumWidth);
            using var table = ImRaii.Table(
                "##autoPartyInviteChannels",
                columnCount,
                ImGuiTableFlags.SizingStretchSame,
                new Vector2(ImGui.GetContentRegionAvail().X, 0f));
            if (!table)
            {
                return changed;
            }

            for (var index = 0; index < ChannelOptions.Length; index++)
            {
                if (index % columnCount == 0)
                {
                    ImGui.TableNextRow();
                }

                var option = ChannelOptions[index];
                ImGui.TableSetColumnIndex(index % columnCount);
                var channelID = (ushort)option.Type;
                var enabled = config.EnabledChannels.Contains(channelID);
                if (!OmniControls.Checkbox(
                        $"{GetChannelLabel(option)}##autoPartyInviteChannel{channelID}",
                        ref enabled))
                {
                    continue;
                }

                if (enabled)
                {
                    config.EnabledChannels.Add(channelID);
                }
                else
                {
                    config.EnabledChannels.Remove(channelID);
                }

                changed = true;
            }

            return changed;
        }

        private static string GetChannelLabel(Channel option) =>
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
}
