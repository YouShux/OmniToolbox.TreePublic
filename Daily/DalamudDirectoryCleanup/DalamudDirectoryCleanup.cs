using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class DalamudDirectoryCleanup : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DalamudDirectoryCleanupTitle"),
        Description = OmniLoc.Get("DalamudDirectoryCleanupDescription"),
        Category = ModuleCategory.Daily
    };

    private Task<ScanResult>? scanTask;
    private Task<CleanupResult>? cleanupTask;
    private CancellationTokenSource? cancellation;
    private ScanResult? preview;
    private string status = string.Empty;
    private List<string> errors = [];

    public override bool HasSettings => true;

    protected override void OnDisable() => cancellation?.Cancel();

    protected override void OnDispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    public override bool DrawSettings()
    {
        CollectResults();
        var busy = scanTask is not null || cleanupTask is not null;
        using (ImRaii.Disabled(busy))
        {
            if (OmniControls.SmallButton(Text("Preview"), false))
                BeginScan();
            OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(Text("Delete")).X);
            using (ImRaii.Disabled(preview is null || GetSelection(preview).Count == 0))
                if (OmniControls.SmallButton(Text("Delete"), false))
                    ImGui.OpenPopup("##cleanupConfirmation");
        }

        if (busy)
        {
            ImGui.SameLine();
            if (OmniControls.SmallButton(Text("Cancel"), false))
                cancellation?.Cancel();
        }

        if (status.Length > 0)
            ImGui.TextWrapped(status);
        foreach (var error in errors.Take(5))
            ImGui.TextWrapped(error);

        using (ImRaii.Disabled(busy))
        {
            if (preview is not null)
                DrawPreview(preview);
            DrawConfirmation();
        }

        return false;
    }

    private unsafe void DrawPreview(ScanResult result)
    {
        var selected = GetSelection(result);
        ImGui.TextWrapped(string.Format(Text("Summary"), result.Entries.Count, selected.Count,
            FormatSize(selected.Bytes)));
        if (result.Entries.Count == 0)
            return;

        var height = Math.Min(result.Entries.Count + 1, 10) * ImGui.GetFrameHeightWithSpacing();
        using var table = ImRaii.Table("##cleanupPreview", 5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings,
            new Vector2(0, height));
        if (!table)
            return;
        ImGui.TableSetupColumn("##selection", ImGuiTableColumnFlags.WidthFixed, OmniTheme.CheckboxSize());
        ImGui.TableSetupColumn(Text("Category"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(85f));
        ImGui.TableSetupColumn(Text("Path"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Text("Size"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(90f));
        ImGui.TableSetupColumn(Text("Modified"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(100f));
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(result.Entries.Count);
            while (clipper.Step())
            {
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                {
                    var entry = result.Entries[index];
                    using var id = ImRaii.PushId(index);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    OmniControls.Checkbox("##selected", ref entry.Selected);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Text(entry.Category.ToString()));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Path.GetRelativePath(result.Root, entry.Path));
                    if (ImGui.IsItemHovered())
                    {
                        using var tooltip = ImRaii.Tooltip();
                        ImGui.TextUnformatted(entry.Path);
                    }
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatSize(entry.Length));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.LastWriteUTC.ToLocalTime().ToString("yyyy-MM-dd"));
                }
            }
        }
        finally
        {
            clipper.End();
            clipper.Destroy();
        }
    }

    private void DrawConfirmation()
    {
        using var popup = ImRaii.Popup("##cleanupConfirmation", ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
            return;
        if (preview is null)
        {
            ImGui.CloseCurrentPopup();
            return;
        }

        var selected = GetSelection(preview);
        ImGui.TextWrapped(string.Format(Text("Confirm"), selected.Count, FormatSize(selected.Bytes)));
        using (ImRaii.Disabled(selected.Count == 0))
        {
            if (OmniControls.SmallButton(Text("Delete"), false))
            {
                var snapshot = preview;
                var entries = snapshot.Entries.Where(static entry => entry.Selected).ToArray();
                var token = CreateOperation();
                cleanupTask = Task.Run(() => DeleteSelected(snapshot, entries, token));
                status = Text("Deleting");
                preview = null;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (OmniControls.SmallButton(Text("Cancel"), false))
            ImGui.CloseCurrentPopup();
    }

    private void BeginScan()
    {
        var pluginConfigs = new DirectoryInfo(DalamudServices.PluginInterface.GetPluginConfigDirectory()).Parent;
        var root = pluginConfigs?.Parent?.FullName;
        if (root is null || pluginConfigs?.Name != "pluginConfigs" ||
            !Directory.Exists(Path.Combine(root, "installedPlugins")))
        {
            status = Text("DirectoryMissing");
            return;
        }

        var token = CreateOperation();
        preview = null;
        status = Text("Scanning");
        scanTask = Task.Run(() => Scan(root, DateTime.UtcNow, token), token);
    }

    private CancellationToken CreateOperation()
    {
        cancellation?.Dispose();
        cancellation = new();
        errors = [];
        return cancellation.Token;
    }

    private void CollectResults()
    {
        try
        {
            if (scanTask is { IsCompleted: true } completedScan)
            {
                scanTask = null;
                preview = completedScan.GetAwaiter().GetResult();
                errors = preview.Errors;
                status = errors.Count == 0 ? string.Empty : string.Format(Text("ScanErrors"), errors.Count);
            }
            if (cleanupTask is { IsCompleted: true } completedCleanup)
            {
                cleanupTask = null;
                var result = completedCleanup.GetAwaiter().GetResult();
                errors = result.Errors;
                status = string.Format(Text("Result"), result.Deleted, FormatSize(result.Bytes), result.Skipped,
                    result.Errors.Count);
                if (result.Cancelled)
                    status += " " + Text("Cancelled");
            }
        }
        catch (OperationCanceledException)
        {
            status = Text("Cancelled");
        }
        catch (Exception exception)
        {
            status = Text("Failed");
            errors = [exception.Message];
        }
    }

    private static string Text(string key) => OmniLoc.Get($"Feature.DalamudDirectoryCleanup.{key}");

    private static (int Count, long Bytes) GetSelection(ScanResult result)
    {
        var count = 0;
        var bytes = 0L;
        foreach (var entry in result.Entries)
        {
            if (!entry.Selected)
                continue;
            count++;
            bytes += entry.Length;
        }
        return (count, bytes);
    }

    private static string FormatSize(long bytes)
    {
        ReadOnlySpan<string> units = ["B", "KiB", "MiB", "GiB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private enum Category
    {
        Logs,
        Backups
    }

    private sealed class Candidate(string path, long length, DateTime lastWriteUTC, DateTime creationUTC, Category category)
    {
        public string Path { get; } = path;
        public long Length { get; } = length;
        public DateTime LastWriteUTC { get; } = lastWriteUTC;
        public DateTime CreationUTC { get; } = creationUTC;
        public Category Category { get; } = category;
        public bool Selected = true;
    }

    private sealed record ScanResult(string Root, List<Candidate> Entries, List<string> Errors);

    private sealed class CleanupResult
    {
        public int Deleted;
        public long Bytes;
        public int Skipped;
        public bool Cancelled;
        public List<string> Errors { get; } = [];
    }
}
