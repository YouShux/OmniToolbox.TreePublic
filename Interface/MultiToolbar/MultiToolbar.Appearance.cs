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
            var theme = OmniTheme.Tokens with { Text = Vector4.One };
            return theme with
            {
                Background = config.BackgroundColor ?? theme.Background,
                Surface = config.BackgroundColor ?? theme.Surface,
                Text = config.TextColor ?? theme.Text,
                Primary = config.AccentColor ?? theme.Primary,
                Accent = config.AccentColor ?? theme.Accent,
                Border = config.BorderColor ?? theme.Border,
            };
        }
    }

    private bool DrawAppearanceSettings(float rowHeight)
    {
        var changed = false;
        var autoExpandLabel = OmniLoc.Get("Feature.MultiToolbar.AutoExpandOnHover");
        using var table = ImRaii.Table("##toolbarColors", 5,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn("##autoExpand", ImGuiTableColumnFlags.WidthFixed,
            ImGui.CalcTextSize(autoExpandLabel).X + rowHeight + ImGui.GetStyle().ItemInnerSpacing.X);
        for (var index = 0; index < 4; index++)
        {
            ImGui.TableSetupColumn($"##color{index}", ImGuiTableColumnFlags.WidthStretch, 1f);
        }
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var autoExpandOnHover = config.AutoExpandOnHover;
        if (OmniControls.Checkbox(autoExpandLabel, ref autoExpandOnHover, rowHeight))
        {
            config.AutoExpandOnHover = autoExpandOnHover;
            changed = true;
        }

        var theme = ToolbarTheme;
        for (var index = 0; index < 4; index++)
        {
            ImGui.TableNextColumn();
            var key = index switch
            {
                0 => "BackgroundColor",
                1 => "TextColor",
                2 => "AccentColor",
                _ => "BorderColor",
            };
            var color = index switch
            {
                0 => theme.Background,
                1 => theme.Text,
                2 => theme.Accent,
                _ => theme.Border,
            };
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get($"Feature.MultiToolbar.{key}"));
            ImGui.SameLine();
            if (OmniControls.ColorEdit($"##toolbar{key}", ref color))
            {
                switch (index)
                {
                    case 0: config.BackgroundColor = color; break;
                    case 1: config.TextColor = color; break;
                    case 2: config.AccentColor = color; break;
                    case 3: config.BorderColor = color; break;
                }
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        return changed;
    }
}
