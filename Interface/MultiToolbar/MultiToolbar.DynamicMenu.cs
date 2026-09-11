using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Interface;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public enum MultiToolbarMenuEntryKind
{
    Command, Url, IndividualMacro, SharedMacro, Category,
    Item = 6, KeyItem, Collection, Emote, Minion, Mount, Ornament, GeneralAction, MainCommand, ExtraCommand, Recipe
}

[Serializable]
[Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed class MultiToolbarMenuEntry
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");
    public string ParentID { get; set; } = string.Empty;
    public MultiToolbarMenuEntryKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public uint IconID { get; set; }
    public int MacroIndex { get; set; }
    public uint TargetID { get; set; }
    public bool Expanded { get; set; } = true;
}

public sealed partial class MultiToolbar
{
    private MultiToolbarWidgetConfig? dynamicMenuWidget;
    private MultiToolbarMenuEntry? editingMenuEntry;
    private bool dynamicMenuEditing;

    private static string MenuText(string key) => OmniLoc.Get($"Feature.MultiToolbar.DynamicMenu.{key}");

    private void DrawDynamicMenuPopup()
    {
        if (dynamicMenuWidget is not { } widget || !config.Widgets.Contains(widget) || widget.Type != MultiToolbarWidgetType.DynamicMenu)
        {
            ClosePopup();
            return;
        }
        var changed = widget.MenuEntries.RemoveAll(entry => !Enum.IsDefined(entry.Kind)) > 0;
        var addAtMouse = false;
        if (dynamicMenuEditing)
        {
            changed |= DrawDynamicMenuEditor(widget);
        }
        else
        {
            using var child = ImRaii.Child("##dynamicMenuItems", new Vector2(0f, OmniTheme.Scale(360f)));
            if (child)
            {
                addAtMouse = ImGui.IsWindowHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                foreach (var entry in widget.MenuEntries)
                {
                    if (widget.MenuEntries.Any(parent => parent.Kind == MultiToolbarMenuEntryKind.Category && parent.ID == entry.ParentID))
                    {
                        continue;
                    }
                    if (entry.Kind == MultiToolbarMenuEntryKind.Category)
                    {
                        using var categoryID = ImRaii.PushId(entry.ID);
                        ImGui.SetNextItemOpen(entry.Expanded, ImGuiCond.Always);
                        var expanded = OmniControls.CollapsingHeader(entry.Label + "###category");
                        if (expanded != entry.Expanded)
                        {
                            entry.Expanded = expanded;
                            changed = true;
                        }
                        if (entry.Expanded)
                        {
                            ImGui.Indent();
                            foreach (var item in widget.MenuEntries.Where(item => item.ParentID == entry.ID && item.Kind != MultiToolbarMenuEntryKind.Category))
                            {
                                DrawDynamicMenuItem(item);
                            }
                            ImGui.Unindent();
                        }
                    }
                    else
                    {
                        DrawDynamicMenuItem(entry);
                    }
                }
            }
        }
        if (!dynamicMenuEditing)
        {
            if (addAtMouse) ImGui.OpenPopup("##addDynamicEntry");
            changed |= DrawDynamicMenuAddPopup(widget);
        }
        if (changed)
        {
            saveConfig();
        }
    }

