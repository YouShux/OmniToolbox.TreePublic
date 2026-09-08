using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private ThemeTokens ToolbarTheme
    {
        get
        {
            var theme = OmniTheme.BaseTokens;
            var background = OmniTheme.UsesDarkPalette ? theme.Background : theme.Surface with { W = 1f };
            return theme with
            {
                Background = config.BackgroundColor ?? background,
                Surface = config.BackgroundColor ?? (OmniTheme.UsesDarkPalette ? theme.Surface : background),
                Text = config.TextColor ?? theme.Text,
                Primary = config.AccentColor ?? theme.Primary,
                Accent = config.AccentColor ?? theme.Accent,
                Secondary = config.AccentColor ?? theme.Secondary,
                Border = config.BorderColor ?? theme.Border,
            };
        }
    }

    private static void DrawEditorBackground(string title)
    {
        using var theme = new OmniTheme.ColorScope(OmniTheme.BaseTokens);
        OmniControls.DrawWindowBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
        ImGui.TextUnformatted(title);
        ImGui.Separator();
        var inset = ImGui.GetStyle().WindowPadding * 0.5f;
        var panelPosition = ImGui.GetCursorScreenPos() - inset;
        var panelSize = ImGui.GetWindowPos() + ImGui.GetWindowSize() - inset - panelPosition;
        OmniControls.DrawPanelBackground(panelPosition, panelSize, OmniTheme.Tokens.Surface);
    }

    private bool DrawColorSettingsButton()
    {
        var label = OmniLoc.Get("Feature.MultiToolbar.ColorSettings");
        if (OmniControls.SmallButton(label + "##multiToolbarColorSettings", false,
                OmniControls.CompactButtonSize(label)))
        {
            ImGui.OpenPopup("##multiToolbarColorSettingsPopup");
        }
        using var popup = ImRaii.Popup("##multiToolbarColorSettingsPopup",
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
        {
            return false;
        }

        DrawEditorBackground(label);
        using var table = ImRaii.Table("##toolbarColors", 2,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX);
        if (!table)
        {
            return false;
        }
        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##color", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());

        var changed = false;
        var theme = ToolbarTheme;
        for (var index = 0; index < 5; index++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var key = index switch
            {
                0 => "BackgroundColor",
                1 => "TextColor",
                2 => "ToolbarTextColor",
                3 => "AccentColor",
                _ => "BorderColor",
            };
            var color = index switch
            {
                0 => theme.Background,
                1 => theme.Text,
                2 => config.ToolbarTextColor,
                3 => theme.Accent,
                _ => theme.Border,
            };
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get($"Feature.MultiToolbar.{key}"));
            ImGui.TableNextColumn();
            if (OmniControls.ColorEdit($"##toolbar{key}", ref color))
            {
                switch (index)
                {
                    case 0: config.BackgroundColor = color; break;
                    case 1: config.TextColor = color; break;
                    case 2: config.ToolbarTextColor = color; break;
                    case 3: config.AccentColor = color; break;
                    case 4: config.BorderColor = color; break;
                }
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        return changed;
    }
}
