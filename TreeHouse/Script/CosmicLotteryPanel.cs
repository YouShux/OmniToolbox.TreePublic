using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.Script;

internal static class CosmicLotteryPanel
{
    private static string targetItemIdInput = string.Empty;

    public static bool Draw(CosmicLotteryConfig config)
    {
        var changed = false;
        var threshold = config.Threshold;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.CosmicLottery.Threshold"));
        ImGui.SameLine(0f, OmniTheme.Scale(10f));
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (OmniControls.InputInt("##cosmicLotteryThreshold", ref threshold, 1_000, 1_000))
        {
            config.Threshold = Math.Max(1_000, threshold);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(0f, OmniTheme.Scale(20f));

        var delayMs = config.DelayMs;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.CosmicLottery.DelayMs"));
        ImGui.SameLine(0f, OmniTheme.Scale(10f));
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (OmniControls.InputInt("##cosmicLotteryDelayMs", ref delayMs, 10, 10))
        {
            config.DelayMs = Math.Max(0, delayMs);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.CosmicLottery.TargetItemIds"));
        ImGui.SetNextItemWidth(OmniTheme.Scale(260f));
        OmniControls.InputText("##cosmicLotteryTargetItemIdInput", ref targetItemIdInput, 256);
        ImGui.SameLine();
        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.CosmicLottery.Add")}##cosmicLotteryAdd", false))
        {
            changed |= AddTargetItemIds(config);
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.CosmicLottery.Clear")}##cosmicLotteryClear", false))
        {
            config.TargetItemIds.Clear();
            targetItemIdInput = string.Empty;
            changed = true;
        }

        return DrawTargetItemTable(config) || changed;
    }

    public static void Normalize(CosmicLotteryConfig config)
    {
        config.Threshold = Math.Max(1_000, config.Threshold);
        config.DelayMs = Math.Max(0, config.DelayMs);
        config.TargetItemIds.Sort();
        for (var index = config.TargetItemIds.Count - 1; index > 0; index--)
        {
            if (config.TargetItemIds[index] == config.TargetItemIds[index - 1])
            {
                config.TargetItemIds.RemoveAt(index);
            }
        }
    }

    private static bool DrawTargetItemTable(CosmicLotteryConfig config)
    {
        using var table = ImRaii.Table(
            "##cosmicLotteryTargetItems",
            3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.CosmicLottery.ItemId"));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.CosmicLottery.ItemName"));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.CosmicLottery.Action"));
        ImGui.TableHeadersRow();
        for (var index = 0; index < config.TargetItemIds.Count; index++)
        {
            var itemId = config.TargetItemIds[index];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(itemId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(LuminaWrapper.GetItemName(itemId));
            ImGui.TableNextColumn();
            if (!OmniControls.SmallButton($"{OmniLoc.Get("Feature.CosmicLottery.Remove")}##cosmicLotteryRemove{itemId}", false))
            {
                continue;
            }

            config.TargetItemIds.RemoveAt(index);
            return true;
        }

        return false;
    }

    private static bool AddTargetItemIds(CosmicLotteryConfig config)
    {
        var added = false;
        foreach (var value in targetItemIdInput.Split(
                     [',', ';', '\r', '\n', ' ', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(value, out var itemId) ||
                itemId == 0 ||
                config.TargetItemIds.Contains(itemId))
            {
                continue;
            }

            config.TargetItemIds.Add(itemId);
            added = true;
        }

        if (!added)
        {
            return false;
        }

        config.TargetItemIds.Sort();
        targetItemIdInput = string.Empty;
        return true;
    }
}
