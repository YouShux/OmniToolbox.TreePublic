using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class DalamudDirectoryCleanup : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DalamudDirectoryCleanupTitle",
            ["Description"] = "DalamudDirectoryCleanupDescription",
            ["Feature.DalamudDirectoryCleanup.Backups"] = "Feature.DalamudDirectoryCleanup.Backups",
            ["Feature.DalamudDirectoryCleanup.Cancel"] = "Feature.DalamudDirectoryCleanup.Cancel",
            ["Feature.DalamudDirectoryCleanup.Cancelled"] = "Feature.DalamudDirectoryCleanup.Cancelled",
            ["Feature.DalamudDirectoryCleanup.Category"] = "Feature.DalamudDirectoryCleanup.Category",
            ["Feature.DalamudDirectoryCleanup.Delete"] = "Feature.DalamudDirectoryCleanup.Delete",
            ["Feature.DalamudDirectoryCleanup.Deleting"] = "Feature.DalamudDirectoryCleanup.Deleting",
            ["Feature.DalamudDirectoryCleanup.DirectoryMissing"] = "Feature.DalamudDirectoryCleanup.DirectoryMissing",
            ["Feature.DalamudDirectoryCleanup.Failed"] = "Feature.DalamudDirectoryCleanup.Failed",
            ["Feature.DalamudDirectoryCleanup.Logs"] = "Feature.DalamudDirectoryCleanup.Logs",
            ["Feature.DalamudDirectoryCleanup.Modified"] = "Feature.DalamudDirectoryCleanup.Modified",
            ["Feature.DalamudDirectoryCleanup.Path"] = "Feature.DalamudDirectoryCleanup.Path",
            ["Feature.DalamudDirectoryCleanup.PluginConfigs"] = "Feature.DalamudDirectoryCleanup.PluginConfigs",
            ["Feature.DalamudDirectoryCleanup.Preview"] = "Feature.DalamudDirectoryCleanup.Preview",
            ["Feature.DalamudDirectoryCleanup.Result"] = "Feature.DalamudDirectoryCleanup.Result",
            ["Feature.DalamudDirectoryCleanup.ScanErrors"] = "Feature.DalamudDirectoryCleanup.ScanErrors",
            ["Feature.DalamudDirectoryCleanup.Scanning"] = "Feature.DalamudDirectoryCleanup.Scanning",
            ["Feature.DalamudDirectoryCleanup.Size"] = "Feature.DalamudDirectoryCleanup.Size",
            ["Feature.DalamudDirectoryCleanup.Summary"] = "Feature.DalamudDirectoryCleanup.Summary"
        });

    private Task<ScanResult>?        scanTask;
    private Task<CleanupResult>?     cleanupTask;
    private CancellationTokenSource? cancellation;
    private ScanResult?              preview;
    private string                   status       = string.Empty;
    private List<string>             errors       = [];

    protected override void OnStart(FeatureLifetime activation) => Activation.OnStopping(() =>
    {
        FrameworkManager.Instance().Unreg(CollectResults);
        cancellation?.Cancel();
    });

    protected override void OnStop()
    {
        cancellation?.Dispose();
        cancellation = null;
        scanTask     = null;
        cleanupTask  = null;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        CollectResults();
        var busy = scanTask is not null || cleanupTask is not null;
        using (ImRaii.Disabled(busy))
        {
            if (OmniControls.SmallButton(Text("Preview"), false))
                BeginScan();
            OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(Text("Delete")).X);
            using (ImRaii.Disabled(preview is not null && GetSelection(preview).Count == 0))
                if (OmniControls.SmallButton(Text("Delete"), false))
                    BeginCleanup();
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

        using var cellPadding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding,
            new Vector2(MathF.Max(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(6f)), OmniTheme.Scale(4f)));
        var checkboxSize = OmniControls.MeasureCheckbox(string.Empty);
        var rowContentHeight = MathF.Max(checkboxSize.Y, ImGui.GetFrameHeight());
        var rowHeight = rowContentHeight + ImGui.GetStyle().CellPadding.Y * 2f;
        ReadOnlySpan<string> labels = ["##selection", Text("Category"), Text("Path"), Text("Size"), Text("Modified")];
        var categoryWidth = ImGui.CalcTextSize(labels[1]).X;
        ReadOnlySpan<Category> categories = [Category.Logs, Category.Backups, Category.PluginConfigs];
        foreach (var category in categories)
            categoryWidth = MathF.Max(categoryWidth, ImGui.CalcTextSize(Text(category.ToString())).X);
        var spacing = ImGui.GetStyle().FramePadding.X * 2f;
        categoryWidth += spacing;
        var sizeWidth = MathF.Max(ImGui.CalcTextSize(labels[3]).X, ImGui.CalcTextSize("999.99 GiB").X) + spacing;
        var dateWidth = MathF.Max(ImGui.CalcTextSize(labels[4]).X, ImGui.CalcTextSize("0000-00-00").X) + spacing;
        var minimumPathWidth = ImGui.GetFontSize() * 12f;
        var pathWidth = MathF.Max(minimumPathWidth, ImGui.GetContentRegionAvail().X - checkboxSize.X -
            categoryWidth - sizeWidth - dateWidth - ImGui.GetStyle().CellPadding.X * labels.Length * 2f -
            labels.Length - 1f - ImGui.GetStyle().ScrollbarSize);
        var height = Math.Min(result.Entries.Count + 1, 10) * rowHeight +
                     ImGui.GetStyle().ScrollbarSize + ImGui.GetStyle().ChildBorderSize * 2f;
        using var table = OmniControls.DataTable("##cleanupPreview", labels,
            [checkboxSize.X, categoryWidth, minimumPathWidth, sizeWidth, dateWidth],
            [checkboxSize.X, categoryWidth, pathWidth, sizeWidth, dateWidth], out _,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings,
            new Vector2(0, height), stretchColumn: 2);
        if (!table)
            return;
        ImGui.TableSetupScrollFreeze(1, 1);
        OmniControls.BeginTableHeaderRow(rowContentHeight);
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var selectAll = selected.Count == result.Entries.Count;
        OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
        if (OmniControls.Checkbox("##selectAll", ref selectAll))
        {
            foreach (var entry in result.Entries)
                entry.Selected = selectAll;
        }
        for (var column = 1; column < labels.Length; column++)
            OmniControls.TableHeader(labels[column], rowContentHeight);
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(result.Entries.Count, rowHeight);
            while (clipper.Step())
            {
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                {
                    var entry = result.Entries[index];
                    using var id = ImRaii.PushId(index);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                    ImGui.TableNextColumn();
                    OmniControls.CenterTableItem(checkboxSize, rowContentHeight);
                    OmniControls.Checkbox("##selected", ref entry.Selected);
                    ImGui.TableNextColumn();
                    OmniControls.TableTextCentered(Text(entry.Category.ToString()), rowContentHeight);
                    ImGui.TableNextColumn();
                    var pathSize = new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), rowContentHeight);
                    OmniControls.DrawTextCentered(Path.GetRelativePath(result.Root, entry.Path),
                        ImGui.GetCursorScreenPos(), pathSize, OmniTheme.Tokens.Text);
                    ImGui.Dummy(pathSize);
                    if (ImGui.IsItemHovered())
                    {
                        using var tooltip = ImRaii.Tooltip();
                        ImGui.TextUnformatted(entry.Path);
                    }
                    ImGui.TableNextColumn();
                    OmniControls.TableTextCentered(FormatSize(entry.Length), rowContentHeight);
                    ImGui.TableNextColumn();
                    OmniControls.TableTextCentered(entry.LastWriteUTC.ToLocalTime().ToString("yyyy-MM-dd"), rowContentHeight);
                }
            }
        }
        finally
        {
            clipper.End();
            clipper.Destroy();
        }
    }

    private void BeginScan()
    {
        if (CreateOperation() is not { } operation)
            return;

        preview  = null;
        status   = Text("Scanning");
        scanTask = Task.Run(() => Scan(operation.Root, DateTime.UtcNow, operation.Token,
            operation.ProtectedPluginNames), operation.Token);
        Activation.Track(scanTask);
    }

    private void BeginCleanup()
    {
        if (CreateOperation() is not { } operation)
            return;

        var snapshot = preview is { } currentPreview
            ? currentPreview with
            {
                ProtectedPluginNames = operation.ProtectedPluginNames
            }
            : null;
        var selected = snapshot?.Entries.Where(static entry => entry.Selected).ToArray();
        status      = Text("Deleting");
        preview     = null;
        cleanupTask = Task.Run(() =>
        {
            var current = snapshot ?? Scan(operation.Root, DateTime.UtcNow, operation.Token,
                operation.ProtectedPluginNames);
            var entries = selected ?? current.Entries.ToArray();
            if (current.Errors.Count == 0)
                return DeleteSelected(current, entries, operation.Token);

            var result = new CleanupResult
            {
                Skipped = entries.Length
            };
            result.Errors.AddRange(current.Errors);
            return result;
        }, operation.Token);
        Activation.Track(cleanupTask);
    }

    private (string Root, IReadOnlySet<string> ProtectedPluginNames, CancellationToken Token)? CreateOperation()
    {
        if (scanTask is not null || cleanupTask is not null)
            return null;

        try
        {
            var ownDirectory = new DirectoryInfo(DalamudServices.PluginInterface.GetPluginConfigDirectory());
            var pluginConfigs = ownDirectory.Parent;
            var root = pluginConfigs?.Parent?.FullName;
            if (pluginConfigs is null || root is null ||
                !string.Equals(pluginConfigs.Name, "pluginConfigs", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(pluginConfigs.FullName) || !Directory.Exists(Path.Combine(root, "installedPlugins")))
            {
                status = Text("DirectoryMissing");
                errors = [];
                NotifyStatus(NotificationType.Error);
                return null;
            }

            var protectedPluginNames = DalamudServices.PluginInterface.InstalledPlugins
                .Select(static plugin => plugin.InternalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            protectedPluginNames.Add(ownDirectory.Name);
            protectedPluginNames.Add("AEAssistV3");
            if (!FrameworkManager.Instance().Reg(CollectResults))
                throw new InvalidOperationException("Directory cleanup completion registration failed.");

            cancellation?.Dispose();
            cancellation = new();
            errors       = [];
            return (root, protectedPluginNames, cancellation.Token);
        }
        catch (Exception exception)
        {
            FrameworkManager.Instance().Unreg(CollectResults);
            status = Text("Failed");
            errors = [exception.Message];
            NotifyStatus(NotificationType.Error);
            return null;
        }
    }

    private void CollectResults(IFramework? _ = null)
    {
        if (scanTask is null && cleanupTask is null)
            return;

        try
        {
            if (scanTask is { IsCompleted: true } completedScan)
            {
                scanTask = null;
                if (completedScan.IsCanceled)
                    throw new OperationCanceledException();
                preview = completedScan.Result;
                errors  = preview.Errors;
                status  = errors.Count == 0 ? string.Empty : string.Format(Text("ScanErrors"), errors.Count);
                if (errors.Count > 0)
                    NotifyStatus(NotificationType.Warning);
            }
            if (cleanupTask is { IsCompleted: true } completedCleanup)
            {
                cleanupTask = null;
                if (completedCleanup.IsCanceled)
                    throw new OperationCanceledException();
                var result = completedCleanup.Result;
                errors = result.Errors;
                status = string.Format(Text("Result"), result.Deleted, FormatSize(result.Bytes), result.Skipped,
                    result.Errors.Count);
                if (result.Cancelled)
                    status += " " + Text("Cancelled");
                NotifyStatus(result.Errors.Count == 0 && result.Skipped == 0 && !result.Cancelled
                    ? NotificationType.Success
                    : NotificationType.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            status = Text("Cancelled");
            NotifyStatus(NotificationType.Info);
        }
        catch (Exception exception)
        {
            status = Text("Failed");
            errors = [exception.GetBaseException().Message];
            NotifyStatus(NotificationType.Error);
        }
        finally
        {
            if (scanTask is null && cleanupTask is null)
            {
                FrameworkManager.Instance().Unreg(CollectResults);
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }

    private void NotifyStatus(NotificationType type) =>
        OmniNotifier.Popup(Title, string.Join("\n", errors.Take(5).Prepend(status)), type);

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
        Backups,
        PluginConfigs
    }

    private sealed class Candidate
    (
        string   path,
        long     length,
        DateTime lastWriteUTC,
        DateTime creationUTC,
        Category category,
        bool     isDirectory = false
    )
    {
        public string Path { get; } = path;
        public long Length { get; } = length;
        public DateTime LastWriteUTC { get; } = lastWriteUTC;
        public DateTime CreationUTC { get; } = creationUTC;
        public Category Category { get; } = category;
        public bool IsDirectory { get; } = isDirectory;
        public bool Selected = true;
    }

    private sealed record ScanResult
    (
        string               Root,
        List<Candidate>      Entries,
        List<string>         Errors,
        IReadOnlySet<string> ProtectedPluginNames
    );

    private sealed class CleanupResult
    {
        public int  Deleted;
        public long Bytes;
        public int  Skipped;
        public bool Cancelled;
        public List<string> Errors { get; } = [];
    }
}
