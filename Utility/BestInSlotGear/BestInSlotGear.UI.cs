using System.Globalization;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BestInSlotGear
{
    private void DrawWindow()
    {
        ProcessWarmup();
        ProcessPendingCalculation();
        if (!isWindowOpen)
        {
            return;
        }

        InitializeDefaults();
        using var font = FontManager.Instance().UIFont.Push();
        var scaleValue = OmniTheme.ScaleValue;
        if (lastScaleValue > 0f &&
            MathF.Abs(scaleValue - lastScaleValue) > 0.001f &&
            expandedWindowSize != Vector2.Zero)
        {
            restoreExpandedSize = true;
        }

        lastScaleValue = scaleValue;
        using var style = new ComicStyleScope();
        if (isCollapsed)
        {
            ImGui.SetNextWindowSize(
                OmniTheme.CollapsedWindowSize(
                    expandedWindowSize == Vector2.Zero
                        ? OmniTheme.Scale(1120f)
                        : OmniTheme.Scale(expandedWindowSize).X),
                ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextWindowSizeConstraints(
                OmniTheme.Scale(new Vector2(860f, 620f)),
                ImGuiHelpers.MainViewport.Size);
            ImGui.SetNextWindowSize(
                expandedWindowSize == Vector2.Zero
                    ? OmniTheme.Scale(new Vector2(1120f, 760f))
                    : OmniTheme.Scale(expandedWindowSize),
                restoreExpandedSize ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
            restoreExpandedSize = false;
        }

        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground;
        if (isCollapsed)
        {
            flags |= ImGuiWindowFlags.NoResize;
        }

        if (!ImGui.Begin("###OmniBestInSlotGear", flags))
        {
            ImGui.End();
            return;
        }

        var closeWithEscape =
            ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
            ImGui.IsKeyPressed(ImGuiKey.Escape, false) &&
            !ImGui.IsPopupOpen("##bestInSlotGearExport") &&
            !ImGui.IsPopupOpen("##bestInSlotGearImport");

        var windowPosition = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (!isCollapsed)
        {
            expandedWindowSize = OmniTheme.Unscale(windowSize);
        }

        var framePosition = isCollapsed
            ? windowPosition + new Vector2(OmniTheme.CollapsedHeaderSafeInset(), OmniTheme.CollapsedHeaderTop())
            : windowPosition + new Vector2(OmniTheme.ChromeFrameInset());
        var frameSize = isCollapsed
            ? new Vector2(
                MathF.Max(1f, windowSize.X - OmniTheme.CollapsedHeaderSafeInset() * 2f),
                OmniTheme.TitleBarHeight())
            : windowSize - new Vector2(OmniTheme.ChromeFrameInset() * 2f);
        var titleBarSize = new Vector2(frameSize.X, OmniTheme.TitleBarHeight());
        OmniControls.DrawWindowBackground(framePosition, frameSize, isCollapsed);
        OmniControls.DrawTextCentered(Info.Title, framePosition, titleBarSize, OmniTheme.Tokens.Text);
        ImGui.SetCursorScreenPos(
            framePosition + new Vector2(
                titleBarSize.X - OmniTheme.TitleExpandIconRight(),
                OmniTheme.TitleIconTop()));
        var collapseChanged = false;
        if (OmniControls.TitleTriangleButton(isCollapsed, "##collapseBestInSlotGear"))
        {
            if (isCollapsed)
            {
                restoreExpandedSize = true;
            }

            isCollapsed = !isCollapsed;
            collapseChanged = true;
        }

        ImGui.SetCursorScreenPos(
            framePosition + new Vector2(
                titleBarSize.X - OmniTheme.TitleCloseIconRight(),
                OmniTheme.TitleIconTop()));
        if (OmniControls.CloseButton("##closeBestInSlotGear", OmniTheme.TitleIconSize()) || closeWithEscape)
        {
            CancelCalculation();
            isWindowOpen = false;
        }

        if (!isWindowOpen || isCollapsed || collapseChanged)
        {
            ImGui.End();
            return;
        }

        DrawWindowContent(framePosition, frameSize);
        if (openExportPopup)
        {
            ImGui.OpenPopup("##bestInSlotGearExport");
            openExportPopup = false;
        }

        if (openImportPopup)
        {
            ImGui.OpenPopup("##bestInSlotGearImport");
            openImportPopup = false;
        }

        DrawExportPopup();
        DrawImportPopup();
        ImGui.End();
    }

    private void DrawWindowContent(Vector2 framePosition, Vector2 frameSize)
    {
        var contentPosition = framePosition + new Vector2(
            OmniTheme.WindowInset(),
            OmniTheme.TitleBarHeight() + OmniTheme.WindowInset());
        var contentSize = new Vector2(
            MathF.Max(1f, frameSize.X - OmniTheme.WindowInset() * 2f),
            MathF.Max(1f, frameSize.Y - OmniTheme.TitleBarHeight() - OmniTheme.WindowInset() * 2f));
        ImGui.SetCursorScreenPos(contentPosition);
        OmniControls.DrawPanelBackground(contentPosition, contentSize, OmniTheme.Tokens.Surface);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.Scale(OmniTheme.Tokens.WindowPadding)))
        using (var content = ImRaii.Child(
                   "##bestInSlotGearContent",
                   contentSize,
                   false,
                   ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (content)
            {
                DrawCalculatorContent();
            }
        }
    }

    private void DrawCalculatorContent()
    {
        ObserveCalculationTask();
        ProcessCalculationPreparation();
        var isCalculating = pendingCalculation is not null || calculationPreparation is not null || calculationTask is not null;
        var buttonLabel = OmniLoc.Get(isCalculating
            ? "Feature.BestInSlotGear.Cancel"
            : "Feature.BestInSlotGear.Calculate");
        var style = ImGui.GetStyle();
        var buttonSize = OmniControls.CompactButtonSize(buttonLabel);
        var jobLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.Job");
        var itemLevelLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.ItemLevel");
        var dutyPresetLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.DutyPreset");
        var gearStageLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.GearStage");
        var targetLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.OptimizationTarget");
        var itemLevelWidth = ImGui.CalcTextSize("99999").X + style.FramePadding.X * 2f;
        var numericTargetWidth = ImGui.CalcTextSize("100.00").X + style.FramePadding.X * 2f;
        var gearStageWidth = ImGui.CalcTextSize(GetGearStageName(gearStage)).X +
                             ImGui.GetFrameHeight() + style.FramePadding.X * 2f;
        var targetWidth = ImGui.CalcTextSize(GetOptimizationTargetName(optimizationTarget)).X +
                          ImGui.GetFrameHeight() + style.FramePadding.X * 2f;
        var dutyPresetWidth = MathF.Min(OmniTheme.Scale(230f), ImGui.GetContentRegionAvail().X);
        var controlSpacing = style.ItemSpacing.X + OmniTheme.Scale(MathF.Max(0f, OmniTheme.Tokens.ShadowOffset));
        var jobWidth = MathF.Min(
            ImGui.CalcTextSize(GetJobPreview(selectedJobID)).X + ImGui.GetFrameHeight() + style.FramePadding.X * 2f,
            OmniTheme.Scale(180f));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(jobLabel);
        ImGui.SameLine(0f, controlSpacing);
        if (DrawJobCombo(jobWidth))
        {
            CancelCalculation();
            targetItemLevel = GetDefaultItemLevel(selectedJobID);
            selectedDutyID = 0;
            result = null;
            loadedSavedResultID = null;
        }

        ContinueControlRow(ImGui.CalcTextSize(itemLevelLabel).X + itemLevelWidth, controlSpacing);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(itemLevelLabel);
        ImGui.SameLine(0f, controlSpacing);
        if (OmniControls.InputInt("##bestInSlotGearItemLevel", ref targetItemLevel, itemLevelWidth))
        {
            CancelCalculation();
            targetItemLevel = Math.Clamp(targetItemLevel, MinimumSyncItemLevel, 999);
            selectedDutyID = 0;
            result = null;
            loadedSavedResultID = null;
        }

        ContinueControlRow(ImGui.CalcTextSize(dutyPresetLabel).X + dutyPresetWidth, controlSpacing);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(dutyPresetLabel);
        ImGui.SameLine(0f, controlSpacing);
        if (DrawDutyPresetCombo(dutyPresetWidth))
        {
            CancelCalculation();
            result = null;
            loadedSavedResultID = null;
        }

        ContinueControlRow(ImGui.CalcTextSize(gearStageLabel).X + gearStageWidth, controlSpacing);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(gearStageLabel);
        ImGui.SameLine(0f, controlSpacing);
        if (DrawGearStageCombo(gearStageWidth))
        {
            CancelCalculation();
            result = null;
            loadedSavedResultID = null;
        }

        ContinueControlRow(ImGui.CalcTextSize(targetLabel).X + targetWidth, controlSpacing);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(targetLabel);
        ImGui.SameLine(0f, controlSpacing);
        if (DrawOptimizationTargetCombo(targetWidth))
        {
            CancelCalculation();
            result = null;
            loadedSavedResultID = null;
        }

        if (optimizationTarget == OptimizationTarget.SpecificGcd)
        {
            var targetGcdLabel = OmniLoc.Get("Feature.BestInSlotGear.Input.TargetGcd");
            ContinueControlRow(ImGui.CalcTextSize(targetGcdLabel).X + numericTargetWidth, controlSpacing);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(targetGcdLabel);
            ImGui.SameLine(0f, controlSpacing);
            if (OmniControls.InputFloat("##bestInSlotGearTargetGcd", ref targetGcd, numericTargetWidth, 0f, 0f, "%.2f"))
            {
                CancelCalculation();
                targetGcd = Math.Clamp(targetGcd, 1.5f, 3f);
                result = null;
                loadedSavedResultID = null;
            }
        }
        ContinueControlRow(buttonSize.X, controlSpacing);
        if (OmniControls.SmallButton(buttonLabel, isCalculating, buttonSize))
        {
            if (isCalculating)
            {
                CancelCalculation();
            }
            else
            {
                StartCalculation(selectedJobID, targetItemLevel);
            }
        }

        ImGui.SameLine(0f, controlSpacing);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.BestInSlotGear.Scope"));
        if (isCalculating)
        {
            ImGui.SameLine(0f, controlSpacing);
            using var progressColors = ImRaii.PushColor(ImGuiCol.PlotHistogram, OmniTheme.ControlAccent)
                .Push(ImGuiCol.FrameBg, OmniTheme.Tokens.Surface);
            var progressPosition = ImGui.GetCursorScreenPos();
            var progressSize = new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), buttonSize.Y);
            ImGui.ProgressBar(calculationProgress, progressSize, string.Empty);
            OmniControls.DrawTextCentered(
                OmniLoc.Get(calculationStatusKey),
                progressPosition,
                progressSize,
                OmniTheme.Tokens.Text);
        }

        if (!isCalculating)
        {
            ImGui.Spacing();
            DrawSavedResultsControls(controlSpacing);
        }

        ImGui.Spacing();

        if (result is null)
        {
            return;
        }

        if (result.Error is not null)
        {
            ImGui.TextWrapped(result.Error);
            return;
        }

        if (result.JobResults.Count == 0)
        {
            DrawSummary(result);
            DrawEquipmentComparison(result.Items, result.Food, EquipmentDisplay.All, true, result.JobID);
            return;
        }

        ImGui.TextUnformatted(OmniLoc.Get("Feature.BestInSlotGear.Section.SharedGear"));
        DrawEquipmentComparison(
            result.Items,
            null,
            EquipmentDisplay.Shared,
            result.JobID == TankPresetID
                ? TankJobIDs.Contains(LocalPlayerState.ClassJob)
                : HealerJobIDs.Contains(LocalPlayerState.ClassJob),
            result.JobID);
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.BestInSlotGear.Section.JobMainHandsAndFood"));
        foreach (var jobResult in result.JobResults)
        {
            DrawSummary(jobResult);
            DrawEquipmentComparison(
                jobResult.Items,
                jobResult.Food,
                EquipmentDisplay.Weapons,
                LocalPlayerState.ClassJob == jobResult.JobID,
                jobResult.JobID);
        }
    }

    private void DrawSavedResultsControls(float spacing)
    {
        var loaded = loadedSavedResultID is null
            ? null
            : config.SavedResults.Find(saved => saved.ID == loadedSavedResultID);
        var comboWidth = MathF.Min(OmniTheme.Scale(320f), MathF.Max(OmniTheme.Scale(160f), ImGui.GetContentRegionAvail().X));
        if (OmniControls.BeginCombo(
                "##bestInSlotGearSavedResults",
                loaded is null
                    ? OmniLoc.Get("Feature.BestInSlotGear.SavedResults")
                    : GetSavedResultLabel(loaded),
                comboWidth))
        {
            if (config.SavedResults.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.BestInSlotGear.NoSavedResults"));
            }
            else
            {
                for (var index = config.SavedResults.Count - 1; index >= 0; index--)
                {
                    var saved = config.SavedResults[index];
                    var selected = saved.ID == loadedSavedResultID;
                    if (ImGui.Selectable(GetSavedResultLabel(saved), selected))
                    {
                        LoadSavedResult(saved);
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
            }

            ImGui.EndCombo();
        }

        if (result is { Error: null })
        {
            var saveLabel = OmniLoc.Get("Feature.BestInSlotGear.Save");
            var saveSize = OmniControls.CompactButtonSize(saveLabel);
            ContinueControlRow(saveSize.X, spacing);
            if (OmniControls.SmallButton(saveLabel, false, saveSize))
            {
                SaveCurrentResult();
            }
        }

        if (loaded is not null)
        {
            var deleteLabel = OmniLoc.Get("Feature.BestInSlotGear.DeleteSaved");
            var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
            ContinueControlRow(deleteSize.X, spacing);
            if (OmniControls.SmallButton(deleteLabel, false, deleteSize))
            {
                config.SavedResults.Remove(loaded);
                loadedSavedResultID = null;
                saveConfig();
            }
        }

        if (result is { Error: null } current)
        {
            var exportLabel = OmniLoc.Get("Feature.BestInSlotGear.Export");
            var exportSize = OmniControls.CompactButtonSize(exportLabel);
            ContinueControlRow(exportSize.X, spacing);
            if (OmniControls.SmallButton(exportLabel, false, exportSize))
            {
                if (current.JobResults.Count > 0)
                {
                    openExportPopup = true;
                }
                else if (!TryExportResult(current, out var exportError))
                {
                    OmniNotifier.Popup(Info.Title, exportError, NotificationType.Error);
                }
            }
        }

        var importLabel = OmniLoc.Get("Feature.BestInSlotGear.Import");
        var importSize = OmniControls.CompactButtonSize(importLabel);
        ContinueControlRow(importSize.X, spacing);
        if (OmniControls.SmallButton(importLabel, false, importSize))
        {
            importShareText = ImGui.GetClipboardText() ?? string.Empty;
            importShareError = string.Empty;
            openImportPopup = true;
        }
    }

    private void DrawExportPopup()
    {
        using var colors = ImRaii.PushColor(ImGuiCol.PopupBg, OmniTheme.Tokens.Background)
            .Push(ImGuiCol.Border, OmniTheme.Tokens.Border);
        using var border = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, OmniTheme.BorderThickness());
        if (!ImGui.BeginPopup("##bestInSlotGearExport", ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextUnformatted(OmniLoc.Get("Feature.BestInSlotGear.Share.SelectExportJob"));
        if (result is { Error: null } current)
        {
            foreach (var jobResult in current.JobResults)
            {
                if (!ImGui.MenuItem(jobResult.JobName))
                {
                    continue;
                }

                if (!TryExportResult(
                        jobResult with { Items = [.. current.Items, .. jobResult.Items] },
                        out var exportError))
                {
                    OmniNotifier.Popup(Info.Title, exportError, NotificationType.Error);
                }

                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void DrawImportPopup()
    {
        using var colors = ImRaii.PushColor(ImGuiCol.PopupBg, OmniTheme.Tokens.Background)
            .Push(ImGuiCol.Border, OmniTheme.Tokens.Border);
        using var border = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, OmniTheme.BorderThickness());
        if (!ImGui.BeginPopup("##bestInSlotGearImport", ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextUnformatted(OmniLoc.Get("Feature.BestInSlotGear.Share.ImportPrompt"));
        OmniControls.InputTextMultiline(
            "##bestInSlotGearImportText",
            ref importShareText,
            4096,
            OmniTheme.Scale(new Vector2(600f, 96f)));
        if (!string.IsNullOrEmpty(importShareError))
        {
            ImGui.TextWrapped(importShareError);
        }

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.BestInSlotGear.Import"), false))
        {
            if (TryImportShare(importShareText, out importShareError))
            {
                OmniNotifier.Popup(
                    Info.Title,
                    OmniLoc.Get("Feature.BestInSlotGear.Share.Imported"),
                    NotificationType.Success);
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Common.Cancel"), false))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void SaveCurrentResult()
    {
        if (result is not { Error: null } current)
        {
            return;
        }

        var savedIndex = loadedSavedResultID is null
            ? config.SavedResults.FindIndex(saved =>
                saved.JobID == current.JobID &&
                saved.ItemLevelLimit == current.ItemLevelLimit &&
                saved.GearStage == (int)gearStage &&
                saved.OptimizationTarget == (int)optimizationTarget &&
                MathF.Abs(saved.TargetGcd - (optimizationTarget == OptimizationTarget.SpecificGcd ? targetGcd : 0f)) < 0.001f)
            : config.SavedResults.FindIndex(saved => saved.ID == loadedSavedResultID);
        var savedResult = new BestInSlotGearConfig.SavedResult
        {
            ID = savedIndex >= 0 ? config.SavedResults[savedIndex].ID : Guid.NewGuid().ToString("N"),
            JobID = current.JobID,
            ItemLevelLimit = current.ItemLevelLimit,
            GearStage = (int)gearStage,
            OptimizationTarget = (int)optimizationTarget,
            TargetGcd = optimizationTarget == OptimizationTarget.SpecificGcd ? targetGcd : 0f,
            Calculation = SaveCalculation(current)
        };
        if (savedIndex >= 0)
        {
            config.SavedResults.RemoveAt(savedIndex);
        }

        config.SavedResults.Add(savedResult);
        loadedSavedResultID = savedResult.ID;
        saveConfig();
    }

    private void LoadSavedResult(BestInSlotGearConfig.SavedResult saved)
    {
        CancelCalculation();
        selectedJobID = saved.JobID;
        targetItemLevel = Math.Clamp(saved.ItemLevelLimit, MinimumSyncItemLevel, 999);
        selectedDutyID = 0;
        gearStage = Enum.IsDefined(typeof(GearStage), saved.GearStage)
            ? (GearStage)saved.GearStage
            : GearStage.Graduation;
        optimizationTarget = Enum.IsDefined(typeof(OptimizationTarget), saved.OptimizationTarget)
            ? (OptimizationTarget)saved.OptimizationTarget
            : OptimizationTarget.MaxDamage;
        if (optimizationTarget == OptimizationTarget.SpecificGcd)
        {
            targetGcd = Math.Clamp(saved.TargetGcd, 1.5f, 3f);
        }
        result = RestoreCalculation(saved.Calculation);
        loadedSavedResultID = saved.ID;
    }

    private static string GetSavedResultLabel(BestInSlotGearConfig.SavedResult saved)
    {
        var savedGearStage = Enum.IsDefined(typeof(GearStage), saved.GearStage)
            ? (GearStage)saved.GearStage
            : GearStage.Graduation;
        var target = Enum.IsDefined(typeof(OptimizationTarget), saved.OptimizationTarget)
            ? (OptimizationTarget)saved.OptimizationTarget
            : OptimizationTarget.MaxDamage;
        return string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get(target switch
            {
                OptimizationTarget.SpecificGcd => "Feature.BestInSlotGear.SavedResult.SpecificGcd",
                _ => "Feature.BestInSlotGear.SavedResult.Default"
            }),
            saved.Calculation.JobName,
            GetItemLevelText((uint)saved.ItemLevelLimit),
            GetGearStageName(savedGearStage),
            GetOptimizationTargetName(target),
            saved.TargetGcd);
    }

    private static BestInSlotGearConfig.SavedCalculation SaveCalculation(CalculationResult current) => new()
    {
        JobID = current.JobID,
        JobName = current.JobName,
        Level = current.Level,
        ItemLevelLimit = current.ItemLevelLimit,
        Items = current.Items.Select(static item => new BestInSlotGearConfig.SavedGearPiece
        {
            ItemID = item.ItemID,
            Slot = (int)item.Slot,
            Name = item.Name,
            IconID = item.IconID,
            ItemLevel = item.ItemLevel,
            IsHighQuality = item.IsHighQuality,
            IsUnique = item.IsUnique,
            OccupiesOffHand = item.OccupiesOffHand,
            Materia = item.Materia.Select(static materia => new BestInSlotGearConfig.SavedMateria
            {
                Name = materia.Name,
                IconID = materia.IconID,
                Count = materia.Count
            }).ToList(),
            Melds = item.Melds.Select(static meld => meld is null
                ? null
                : new BestInSlotGearConfig.SavedMeld
                {
                    ParameterID = meld.Value.ParameterID,
                    Grade = meld.Value.Grade
                }).ToList(),
            CustomStats = item.CustomStats?.Select(static stat => new BestInSlotGearConfig.SavedCustomStat
            {
                ParameterID = stat.ParameterID,
                Value = stat.Value
            }).ToList(),
            StatText = item.StatText
        }).ToList(),
        Stats = new()
        {
            MainStat = current.Stats.MainStat,
            Vitality = current.Stats.Vitality,
            CriticalHit = current.Stats.CriticalHit,
            Determination = current.Stats.Determination,
            DirectHit = current.Stats.DirectHit,
            Speed = current.Stats.Speed,
            Piety = current.Stats.Piety,
            Tenacity = current.Stats.Tenacity,
            WeaponDamage = current.Stats.WeaponDamage
        },
        Damage = current.Damage,
        Gcd = current.Gcd,
        Food = current.Food is null
            ? null
            : new()
            {
                ItemID = current.Food.ItemID,
                Name = current.Food.Name,
                IconID = current.Food.IconID,
                ItemLevel = current.Food.ItemLevel,
                IsHighQuality = current.Food.IsHighQuality,
                EffectText = current.Food.EffectText
            },
        JobResults = current.JobResults.Select(SaveCalculation).ToList()
    };

    private static CalculationResult RestoreCalculation(BestInSlotGearConfig.SavedCalculation saved) => new(
        null,
        saved.JobID,
        saved.JobName,
        saved.Level,
        saved.ItemLevelLimit,
        saved.Items.Select(static item => new GearPiece(
            item.ItemID,
            Enum.IsDefined(typeof(GearSlot), item.Slot) ? (GearSlot)item.Slot : GearSlot.MainHand,
            item.Name,
            item.IconID,
            item.ItemLevel,
            item.IsHighQuality,
            item.IsUnique,
            item.OccupiesOffHand,
            item.Materia.Select(static materia => new MateriaDisplay(materia.Name, materia.IconID, materia.Count)).ToList(),
            item.Melds.Select(static meld => meld is null
                ? (MateriaSelection?)null
                : new MateriaSelection(meld.ParameterID, meld.Grade)).ToList(),
            item.CustomStats?.Select(static stat => new CustomStatValue(stat.ParameterID, stat.Value)).ToList(),
            item.StatText)).ToList(),
        new(
            saved.Stats.MainStat,
            saved.Stats.Vitality,
            saved.Stats.CriticalHit,
            saved.Stats.Determination,
            saved.Stats.DirectHit,
            saved.Stats.Speed,
            saved.Stats.Piety,
            saved.Stats.Tenacity,
            saved.Stats.WeaponDamage),
        saved.Damage,
        saved.Gcd,
        saved.Food is null
            ? null
            : new(
                saved.Food.ItemID,
                saved.Food.Name,
                saved.Food.IconID,
                saved.Food.ItemLevel,
                saved.Food.IsHighQuality,
                saved.Food.EffectText),
        saved.JobResults.Select(RestoreCalculation).ToList());

    private void InitializeDefaults()
    {
        if (initialized)
        {
            return;
        }

        if (JobProfiles.ContainsKey(LocalPlayerState.ClassJob))
        {
            selectedJobID = LocalPlayerState.ClassJob;
        }

        targetItemLevel = GetDefaultItemLevel(selectedJobID);
        initialized = true;
    }

    private bool DrawJobCombo(float width)
    {
        if (!OmniControls.BeginCombo("##bestInSlotGearJob", GetJobPreview(selectedJobID), width))
        {
            return false;
        }

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetScrollY(0f);
        }

        var changed = false;
        foreach (var job in jobs)
        {
            var selected = job.RowId == selectedJobID;
            if (ImGui.Selectable(GetJobPreview(job), selected))
            {
                selectedJobID = job.RowId;
                changed = true;
            }
        }

        ImGui.Separator();
        foreach (var presetID in PresetIDs)
        {
            var selected = selectedJobID == presetID;
            if (ImGui.Selectable(GetJobPreview(presetID), selected))
            {
                selectedJobID = presetID;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static string GetJobPreview(uint jobID) => jobID switch
    {
        TankPresetID => OmniLoc.Get("Feature.BestInSlotGear.Preset.Tank"),
        HealerPresetID => OmniLoc.Get("Feature.BestInSlotGear.Preset.Healer"),
        _ => LuminaGetter.TryGetRow<ClassJob>(jobID, out var job) ? GetJobPreview(job) : string.Empty
    };

    private static string GetJobPreview(ClassJob job) => job.Name.ExtractText();

    private bool DrawDutyPresetCombo(float width)
    {
        if (!OmniControls.BeginCombo(
                "##bestInSlotGearDutyPreset",
                GetDutyPresetPreview(selectedDutyID),
                width))
        {
            return false;
        }

        var changed = false;
        if (ImGui.Selectable(
                OmniLoc.Get("Feature.BestInSlotGear.DutyPreset.Custom"),
                selectedDutyID == 0))
        {
            selectedDutyID = 0;
            changed = true;
        }

        ImGui.Separator();
        foreach (var dutyID in UltimateDutyIDs)
        {
            if (!LuminaGetter.TryGetRow<ContentFinderCondition>(dutyID, out var duty))
            {
                continue;
            }

            var selected = selectedDutyID == dutyID;
            if (ImGui.Selectable(GetDutyPresetPreview(duty), selected))
            {
                selectedDutyID = dutyID;
                targetItemLevel = GetDutyPresetItemLevel(duty);
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private string GetDutyPresetPreview(uint dutyID) =>
        dutyID != 0 && LuminaGetter.TryGetRow<ContentFinderCondition>(dutyID, out var duty)
            ? GetDutyPresetPreview(duty)
            : OmniLoc.Get("Feature.BestInSlotGear.DutyPreset.Custom");

    private string GetDutyPresetPreview(ContentFinderCondition duty) => string.Format(
        CultureInfo.CurrentCulture,
        OmniLoc.Get("Feature.BestInSlotGear.DutyPreset.Value"),
        duty.Name.ExtractText(),
        GetItemLevelText((uint)GetDutyPresetItemLevel(duty)));

    private int GetDutyPresetItemLevel(ContentFinderCondition duty) => duty.ItemLevelSync > 0
        ? duty.ItemLevelSync
        : GetDefaultItemLevel(selectedJobID);

    private static string GetGearStageName(GearStage stage) => OmniLoc.Get(stage == GearStage.Opening
        ? "Feature.BestInSlotGear.GearStage.Opening"
        : "Feature.BestInSlotGear.GearStage.Graduation");

    private bool DrawGearStageCombo(float width)
    {
        if (!OmniControls.BeginCombo(
                "##bestInSlotGearGearStage",
                GetGearStageName(gearStage),
                width))
        {
            return false;
        }

        var changed = false;
        foreach (var stage in Enum.GetValues<GearStage>())
        {
            var selected = gearStage == stage;
            if (ImGui.Selectable(GetGearStageName(stage), selected))
            {
                gearStage = stage;
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static string GetOptimizationTargetName(OptimizationTarget target) => OmniLoc.Get(target switch
    {
        OptimizationTarget.SpecificGcd => "Feature.BestInSlotGear.Target.SpecificGcd",
        _ => "Feature.BestInSlotGear.Target.MaxDamage"
    });

    private bool DrawOptimizationTargetCombo(float width)
    {
        if (!OmniControls.BeginCombo(
                "##bestInSlotGearOptimizationTarget",
                GetOptimizationTargetName(optimizationTarget),
                width))
        {
            return false;
        }

        var changed = false;
        foreach (var target in Enum.GetValues<OptimizationTarget>())
        {
            var selected = optimizationTarget == target;
            if (ImGui.Selectable(GetOptimizationTargetName(target), selected))
            {
                optimizationTarget = target;
                changed = true;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static void ContinueControlRow(float requiredWidth, float spacing)
    {
        if (ImGui.GetContentRegionAvail().X >= requiredWidth + spacing)
        {
            ImGui.SameLine(0f, spacing);
        }
    }

    private void DrawSummary(CalculationResult current)
    {
        using var table = ImRaii.Table(
            "##bestInSlotGearSummary",
            13,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PreciseWidths |
            ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##job", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("##itemLevel", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##damage", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("##gcd", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##main", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##weapon", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##critical", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##determination", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##directHit", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##speed", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##piety", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##tenacity", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##vitality", ImGuiTableColumnFlags.WidthStretch, 1f);
        var rowContentHeight = MathF.Max(
            ImGui.GetTextLineHeight() * 2f + ImGui.GetStyle().ItemSpacing.Y,
            OmniTheme.TableItemIconSize() * 1.35f);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        DrawJobSummaryCell(
            current.JobID,
            OmniLoc.Get("Feature.BestInSlotGear.Job"),
            $"{current.JobName} Lv.{current.Level}",
            rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.ItemLevelLimit"), GetItemLevelText((uint)current.ItemLevelLimit), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.ExpectedDamage"), current.Damage.ToString("0.00000", CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Gcd"), $"{current.Gcd:0.00}s", rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.MainStat"), current.Stats.MainStat.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.WeaponDamage"), current.Stats.WeaponDamage.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.CriticalHit"), current.Stats.CriticalHit.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Determination"), current.Stats.Determination.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.DirectHit"), current.Stats.DirectHit.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Speed"), current.Stats.Speed.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Piety"), current.Stats.Piety.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Tenacity"), current.Stats.Tenacity.ToString(CultureInfo.InvariantCulture), rowContentHeight);
        DrawSummaryCell(OmniLoc.Get("Feature.BestInSlotGear.Vitality"), current.Stats.Vitality.ToString(CultureInfo.InvariantCulture), rowContentHeight);
    }

    private static void DrawJobSummaryCell(uint jobID, string label, string value, float rowContentHeight)
    {
        ImGui.TableNextColumn();
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, rowContentHeight);
        var iconSize = rowContentHeight;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var iconColumnWidth = MathF.Min(OmniTheme.Scale(72f), size.X);
        var textWidth = MathF.Min(
            MathF.Max(ImGui.CalcTextSize(value).X, ImGui.CalcTextSize(label).X),
            MathF.Max(1f, size.X - iconColumnWidth - spacing));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(position, position + size, true);
        FramedGameIcon.Draw(
            62000u + jobID,
            position + new Vector2(MathF.Max(0f, (iconColumnWidth - iconSize) * 0.5f), 0f),
            new(iconSize),
            drawFrame: false);
        var lineSize = new Vector2(textWidth, ImGui.GetTextLineHeight());
        var textPosition = position + new Vector2(
            iconColumnWidth + spacing,
            MathF.Max(0f, (rowContentHeight - lineSize.Y * 2f - ImGui.GetStyle().ItemSpacing.Y) * 0.5f));
        OmniControls.DrawTextCentered(
            value,
            textPosition,
            lineSize,
            OmniTheme.Tokens.Text);
        OmniControls.DrawTextCentered(
            label,
            textPosition + new Vector2(0f, lineSize.Y + ImGui.GetStyle().ItemSpacing.Y),
            lineSize,
            ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        drawList.PopClipRect();
        ImGui.Dummy(size);
    }

    private static void DrawSummaryCell(string label, string value, float rowContentHeight)
    {
        ImGui.TableNextColumn();
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight());
        var textPosition = position + new Vector2(
            0f,
            MathF.Max(0f, (rowContentHeight - size.Y * 2f - ImGui.GetStyle().ItemSpacing.Y) * 0.5f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(
            position,
            position + new Vector2(size.X, rowContentHeight),
            true);
        OmniControls.DrawTextCentered(value, textPosition, size, OmniTheme.Tokens.Text);
        OmniControls.DrawTextCentered(
            label,
            textPosition + new Vector2(0f, size.Y + ImGui.GetStyle().ItemSpacing.Y),
            size,
            ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        drawList.PopClipRect();
        ImGui.Dummy(new Vector2(size.X, rowContentHeight));
    }

    private void DrawEquipmentComparison(
        IReadOnlyList<GearPiece> items,
        FoodResult? food,
        EquipmentDisplay display,
        bool showCurrentEquipment,
        uint tableID)
    {
        using var id = ImRaii.PushId((int)tableID);
        ImGui.Spacing();
        ImGui.Separator();
        var tableWidth = ImGui.GetContentRegionAvail().X;
        var slotColumnWidth = OmniTheme.Scale(72f);
        var availableWidth = MathF.Max(1f, tableWidth - slotColumnWidth - ImGui.GetStyle().CellPadding.X * 8f);
        var comparisonWidth = availableWidth * 3.5f / 7.2f;
        var materiaWidth = availableWidth * 1.4f / 7.2f;
        var statsWidth = availableWidth * 2.3f / 7.2f;
        using var table = ImRaii.Table(
            "##bestInSlotGearEquipment",
            4,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(tableWidth, 0f));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BestInSlotGear.Column.Slot"), ImGuiTableColumnFlags.WidthFixed, slotColumnWidth);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BestInSlotGear.Column.Comparison"), ImGuiTableColumnFlags.WidthStretch, 3.5f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BestInSlotGear.Column.Materia"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.BestInSlotGear.Column.Stats"), ImGuiTableColumnFlags.WidthStretch, 2.3f);
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.BestInSlotGear.Column.Slot"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.BestInSlotGear.Column.Comparison"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.BestInSlotGear.Column.Materia"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.BestInSlotGear.Column.Stats"));

        if (display != EquipmentDisplay.Shared)
        {
            DrawFoodRow(
                OmniLoc.Get("Feature.BestInSlotGear.Slot.Food"),
                food is null ? null : new(food.Name, food.IconID, food.ItemLevel, food.IsHighQuality),
                OmniLoc.Get("Feature.BestInSlotGear.NoFood"),
                OmniLoc.Get("Feature.BestInSlotGear.NotApplicable"),
                food?.EffectText ?? OmniLoc.Get("Feature.BestInSlotGear.NoFood"),
                comparisonWidth,
                materiaWidth,
                statsWidth);
        }

        var inventoryManager = InventoryManager.Instance();
        var equippedItems = inventoryManager == null
            ? null
            : inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
        foreach (var comparison in ComparisonSlots)
        {
            if (display == EquipmentDisplay.Shared && comparison.Slot is GearSlot.MainHand or GearSlot.OffHand ||
                display == EquipmentDisplay.Weapons && comparison.Slot is not (GearSlot.MainHand or GearSlot.OffHand))
            {
                continue;
            }

            if (comparison.Slot == GearSlot.OffHand && !items.Any(static piece => piece.Slot == GearSlot.OffHand))
            {
                continue;
            }

            var item = FindRecommendedItem(items, comparison.Slot, comparison.RingIndex);
            DrawComparisonRow(
                GetSlotName(comparison.Slot, comparison.RingIndex),
                showCurrentEquipment && TryGetEquippedPiece(equippedItems, comparison.InventorySlot, out var equipped)
                    ? equipped
                    : null,
                showCurrentEquipment
                    ? OmniLoc.Get("Feature.BestInSlotGear.NotEquipped")
                    : OmniLoc.Get("Feature.BestInSlotGear.NotApplicable"),
                item is null ? null : new(item.Name, item.IconID, item.ItemLevel, item.IsHighQuality),
                OmniLoc.Get("Feature.BestInSlotGear.NoRecommendedItem"),
                item?.Materia ?? [],
                item?.StatText ?? OmniLoc.Get("Feature.BestInSlotGear.NotApplicable"),
                comparisonWidth,
                materiaWidth,
                statsWidth);
        }
    }

    private void DrawFoodRow(
        string slot,
        ItemDisplay? food,
        string fallback,
        string materia,
        string stats,
        float comparisonWidth,
        float materiaWidth,
        float statsWidth)
    {
        var rowContentHeight = MathF.Max(
            GetItemHeight(food, fallback, comparisonWidth),
            MathF.Max(GetWrappedTextHeight(materia, materiaWidth), GetWrappedTextHeight(stats, statsWidth)));
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableNextColumn();
        OmniControls.TableTextCentered(slot, rowContentHeight);
        ImGui.TableNextColumn();
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        DrawItemBlock(food, fallback, position, width, rowContentHeight, true);
        ImGui.SetCursorScreenPos(position);
        ImGui.Dummy(new Vector2(width, rowContentHeight));
        ImGui.TableNextColumn();
        DrawWrappedTextCentered(materia, rowContentHeight);
        ImGui.TableNextColumn();
        DrawWrappedTextCentered(stats, rowContentHeight);
    }

    private void DrawComparisonRow(
        string slot,
        ItemDisplay? current,
        string currentFallback,
        ItemDisplay? recommended,
        string recommendedFallback,
        IReadOnlyList<MateriaDisplay> materia,
        string stats,
        float comparisonWidth,
        float materiaWidth,
        float statsWidth)
    {
        var materiaHeight = GetMateriaHeight(materia, materiaWidth);
        var rowContentHeight = MathF.Max(
            GetReplacementHeight(current, currentFallback, recommended, recommendedFallback, comparisonWidth),
            MathF.Max(materiaHeight, GetWrappedTextHeight(stats, statsWidth)));
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableNextColumn();
        OmniControls.TableTextCentered(slot, rowContentHeight);
        ImGui.TableNextColumn();
        DrawReplacement(current, currentFallback, recommended, recommendedFallback, rowContentHeight);
        ImGui.TableNextColumn();
        DrawMateriaCell(materia, materiaHeight, rowContentHeight);
        ImGui.TableNextColumn();
        DrawWrappedTextCentered(stats, rowContentHeight);
    }

    private static float GetReplacementHeight(
        ItemDisplay? current,
        string currentFallback,
        ItemDisplay? recommended,
        string recommendedFallback,
        float width)
    {
        var blockWidth = MathF.Max(
            1f,
            (width - ImGui.CalcTextSize(OmniLoc.Get("Feature.BestInSlotGear.Arrow")).X -
             ImGui.GetStyle().ItemInnerSpacing.X * 2f) * 0.5f);
        return MathF.Max(
            GetItemHeight(current, currentFallback, blockWidth),
            GetItemHeight(recommended, recommendedFallback, blockWidth));
    }

    private static float GetItemHeight(ItemDisplay? item, string fallback, float width)
    {
        if (item is null)
        {
            return GetWrappedTextHeight(fallback, width);
        }

        return MathF.Max(
            OmniTheme.TableItemIconSize(),
            GetItemTextSize(
                GetItemText(item.Value),
                MathF.Max(1f, width - OmniTheme.TableItemIconSize() - ImGui.GetStyle().ItemInnerSpacing.X)).Y);
    }

    private static float GetWrappedTextHeight(string text, float width) =>
        ImGui.CalcTextSize(text, false, MathF.Max(1f, width)).Y;

    private static void DrawReplacement(
        ItemDisplay? current,
        string currentFallback,
        ItemDisplay? recommended,
        string recommendedFallback,
        float rowContentHeight)
    {
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var arrow = OmniLoc.Get("Feature.BestInSlotGear.Arrow");
        var arrowWidth = ImGui.CalcTextSize(arrow).X;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var centerX = width * 0.5f;
        var blockWidth = MathF.Max(1f, centerX - arrowWidth * 0.5f - spacing);
        DrawItemBlock(current, currentFallback, position, blockWidth, rowContentHeight, false);
        OmniControls.DrawTextCentered(
            arrow,
            position + new Vector2(centerX - arrowWidth * 0.5f, 0f),
            new Vector2(arrowWidth, rowContentHeight),
            OmniTheme.Tokens.Text);
        DrawItemBlock(
            recommended,
            recommendedFallback,
            position + new Vector2(centerX + arrowWidth * 0.5f + spacing, 0f),
            blockWidth,
            rowContentHeight,
            false);
        ImGui.SetCursorScreenPos(position);
        ImGui.Dummy(new Vector2(width, rowContentHeight));
    }

    private static void DrawItemBlock(
        ItemDisplay? item,
        string fallback,
        Vector2 position,
        float width,
        float rowContentHeight,
        bool centered)
    {
        if (item is null)
        {
            var fallbackTextSize = ImGui.CalcTextSize(fallback, false, width);
            var offsetX = centered ? MathF.Max(0f, (width - fallbackTextSize.X) * 0.5f) : 0f;
            ImGui.SetCursorScreenPos(position + new Vector2(
                offsetX,
                MathF.Max(0f, (rowContentHeight - fallbackTextSize.Y) * 0.5f)));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - offsetX);
            ImGui.TextWrapped(fallback);
            ImGui.PopTextWrapPos();
            return;
        }

        var iconSize = OmniTheme.TableItemIconSize();
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var text = GetItemText(item.Value);
        var textWidth = MathF.Max(1f, width - iconSize - spacing);
        var textSize = GetItemTextSize(text, textWidth);
        var contentWidth = iconSize + spacing + MathF.Min(textWidth, textSize.X);
        var contentHeight = MathF.Max(iconSize, textSize.Y);
        var contentPosition = position + new Vector2(
            centered ? MathF.Max(0f, (width - contentWidth) * 0.5f) : 0f,
            MathF.Max(0f, (rowContentHeight - contentHeight) * 0.5f));
        FramedGameIcon.Draw(item.Value.IconID, contentPosition, new(iconSize), itemHQ: item.Value.IsHighQuality);
        var textPosition = contentPosition + new Vector2(
            iconSize + spacing,
            MathF.Max(0f, (contentHeight - textSize.Y) * 0.5f));
        var drawParams = new SeStringDrawParams
        {
            TargetDrawList = ImGui.GetWindowDrawList(),
            ScreenOffset = textPosition,
            Font = ImGui.GetFont(),
            FontSize = ImGui.GetFontSize(),
            WrapWidth = textWidth,
            Color = OmniTheme.Color(OmniTheme.Tokens.Text)
        };
        ImGuiHelpers.CompileSeStringWrapped(text, drawParams);
    }

    private static string GetItemText(ItemDisplay item) => $"{item.Name}  {GetItemLevelText(item.ItemLevel)}";

    private static string GetItemLevelText(uint itemLevel) =>
        $"{(char)SeIconChar.ItemLevel} {itemLevel.ToString(CultureInfo.InvariantCulture)}";

    private static Vector2 GetItemTextSize(string text, float width)
    {
        var drawParams = new SeStringDrawParams
        {
            TargetDrawList = default(ImDrawListPtr),
            ScreenOffset = Vector2.Zero,
            Font = ImGui.GetFont(),
            FontSize = ImGui.GetFontSize(),
            WrapWidth = MathF.Max(1f, width)
        };
        return ImGuiHelpers.CompileSeStringWrapped(text, drawParams).Size;
    }

    private static float GetMateriaHeight(IReadOnlyList<MateriaDisplay> materia, float width)
    {
        if (materia.Count == 0)
        {
            return GetWrappedTextHeight(OmniLoc.Get("Feature.BestInSlotGear.NoMateria"), width);
        }

        var iconSize = OmniTheme.TableItemIconSize() * 0.55f;
        var textWidth = MathF.Max(1f, width - iconSize - ImGui.GetStyle().ItemInnerSpacing.X);
        var height = 0f;
        for (var index = 0; index < materia.Count; index++)
        {
            height += MathF.Max(
                iconSize,
                GetWrappedTextHeight($"{materia[index].Name} x{materia[index].Count}", textWidth));
            if (index < materia.Count - 1)
            {
                height += ImGui.GetStyle().ItemInnerSpacing.Y;
            }
        }

        return height;
    }

    private static void DrawMateriaCell(
        IReadOnlyList<MateriaDisplay> materia,
        float materiaHeight,
        float rowContentHeight)
    {
        if (materia.Count == 0)
        {
            DrawWrappedTextCentered(OmniLoc.Get("Feature.BestInSlotGear.NoMateria"), rowContentHeight);
            return;
        }

        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var iconSize = OmniTheme.TableItemIconSize() * 0.55f;
        var spacing = ImGui.GetStyle().ItemInnerSpacing;
        var textWidth = MathF.Max(1f, width - iconSize - spacing.X);
        var offsetY = MathF.Max(0f, (rowContentHeight - materiaHeight) * 0.5f);
        foreach (var current in materia)
        {
            var text = $"{current.Name} x{current.Count}";
            var textSize = ImGui.CalcTextSize(text, false, textWidth);
            var contentWidth = iconSize + spacing.X + MathF.Min(textWidth, textSize.X);
            var offsetX = MathF.Max(0f, (width - contentWidth) * 0.5f);
            var textHeight = textSize.Y;
            var contentHeight = MathF.Max(iconSize, textHeight);
            FramedGameIcon.Draw(
                current.IconID,
                position + new Vector2(offsetX, offsetY + MathF.Max(0f, (contentHeight - iconSize) * 0.5f)),
                new(iconSize),
                drawFrame: false);
            ImGui.SetCursorScreenPos(position + new Vector2(
                offsetX + iconSize + spacing.X,
                offsetY + MathF.Max(0f, (contentHeight - textHeight) * 0.5f)));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
            offsetY += contentHeight + spacing.Y;
        }

        ImGui.SetCursorScreenPos(position);
        ImGui.Dummy(new Vector2(width, rowContentHeight));
    }

    private static void DrawWrappedTextCentered(string text, float rowContentHeight)
    {
        var textHeight = GetWrappedTextHeight(text, ImGui.GetContentRegionAvail().X);
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPosY(cursor.Y + MathF.Max(0f, (rowContentHeight - textHeight) * 0.5f));
        OmniControls.TableTextWrappedCentered(text);
    }

    private static GearPiece? FindRecommendedItem(IReadOnlyList<GearPiece> items, GearSlot slot, int ringIndex)
    {
        var currentRing = 0;
        foreach (var item in items)
        {
            if (item.Slot != slot)
            {
                continue;
            }

            if (slot != GearSlot.Ring || currentRing++ == ringIndex)
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryGetEquippedPiece(
        InventoryContainer* equippedItems,
        int inventorySlot,
        out ItemDisplay piece)
    {
        if (equippedItems != null && inventorySlot < equippedItems->Size)
        {
            var inventoryItem = equippedItems->GetInventorySlot(inventorySlot);
            if (inventoryItem != null &&
                inventoryItem->ItemId != 0 &&
                LuminaGetter.TryGetRow<Item>(inventoryItem->GetBaseItemId(), out var item))
            {
                piece = new(
                    item.Name.ExtractText(),
                    item.Icon,
                    item.LevelItem.RowId,
                    inventoryItem->IsHighQuality());
                return true;
            }
        }

        piece = default;
        return false;
    }

}
