using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoSwitchConfiguration
{
    private JobSelectionButton? jobSelector;
    private ZoneSelectionButton? territorySelector;
    private Rule? draft;
    private Rule? editingRule;
    private IReadOnlyList<PluginCollectionOption> collections = [];
    private IReadOnlyList<ToolbarCommandOption> toolbarCommands = [];
    private long optionsRefreshedAt;
    private static readonly ActionKind[] ActionKinds = [ActionKind.EnableCollection, ActionKind.DisableCollection, ActionKind.SendCommand];

    public override bool DrawSettings()
    {
        jobSelector ??= new("autoSwitchConfigurationJob");
        territorySelector ??= new("autoSwitchConfigurationTerritory");
        if (editingRule is not null && !config.Rules.Contains(editingRule))
        {
            editingRule = null;
            draft = null;
        }
        draft ??= new()
        {
            TerritoryID = GameState.IsLoggedIn ? GameState.TerritoryType : 0,
            ClassJobID = GameState.IsLoggedIn ? LocalPlayerState.ClassJob : 0
        };
        if (optionsRefreshedAt == 0 || Environment.TickCount64 - optionsRefreshedAt >= 5000)
        {
            collections = source.GetPluginCollections();
            toolbarCommands = source.GetToolbarCommands();
            optionsRefreshedAt = Environment.TickCount64;
        }

        DrawEditor(draft);
        ImGui.Spacing();
        DrawRules();
        return false;
    }

    private void DrawEditor(Rule editor)
    {
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var rowHeight = MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight());
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, MathF.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
        var nameLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Name");
        var anyTerritoryLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.AnyTerritory");
        var noJobLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.NoClassJob");
        var gearsetLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Gearset");
        var job = LuminaGetter.GetRow<ClassJob>(editor.ClassJobID).GetValueOrDefault();
        var jobPreview = job.Name.ToString();
        if (editor.ClassJobID == 0 || string.IsNullOrWhiteSpace(jobPreview))
            jobPreview = OmniLoc.Get("Feature.AutoSwitchConfiguration.SelectJob");
        var territoryPreview = editor.TerritoryID == 0 ? string.Empty : GetTerritoryName(editor.TerritoryID);
        if (string.IsNullOrWhiteSpace(territoryPreview))
            territoryPreview = OmniLoc.Get("Feature.AutoSwitchConfiguration.SelectTerritory");
        var gearsetPreview = GetGearsetName(editor.GearsetID);
        var territorySize = OmniControls.CompactButtonSize(territoryPreview);
        var nameWidth = MathF.Max(1f,
            MathF.Floor(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(nameLabel).X - spacing));
        var jobSize = OmniControls.CompactButtonSize(jobPreview);
        var noJobSize = OmniControls.MeasureCheckbox(noJobLabel, rowHeight);
        var gearsetSize = OmniControls.MeasureCombo(gearsetPreview);
        var saveSize = OmniControls.CompactButtonSize(OmniLoc.Get(editingRule is null
            ? "Feature.AutoSwitchConfiguration.Add" : "Feature.AutoSwitchConfiguration.Save"));
        var cancelSize = editingRule is null ? Vector2.Zero :
            OmniControls.CompactButtonSize(OmniLoc.Get("Feature.AutoSwitchConfiguration.Cancel"));

        using (ImRaii.Group())
        {
            if (OmniControls.InputText($"{nameLabel}##name", ref editor.Name, 128, nameWidth))
                CancelFlow();
        }
        using (ImRaii.Group())
        {
            var anyTerritory = editor.TerritoryID == 0;
            if (OmniControls.Checkbox(anyTerritoryLabel, ref anyTerritory, rowHeight))
            {
                editor.TerritoryID = anyTerritory ? 0 : GameState.IsLoggedIn ? GameState.TerritoryType : 0;
                CancelFlow();
            }
            territorySelector!.SelectedID = editor.TerritoryID;
            OmniControls.SameLineOrWrap(territorySize.X, spacing);
            if (territorySelector.DrawButton(territoryPreview))
            {
                editor.TerritoryID = territorySelector.SelectedID;
                CancelFlow();
            }
        }

        OmniControls.SameLineOrWrap(OmniControls.MeasureGroup(
            [noJobSize, jobSize, ImGui.CalcTextSize(gearsetLabel), gearsetSize], spacing).X, spacing);
        using (ImRaii.Group())
        {
            var noJob = editor.ClassJobID == 0;
            if (OmniControls.Checkbox(noJobLabel, ref noJob, rowHeight))
            {
                editor.ClassJobID = noJob ? 0 : GameState.IsLoggedIn ? LocalPlayerState.ClassJob : 0;
                editor.GearsetID = -1;
                CancelFlow();
            }
            jobSelector!.SelectedID = editor.ClassJobID;
            OmniControls.SameLineOrWrap(jobSize.X, spacing);
            if (jobSelector.DrawButton(jobPreview))
            {
                editor.ClassJobID = jobSelector.SelectedID;
                editor.GearsetID = -1;
                CancelFlow();
            }
            OmniControls.SameLineOrWrap(OmniControls.MeasureGroup(
                [ImGui.CalcTextSize(gearsetLabel), gearsetSize], spacing).X, spacing);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(gearsetLabel);
            OmniControls.SameLineOrWrap(gearsetSize.X, spacing);
            using (ImRaii.Disabled(editor.ClassJobID == 0))
                DrawGearsetCombo(editor, gearsetPreview, gearsetSize.X);
        }
        OmniControls.SameLineOrWrap(editingRule is null ? saveSize.X :
            OmniControls.MeasureGroup([saveSize, cancelSize]).X, spacing);
        using (ImRaii.Group())
            DrawEditorButtons();
    }

    private unsafe void DrawGearsetCombo(Rule editor, string preview, float width)
    {
        if (!OmniControls.BeginCombo("##gearset", preview, width))
            return;

        if (OmniControls.WrappedSelectable(GetGearsetName(-1), editor.GearsetID < 0))
        {
            editor.GearsetID = -1;
            CancelFlow();
        }
        var module = RaptureGearsetModule.Instance();
        if (module != null)
        {
            for (var index = 0; index < module->Entries.Length; index++)
            {
                var gearset = GetGearset(index);
                if (gearset != null && gearset->ClassJob == editor.ClassJobID &&
                    OmniControls.WrappedSelectable(GetGearsetName(index), editor.GearsetID == index))
                {
                    editor.GearsetID = index;
                    CancelFlow();
                }
            }
        }
        ImGui.EndCombo();
    }

    private void DrawEditorButtons()
    {
        if (OmniControls.SmallButton(OmniLoc.Get(editingRule is null
                ? "Feature.AutoSwitchConfiguration.Add" : "Feature.AutoSwitchConfiguration.Save"), false))
        {
            var editor = draft!;
            if (string.IsNullOrWhiteSpace(editor.Name))
                editor.Name = string.Format(OmniLoc.Get("Feature.AutoSwitchConfiguration.RuleNameDefault"), config.Rules.Count + 1);
            if (editingRule is null)
                config.Rules.Add(editor);
            else
            {
                editingRule.Name = editor.Name;
                editingRule.TerritoryID = editor.TerritoryID;
                editingRule.ClassJobID = editor.ClassJobID;
                editingRule.GearsetID = editor.GearsetID;
            }
            editingRule = null;
            draft = null;
            SaveChanges();
        }
        if (editingRule is null)
            return;

        var cancelLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Cancel");
        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(cancelLabel).X);
        if (OmniControls.SmallButton(cancelLabel, false))
        {
            editingRule = null;
            draft = null;
        }
    }

    private void DrawRules()
    {
        if (config.Rules.Count == 0)
            return;

        var editLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Edit");
        var deleteLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Delete");
        var editSize = OmniControls.CompactButtonSize(editLabel);
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var enabledSize = OmniControls.MeasureCheckbox(string.Empty);
        var actionWidth = editSize.X + deleteSize.X + ImGui.GetStyle().ItemSpacing.X;
        string[] labels = [OmniLoc.Get("Feature.AutoSwitchConfiguration.Enabled"),
            OmniLoc.Get("Feature.AutoSwitchConfiguration.Rule"), OmniLoc.Get("Feature.AutoSwitchConfiguration.Actions")];
        using var table = OmniControls.DataTable("##autoSwitchRules", labels,
            [enabledSize.X, ImGui.GetFontSize() * 8f, actionWidth],
            [enabledSize.X, ImGui.GetFontSize() * 24f, actionWidth], out var detailLayout,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
            stretchColumn: 1);
        if (!table)
            return;

        var headerHeight = MathF.Max(ImGui.GetFrameHeight(), enabledSize.Y);
        OmniControls.BeginTableHeaderRow(headerHeight);
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        OmniControls.CenterTableItem(enabledSize, headerHeight);
        var allEnabled = config.Rules.TrueForAll(rule => rule.Enabled);
        if (OmniControls.Checkbox("##enableAll", ref allEnabled))
        {
            foreach (var rule in config.Rules)
                rule.Enabled = allEnabled;
            SaveChanges();
        }
        OmniControls.HelpTooltip(OmniLoc.Get("Feature.AutoSwitchConfiguration.EnableAll"));
        if (!detailLayout)
        {
            OmniControls.TableHeader(labels[1]);
            OmniControls.TableHeader(labels[2]);
        }
        for (var index = 0; index < config.Rules.Count; index++)
        {
            var rule = config.Rules[index];
            using var ruleID = ImRaii.PushId(rule.GetHashCode());
            var title = $"{GetTerritoryName(rule.TerritoryID)} · {GetClassJobName(rule.ClassJobID)} · {rule.Name}";
            ImGui.TableNextRow();
            OmniControls.NextTableField(labels[0], detailLayout);
            var headerWidth = ImGui.GetContentRegionAvail().X;
            if (!detailLayout)
            {
                ImGui.TableSetColumnIndex(1);
                headerWidth = ImGui.GetContentRegionAvail().X;
                ImGui.TableSetColumnIndex(0);
            }
            var textWidth = MathF.Max(1f, headerWidth - ImGui.GetFontSize() - ImGui.GetStyle().FramePadding.X * 2f -
                ImGui.GetStyle().CellPadding.X * 2f - ImGui.GetStyle().ItemInnerSpacing.X);
            var textSize = ImGui.CalcTextSize(title, false, textWidth);
            var rowContentHeight = MathF.Max(MathF.Max(editSize.Y, enabledSize.Y),
                textSize.Y + ImGui.GetStyle().FramePadding.Y * 2f);
            if (!detailLayout)
                OmniControls.CenterTableItem(enabledSize, rowContentHeight);
            if (OmniControls.Checkbox("##enabled", ref rule.Enabled))
                SaveChanges();

            OmniControls.NextTableField(labels[1], detailLayout);
            bool open;
            using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
                       new Vector2(ImGui.GetStyle().FramePadding.X,
                           MathF.Max(0f, (rowContentHeight - ImGui.GetTextLineHeight()) * 0.5f))))
                open = OmniControls.CollapsingHeader("##rule");
            var headerMin = ImGui.GetItemRectMin();
            var headerMax = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(headerMin, headerMax, true);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                headerMin + new Vector2(ImGui.GetStyle().FramePadding.X + ImGui.GetFontSize() +
                    ImGui.GetStyle().ItemInnerSpacing.X, MathF.Max(0f, (headerMax.Y - headerMin.Y - textSize.Y) * 0.5f)),
                ImGui.GetColorU32(ImGuiCol.Text), title, textWidth);
            drawList.PopClipRect();
            if (open)
                DrawActionsTable(rule);

            OmniControls.NextTableField(labels[2], detailLayout);
            if (!detailLayout)
                OmniControls.CenterTableItem(new Vector2(actionWidth, editSize.Y), rowContentHeight);
            if (OmniControls.SmallButton(editLabel, false))
            {
                CancelFlow();
                editingRule = rule;
                draft = rule.Copy();
            }
            OmniControls.SameLineOrWrap(deleteSize.X);
            if (!OmniControls.SmallButton(deleteLabel, false))
                continue;

            config.Rules.RemoveAt(index--);
            SaveChanges();
        }
    }

    private void DrawActionsTable(Rule rule)
    {
        var addLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.AddAction");
        var addSize = OmniControls.CompactButtonSize(addLabel);
        var deleteLabel = OmniLoc.Get("Feature.AutoSwitchConfiguration.Delete");
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var arrowSize = new Vector2(OmniTheme.CheckboxSize());
        var controlHeight = MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight());
        var actionWidth = arrowSize.X * 2f + MathF.Max(deleteSize.X, addSize.X) + ImGui.GetStyle().ItemSpacing.X * 2f;
        var typeWidth = 0f;
        foreach (var type in ActionKinds)
            typeWidth = MathF.Max(typeWidth, OmniControls.MeasureCombo(GetActionKindName(type)).X);
        string[] labels = [OmniLoc.Get("Feature.AutoSwitchConfiguration.ActionType"),
            OmniLoc.Get("Feature.AutoSwitchConfiguration.ActionContent"), OmniLoc.Get("Feature.AutoSwitchConfiguration.Actions")];
        var contentWidth = MathF.Max(ImGui.GetFontSize() * 6f, OmniControls.MeasureTableHeader(labels[1], true).X);
        using var table = OmniControls.DataTable("##actions", labels,
            [typeWidth, contentWidth, actionWidth],
            [typeWidth, ImGui.GetFontSize() * 16f, actionWidth], out var detailLayout,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp, stretchColumn: 1);
        if (!table)
            return;

        if (!detailLayout)
        {
            OmniControls.BeginTableHeaderRow();
            OmniControls.TableHeader(labels[0]);
            OmniControls.TableHeader(labels[1], OmniLoc.Get("Feature.AutoSwitchConfiguration.CommandsHelp"));
            OmniControls.TableHeader(labels[2]);
        }
        for (var index = 0; index < rule.Actions.Count; index++)
        {
            var step = rule.Actions[index];
            using var stepID = ImRaii.PushId(step.GetHashCode());
            var commandLines = 1;
            if (step.Type == ActionKind.SendCommand)
            {
                foreach (var character in step.Command)
                    if (character == '\n') commandLines++;
            }
            var commandHeight = MathF.Max(controlHeight,
                ImGui.GetTextLineHeightWithSpacing() * Math.Min(3, commandLines) + ImGui.GetStyle().FramePadding.Y * 2f);
            var rowContentHeight = MathF.Max(MathF.Max(arrowSize.Y, deleteSize.Y),
                step.Type == ActionKind.SendCommand
                    ? commandHeight + controlHeight + ImGui.GetStyle().ItemSpacing.Y
                    : controlHeight);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, detailLayout ? 0f : rowContentHeight);
            OmniControls.NextTableField(labels[0], detailLayout);
            if (!detailLayout)
                OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, controlHeight), rowContentHeight);
            if (OmniControls.BeginCombo("##type", GetActionKindName(step.Type), ImGui.GetContentRegionAvail().X))
            {
                foreach (var type in ActionKinds)
                {
                    if (!OmniControls.WrappedSelectable(GetActionKindName(type), step.Type == type))
                        continue;
                    step.Type = type;
                    SaveChanges();
                }
                ImGui.EndCombo();
            }

            OmniControls.NextTableField(labels[1], detailLayout);
            if (detailLayout)
            {
                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get("Feature.AutoSwitchConfiguration.CommandsHelp"));
            }
            if (step.Type == ActionKind.SendCommand)
            {
                DrawToolbarCommandCombo(step);
                if (OmniControls.InputTextMultiline("##command", ref step.Command, 4096,
                        new Vector2(ImGui.GetContentRegionAvail().X, commandHeight)))
                    CancelFlow();
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveChanges();
            }
            else
            {
                if (!detailLayout)
                    OmniControls.CenterTableItem(new Vector2(ImGui.GetContentRegionAvail().X, controlHeight), rowContentHeight);
                DrawCollectionCombo(step);
            }

            OmniControls.NextTableField(labels[2], detailLayout);
            if (!detailLayout)
                OmniControls.CenterTableItem(new Vector2(actionWidth, MathF.Max(arrowSize.Y, deleteSize.Y)), rowContentHeight);
            using (ImRaii.Disabled(index == 0))
            {
                if (OmniControls.IconButton("##up", FontAwesomeIcon.ArrowUp, false,
                        OmniLoc.Get("Feature.AutoSwitchConfiguration.MoveUp")))
                {
                    (rule.Actions[index - 1], rule.Actions[index]) = (step, rule.Actions[index - 1]);
                    SaveChanges();
                    return;
                }
            }
            OmniControls.SameLineOrWrap(arrowSize.X);
            using (ImRaii.Disabled(index == rule.Actions.Count - 1))
            {
                if (OmniControls.IconButton("##down", FontAwesomeIcon.ArrowDown, false,
                        OmniLoc.Get("Feature.AutoSwitchConfiguration.MoveDown")))
                {
                    (rule.Actions[index + 1], rule.Actions[index]) = (step, rule.Actions[index + 1]);
                    SaveChanges();
                    return;
                }
            }
            OmniControls.SameLineOrWrap(deleteSize.X);
            if (!OmniControls.SmallButton(deleteLabel, false))
                continue;

            rule.Actions.Remove(step);
            index--;
            SaveChanges();
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.None, detailLayout ? 0f : addSize.Y);
        OmniControls.NextTableField(labels[0], detailLayout);
        OmniControls.NextTableField(labels[1], detailLayout);
        OmniControls.NextTableField(labels[2], detailLayout);
        if (!detailLayout)
            OmniControls.CenterTableItem(new Vector2(actionWidth, addSize.Y), addSize.Y);
        if (actionWidth <= ImGui.GetContentRegionAvail().X + 1f)
        {
            ImGui.Dummy(new Vector2(arrowSize.X * 2f + ImGui.GetStyle().ItemSpacing.X, addSize.Y));
            ImGui.SameLine();
        }
        if (OmniControls.SmallButton(addLabel, false))
        {
            rule.Actions.Add(new() { Type = ActionKind.EnableCollection });
            SaveChanges();
        }
    }

    private void DrawToolbarCommandCombo(ActionStep step)
    {
        if (!OmniControls.BeginCombo("##toolbarCommand", OmniLoc.Get("Feature.AutoSwitchConfiguration.SelectToolbarCommand"),
                ImGui.GetContentRegionAvail().X))
            return;

        if (toolbarCommands.Count == 0)
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoSwitchConfiguration.NoToolbarCommands"));
        for (var index = 0; index < toolbarCommands.Count; index++)
        {
            var command = toolbarCommands[index];
            if (!OmniControls.WrappedSelectable($"{command.Name}##command{index}"))
                continue;

            step.Command = command.Command;
            SaveChanges();
        }
        ImGui.EndCombo();
    }

    private void DrawCollectionCombo(ActionStep step)
    {
        var preview = OmniLoc.Get(step.CollectionID == Guid.Empty
            ? "Feature.AutoSwitchConfiguration.SelectCollection" : "Feature.AutoSwitchConfiguration.CollectionUnavailable");
        foreach (var collection in collections)
        {
            if (collection.ID == step.CollectionID)
            {
                preview = collection.Name;
                break;
            }
        }
        if (!OmniControls.BeginCombo("##collection", preview, ImGui.GetContentRegionAvail().X))
            return;

        if (collections.Count == 0)
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoSwitchConfiguration.NoCollections"));
        foreach (var collection in collections)
        {
            if (!OmniControls.WrappedSelectable($"{collection.Name}##{collection.ID}", step.CollectionID == collection.ID))
                continue;

            step.CollectionID = collection.ID;
            SaveChanges();
        }
        ImGui.EndCombo();
    }

    private static string GetActionKindName(ActionKind type) => OmniLoc.Get(type switch
    {
        ActionKind.EnableCollection => "Feature.AutoSwitchConfiguration.EnableCollection",
        ActionKind.DisableCollection => "Feature.AutoSwitchConfiguration.DisableCollection",
        _ => "Feature.AutoSwitchConfiguration.SendCommand"
    });

    private static string GetTerritoryName(uint territoryID) => territoryID == 0
        ? OmniLoc.Get("Feature.AutoSwitchConfiguration.AnyTerritory")
        : LuminaGetter.GetRow<TerritoryType>(territoryID).GetValueOrDefault().ExtractPlaceName();

    private static string GetClassJobName(uint classJobID) => classJobID == 0
        ? OmniLoc.Get("Feature.AutoSwitchConfiguration.NoClassJob")
        : LuminaGetter.GetRow<ClassJob>(classJobID).GetValueOrDefault().Name.ToString();

    private static unsafe string GetGearsetName(int gearsetID)
    {
        if (gearsetID < 0)
            return OmniLoc.Get("Feature.AutoSwitchConfiguration.AutoGearset");

        var gearset = GetGearset(gearsetID);
        return gearset == null
            ? string.Format(OmniLoc.Get("Feature.AutoSwitchConfiguration.GearsetFallback"), gearsetID + 1)
            : $"[{gearsetID + 1}] {gearset->NameString}";
    }
}
