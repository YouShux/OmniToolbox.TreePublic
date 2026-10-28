using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using AgentWeeklyBingo = FFXIVClientStructs.Expansion.FFXIV.Client.UI.Agent.AgentWeeklyBingo;
using DutySlot = FFXIVClientStructs.Expansion.FFXIV.Client.UI.DutySlot;
using NativeDutySlot = FFXIVClientStructs.FFXIV.Client.UI.DutySlot;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterWondrousTails
{
    private void OnContentListEvent(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PostSetup)
        {
            centerContentListPending = config.OpenDuty;
            return;
        }

        if (eventType == AddonEvent.PreFinalize || !config.OpenDuty)
        {
            centerContentListPending = false;
            return;
        }

        if (!centerContentListPending || args.Addon.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        var stage = AtkStage.Instance();
        if (!addon->IsReady || stage == null)
            return;

        var screenSize = (Vector2)stage->ScreenSize;
        if (stage->IsScreenSizeScaled)
            screenSize *= stage->ScreenSizeScale;
        var windowSize = new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true));
        if (screenSize.X <= 0f || screenSize.Y <= 0f || windowSize.X <= 0f || windowSize.Y <= 0f)
            return;

        var position = Vector2.Max(Vector2.Zero, (screenSize - windowSize) * 0.5f);
        addon->SetPosition((short)MathF.Round(position.X), (short)MathF.Round(position.Y));
        centerContentListPending = false;
    }

    private void DutySlotReceiveEventDetour(
        DutySlot* slot, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* eventData)
    {
        var hook = DutySlotReceiveEventHook;
        if (hook == null)
            return;

        try
        {
            if (config.OpenDuty && slot != null && eventType == AtkEventType.ButtonClick &&
                eventParam == 0 && eventData != null && eventData->MouseData.ButtonId == 0 && TryOpenDuty((NativeDutySlot*)slot))
                return;
        }
        catch (Exception exception)
        {
            DService.Instance().Log.Error(exception, "Failed to open the Wondrous Tails duty.");
        }

        hook.Original(slot, eventType, eventParam, atkEvent, eventData);
    }

    private static bool TryOpenDuty(NativeDutySlot* slot)
    {
        if ((uint)slot->index >= 16 || DService.Instance().Condition.IsBoundByDuty)
            return false;

        var addon = AddonHelper.GetByName(ADDON_NAME);
        if (addon == null || (nint)addon != (nint)slot->addon)
            return false;

        var player = PlayerState.Instance();
        if (player == null || !player->HasWeeklyBingoJournal || player->IsWeeklyBingoExpired() ||
            player->GetWeeklyBingoTaskStatus(slot->index) != PlayerState.WeeklyBingoTaskStatus.Open)
            return false;

        var agent = AgentWeeklyBingo.Instance();
        if (agent == null || agent->ContentListAddonID != 0)
            return false;

        var orderID = player->WeeklyBingoOrderData[slot->index];
        if (!LuminaGetter.TryGetRow<WeeklyBingoOrderData>(orderID, out var order))
            return false;

        agent->SelectedDutySlotIndex = (uint)slot->index;
        if (order.Type is 0 or 3 or > 9)
        {
            agent->OpenDutyOrContentList();
            return true;
        }

        var originalListID = agent->ContentListAddonID;
        try
        {
            agent->ContentListAddonID = 1;
            agent->OpenDutyOrContentList();
        }
        finally
        {
            agent->ContentListAddonID = originalListID;
        }

        if (agent->ContentList.Count == 1)
        {
            var finder = AgentContentsFinder.Instance();
            if (finder == null)
                return false;
            finder->OpenRegularDuty(agent->ContentList[0].ContentFinderConditionID);
            return true;
        }

        if (agent->ContentList.Count == 0)
            return false;

        agent->OpenContentList();
        return true;
    }
}
