using System.Diagnostics;
using System.Globalization;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse;

public sealed class IconBrowser
{
    private const int IconMax = 350_000;
    private const int ScanBudgetPerFrame = 64;
    private const int DiagnosticLogIntervalMs = 250;
    private const float SearchControlWidth = 160f;
    private const float IconSizeControlWidth = 160f;
    private const float ModeControlWidth = 180f;

    private static readonly IconTabDefinition[] GameIconTabs =
    [
        new("IconBrowser.Tab.Featured",
        [
            new(0, 100),
            new(62_000, 62_600),
            new(62_800, 62_900),
            new(66_000, 66_400),
            new(90_000, 100_000),
            new(114_000, 114_100),
            new(230_850, 231_000),
            new(10_000_000, 10_003_000)
        ]),
        new("IconBrowser.Tab.Misc",
        [
            new(60_000, 61_000),
            new(61_200, 61_250),
            new(61_290, 62_000),
            new(62_600, 62_620),
            new(63_900, 64_000),
            new(64_500, 64_550),
            new(65_000, 65_900),
            new(180_000, 180_060),
            new(230_000, 230_850),
            new(231_000, 240_000)
        ]),
        new("IconBrowser.Tab.Misc2",
        [
            new(62_900, 63_200),
            new(63_875, 63_900),
            new(65_900, 66_000),
            new(66_400, 66_500),
            new(67_000, 68_000),
            new(70_120, 70_200),
            new(70_500, 70_960),
            new(70_960, 71_450),
            new(72_000, 72_500),
            new(72_500, 72_620),
            new(76_000, 76_200),
            new(80_000, 80_200),
            new(80_730, 81_000),
            new(82_000, 82_100),
            new(82_270, 82_325),
            new(83_000, 84_000),
            new(180_060, 180_100),
            new(240_000, 241_000)
        ]),
        new("IconBrowser.Tab.Actions",
        [
            new(100, 4_000),
            new(5_100, 8_000),
            new(8_000, 9_000),
            new(9_000, 10_000),
            new(19_600, 19_800),
            new(19_800, 20_000),
            new(61_250, 61_290),
            new(64_200, 64_325),
            new(64_550, 64_600),
            new(64_600, 64_800),
            new(64_800, 65_000),
            new(70_000, 70_120),
            new(82_200, 82_270),
            new(246_000, 250_000)
        ]),
        new("IconBrowser.Tab.MountsMinions",
        [
            new(4_000, 4_400),
            new(4_400, 5_100),
            new(59_000, 59_400),
            new(59_400, 60_000),
            new(68_000, 68_400),
            new(68_400, 69_000)
        ]),
        new("IconBrowser.Tab.Items",
        [
            new(20_000, 30_000),
            new(50_000, 54_000),
            new(58_000, 59_000)
        ]),
        new("IconBrowser.Tab.Equipment",
        [
            new(30_000, 50_000),
            new(54_000, 54_225),
            new(54_225, 54_400),
            new(54_400, 58_000),
            new(200_000, 210_000)
        ]),
        new("IconBrowser.Tab.Artwork",
        [
            new(130_000, 142_000),
            new(250_000, 251_004),
            new(251_600, 251_602),
            new(251_700, 251_704)
        ]),
        new("IconBrowser.Tab.StatusEffects", [new(210_000, 230_000)], true),
        new("IconBrowser.Tab.Garbage",
        [
            new(61_000, 61_100),
            new(62_620, 62_800),
            new(63_200, 63_875),
            new(66_500, 67_000),
            new(69_000, 70_000),
            new(70_200, 70_500),
            new(71_450, 71_500),
            new(78_000, 80_000),
            new(80_200, 80_730),
            new(81_000, 82_000),
            new(82_100, 82_200),
            new(84_000, 85_000),
            new(85_000, 87_000),
            new(150_000, 170_000),
            new(190_000, 200_000),
            new(241_000, 241_200)
        ]),
        new("IconBrowser.Tab.Spoilers",
        [
            new(87_000, 90_000),
            new(72_620, 76_000),
            new(120_000, 130_000),
            new(142_000, 150_000),
            new(181_000, 181_500)
        ]),
        new("IconBrowser.Tab.Spoilers2",
        [
            new(71_500, 72_000),
            new(100_000, 114_000),
            new(114_100, 120_000)
        ]),
        new("IconBrowser.Tab.Uncategorized",
        [
            new(10_000, 19_600),
            new(61_100, 61_200),
            new(64_000, 64_200),
            new(64_325, 64_500),
            new(76_200, 78_000),
            new(82_325, 83_000),
            new(181_500, 190_000),
            new(241_200, 246_000),
            new(251_004, 251_600),
            new(251_602, 251_700),
            new(251_704, IconMax)
        ])
    ];

