using System.Globalization;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal sealed class ChatLogReplayWindow
{
    private readonly List<ChatLogReplayFile> files = [];
    private readonly List<ChatLogReplayEntry> entries = [];
    private readonly List<ChatLogReplayEntry> filteredEntries = [];
    private readonly Dictionary<string, bool> channelVisibility = new(StringComparer.Ordinal);
    private readonly List<string> orderedChannels = [];
    private readonly ChatLogReplayRenderer renderer = new();
    private ChatLogReplayStorage? storage;
    private string? selectedPath;
    private string searchText = string.Empty;
    private string anonymousPrefix = string.Empty;
    private string statusKey = "Feature.ChatLogReplay.Status.SelectFile";
    private object[] statusArguments = [];
    private int refreshRequestID;
    private int readRequestID;
    private int skippedLineCount;
    private bool isOpen;
    private bool showTime = true;
    private bool anonymousMode;
    private bool filterDirty = true;
    private float lastScale = float.NaN;

    public void Open(ChatLogReplayStorage value)
    {
        storage = value;
        if (string.IsNullOrWhiteSpace(anonymousPrefix))
        {
            anonymousPrefix = OmniLoc.Get("Feature.ChatLogReplay.Anonymous.Default");
        }

        isOpen = true;
        RequestRefresh();
    }

    public void Close()
    {
        isOpen = false;
        storage = null;
        refreshRequestID++;
        readRequestID++;
        ClearEntries();
    }

    public void Draw()
    {
        if (!isOpen || storage is null)
        {
            return;
        }

        ApplyStorageResults();
        if (filterDirty)
        {
            RebuildFilter();
        }

        var scale = OmniTheme.ScaleValue;
        ImGui.SetNextWindowSize(
            OmniTheme.Scale(new Vector2(960f, 640f)),
            MathF.Abs(lastScale - scale) > 0.001f ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        lastScale = scale;
        if (!ImGui.Begin(OmniLoc.Get("Feature.ChatLogReplay.Title"), ref isOpen))
        {
            ImGui.End();
            return;
        }

        DrawToolbar();
        ImGui.Separator();
        using (var filePane = ImRaii.Child(
                   "##chatLogReplayFiles",
                   new Vector2(Math.Clamp(
                       250f * scale,
                       210f,
                       330f), 0f),
                   true))
        {
            if (filePane)
            {
                DrawFileList();
            }
        }

        ImGui.SameLine();
        using (var mainPane = ImRaii.Child("##chatLogReplayMain", Vector2.Zero, false))
        {
            if (mainPane)
            {
                DrawReplayControls();
                DrawChannelFilters();
                DrawReplayBody();
            }
        }

        ImGui.End();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button(OmniLoc.Get("Feature.ChatLogReplay.Refresh")))
        {
            RequestRefresh();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.ChatLogReplay.Directory"),
            storage!.DirectoryPath));
    }

    private void DrawFileList()
    {
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ChatLogReplay.Files"));
        ImGui.Separator();
        if (files.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.ChatLogReplay.NoFiles"));
            return;
        }

        foreach (var file in files)
        {
            ImGui.PushID(file.Path);
            if (ImGui.Selectable(file.Name, string.Equals(selectedPath, file.Path, StringComparison.Ordinal)))
            {
                RequestRead(file.Path);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.ChatLogReplay.FileTooltip"),
                    file.Path,
                    file.LastWriteTime,
                    ChatLogReplayPresentation.FormatFileSize(file.Length)));
            }

            ImGui.SameLine();
            ImGui.TextDisabled(ChatLogReplayPresentation.FormatFileSize(file.Length));
            ImGui.PopID();
        }
    }

    private void DrawReplayControls()
    {
        ImGui.SetNextItemWidth(MathF.Min(360f, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputText(
                $"{OmniLoc.Get("Feature.ChatLogReplay.Search")}##chatLogReplaySearch",
                ref searchText,
                128))
        {
            filterDirty = true;
        }

        ImGui.SameLine();
        ImGui.Checkbox(
            $"{OmniLoc.Get("Feature.ChatLogReplay.ShowTime")}##chatLogReplayShowTime",
            ref showTime);
        ImGui.SameLine();
        ImGui.Checkbox(
            $"{OmniLoc.Get("Feature.ChatLogReplay.Anonymous")}##chatLogReplayAnonymous",
            ref anonymousMode);
        ImGui.SameLine();
        using (ImRaii.Disabled(!anonymousMode))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText("##chatLogReplayAnonymousPrefix", ref anonymousPrefix, 24))
            {
                renderer.RebuildAnonymous(GetAnonymousPrefix());
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(OmniLoc.Get("Feature.ChatLogReplay.Anonymous.Help"));
        }

        ImGui.TextDisabled(string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.ChatLogReplay.Status.Summary"),
            GetStatusText(),
            filteredEntries.Count,
            entries.Count));
    }

    private void DrawChannelFilters()
    {
        if (orderedChannels.Count == 0)
        {
            return;
        }

        if (ImGui.SmallButton(OmniLoc.Get("Feature.ChatLogReplay.Channel.SelectAll")))
        {
            SetAllChannels(true);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(OmniLoc.Get("Feature.ChatLogReplay.Channel.SelectNone")))
        {
            SetAllChannels(false);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(OmniLoc.Get("Feature.ChatLogReplay.Channel.Filter"));
        using var table = ImRaii.Table(
            "##chatLogReplayChannelFilters",
            5,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return;
        }

        for (var index = 0; index < orderedChannels.Count; index++)
        {
            if (index % 5 == 0)
            {
                ImGui.TableNextRow();
            }

            ImGui.TableSetColumnIndex(index % 5);
            var channel = orderedChannels[index];
            var visible = channelVisibility[channel];
            if (ImGui.Checkbox(
                    $"{ChatLogReplayPresentation.GetChannelDisplayName(channel)}##chatLogReplayChannel{channel}",
                    ref visible))
            {
                channelVisibility[channel] = visible;
                filterDirty = true;
            }
        }
    }

    private void DrawReplayBody()
    {
        ImGui.Spacing();
        using (var body = ImRaii.Child(
                   "##chatLogReplayBody",
                   Vector2.Zero,
                   true,
                   ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body)
            {
                if (filteredEntries.Count == 0)
                {
                    ImGui.TextDisabled(OmniLoc.Get(entries.Count == 0
                        ? "Feature.ChatLogReplay.Empty"
                        : "Feature.ChatLogReplay.NoMatches"));
                }
                else
                {
                    renderer.Draw(filteredEntries, showTime, anonymousMode);
                }
            }
        }
    }

    private void RequestRefresh()
    {
        SetStatus("Feature.ChatLogReplay.Status.Refreshing");
        storage?.RequestRefresh(++refreshRequestID);
    }

    private void RequestRead(string path)
    {
        selectedPath = path;
        ClearEntries();
        SetStatus("Feature.ChatLogReplay.Status.Reading");
        storage?.RequestRead(++readRequestID, path);
    }

    private void ApplyStorageResults()
    {
        while (storage!.TryDequeueResult(out var result))
        {
            switch (result)
            {
                case ChatLogReplayFileListResult fileList when fileList.RequestID == refreshRequestID:
                    ApplyFileList(fileList);
                    break;
                case ChatLogReplayReadResult read when read.RequestID == readRequestID:
                    ApplyReadResult(read);
                    break;
            }
        }
    }

    private void ApplyFileList(ChatLogReplayFileListResult result)
    {
        files.Clear();
        if (!result.Succeeded)
        {
            SetStatus("Feature.ChatLogReplay.Status.RefreshFailed");
            return;
        }

        files.AddRange(result.Files);
        if (files.Count == 0)
        {
            selectedPath = null;
            ClearEntries();
            SetStatus("Feature.ChatLogReplay.Status.NoFiles");
            return;
        }

        var path = files[0].Path;
        foreach (var file in files)
        {
            if (string.Equals(file.Path, selectedPath, StringComparison.Ordinal))
            {
                path = file.Path;
                break;
            }
        }

        RequestRead(path);
    }

    private void ApplyReadResult(ChatLogReplayReadResult result)
    {
        if (!result.Succeeded)
        {
            SetStatus("Feature.ChatLogReplay.Status.ReadFailed");
            return;
        }

        entries.AddRange(result.Entries);
        skippedLineCount = result.SkippedLineCount;
        channelVisibility.Clear();
        orderedChannels.Clear();
        foreach (var entry in entries)
        {
            if (channelVisibility.TryAdd(entry.ChannelName, true))
            {
                orderedChannels.Add(entry.ChannelName);
            }
        }

        orderedChannels.Sort(static (left, right) => string.Compare(
            ChatLogReplayPresentation.GetChannelDisplayName(left),
            ChatLogReplayPresentation.GetChannelDisplayName(right),
            StringComparison.CurrentCulture));
        renderer.Reset(entries, GetAnonymousPrefix());
        filterDirty = true;
        SetStatus(
            skippedLineCount == 0
                ? "Feature.ChatLogReplay.Status.Read"
                : "Feature.ChatLogReplay.Status.ReadWithSkipped",
            entries.Count,
            skippedLineCount);
    }

    private void RebuildFilter()
    {
        filteredEntries.Clear();
        var keyword = searchText.Trim();
        foreach (var entry in entries)
        {
            if (!channelVisibility.TryGetValue(entry.ChannelName, out var visible) || !visible)
            {
                continue;
            }

            if (keyword.Length == 0 ||
                entry.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                ChatLogReplayPresentation.GetChannelDisplayName(entry.ChannelName)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                filteredEntries.Add(entry);
            }
        }

        filterDirty = false;
    }

    private void SetAllChannels(bool visible)
    {
        foreach (var channel in orderedChannels)
        {
            channelVisibility[channel] = visible;
        }

        filterDirty = true;
    }

    private void ClearEntries()
    {
        entries.Clear();
        filteredEntries.Clear();
        channelVisibility.Clear();
        orderedChannels.Clear();
        skippedLineCount = 0;
        renderer.Reset(entries, GetAnonymousPrefix());
        filterDirty = false;
    }

    private string GetAnonymousPrefix() =>
        string.IsNullOrWhiteSpace(anonymousPrefix)
            ? OmniLoc.Get("Feature.ChatLogReplay.Anonymous.Default")
            : anonymousPrefix.Trim();

    private void SetStatus(string key, params object[] arguments)
    {
        statusKey = key;
        statusArguments = arguments;
    }

    private string GetStatusText() =>
        statusArguments.Length == 0
            ? OmniLoc.Get(statusKey)
            : string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(statusKey), statusArguments);
}
