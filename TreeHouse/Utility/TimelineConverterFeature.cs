using Dalamud.Interface;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.Utility;

public sealed class TimelineConverterFeature(TreeHouseConfig config) : TreeHouseFeatureBase(new(
    "timeline-converter",
    "Feature.TimelineConverter.Title",
    "Feature.TimelineConverter.Description",
    TreeHouseCategory.Utility))
{
    private TimelineConversionResult? lastResult;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var save = false;
        ImGui.TextUnformatted(OmniLoc.Get("Feature.TimelineConverter.Path"));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var path = config.TimelineConverter.DirectoryPath;
        if (ImGui.InputText("##timelineConverterPath", ref path, 512))
        {
            config.TimelineConverter.DirectoryPath = path;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.TimelineConverter.DirectoryPath = path.Trim();
            save = true;
        }

        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.SyncAlt, OmniLoc.Get("Feature.TimelineConverter.Convert")))
        {
            lastResult = TimelineConverter.ConvertDirectory(config.TimelineConverter.DirectoryPath.Trim());
            LogResult(lastResult);
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.TrashAlt, OmniLoc.Get("Feature.TimelineConverter.Cleanup")))
        {
            lastResult = TimelineConverter.CleanupBackups(config.TimelineConverter.DirectoryPath.Trim());
            LogResult(lastResult);
        }

        DrawResult();
        return save;
    }

    private void DrawResult()
    {
        if (lastResult is null)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        if (!lastResult.DirectoryExists)
        {
            ImGui.TextColored(OmniTheme.Tokens.Error, OmniLoc.Get("Feature.TimelineConverter.DirectoryMissing"));
            return;
        }

        ImGui.TextUnformatted(lastResult.IsCleanup
            ? string.Format(OmniLoc.Get("Feature.TimelineConverter.CleanupResult"), lastResult.DeletedBackups)
            : string.Format(OmniLoc.Get("Feature.TimelineConverter.ConvertResult"), lastResult.ConvertedFiles, lastResult.Replacements));
        foreach (var failure in lastResult.Failures)
        {
            ImGui.TextColored(OmniTheme.Tokens.Error, failure);
        }
    }

    private static void LogResult(TimelineConversionResult result)
    {
        if (!result.DirectoryExists)
        {
            DalamudServices.PluginLog.Error("Timeline converter directory does not exist: {Path}", result.DirectoryPath);
            return;
        }

        if (result.IsCleanup)
        {
            DalamudServices.PluginLog.Information(
                "Timeline converter deleted {BackupCount} backup files under {Path}.",
                result.DeletedBackups,
                result.DirectoryPath);
        }
        else
        {
            DalamudServices.PluginLog.Information(
                "Timeline converter changed {FileCount} files and {ReplacementCount} type names under {Path}.",
                result.ConvertedFiles,
                result.Replacements,
                result.DirectoryPath);
        }

        foreach (var failure in result.Failures)
        {
            DalamudServices.PluginLog.Error("Timeline converter file operation failed: {Failure}", failure);
        }
    }
}