    private static readonly long ScanBudgetTicks = Math.Max(1, Stopwatch.Frequency / 1000);

    private readonly IconBrowserConfig config;
    private readonly Action saveConfig;
    private readonly Dictionary<int, IconTabCache> tabCaches = [];
    private readonly List<int> filteredGameIcons = [];
    private List<SeIconEntry>? seIconEntries;
    private Action<uint>? gameIconApplyTarget;
    private Action<string>? seIconApplyTarget;
    private BrowserMode mode;
    private string gameIconFilter = string.Empty;
    private string seIconFilter = string.Empty;
    private Vector2 lastWindowSize;
    private Vector2 diagnosticWindowSize;
    private long nextDiagnosticLogTick;
    private int lastLoggedColumns = -1;
    private bool logCurrentDraw;

    public IconBrowser(IconBrowserConfig config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        config.IconSize = Math.Clamp(config.IconSize, 16f, 128f);
    }

    public bool IsOpen { get; private set; }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        ClearApplyTargets();
        IsOpen = true;
    }

    public void OpenForSelection(
        bool seIconMode,
        Action<uint>? gameIconTarget,
        Action<string>? seIconTarget)
    {
        gameIconApplyTarget = gameIconTarget;
        seIconApplyTarget = seIconTarget;
        mode = seIconMode ? BrowserMode.SeIconChar : BrowserMode.GameIcon;
        IsOpen = true;
    }

    public void Draw()
    {
        logCurrentDraw = false;
        if (!IsOpen)
        {
            return;
        }

        using var style = new ComicStyleScope();
        var iconSize = OmniTheme.Scale(config.IconSize);
        var viewportSize = ImGuiHelpers.MainViewport.Size;
        var imguiStyle = ImGui.GetStyle();
        var toolbarMinWidth =
            OmniTheme.Scale(SearchControlWidth + IconSizeControlWidth + ModeControlWidth) +
            OmniControls.CompactButtonSize(OmniLoc.Get("IconBrowser.RebuildCache")).X +
            ImGui.CalcTextSize(OmniLoc.Get("IconBrowser.Search")).X +
            ImGui.CalcTextSize(OmniLoc.Get("IconBrowser.IconSize")).X +
            ImGui.CalcTextSize(OmniLoc.Get("IconBrowser.Mode")).X +
            (imguiStyle.ItemSpacing.X + imguiStyle.ItemInnerSpacing.X) * 3f +
            imguiStyle.WindowPadding.X * 2f;
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(
                MathF.Min(
                    MathF.Max(
                        (iconSize + imguiStyle.ItemSpacing.X) * 11f + imguiStyle.WindowPadding.X * 2f,
                        toolbarMinWidth),
                    viewportSize.X),
                MathF.Min(OmniTheme.Scale(420f), viewportSize.Y)),
            viewportSize);

        var isOpen = IsOpen;
        if (!ImGui.Begin($"{OmniLoc.Get("IconBrowser.Title")}###OmniIconBrowser", ref isOpen))
        {
            ImGui.End();
            if (!isOpen)
            {
                Close();
            }

            return;
        }

        diagnosticWindowSize = ImGui.GetWindowSize();
        if (diagnosticWindowSize != lastWindowSize &&
            Environment.TickCount64 >= nextDiagnosticLogTick)
        {
            logCurrentDraw = true;
            nextDiagnosticLogTick = Environment.TickCount64 + DiagnosticLogIntervalMs;
            DalamudServices.PluginLog.Information(
                "[IconBrowser.Diagnostics] Frame begin: Window={WindowWidth}x{WindowHeight}, Mode={Mode}, IconSize={IconSize}, WorkingSet={WorkingSet}, ManagedMemory={ManagedMemory}.",
                diagnosticWindowSize.X,
                diagnosticWindowSize.Y,
                mode,
                iconSize,
                Environment.WorkingSet,
                GC.GetTotalMemory(false));
        }

        lastWindowSize = diagnosticWindowSize;
        DrawToolbar();
        if (mode == BrowserMode.SeIconChar)
        {
            DrawSeIconBrowser(iconSize);
        }
        else
        {
            DrawGameIconBrowser(iconSize);
        }

        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Frame body complete; entering ImGui.End.");
        }

        ImGui.End();
        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Frame end complete.");
        }

        if (!isOpen)
        {
            Close();
        }
    }

    private void DrawToolbar()
    {
        if (mode == BrowserMode.GameIcon)
        {
            if (OmniControls.InputText(
                    $"{OmniLoc.Get("IconBrowser.Search")}##iconBrowserGameFilter",
                    ref gameIconFilter,
                    16,
                    OmniTheme.Scale(SearchControlWidth),
                    ImGuiInputTextFlags.CharsDecimal))
            {
                filteredGameIcons.Clear();
                if (int.TryParse(
                        gameIconFilter,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var icon) &&
                    IconExists((uint)icon))
                {
                    filteredGameIcons.Add(icon);
                }
            }
        }
        else
        {
            OmniControls.InputText(
                $"{OmniLoc.Get("IconBrowser.Search")}##iconBrowserSeFilter",
                ref seIconFilter,
                128,
                OmniTheme.Scale(SearchControlWidth));
        }

        ImGui.SameLine();
        var iconSize = config.IconSize;
        if (OmniControls.SliderFloat(
                $"{OmniLoc.Get("IconBrowser.IconSize")}##iconBrowserSize",
                ref iconSize,
                16f,
                128f,
                "%.0f",
                OmniTheme.Scale(IconSizeControlWidth)))
        {
            config.IconSize = Math.Clamp(iconSize, 16f, 128f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            saveConfig();
        }

        ImGui.SameLine();
        DrawModeSelector();
        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("IconBrowser.RebuildCache"), false))
        {
            tabCaches.Clear();
        }
    }

    private void DrawModeSelector()
    {
        var current = OmniLoc.Get(mode == BrowserMode.GameIcon
            ? "IconBrowser.Mode.GameIcon"
            : "IconBrowser.Mode.SeIconChar");
        if (!OmniControls.BeginCombo(
                $"{OmniLoc.Get("IconBrowser.Mode")}##iconBrowserMode",
                current,
                OmniTheme.Scale(ModeControlWidth)))
        {
            return;
        }

        if (ImGui.Selectable(
                OmniLoc.Get("IconBrowser.Mode.GameIcon"),
                mode == BrowserMode.GameIcon))
        {
            mode = BrowserMode.GameIcon;
        }

        if (ImGui.Selectable(
                OmniLoc.Get("IconBrowser.Mode.SeIconChar"),
                mode == BrowserMode.SeIconChar))
        {
            mode = BrowserMode.SeIconChar;
        }

        ImGui.EndCombo();
    }

    private void DrawGameIconBrowser(float iconSize)
    {
        if (!ImGui.BeginTabBar("##iconBrowserTabs", ImGuiTabBarFlags.NoTooltip))
        {
            return;
        }

        for (var i = 0; i < GameIconTabs.Length; i++)
        {
            if (!ImGui.BeginTabItem($"{OmniLoc.Get(GameIconTabs[i].LabelKey)}##iconBrowserTab{i}"))
            {
                continue;
            }

            if (gameIconFilter.Length > 0)
            {
                DrawIconGrid(
                    i,
                    filteredGameIcons,
                    iconSize,
                    GameIconTabs[i].StatusIcons);
            }
            else
            {
                var cache = GetTabCache(i);
                if (logCurrentDraw)
                {
                    DalamudServices.PluginLog.Information(
                        "[IconBrowser.Diagnostics] Scan begin: Tab={Tab}, Icons={IconCount}, Range={Range}, NextIcon={NextIcon}, Complete={Complete}.",
                        i,
                        cache.Icons.Count,
                        cache.RangeIndex,
                        cache.NextIcon,
                        cache.IsComplete);
                }

                ScanTab(GameIconTabs[i], cache);
                if (logCurrentDraw)
                {
                    DalamudServices.PluginLog.Information(
                        "[IconBrowser.Diagnostics] Scan end: Tab={Tab}, Icons={IconCount}, Range={Range}, NextIcon={NextIcon}, Complete={Complete}.",
                        i,
                        cache.Icons.Count,
                        cache.RangeIndex,
                        cache.NextIcon,
                        cache.IsComplete);
                }

                DrawIconGrid(
                    i,
                    cache.Icons,
                    iconSize,
                    GameIconTabs[i].StatusIcons);
            }

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private IconTabCache GetTabCache(int tabIndex)
    {
        if (tabCaches.TryGetValue(tabIndex, out var cache))
        {
            return cache;
        }

        cache = new IconTabCache(GameIconTabs[tabIndex].Ranges[0].Start);
        tabCaches.Add(tabIndex, cache);
        return cache;
    }

    private static void ScanTab(IconTabDefinition tab, IconTabCache cache)
    {
        var remaining = ScanBudgetPerFrame;
        var deadline = Stopwatch.GetTimestamp() + ScanBudgetTicks;
        while (remaining > 0 && !cache.IsComplete && Stopwatch.GetTimestamp() < deadline)
        {
            var range = tab.Ranges[cache.RangeIndex];
            if (cache.NextIcon >= range.End)
            {
                cache.RangeIndex++;
                if (cache.RangeIndex >= tab.Ranges.Length)
                {
                    cache.IsComplete = true;
                    continue;
                }

                cache.NextIcon = tab.Ranges[cache.RangeIndex].Start;
                continue;
            }

            if (IconExists((uint)cache.NextIcon))
            {
                cache.Icons.Add(cache.NextIcon);
            }

            cache.NextIcon++;
            remaining--;
        }
    }

    private static bool IconExists(uint icon)
    {
        var folder = icon / 1000;
        return DalamudServices.DataManager.FileExists($"ui/icon/{folder:D3}000/{icon:D6}.tex")
            || DalamudServices.DataManager.FileExists($"ui/icon/{folder:D3}000/en/{icon:D6}.tex");
    }

    private void DrawIconGrid(
        int tabIndex,
        List<int> icons,
        float iconSize,
        bool statusIcons)
    {
        var spacing = ImGui.GetStyle().ItemSpacing;
        var contentWidth = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X;
        if (!float.IsFinite(contentWidth) || !float.IsFinite(iconSize) || iconSize <= 0f)
        {
            return;
        }

        var columns = Math.Max(1, (int)(contentWidth / (iconSize + spacing.X)));
        var rows = icons.Count == 0 ? 0 : (icons.Count - 1) / columns + 1;
        if (columns != lastLoggedColumns)
        {
            logCurrentDraw = true;
            lastLoggedColumns = columns;
        }

        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information(
                "[IconBrowser.Diagnostics] Grid begin: Tab={Tab}, Window={WindowWidth}x{WindowHeight}, ContentWidth={ContentWidth}, Columns={Columns}, Rows={Rows}, Icons={IconCount}, IconSize={IconSize}.",
                tabIndex,
                diagnosticWindowSize.X,
                diagnosticWindowSize.Y,
                contentWidth,
                columns,
                rows,
                icons.Count,
                iconSize);
        }

        if (!ImGui.BeginChild($"##iconBrowserGrid{tabIndex}"))
        {
            if (logCurrentDraw)
            {
                DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Grid child skipped: Tab={Tab}.", tabIndex);
            }

            ImGui.EndChild();
            return;
        }

        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information(
                "[IconBrowser.Diagnostics] Grid child begin: Tab={Tab}, Child={ChildWidth}x{ChildHeight}, ScrollY={ScrollY}, ScrollMaxY={ScrollMaxY}.",
                tabIndex,
                ImGui.GetWindowSize().X,
                ImGui.GetWindowSize().Y,
                ImGui.GetScrollY(),
                ImGui.GetScrollMaxY());
        }

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rows, iconSize + spacing.Y);
        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information(
                "[IconBrowser.Diagnostics] Clipper begin: Tab={Tab}, Rows={Rows}, RowHeight={RowHeight}.",
                tabIndex,
                rows,
                iconSize + spacing.Y);
        }

        while (clipper.Step())
        {
            var displayStart = Math.Max(0, clipper.DisplayStart);
            var displayEnd = Math.Min(rows, clipper.DisplayEnd);
            if (logCurrentDraw)
            {
                DalamudServices.PluginLog.Information(
                    "[IconBrowser.Diagnostics] Clipper step: Tab={Tab}, DisplayStart={DisplayStart}, DisplayEnd={DisplayEnd}.",
                    tabIndex,
                    displayStart,
                    displayEnd);
            }

            for (var row = displayStart; row < displayEnd; row++)
            {
                var startIndex = row * columns;
                var endIndex = Math.Min(startIndex + columns, icons.Count);
                if (logCurrentDraw)
                {
                    DalamudServices.PluginLog.Information(
                        "[IconBrowser.Diagnostics] Row begin: Tab={Tab}, Row={Row}, StartIndex={StartIndex}, EndIndex={EndIndex}, FirstIcon={FirstIcon}, LastIcon={LastIcon}.",
                        tabIndex,
                        row,
                        startIndex,
                        endIndex,
                        icons[startIndex],
                        icons[endIndex - 1]);
                }

                for (var index = startIndex; index < endIndex; index++)
                {
                    DrawGameIcon(icons[index], iconSize, statusIcons);
                    if (index < endIndex - 1)
                    {
                        ImGui.SameLine();
                    }
                }
            }
        }

        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Clipper end: Tab={Tab}.", tabIndex);
        }

        clipper.End();
        clipper.Destroy();
        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Grid body complete; entering EndChild: Tab={Tab}.", tabIndex);
        }

        ImGui.EndChild();
        if (logCurrentDraw)
        {
            DalamudServices.PluginLog.Information("[IconBrowser.Diagnostics] Grid end complete: Tab={Tab}.", tabIndex);
        }
    }

    private void DrawGameIcon(int icon, float iconSize, bool statusIcon)
    {
        DalamudServices.TextureProvider.TryGetFromGameIcon(
            new GameIconLookup((uint)icon),
            out var sharedTexture);
        var texture = sharedTexture?.GetWrapOrDefault();
        var itemSize = new Vector2(iconSize);
        if (texture is null)
        {
            ImGui.Dummy(itemSize);
        }
        else if (statusIcon)
        {
            var position = ImGui.GetCursorScreenPos();
            var statusIconSize = OmniTheme.StatusIconSize(iconSize);
            var iconPosition = position + new Vector2((itemSize.X - statusIconSize.X) * 0.5f, 0f);
            ImGui.InvisibleButton($"##statusIcon{icon}", itemSize);
            ImGui.GetWindowDrawList().AddImage(
                texture.Handle,
                iconPosition,
                iconPosition + statusIconSize);
        }
        else
        {
            ImGui.Image(texture.Handle, itemSize);
        }

        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(icon.ToString(CultureInfo.InvariantCulture));
            gameIconApplyTarget?.Invoke((uint)icon);
        }

        if (!ImGui.IsItemHovered())
        {
            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Right) || texture is null)
        {
            ImGui.SetTooltip(icon.ToString(CultureInfo.InvariantCulture));
            return;
        }

        var viewportSize = ImGuiHelpers.MainViewport.Size;
        var previewSize = MathF.Max(
            iconSize,
            MathF.Min(
                OmniTheme.Scale(700f),
                MathF.Min(viewportSize.X, viewportSize.Y) - OmniTheme.Scale(48f)));
        ImGui.BeginTooltip();
        ImGui.Image(
            texture.Handle,
            statusIcon
                ? OmniTheme.StatusIconSize(previewSize)
                : OmniTheme.FitImageSize(new Vector2(texture.Width, texture.Height), previewSize));
        ImGui.EndTooltip();
    }

    private void DrawSeIconBrowser(float iconSize)
    {
        EnsureSeIconEntries();
        if (!ImGui.BeginChild("##iconBrowserSeIcons", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable(
                "##iconBrowserSeIconTable",
                4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                OmniLoc.Get("IconBrowser.Column.Preview"),
                ImGuiTableColumnFlags.WidthFixed,
                MathF.Max(OmniTheme.Scale(46f), iconSize + OmniTheme.Scale(12f)));
            ImGui.TableSetupColumn(OmniLoc.Get("IconBrowser.Column.Name"), ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn(OmniLoc.Get("IconBrowser.Column.Value"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(72f));
            ImGui.TableSetupColumn(OmniLoc.Get("IconBrowser.Column.Hex"), ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(72f));
            ImGui.TableHeadersRow();

            var filter = seIconFilter.Trim();
            foreach (var entry in seIconEntries!)
            {
                if (filter.Length > 0 &&
                    !entry.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Button($"{entry.IconText}##seIcon{entry.Value}", new Vector2(iconSize)))
                {
                    ApplySeIcon(entry);
                }

                ImGui.TableNextColumn();
                if (ImGui.Selectable(entry.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                {
                    ApplySeIcon(entry);
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.DecimalValue);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.HexValue);
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private void EnsureSeIconEntries()
    {
        if (seIconEntries is not null)
        {
            return;
        }

        seIconEntries = [];
        foreach (var icon in Enum.GetValues<SeIconChar>())
        {
            var value = (int)icon;
            var name = icon.ToString();
            var decimalValue = value.ToString(CultureInfo.InvariantCulture);
            var hexValue = $"0x{value:X}";
            seIconEntries.Add(new(
                name,
                icon.ToIconString(),
                value,
                decimalValue,
                hexValue,
                $"{name}\n{decimalValue}\n{hexValue}"));
        }
    }

    private void ApplySeIcon(SeIconEntry entry)
    {
        ImGui.SetClipboardText(entry.Name);
        seIconApplyTarget?.Invoke(entry.Name);
    }

    private void Close()
    {
        IsOpen = false;
        lastWindowSize = Vector2.Zero;
        diagnosticWindowSize = Vector2.Zero;
        nextDiagnosticLogTick = 0;
        lastLoggedColumns = -1;
        logCurrentDraw = false;
        ClearApplyTargets();
    }

    private void ClearApplyTargets()
    {
        gameIconApplyTarget = null;
        seIconApplyTarget = null;
    }

    private enum BrowserMode
    {
        GameIcon,
        SeIconChar
    }

    private readonly record struct IconRange(int Start, int End);

    private sealed record IconTabDefinition(string LabelKey, IconRange[] Ranges, bool StatusIcons = false);

    private sealed class IconTabCache(int nextIcon)
    {
        public List<int> Icons { get; } = [];

        public int RangeIndex { get; set; }

        public int NextIcon { get; set; } = nextIcon;

        public bool IsComplete { get; set; }
    }

    private sealed record SeIconEntry(
        string Name,
        string IconText,
        int Value,
        string DecimalValue,
        string HexValue,
        string SearchText);
}
