using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmniToolbox.TreePublic;

public sealed partial class DalamudDirectoryCleanup
{
    private static ScanResult Scan(string root, DateTime nowUTC, CancellationToken token,
        IReadOnlySet<string> protectedPluginNames)
    {
        var rootDirectory = new DirectoryInfo(root);
        root = rootDirectory.ResolveLinkTarget(true)?.FullName ?? rootDirectory.FullName;
        var result = new ScanResult(Path.GetFullPath(root), [], [], protectedPluginNames);
        var rootFiles = GetFiles(result, string.Empty, token);
        AddExpired(result, rootFiles.Where(file => IsRootLog(file.Name)), Category.Logs, LOG_RETENTION_DAYS, nowUTC);
        foreach (var group in rootFiles.Where(file => IsConfigBackup(file.Name)).GroupBy(
                     file => file.Name[..file.Name.IndexOf(".bak-", StringComparison.OrdinalIgnoreCase)],
                     StringComparer.OrdinalIgnoreCase))
            AddBackupGroup(result, group, nowUTC);
        foreach (var directory in GetDirectories(result, "backups", token))
        {
            var files = GetFiles(result, Path.GetRelativePath(root, directory.FullName), token)
                .Where(file => file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            AddBackupGroup(result, files, nowUTC);
        }
        AddOrphanedPluginConfigs(result, token);

        result.Entries.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return result;
    }

    private static void AddOrphanedPluginConfigs(ScanResult result, CancellationToken token)
    {
        var installedPath = Path.Combine(result.Root, "installedPlugins");
        try
        {
            var installedNames = new DirectoryInfo(installedPath).EnumerateDirectories()
                .Select(static directory => directory.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            installedNames.UnionWith(result.ProtectedPluginNames);
            foreach (var file in GetFiles(result, "pluginConfigs", token))
            {
                var suffix = file.Name.IndexOf(".json", StringComparison.OrdinalIgnoreCase);
                if (suffix > 0 && !installedNames.Contains(file.Name[..suffix]))
                    result.Entries.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc,
                        file.CreationTimeUtc, Category.PluginConfigs));
            }
            var pending = new Stack<DirectoryInfo>(GetDirectories(result, "pluginConfigs", token)
                .Where(directory => !installedNames.Contains(directory.Name)));
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(result.Root, directory.FullName);
                foreach (var file in GetFiles(result, relativePath, token))
                    result.Entries.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc,
                        file.CreationTimeUtc, Category.PluginConfigs));
                foreach (var child in GetDirectories(result, relativePath, token))
                    pending.Push(child);
                result.Entries.Add(new(directory.FullName, 0, directory.LastWriteTimeUtc,
                    directory.CreationTimeUtc, Category.PluginConfigs, true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{installedPath}: {exception.Message}");
        }
    }

    private static void AddBackupGroup(ScanResult result, IEnumerable<FileInfo> files, DateTime nowUTC) =>
        AddExpired(result, files.OrderByDescending(static file => file.LastWriteTimeUtc)
                .ThenByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(MINIMUM_BACKUPS), Category.Backups, BACKUP_RETENTION_DAYS, nowUTC);

