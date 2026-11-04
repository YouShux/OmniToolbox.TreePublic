using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AutoMount
{
    private readonly TerritorySelector territorySelector = new("autoMountTerritory");
    private MountSelection?            mountSelection;

    public override bool DrawSettings()
    {
        var changed = false;
        var spacing = OmniTheme.Scale(12f);
        var helpSpacing = OmniTheme.Scale(4f);
        var helpSize = OmniControls.HelpIconSize();
        ReadOnlySpan<string> triggers = [OmniLoc.Get(this, "ZoneChange"), OmniLoc.Get(this, "CraftEnd"),
            OmniLoc.Get(this, "GatherEnd"), OmniLoc.Get(this, "CombatEnd")];
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "AutoMountTriggers"));
        OmniControls.SameLineOrWrap(OmniControls.MeasureCheckbox(triggers[0]).X, spacing);
        changed |= OmniControls.Checkbox(triggers[0], ref config.MountWhenZoneChange);

        OmniControls.SameLineOrWrap(OmniControls.MeasureCheckbox(triggers[1]).X, spacing);
        changed |= OmniControls.Checkbox(triggers[1], ref config.MountWhenCraftEnd);

        OmniControls.SameLineOrWrap(OmniControls.MeasureCheckbox(triggers[2]).X, spacing);
        changed |= OmniControls.Checkbox(triggers[2], ref config.MountWhenGatherEnd);

        OmniControls.SameLineOrWrap(OmniControls.MeasureCheckbox(triggers[3]).X, spacing);
        changed |= OmniControls.Checkbox(triggers[3], ref config.MountWhenCombatEnd);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "RidingBehavior"));
        var takeOffLabel = OmniLoc.Get(this, "JumpAfterMount");
        OmniControls.SameLineOrWrap(
            OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(takeOffLabel), helpSize], helpSpacing).X, spacing);
        using (ImRaii.Group())
        {
            changed |= OmniControls.Checkbox(takeOffLabel, ref config.JumpAfterMount);
            ImGui.SameLine(0f, helpSpacing);
            OmniControls.HelpIcon(OmniLoc.Get(this, "JumpAfterMount.Help"), referenceText: takeOffLabel);
        }

        var dismountLabel = OmniLoc.Get(this, "AutoDismount");
        OmniControls.SameLineOrWrap(
            OmniControls.MeasureGroup([OmniControls.MeasureCheckbox(dismountLabel), helpSize], helpSpacing).X, spacing);
        using (ImRaii.Group())
        {
            changed |= OmniControls.Checkbox(dismountLabel, ref config.AutoDismount);
            ImGui.SameLine(0f, helpSpacing);
            OmniControls.HelpIcon(OmniLoc.Get(this, "AutoDismount.Help"), referenceText: dismountLabel);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "SummonSettings"));
        mountSelection ??= new();
        var mountWidth = OmniControls.MeasureCombo(mountSelection.GetPreview(config.SelectedMounts), ImGui.GetFontSize() * 10f).X;
        OmniControls.SameLineOrWrap(OmniControls.MeasureGroup(
            [new Vector2(mountWidth, ImGui.GetFrameHeight()), helpSize], helpSpacing).X, spacing);
        using (ImRaii.Group())
        {
            changed |= mountSelection.Draw(config.SelectedMounts, mountWidth);
            ImGui.SameLine(0f, helpSpacing);
            OmniControls.HelpIcon(OmniLoc.Get(this, "Mount.Help"), referenceText: mountSelection.GetPreview(config.SelectedMounts));
        }

        var delayLabel = OmniLoc.Get(this, "Delay");
        var delaySeconds = config.Delay / 1000f;
        var delayFormat = OmniNumberFormatter.SecondsFormat("%.1f");
        var delayWidth = OmniControls.MeasureFloatInput(delaySeconds, delayFormat, ImGui.GetFontSize() * 8f).X;
        OmniControls.SameLineOrWrap(
            OmniControls.MeasureGroup([ImGui.CalcTextSize(delayLabel), new Vector2(delayWidth, ImGui.GetFrameHeight())], helpSpacing).X,
            spacing);
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(delayLabel);
            ImGui.SameLine(0f, helpSpacing);
            if (OmniControls.SliderFloat("##autoMountDelay", ref delaySeconds, 0f, MathF.Max(10f, delaySeconds),
                    delayFormat, delayWidth))
                config.Delay = (int)Math.Clamp(Math.Round(delaySeconds * 1000d), 0d, int.MaxValue);
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "WorkMode"));
        ImGui.SameLine();
        if (OmniControls.RadioButton($"{OmniLoc.Get(this, "Blacklist")}##autoMountBlacklist", !config.UseWhitelist))
        {
            config.UseWhitelist = false;
            changed = true;
        }

        ImGui.SameLine();
        var whitelistLabel = OmniLoc.Get(this, "Whitelist");
        if (OmniControls.RadioButton($"{whitelistLabel}##autoMountWhitelist", config.UseWhitelist))
        {
            config.UseWhitelist = true;
            changed = true;
        }

        ImGui.SameLine(0f, helpSpacing);
        OmniControls.HelpIcon(OmniLoc.Get(this, "WorkMode.Help"), referenceText: whitelistLabel);
        ImGui.Spacing();
        changed |= territorySelector.Draw(config.UseWhitelist ? config.WhitelistZones : config.BlacklistZones, string.Empty);

        if (changed)
        {
            mountTasks?.Abort();
            dismountTasks?.Abort();
        }
        return changed;
    }

    private sealed class MountSelection
    {
        private readonly LuminaSearcher<Mount> searcher = new(
            LuminaGetter.Get<Mount>().Where(mount => !string.IsNullOrEmpty(mount.Singular.ToString())),
            [mount => mount.RowId.ToString(), mount => mount.Singular.ToString(), mount => (mount.ExtraSeats + 1).ToString()],
            resultLimit: int.MaxValue);
        private readonly List<Mount> visibleMounts = [];
        private string               search = string.Empty;
        private bool                 selectedOnly;

        public string GetPreview(HashSet<uint> selectedMounts)
        {
            var playerState = PlayerState.Instance();
            var count = 0;
            if (playerState != null)
            {
                foreach (var mountID in selectedMounts)
                {
                    if (playerState->IsMountUnlocked(mountID))
                        count++;
                }
            }
            return count == 0 ? OmniLoc.Get("Feature.AutoMount.Roulette") :
                string.Format(OmniLoc.Get("Feature.AutoMount.SelectedCount"), count);
        }

        public bool Draw(HashSet<uint> selectedMounts, float width)
        {
            const int COLUMN_COUNT = 3;
            var maximumSize = OmniWindowPlacement.GetWorkArea().Size - OmniTheme.Scale(new Vector2(16f));
            var rowHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
            var checkWidth = OmniControls.MeasureCheckbox(string.Empty).X;
            var minimumColumnWidth = checkWidth + rowHeight + ImGui.GetStyle().ItemSpacing.X * 2f + ImGui.GetFontSize() * 8f;
            var minimumPopupWidth = (minimumColumnWidth + ImGui.GetStyle().CellPadding.X * 2f) * COLUMN_COUNT +
                ImGui.GetStyle().ScrollbarSize + ImGui.GetStyle().WindowPadding.X * 2f;
            var popupWidth = MathF.Floor(MathF.Min(maximumSize.X, MathF.Max(width, minimumPopupWidth)));
            OmniControls.SetNextAutoResizeWindowSizeConstraints(
                new Vector2(popupWidth, 0f), new Vector2(popupWidth, maximumSize.Y));
            using var combo = OmniControls.Combo("##autoMountMounts", GetPreview(selectedMounts), width);
            if (!combo)
                return false;

            var selectedOnlyLabel = OmniLoc.Get("Feature.AutoMount.SelectedOnly");
            var filterWidth = OmniControls.MeasureCheckbox(selectedOnlyLabel).X;
            var searchWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - filterWidth - ImGui.GetStyle().ItemSpacing.X);
            if (OmniControls.InputTextWithHint("##autoMountSearch", LuminaWrapper.GetAddonText(8128), ref search, 128,
                    searchWidth))
                searcher.Search(search);
            ImGui.SameLine();
            OmniControls.Checkbox(selectedOnlyLabel, ref selectedOnly);

            visibleMounts.Clear();
            var playerState = PlayerState.Instance();
            if (playerState != null)
            {
                foreach (var mount in searcher.SearchResult)
                {
                    if (playerState->IsMountUnlocked(mount.RowId) && (!selectedOnly || selectedMounts.Contains(mount.RowId)))
                        visibleMounts.Add(mount);
                }
            }
            if (visibleMounts.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoMount.NoMatches"));
                return false;
            }

            var rowCount = (visibleMounts.Count + COLUMN_COUNT - 1) / COLUMN_COUNT;
            var rowStride = rowHeight + ImGui.GetStyle().CellPadding.Y * 2f;
            var tableHeight = MathF.Min(rowStride * Math.Min(rowCount, 8) + OmniTheme.BorderThickness() * 2f,
                MathF.Max(rowStride, maximumSize.Y - ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y));
            using var table = ImRaii.Table("##autoMountMountTable", COLUMN_COUNT,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchSame,
                new Vector2(0f, tableHeight));
            if (!table)
                return false;

            for (var column = 0; column < COLUMN_COUNT; column++)
                ImGui.TableSetupColumn($"##mountColumn{column}", ImGuiTableColumnFlags.WidthStretch);
            var changed = false;
            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(rowCount, rowStride);
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                    for (var column = 0; column < COLUMN_COUNT; column++)
                    {
                        var index = row * COLUMN_COUNT + column;
                        if (index >= visibleMounts.Count)
                            break;

                        ImGui.TableNextColumn();
                        var mount = visibleMounts[index];
                        using var rowID = ImRaii.PushId((int)mount.RowId);
                        var selected = selectedMounts.Contains(mount.RowId);
                        var toggled = OmniControls.Checkbox("##selected", ref selected);
                        ImGui.SameLine();
                        var iconSize = new Vector2(rowHeight);
                        FramedGameIcon.Draw(mount.Icon, ImGui.GetCursorScreenPos(), iconSize);
                        ImGui.Dummy(iconSize);
                        if (ImGui.IsItemClicked())
                        {
                            selected ^= true;
                            toggled = true;
                        }
                        ImGui.SameLine();
                        var mountName = mount.Singular.ToString();
                        if (OmniControls.RoundedSelectable(mountName, selected,
                                ImGuiSelectableFlags.DontClosePopups,
                                new Vector2(ImGui.GetContentRegionAvail().X, rowHeight), false))
                        {
                            selected ^= true;
                            toggled = true;
                        }
                        OmniControls.HelpTooltip(mountName);
                        if (toggled)
                            changed |= selected ? selectedMounts.Add(mount.RowId) : selectedMounts.Remove(mount.RowId);
                    }
                }
            }
            clipper.End();
            return changed;
        }
    }
}
