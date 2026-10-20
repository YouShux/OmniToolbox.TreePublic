using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmniToolbox.TreePublic;

public sealed partial class DalamudDirectoryCleanup
{
    private static ScanResult Scan(string root, DateTime nowUTC, CancellationToken token)
    {
        var rootDirectory = new DirectoryInfo(root);
        root = rootDirectory.ResolveLinkTarget(true)?.FullName ?? rootDirectory.FullName;
        var result = new ScanResult(Path.GetFullPath(root), [], []);
        var rootFiles = GetFiles(result, string.Empty, token);
        AddExpired(result, rootFiles.Where(file => IsRootLog(file.Name)), Category.Logs, LOG_RETENTION_DAYS, nowUTC);
        foreach (var group in rootFiles.Where(file => IsConfigBackup(file.Name)).GroupBy(
                     file => file.Name[..file.Name.IndexOf(".bak-", StringComparison.OrdinalIgnoreCase)],
                     StringComparer.OrdinalIgnoreCase))
            AddBackupGroup(result, group, nowUTC);
        foreach (var directory in GetBackupDirectories(result))
        {
            var files = GetFiles(result, Path.GetRelativePath(root, directory.FullName), token)
                .Where(file => file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            AddBackupGroup(result, files, nowUTC);
        }

        result.Entries.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return result;
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

    private static DirectoryInfo[] GetBackupDirectories(ScanResult result)
    {
        var path = Path.Combine(result.Root, "backups");
        if (!Directory.Exists(path))
            return [];
        try
        {
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
        var current = Scan(preview.Root, DateTime.UtcNow, token);
        if (current.Errors.Count > 0)
        {
            result.Errors.AddRange(current.Errors);
            result.Skipped = selected.Length;
            return result;
        }

        var eligible = current.Entries.ToDictionary(static entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in selected)
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

    #region 常量

    private const int LOG_RETENTION_DAYS = 30;

    private const int BACKUP_RETENTION_DAYS = 90;

    private const int MINIMUM_BACKUPS = 3;

    #endregion
}