    private static void AddExpired(ScanResult result, IEnumerable<FileInfo> files, Category category, int days, DateTime nowUTC)
    {
        var cutoff = nowUTC.AddDays(-days);
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc >= cutoff)
                continue;
            result.Entries.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc, category));
        }
    }

    private static List<FileInfo> GetFiles(ScanResult result, string relativePath, CancellationToken token)
    {
        var files = new List<FileInfo>();
        var path = Path.Combine(result.Root, relativePath);
        if (!Directory.Exists(path))
            return files;
        try
        {
            if (!IsSafePath(result.Root, path))
                return files;
            foreach (var file in new DirectoryInfo(path).GetFiles())
            {
                token.ThrowIfCancellationRequested();
                if (IsSafePath(result.Root, file.FullName))
                    files.Add(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{path}: {exception.Message}");
        }
        return files;
    }

    private static DirectoryInfo[] GetDirectories(ScanResult result, string relativePath, CancellationToken token)
    {
        var path = Path.Combine(result.Root, relativePath);
        if (!Directory.Exists(path))
            return [];
        try
        {
            token.ThrowIfCancellationRequested();
            return IsSafePath(result.Root, path)
                ? new DirectoryInfo(path).GetDirectories().Where(directory => IsSafePath(result.Root, directory.FullName)).ToArray()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{path}: {exception.Message}");
            return [];
        }
    }

    private static bool IsSafePath(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        while (true)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return false;
            var parent = Path.GetDirectoryName(path);
            if (parent is null)
                return true;
            path = parent;
        }
    }

    private static bool IsRootLog(string name) =>
        name.Equals("argReader.log", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("aria2.log", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("patcher.log", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("output.log", StringComparison.OrdinalIgnoreCase) ||
        Matches(name, @"^vcdiff-shim\d{8}\.log$") ||
        Matches(name, @"^(?:dalamud(?:\.boot|\.injector|\.crashhandler)?|output|patcher|argReader|aria2)(?:\.old\.log|\.log\.\d+)$") ||
        Matches(name, @"^dalamud_appcrash_.+\.(?:log|dmp)$");

    private static bool IsConfigBackup(string name) =>
        Matches(name, @"^(?:dalamudConfig|launcherConfigV3)\.json\.bak-\d{8}[-_]?\d{6,9}$");

    private static bool Matches(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    private static CleanupResult DeleteSelected(ScanResult preview, Candidate[] selected, CancellationToken token)
    {
        var result = new CleanupResult();
        var current = Scan(preview.Root, DateTime.UtcNow, token, preview.ProtectedPluginNames);
        if (current.Errors.Count > 0)
        {
            result.Errors.AddRange(current.Errors);
            result.Skipped = selected.Length;
            return result;
        }

        var eligible = current.Entries.ToDictionary(static entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in selected.OrderBy(static entry => entry.IsDirectory)
                     .ThenByDescending(static entry => entry.Path.Length))
        {
            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }

            try
            {
                if (!eligible.ContainsKey(entry.Path) || !IsSafePath(preview.Root, entry.Path))
                {
                    result.Skipped++;
                    continue;
                }
                if (entry.Category == Category.PluginConfigs && !IsOrphanedPluginConfig(preview, entry))
                {
                    result.Skipped++;
                    continue;
                }
                if (entry.IsDirectory)
                {
                    var directory = new DirectoryInfo(entry.Path);
                    if (!directory.Exists || directory.CreationTimeUtc != entry.CreationUTC ||
                        (directory.Attributes & FileAttributes.ReadOnly) != 0 ||
                        directory.EnumerateFileSystemInfos().Any())
                    {
                        result.Skipped++;
                        continue;
                    }
                    directory.Delete();
                    result.Deleted++;
                    continue;
                }

                var file = new FileInfo(entry.Path);
                if (!file.Exists || file.Length != entry.Length || file.LastWriteTimeUtc != entry.LastWriteUTC ||
                    file.CreationTimeUtc != entry.CreationUTC || (file.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    result.Skipped++;
                    continue;
                }

                using (file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }
                File.Delete(entry.Path);
                result.Deleted++;
                result.Bytes += entry.Length;
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{entry.Path}: {exception.Message}");
            }
        }
        return result;
    }

    private static bool IsOrphanedPluginConfig(ScanResult result, Candidate entry)
    {
        var relativePath = Path.GetRelativePath(Path.Combine(result.Root, "pluginConfigs"), entry.Path);
        var separator = relativePath.IndexOf(Path.DirectorySeparatorChar);
        var name = separator >= 0 ? relativePath[..separator] : relativePath;
        if (separator < 0 && !entry.IsDirectory)
            name = name[..name.IndexOf(".json", StringComparison.OrdinalIgnoreCase)];
        var installedPath = Path.Combine(result.Root, "installedPlugins");
        return Directory.Exists(installedPath) && !result.ProtectedPluginNames.Contains(name) &&
               !Directory.Exists(Path.Combine(installedPath, name));
    }

    #region 常量

    private const int LOG_RETENTION_DAYS = 30;

    private const int BACKUP_RETENTION_DAYS = 90;

    private const int MINIMUM_BACKUPS = 3;

    #endregion
}
