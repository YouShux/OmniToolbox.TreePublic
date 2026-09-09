using System.Globalization;
using Dalamud.Interface;
using Dalamud.Game.Text;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private bool openCommandEditor;
    private static bool TryGetCommandGlyph(string text, out SeIconChar glyph)
    {
        if (Enum.TryParse(text, true, out glyph) && Enum.IsDefined(glyph))
        {
            return true;
        }
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            glyph = (SeIconChar)value;
            return Enum.IsDefined(glyph);
        }
        return false;
    }

    private void DrawCommandEditorButton()
    {
        var editCommandsLabel = OmniLoc.Get("Feature.MultiToolbar.EditCommands");
        if (OmniControls.SmallButton(editCommandsLabel + "##multiToolbarEditCommands", false,
                OmniControls.CompactButtonSize(editCommandsLabel)) || openCommandEditor)
        {
            openCommandEditor = false;
            ImGui.OpenPopup("##multiToolbarCommandsEditor");
        }
        ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(1000f, 420f)), ImGui.GetMainViewport().WorkSize), ImGuiCond.Always);
        using var popup = ImRaii.Popup("##multiToolbarCommandsEditor", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        if (!popup)
        {
            return;
        }

        ImGui.SetWindowFontScale(1f);
        DrawEditorBackground(OmniLoc.Get("Feature.MultiToolbar.EditCommands"));
        using var childBackground = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        var changed = false;
        if (OmniControls.IconButton("addCommand", FontAwesomeIcon.Plus, false, OmniLoc.Get("Feature.MultiToolbar.AddCommand")))
        {
            config.Commands.Add(new() { Type = MultiToolbarWidgetType.CustomButton });
            changed = true;
        }
        using (var child = ImRaii.Child("##commandEditorRows", Vector2.Zero, true,
                   ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child)
            {
                ImGui.SetWindowFontScale(1f);
                var rowHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
                using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
                    new Vector2(ImGui.GetStyle().FramePadding.X, MathF.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
                using var table = ImRaii.Table("##commands", 7, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
                if (!table)
                {
                    return;
                }
                ImGui.TableSetupColumn("##reorder", ImGuiTableColumnFlags.WidthFixed, rowHeight);
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.WidgetEnabled"), ImGuiTableColumnFlags.WidthFixed, rowHeight);
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.CommandName"), ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.CommandBody"), ImGuiTableColumnFlags.WidthStretch, 2f);
                ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.CommandIcon"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(90f));
                ImGui.TableSetupColumn("##browser", ImGuiTableColumnFlags.WidthFixed, rowHeight);
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, rowHeight);
                OmniControls.BeginTableHeaderRow(ImGui.GetTextLineHeight());
                OmniControls.TableHeader(string.Empty, ImGui.GetTextLineHeight());
                OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.WidgetEnabled"), ImGui.GetTextLineHeight());
                OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.CommandName"), ImGui.GetTextLineHeight());
                OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.CommandBody"), ImGui.GetTextLineHeight());
                OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.CommandIcon"), ImGui.GetTextLineHeight());
                OmniControls.TableHeader(string.Empty, ImGui.GetTextLineHeight());
                OmniControls.TableHeader(string.Empty, ImGui.GetTextLineHeight());
                var removeIndex = -1;
                var reorderFrom = -1;
                var reorderTo = -1;
                for (var index = 0; index < config.Commands.Count; index++)
                {
                    using var id = ImRaii.PushId(index);
                    var command = config.Commands[index];
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                    ImGui.TableNextColumn();
                    var draggedIndex = DrawReorderHandle("OMNI_TOOLBAR_COMMAND", index, rowHeight);
                    if (draggedIndex >= 0)
                    {
                        reorderFrom = draggedIndex;
                        reorderTo = index;
                    }
                    ImGui.TableNextColumn();
                    var enabled = command.Enabled;
                    if (OmniControls.Checkbox("##enabled", ref enabled, rowHeight))
                    {
                        command.Enabled = enabled;
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    var name = command.Name;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputTextWithHint("##name", OmniLoc.Get("Feature.MultiToolbar.CommandName"), ref name, 128))
                    {
                        command.Name = name;
                    }
                    changed |= ImGui.IsItemDeactivatedAfterEdit();
                    ImGui.TableNextColumn();
                    var text = command.Command;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputTextWithHint("##command", OmniLoc.Get("Feature.MultiToolbar.CommandBody"), ref text, 256))
                    {
                        command.Command = text;
                    }
                    changed |= ImGui.IsItemDeactivatedAfterEdit();
                    ImGui.TableNextColumn();
                    var iconText = command.GameIconID.ToString(CultureInfo.InvariantCulture);
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputText("##iconId", ref iconText, 16, ImGuiInputTextFlags.CharsDecimal) &&
                        uint.TryParse(iconText, out var iconId))
                    {
                        command.GameIconID = iconId;
                        command.IconFilePath = string.Empty;
                        command.IconGlyph = string.Empty;
                        command.ShowIcon = true;
                    }
                    changed |= ImGui.IsItemDeactivatedAfterEdit();
                    ImGui.TableNextColumn();
                    if (OmniControls.IconButton("browseIcon", FontAwesomeIcon.Image, false, new Vector2(rowHeight),
                            OmniLoc.Get("Feature.MultiToolbar.IconBrowser")))
                    {
                        iconBrowser.OpenForSelection(false, value =>
                        {
                            command.GameIconID = value;
                            command.IconFilePath = string.Empty;
                            command.IconGlyph = string.Empty;
                            command.ShowIcon = true;
                            saveConfig();
                        }, null);
                    }
                    ImGui.TableNextColumn();
                    if (OmniControls.IconButton("deleteCommand", FontAwesomeIcon.Trash, false, new Vector2(rowHeight),
                            OmniLoc.Get("Feature.MultiToolbar.RemoveWidget")))
                    {
                        removeIndex = index;
                    }
                }
                if (removeIndex >= 0)
                {
                    config.Commands.RemoveAt(removeIndex);
                    changed = true;
                }
                else
                {
                    changed |= MoveEntry(config.Commands, reorderFrom, reorderTo);
                }
            }
        }
        if (changed)
        {
            saveConfig();
        }
    }
}
