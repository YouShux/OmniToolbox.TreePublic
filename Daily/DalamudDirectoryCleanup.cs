using System.IO;
using System.Linq;
using Dalamud.Interface.ImGuiNotification;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed class DalamudDirectoryCleanup : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DalamudDirectoryCleanupTitle"),
        Description = OmniLoc.Get("DalamudDirectoryCleanupDescription"),
        Category = ModuleCategory.Daily
    };

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.DalamudDirectoryCleanup.PluginConfigs"), false))
        {
            CleanupPluginConfigs();
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.DalamudDirectoryCleanup.ExpiredLogs"), false))
        {
            CleanupExpiredLogs();
        }

        return false;
    }

    private void CleanupPluginConfigs()
    {
        if (!TryResolveDirectories(out var dalamudDirectory, out var pluginConfigsDirectory))
        {
            NotifyDirectoryMissing();
            return;
        }

        var installedPluginNames = new DirectoryInfo(Path.Combine(dalamudDirectory, "installedPlugins"))
            .EnumerateDirectories()
            .Select(static directory => directory.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        installedPluginNames.Add("AEAssistV3");

        // 当前插件可能从开发目录或自定义目录加载，因此不一定存在对应的已安装插件目录。
        installedPluginNames.Add(
            new DirectoryInfo(DalamudServices.PluginInterface.GetPluginConfigDirectory()).Name);

        var deleted = 0;
        var failed = 0;

        foreach (var directory in pluginConfigsDirectory.EnumerateDirectories().ToArray())
        {
            if (installedPluginNames.Contains(directory.Name))
            {
                continue;
            }

            if (TryDelete(directory.FullName, true))
            {
                deleted++;
            }
            else
            {
                failed++;
            }
        }

        foreach (var file in pluginConfigsDirectory.EnumerateFiles().ToArray())
        {
            var jsonSuffixIndex = file.Name.IndexOf(".json", StringComparison.OrdinalIgnoreCase);
            if (jsonSuffixIndex <= 0 || installedPluginNames.Contains(file.Name[..jsonSuffixIndex]))
            {
                continue;
            }

            if (TryDelete(file.FullName, false))
            {
                deleted++;
            }
            else
            {
                failed++;
            }
        }

        NotifyResult(
            "Feature.DalamudDirectoryCleanup.PluginConfigs.Result",
            "Feature.DalamudDirectoryCleanup.PluginConfigs.PartialResult",
            deleted,
            failed);
    }

    private void CleanupExpiredLogs()
    {
        if (!TryResolveDirectories(out var dalamudDirectory, out _))
        {
            NotifyDirectoryMissing();
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var file in new DirectoryInfo(dalamudDirectory).EnumerateFiles().ToArray())
        {
            if (!IsCleanupCandidate(file.Name))
            {
                continue;
            }

            if (TryDelete(file.FullName, false))
            {
                deleted++;
            }
            else
            {
                failed++;
            }
        }

        NotifyResult(
            "Feature.DalamudDirectoryCleanup.ExpiredLogs.Result",
            "Feature.DalamudDirectoryCleanup.ExpiredLogs.PartialResult",
            deleted,
            failed);
    }

    private static bool IsCleanupCandidate(string fileName) =>
        fileName.EndsWith(".old.log", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("dalamudConfig.json.bak-", StringComparison.OrdinalIgnoreCase) ||
        (fileName.StartsWith("dalamud_appcrash_", StringComparison.OrdinalIgnoreCase) &&
         (Path.GetExtension(fileName).Equals(".log", StringComparison.OrdinalIgnoreCase) ||
          Path.GetExtension(fileName).Equals(".dmp", StringComparison.OrdinalIgnoreCase)));

    private static bool TryResolveDirectories(out string dalamudDirectory, out DirectoryInfo pluginConfigsDirectory)
    {
        var resolvedPluginConfigsDirectory =
            new DirectoryInfo(DalamudServices.PluginInterface.GetPluginConfigDirectory()).Parent;
        if (resolvedPluginConfigsDirectory is null)
        {
            dalamudDirectory = string.Empty;
            pluginConfigsDirectory = null!;
            return false;
        }

        pluginConfigsDirectory = resolvedPluginConfigsDirectory;
        dalamudDirectory = pluginConfigsDirectory.Parent?.FullName ?? string.Empty;
        return pluginConfigsDirectory.Name.Equals("pluginConfigs", StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(pluginConfigsDirectory.FullName) &&
               Directory.Exists(Path.Combine(dalamudDirectory, "installedPlugins"));
    }

    private static bool TryDelete(string path, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Dalamud directory cleanup failed for {Path}.", path);
            return false;
        }
    }

    private void NotifyResult(string resultKey, string partialResultKey, int deleted, int failed)
    {
        OmniNotifier.Popup(
            Info.Title,
            string.Format(
                OmniLoc.Get(failed == 0 ? resultKey : partialResultKey),
                deleted,
                failed),
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private void NotifyDirectoryMissing() => OmniNotifier.Popup(
        Info.Title,
        OmniLoc.Get("Feature.DalamudDirectoryCleanup.DirectoryMissing"),
        NotificationType.Error);
}
