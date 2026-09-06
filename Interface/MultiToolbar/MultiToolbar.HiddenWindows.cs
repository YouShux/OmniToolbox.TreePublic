using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private const string HiddenWindowDtrTitle = "OmniToolbox.Toolbar.HiddenWindows";
    private bool hiddenWindowManagerOpen;
    private bool pickingHiddenWindow;
    private string hiddenWindowFilter = string.Empty;
    private long nextHiddenWindowRefresh;
    private readonly HashSet<string> revealedWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> appliedHiddenWindows = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterHiddenWindowEntry(FeatureLifetime lifetime)
    {
        var entry = DService.Instance().DTRBar.Get(HiddenWindowDtrTitle);
        entry.Text = new SeStringBuilder().AddText(OmniLoc.Get("Feature.MultiToolbar.HiddenWindows")).Build();
        entry.OnClick = click =>
        {
            if (click.ClickType == MouseClickType.Left)
            {
                hiddenWindowManagerOpen = !hiddenWindowManagerOpen;
            }
        };
        entry.Shown = true;
        lifetime.Add(() => DService.Instance().DTRBar.Remove(HiddenWindowDtrTitle));
    }

    private void DrawHiddenWindowManager()
    {
        DrawHiddenWindowPicker();
        if (hiddenWindowManagerOpen)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowSize(Vector2.Min(OmniTheme.Scale(new Vector2(520f, 360f)), viewport.WorkSize), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(OmniLoc.Get("Feature.MultiToolbar.HiddenWindows") + "###OmniToolbarHiddenWindows",
                    ref hiddenWindowManagerOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground))
            {
                OmniControls.DrawWindowBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize(), false);
                var pickLabel = OmniLoc.Get("Feature.MultiDock.WindowSource.Pick");
                var pickSize = OmniControls.CompactButtonSize(pickLabel);
                var inputWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - pickSize.X - ImGui.GetStyle().ItemSpacing.X);
                OmniControls.InputTextWithHint("##hiddenFilter", OmniLoc.Get("Feature.MultiDock.Filter"),
                    ref hiddenWindowFilter, 128, inputWidth);
                ImGui.SameLine();
                if (OmniControls.SmallButton(pickLabel, pickingHiddenWindow, pickSize))
                {
                    pickingHiddenWindow = !pickingHiddenWindow;
                }
                OmniControls.HelpTooltip(OmniLoc.Get("Feature.MultiDock.WindowSource.Pick.Help"));
                using var child = ImRaii.Child("##hiddenWindowRows", Vector2.Zero, true);
                if (child)
                {
                    if (config.HiddenWindows.Count == 0)
                    {
                        ImGui.TextWrapped(OmniLoc.Get("Feature.MultiToolbar.HiddenWindowsEmpty"));
                    }
                    string? remove = null;
                    foreach (var name in config.HiddenWindows.Order(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!name.Contains(hiddenWindowFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        using var id = ImRaii.PushId(name);
                        var buttonSize = new Vector2(OmniTheme.CheckboxSize());
                        var start = ImGui.GetCursorScreenPos();
                        var width = ImGui.GetContentRegionAvail().X;
                        if (OmniControls.IconButton("reveal", revealedWindows.Contains(name) ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash,
                                false, buttonSize, OmniLoc.Get("Feature.MultiToolbar.ToggleHiddenWindow")))
                        {
                            nextHiddenWindowRefresh = 0;
                            if (!revealedWindows.Add(name))
                            {
                                revealedWindows.Remove(name);
                            }
                        }
                        ImGui.SameLine();
                        var labelWidth = MathF.Max(1f, width - buttonSize.X * 2f - ImGui.GetStyle().ItemSpacing.X * 2f);
                        if (ImGui.Selectable(EllipsizeToolbarText(name, labelWidth) + "##window", revealedWindows.Contains(name),
                                ImGuiSelectableFlags.None, new Vector2(labelWidth, buttonSize.Y)))
                        {
                            nextHiddenWindowRefresh = 0;
                            if (!revealedWindows.Add(name))
                            {
                                revealedWindows.Remove(name);
                            }
                        }
                        OmniControls.HelpTooltip(name);
                        ImGui.SameLine();
                        ImGui.SetCursorScreenPos(new Vector2(start.X + width - buttonSize.X, start.Y));
                        if (OmniControls.IconButton("remove", FontAwesomeIcon.Trash, false, buttonSize,
                                OmniLoc.Get("Feature.MultiToolbar.RemoveHiddenWindow")))
                        {
                            remove = name;
                        }
                    }
                    if (remove is not null)
                    {
                        config.HiddenWindows.Remove(remove);
                        nextHiddenWindowRefresh = 0;
                        revealedWindows.Remove(remove);
                        saveConfig();
                    }
                }
            }
            ImGui.End();
        }
        if (Environment.TickCount64 >= nextHiddenWindowRefresh)
        {
            ApplyHiddenWindows();
            nextHiddenWindowRefresh = Environment.TickCount64 + 250;
        }
    }

    private unsafe void DrawHiddenWindowPicker()
    {
        if (!pickingHiddenWindow)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            pickingHiddenWindow = false;
            return;
        }
        var context = ImGuiNative.GetCurrentContext();
        if (context == null)
        {
            return;
        }
        var window = new ImGuiContextPtr(context).HoveredWindow;
        while (window.Handle != null && (window.Flags & ImGuiWindowFlags.ChildWindow) != 0 && window.ParentWindow.Handle != null)
        {
            window = window.ParentWindow;
        }
        if (window.Handle == null || window.Name == null ||
            (window.Flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0)
        {
            return;
        }
        var name = NormalizeHiddenWindowName(Marshal.PtrToStringUTF8((nint)window.Name));
        if (!CanManageHiddenWindow(name))
        {
            return;
        }
        ImGui.GetForegroundDrawList().AddRect(window.Pos, window.Pos + window.Size,
            ImGui.GetColorU32(OmniTheme.ControlAccent), 0f, ImDrawFlags.None, OmniTheme.Scale(2f));
        ImGui.SetTooltip(name);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            config.HiddenWindows.Add(name);
            nextHiddenWindowRefresh = 0;
            revealedWindows.Remove(name);
            pickingHiddenWindow = false;
            saveConfig();
        }
    }

    private static string NormalizeHiddenWindowName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        var separator = name.IndexOf("###", StringComparison.Ordinal);
        return separator >= 0 ? name[separator..] : name;
    }

    private static bool CanManageHiddenWindow(string name) => !string.IsNullOrWhiteSpace(name) &&
        !name.Contains("Omni", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("##multiToolbar", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("##multiDock", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("Debug##Default", StringComparison.Ordinal);

    private unsafe void ApplyHiddenWindows(bool restore = false)
    {
        if (config.HiddenWindows.Count == 0 && appliedHiddenWindows.Count == 0)
        {
            return;
        }
        var context = ImGuiNative.GetCurrentContext();
        if (context == null)
        {
            return;
        }
        ref var windows = ref new ImGuiContextPtr(context).Windows;
        for (var index = 0; index < windows.Size; index++)
        {
            var window = windows[index];
            if (window.Handle == null || window.Name == null)
            {
                continue;
            }
            var root = window;
            while ((root.Flags & ImGuiWindowFlags.ChildWindow) != 0 && root.ParentWindow.Handle != null)
            {
                root = root.ParentWindow;
            }
            var name = NormalizeHiddenWindowName(Marshal.PtrToStringUTF8((nint)root.Name));
            var hidden = !restore && config.HiddenWindows.Contains(name) && !revealedWindows.Contains(name) && CanManageHiddenWindow(name);
            if (!hidden && !appliedHiddenWindows.Contains(name))
            {
                continue;
            }
            // 与多功能收纳使用同一 ImGui 隐藏字段，不修改第三方窗口的 IsOpen。
            window.Hidden = hidden;
            window.SkipItems = hidden;
            window.HiddenFramesCanSkipItems = hidden ? sbyte.MaxValue : (sbyte)0;
            window.HiddenFramesCannotSkipItems = hidden ? sbyte.MaxValue : (sbyte)0;
            window.HiddenFramesForRenderOnly = hidden ? sbyte.MaxValue : (sbyte)0;
            window.DisableInputsFrames = hidden ? sbyte.MaxValue : (sbyte)0;
            if (hidden)
            {
                ref var drawList = ref window.DrawList;
                drawList.CmdBuffer.Clear();
                drawList.IdxBuffer.Clear();
                drawList.VtxBuffer.Clear();
            }
        }
        appliedHiddenWindows.Clear();
        if (!restore)
        {
            appliedHiddenWindows.UnionWith(config.HiddenWindows.Where(name => !revealedWindows.Contains(name) && CanManageHiddenWindow(name)));
        }
    }

    private void RestoreHiddenWindows()
    {
        ApplyHiddenWindows(true);
        nextHiddenWindowRefresh = 0;
        revealedWindows.Clear();
        hiddenWindowManagerOpen = false;
        pickingHiddenWindow = false;
    }
}
