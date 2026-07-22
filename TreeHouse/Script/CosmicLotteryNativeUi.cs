using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.Script;

internal static unsafe class CosmicLotteryNativeUi
{
    private const string HudAddonName = "WKSHud";

    public static bool IsLotteryHudReady() => TryGetHud(out _);

    public static void Draw(CosmicLotteryDriver driver)
    {
        if (!driver.TryGetCurrentEntry(out var entry) ||
            CurrencyManager.Instance()->GetItemCount(entry.CurrencyItemId) < 1_000 ||
            !TryGetHud(out var addon))
        {
            return;
        }

        var position = addon->RootNode->GetNodeState().BottomLeft + OmniTheme.Scale(new Vector2(0f, 8f));
        var displaySize = ImGui.GetIO().DisplaySize;
        position.X = Math.Clamp(position.X, 0f, Math.Max(0f, displaySize.X - OmniTheme.Scale(160f)));
        position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, displaySize.Y - OmniTheme.Scale(48f)));

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.82f);
        using var padding = ImRaii.PushStyle(
            ImGuiStyleVar.WindowPadding,
            OmniTheme.Scale(new Vector2(6f, 4f)));
        using var border = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 0f);
        var windowOpen = ImGui.Begin(
            "##OmniCosmicLotteryVoidOverlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus);
        try
        {
            if (!windowOpen)
            {
                return;
            }

            using var disabled = ImRaii.Disabled(driver.IsBusy);
            if (ImGui.Button($"{OmniLoc.Get("Feature.CosmicLottery.VoidPull")}##cosmicLotteryVoidPull"))
            {
                driver.QueueManual(entry);
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    public static void ExecuteLottery(uint lotteryType)
    {
        var manager = ExecuteCommandManager.Instance();
        manager.ExecuteCommand(ExecuteCommandFlag.StartWKSLottery, lotteryType);
        manager.ExecuteCommand(ExecuteCommandFlag.ChooseWKSLotteryType, lotteryType);
        manager.ExecuteCommand(ExecuteCommandFlag.FinishWKSLottery, lotteryType);
    }

    private static bool TryGetHud(out AtkUnitBase* addon)
    {
        addon = null;
        return AddonHelper.TryGetByName(HudAddonName, out addon) &&
               addon != null &&
               addon->IsVisible &&
               addon->IsAddonAndNodesReady() &&
               addon->RootNode != null &&
               addon->RootNode->GetNodeState().Visible;
    }
}