    private void DrawDynamicMenuItem(MultiToolbarMenuEntry entry)
    {
        using var id = ImRaii.PushId(entry.ID);
        var height = MathF.Max(OmniTheme.Scale(38f), ImGui.GetTextLineHeightWithSpacing());
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var clicked = ImGui.Selectable("##invoke", false, ImGuiSelectableFlags.DontClosePopups, new Vector2(width, height));
        var iconID = dynamicMacroIcons.GetValueOrDefault(entry.ID, entry.IconID);
        var offset = iconID == 0 ? 0f : height + ImGui.GetStyle().ItemSpacing.X;
        if (iconID != 0)
        {
            FramedGameIcon.Draw(iconID, position, new Vector2(height),
                drawFrame: ShouldFrameDynamicIcon(entry.Kind, entry.TargetID, iconID));
        }
        var textHeight = ImGui.GetTextLineHeight();
        var textPosition = position + new Vector2(offset, MathF.Max(0f, (height - textHeight) * 0.5f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(textPosition, OmniTheme.Color(OmniTheme.Tokens.Text),
            EllipsizeToolbarText(entry.Label, MathF.Max(1f, width - offset)));
        OmniControls.HelpTooltip(entry.Label);
        if (clicked && InvokeDynamicMenuItem(entry)) ClosePopup();
    }

    private unsafe bool InvokeDynamicMenuItem(MultiToolbarMenuEntry entry)
    {
        switch (entry.Kind)
        {
            case MultiToolbarMenuEntryKind.Command:
                if (!entry.Value.TrimStart().StartsWith('/')) return false;
                ExecuteCommand(entry.Value);
                return true;
            case MultiToolbarMenuEntryKind.Url:
                var value = entry.Value.Trim();
                if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return false;
                Util.OpenLink(uri.AbsoluteUri);
                return true;
            case MultiToolbarMenuEntryKind.IndividualMacro:
            case MultiToolbarMenuEntryKind.SharedMacro:
                var shell = RaptureShellModule.Instance();
                var macros = RaptureMacroModule.Instance();
                if (shell == null || macros == null || shell->MacroLocked || (uint)entry.MacroIndex >= 100) return false;
                var macro = macros->GetMacro(entry.Kind == MultiToolbarMenuEntryKind.SharedMacro ? 1u : 0u, (uint)entry.MacroIndex);
                if (macro == null) return false;
                shell->ExecuteMacro(macro);
                return true;
            default:
                return InvokeDynamicShortcut(entry);
        }
    }

    private bool DrawDynamicMenuEditor(MultiToolbarWidgetConfig widget)
    {
        var changed = false;
        widget.MenuEntries ??= [];
        var entries = widget.MenuEntries;
        if (OmniControls.SmallButton(MenuText("Add"), false)) ImGui.OpenPopup("##addDynamicEntry");
        changed |= DrawDynamicMenuAddPopup(widget);
        return changed | DrawDynamicMenuEntryFields(widget);
    }

    private bool DrawDynamicMenuAddPopup(MultiToolbarWidgetConfig widget)
    {
        var changed = false;
        using (var popup = ImRaii.Popup("##addDynamicEntry"))
        {
            if (popup)
            {
                foreach (var kind in DynamicMenuAddOrder)
                {
                    if (!DrawDynamicAddOption(kind)) continue;
                    if (IsDynamicShortcut(kind))
                    {
                        OpenDynamicShortcutPicker(kind);
                        continue;
                    }
                    editingMenuEntry = new() { Kind = kind, Label = MenuText(kind.ToString()) };
                    widget.MenuEntries.Add(editingMenuEntry);
                    if (ReferenceEquals(dynamicMenuWidget, widget)) dynamicMenuEditing = true;
                    changed = true;
                }
            }
        }
        changed |= DrawDynamicShortcutPicker(widget);
        return changed;
    }

    private bool DrawDynamicMenuEntryFields(MultiToolbarWidgetConfig widget)
    {
        var changed = false;
        var entries = widget.MenuEntries;
        var remove = -1;
        var from = -1;
        var to = -1;
        var fieldSpace = 2f * ImGui.GetFrameHeight() + 4f * ImGui.GetStyle().CellPadding.Y +
                         3f * ImGui.GetStyle().ItemSpacing.Y;
        var listHeight = MathF.Max(ImGui.GetFrameHeight(), ImGui.GetContentRegionAvail().Y - fieldSpace);
        using (var list = ImRaii.Child("##dynamicEntryEditor", new Vector2(0f, listHeight), true))
        {
            if (list)
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    using var id = ImRaii.PushId(entry.ID);
                    var start = ImGui.GetCursorScreenPos();
                    var height = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
                    var spacing = ImGui.GetStyle().ItemSpacing.X;
                    var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - 2f * (height + spacing));
                    if (ImGui.InvisibleButton("##entry", new Vector2(width, height))) editingMenuEntry = entry;
                    var drawList = ImGui.GetWindowDrawList();
                    if (ReferenceEquals(entry, editingMenuEntry) || ImGui.IsItemHovered())
                    {
                        drawList.AddRectFilled(start, start + new Vector2(width, height),
                            ImGui.GetColorU32(ImGui.IsItemHovered() ? ImGuiCol.HeaderHovered : ImGuiCol.Header));
                    }
                    var isCategory = entry.Kind == MultiToolbarMenuEntryKind.Category;
                    var iconID = isCategory ? 0u : dynamicMacroIcons.GetValueOrDefault(entry.ID, entry.IconID);
                    var textOffset = iconID == 0 ? 0f : height + spacing;
                    if (iconID != 0)
                    {
                        FramedGameIcon.Draw(drawList, iconID, start, new Vector2(height),
                            drawFrame: ShouldFrameDynamicIcon(entry.Kind, entry.TargetID, iconID));
                    }
                    var suffix = isCategory ? $"（{MenuText("Category")}）" : string.Empty;
                    var text = EllipsizeToolbarText(entry.Label,
                        MathF.Max(1f, width - textOffset - ImGui.CalcTextSize(suffix).X)) + suffix;
                    drawList.AddText(start + new Vector2(textOffset, (height - ImGui.GetTextLineHeight()) * 0.5f),
                        OmniTheme.Color(OmniTheme.Tokens.Text), text);
                    ImGui.SetCursorScreenPos(start + new Vector2(width + spacing, 0f));
                    var source = DrawReorderHandle($"OMNI_DYNAMIC_MENU_{RuntimeHelpers.GetHashCode(widget)}", index, height);
                    if (source >= 0) { from = source; to = index; }
                    ImGui.SetCursorScreenPos(start + new Vector2(width + height + 2f * spacing, 0f));
                    if (OmniControls.IconButton("delete", FontAwesomeIcon.Trash, false, new Vector2(height), OmniLoc.Get("Common.Delete"))) remove = index;
                    ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + ImGui.GetStyle().ItemSpacing.Y));
                }
            }
        }
        if (remove >= 0)
        {
            var removed = entries[remove];
            foreach (var entry in entries.Where(entry => entry.ParentID == removed.ID)) entry.ParentID = string.Empty;
            entries.RemoveAt(remove);
            if (ReferenceEquals(editingMenuEntry, removed)) editingMenuEntry = null;
            changed = true;
        }
        else changed |= MoveEntry(entries, from, to);
        if (editingMenuEntry is not { } selected || !entries.Contains(selected)) return changed;
        using var fields = ImRaii.Table("##dynamicMenuFields", 2, ImGuiTableFlags.SizingStretchSame);
        if (!fields) return changed;
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var label = selected.Label;
        if (OmniControls.InputText(MenuText("Name"), ref label, 128)) { selected.Label = label; changed = true; }
        if (selected.Kind == MultiToolbarMenuEntryKind.Category) return changed;
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var icon = (int)selected.IconID;
        if (OmniControls.InputInt(MenuText("Icon"), ref icon))
        {
            selected.IconID = (uint)Math.Max(0, icon);
            dynamicMacroIcons.Remove(selected.ID);
            changed = true;
        }
        if (selected.Kind is MultiToolbarMenuEntryKind.Command or MultiToolbarMenuEntryKind.Url)
        {
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var value = selected.Value;
            if (OmniControls.InputText(MenuText(selected.Kind.ToString()), ref value, 1024)) { selected.Value = value; changed = true; }
        }
        if (selected.Kind is MultiToolbarMenuEntryKind.IndividualMacro or MultiToolbarMenuEntryKind.SharedMacro)
        {
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var index = selected.MacroIndex;
            if (OmniControls.InputInt(MenuText("MacroIndex"), ref index)) { selected.MacroIndex = Math.Clamp(index, 0, 99); changed = true; }
        }
        ImGui.TableNextColumn();
        if (OmniControls.BeginCombo("##dynamicParent", entries.FirstOrDefault(entry => entry.ID == selected.ParentID)?.Label ?? MenuText("Root"), ImGui.GetContentRegionAvail().X))
        {
            if (ImGui.Selectable(MenuText("Root"), selected.ParentID.Length == 0)) { selected.ParentID = string.Empty; changed = true; }
            foreach (var category in entries.Where(entry => entry.Kind == MultiToolbarMenuEntryKind.Category))
            {
                if (ImGui.Selectable(category.Label + "##" + category.ID, category.ID == selected.ParentID)) { selected.ParentID = category.ID; changed = true; }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
