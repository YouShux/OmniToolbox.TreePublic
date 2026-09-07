using Dalamud.Game.ClientState.Conditions;
using OmenTools;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Info.Game.Enums;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly uint[] DurabilityActionIds = [6, 14, 13];
    private static readonly uint[] DarkMatterItemIds = [5594, 5595, 5596, 5597, 5598, 10386, 17837, 33916];

    private bool DrawInventoryWidgetPopup(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.GearsetSwitcher:
                DrawGearsetSwitcherPopup();
                return true;
            case MultiToolbarWidgetType.Durability:
                DrawDurabilityPopup();
                return true;
            case MultiToolbarWidgetType.RetainerList:
                DrawRetainerPopup();
                return true;
            case MultiToolbarWidgetType.Currencies:
                DrawCurrenciesPopup();
                return true;
            default:
                return false;
        }
    }

    private string? InventoryWidgetLabel(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.GearsetSwitcher => GetCurrentGearsetLabel(),
        MultiToolbarWidgetType.Durability => GetDurabilityLabel(),
        MultiToolbarWidgetType.RetainerList => OmniLoc.Get("Feature.MultiToolbar.WidgetRetainerList"),
        MultiToolbarWidgetType.Currencies => config.TrackedCurrencyID == 0 ? OmniLoc.Get("Feature.MultiToolbar.WidgetCurrencies") : FormatCurrency(LocalPlayerState.GetItemCount(config.TrackedCurrencyID)),
        _ => null,
    };

    private uint InventoryWidgetGameIcon(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.GearsetSwitcher => GetCurrentGearsetIcon(),
        MultiToolbarWidgetType.Durability => GetDurabilityIcon(),
        MultiToolbarWidgetType.RetainerList => 60560,
        MultiToolbarWidgetType.Currencies => GetCurrencyIcon(config.TrackedCurrencyID),
        _ => 0,
    };

    private unsafe void DrawGearsetSwitcherPopup()
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.GearsetsUnavailable"));
            return;
        }

        DrawCurrentGearsetHeader(module);
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(ScaleToolbar(8f), ScaleToolbar(4f)));
        using var table = ImRaii.Table("##multiToolbarGearsets", 3,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings);
        if (!table)
        {
            return;
        }

        ReadOnlySpan<string> groups = [OmniLoc.Get("Feature.MultiToolbar.GearsetTank"), OmniLoc.Get("Feature.MultiToolbar.GearsetHealer"), OmniLoc.Get("Feature.MultiToolbar.GearsetMelee"), OmniLoc.Get("Feature.MultiToolbar.GearsetRanged"), OmniLoc.Get("Feature.MultiToolbar.GearsetCaster"), OmniLoc.Get("Feature.MultiToolbar.GearsetCrafter"), OmniLoc.Get("Feature.MultiToolbar.GearsetGatherer")];
        for (var column = 0; column < 3; column++)
        {
            ImGui.TableNextColumn();
            ReadOnlySpan<int> order = [0, 1, 2, 3, 4, 6, 5];
            foreach (var group in order)
            {
                if ((group < 2 ? 0 : group < 5 ? 1 : 2) != column)
                {
                    continue;
                }

                var shownHeader = false;
                for (byte id = 0; id < 100; id++)
                {
                    var gearset = module->GetGearset(id);
                    if (gearset == null || !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) ||
                        !LuminaGetter.TryGetRow<ClassJob>(gearset->ClassJob, out var job))
                    {
                        continue;
                    }

                    var category = GetGearsetCategory(job);
                    if (category != group)
                    {
                        continue;
                    }

                    if (!shownHeader)
                    {
                        ImGui.Spacing();
                        var labelWidth = ImGui.CalcTextSize(groups[group]).X;
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - labelWidth) / 2f));
                        ImGui.TextUnformatted(groups[group]);
                        shownHeader = true;
                    }

                    var name = string.IsNullOrWhiteSpace(gearset->NameString) ? LuminaWrapper.GetJobName(gearset->ClassJob) : gearset->NameString;
                    if (DrawGearsetCard(id, name, gearset->ClassJob, gearset->ItemLevel, category, module->CurrentGearsetIndex == id) &&
                        LocalPlayerState.SwitchGearset(id))
                    {
                        ClosePopup();
                    }

                    DrawGearsetContextMenu(id, category);
                }
            }
        }
    }

    private unsafe void DrawCurrentGearsetHeader(RaptureGearsetModule* module)
    {
        if (module->CurrentGearsetIndex < 0)
        {
            return;
        }

        var current = module->GetGearset((byte)module->CurrentGearsetIndex);
        if (current == null)
        {
            return;
        }

        var iconSize = ImGui.GetTextLineHeight() * 3f;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var bisLabel = OmniLoc.Get("BestInSlotGearTitle");
        var bisSize = OmniControls.CompactButtonSize(bisLabel);
        ImGui.SetCursorScreenPos(origin + new Vector2(MathF.Max(0f, width - bisSize.X), 0f));
        if (OmniControls.SmallButton($"{bisLabel}##gearsetBestInSlot", false, bisSize))
        {
            ExecuteCommand("/omni BIS配装");
            ClosePopup();
        }
        ImGui.SetCursorScreenPos(origin);
        if (ImageHelper.GetGameIcon(LuminaWrapper.GetJobIcon(current->ClassJob, ClassJobIconType.Normal)) is { } icon)
        {
            ImGui.Image(icon.Handle, new Vector2(iconSize));
        }
        else
        {
            ImGui.Dummy(new Vector2(iconSize));
        }

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted(EllipsizeToolbarText(GetCurrentGearsetLabel(), width - iconSize - bisSize.X - ScaleToolbar(28f)));
            ImGui.TextUnformatted($"{string.Format(OmniLoc.Get("Feature.MultiToolbar.GearsetLevel"), LocalPlayerState.GetClassJobLevel(current->ClassJob, false))} / {current->ItemLevel}");

            if (DrawGearsetNativeButton("##gearsetRecommend", 13, 2, OmniLoc.Get("Feature.MultiToolbar.GearsetRecommend")))
            {
                AgentModule.Instance()->GetAgentByInternalId(AgentId.RecommendEquip)->Show();
                ClosePopup();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 25) != 0))
            {
                if (DrawGearsetNativeButton("##gearsetGlamour", 13, 3, OmniLoc.Get("Feature.MultiToolbar.LinkGlamour")))
                {
                    ActionManager.Instance()->UseAction(ActionType.GeneralAction, 25);
                }
            }

            ImGui.SameLine();
            if (DrawGearsetNativeButton("##gearsetUpdate", 15, 4, OmniLoc.Get("Feature.MultiToolbar.GearsetUpdate")))
            {
                module->UpdateGearset(module->CurrentGearsetIndex);
            }

            ImGui.SameLine();
            if (DrawGearsetNativeButton("##gearsetCreate", 15, 8, OmniLoc.Get("Feature.MultiToolbar.GearsetCreate")))
            {
                var createdID = module->CreateGearset();
                if (createdID >= 0)
                {
                    LocalPlayerState.SwitchGearset((byte)createdID);
                }
            }

            ImGui.SameLine();
            var candidates = GetRandomGearsetCandidates(module);
            using (ImRaii.Disabled(candidates.Count == 0))
            {
                if (DrawGearsetNativeButton("##gearsetRandom", 15, 3, OmniLoc.Get("Feature.MultiToolbar.GearsetRandom")))
                {
                    LocalPlayerState.SwitchGearset(candidates[Random.Shared.Next(candidates.Count)]);
                }
            }
        }

        ImGui.Spacing();
    }

    private static unsafe List<byte> GetRandomGearsetCandidates(RaptureGearsetModule* module)
    {
        List<byte> candidates = [];
        var current = module->GetGearset((byte)module->CurrentGearsetIndex);
        if (current == null || !LuminaGetter.TryGetRow<ClassJob>(current->ClassJob, out var currentJob) ||
            GetGearsetCategory(currentJob) is < 0 or > 4)
        {
            return candidates;
        }

        var level = LocalPlayerState.GetClassJobLevel(current->ClassJob, false);
        for (byte id = 0; id < 100; id++)
        {
            var candidate = module->GetGearset(id);
            if (id != module->CurrentGearsetIndex && candidate != null &&
                candidate->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) &&
                !candidate->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing) &&
                LuminaGetter.TryGetRow<ClassJob>(candidate->ClassJob, out var job) &&
                GetGearsetCategory(job) is >= 0 and <= 4 &&
                LocalPlayerState.GetClassJobLevel(candidate->ClassJob, false) == level)
            {
                candidates.Add(id);
            }
        }

        return candidates;
    }

    private bool DrawGearsetCard(byte id, string name, byte classJob, short itemLevel, int category, bool selected)
    {
        var theme = ToolbarTheme;
        var padding = ScaleToolbar(2f);
        var height = ImGui.GetTextLineHeight() * 1.7f + padding * 2f;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var clicked = ImGui.InvisibleButton($"##gearset{id}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var tint = category switch
        {
            0 => theme.Primary,
            1 => theme.Success,
            2 or 3 or 4 => theme.Error,
            5 => theme.Secondary,
            _ => theme.Warning,
        };
        var background = Vector4.Lerp(theme.Surface, tint, selected ? 0.5f : hovered ? 0.35f : 0.18f);
        var drawList = ImGui.GetWindowDrawList();
        var radius = MathF.Min(ScaleToolbar(4f), theme.ButtonRadius);
        drawList.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(background), radius);
        drawList.AddRect(origin, origin + new Vector2(width, height), ImGui.GetColorU32(selected ? theme.Accent : theme.Border), radius);
        var iconSize = height - padding * 2f;
        if (ImageHelper.GetGameIcon(LuminaWrapper.GetJobIcon(classJob, ClassJobIconType.Normal)) is { } icon)
        {
            drawList.AddImage(icon.Handle, origin + new Vector2(padding), origin + new Vector2(padding + iconSize));
        }

        var left = origin.X + iconSize + padding * 2f;
        var value = itemLevel.ToString();
        var valueWidth = ImGui.CalcTextSize(value).X;
        var valueX = origin.X + width - valueWidth - padding;
        var textColor = ImGui.GetColorU32(theme.Text);
        drawList.AddText(new Vector2(left, origin.Y + padding), textColor,
            EllipsizeToolbarText(name, MathF.Max(0f, valueX - left - padding)));
        var level = string.Format(OmniLoc.Get("Feature.MultiToolbar.GearsetLevel"), LocalPlayerState.GetClassJobLevel(classJob, false));
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.7f, new Vector2(left, origin.Y + height - ImGui.GetTextLineHeight() * 0.7f - padding), ImGui.GetColorU32(theme.Warning), level);
        drawList.AddText(new Vector2(valueX, origin.Y + (height - ImGui.GetTextLineHeight()) / 2f), textColor, value);
        if (hovered)
        {
            ImGui.SetTooltip(name);
        }

        return clicked;
    }

    private static int GetGearsetCategory(ClassJob job) => job.ClassJobCategory.RowId switch
    {
        30 when job.Role == 1 => 0,
        31 when job.Role != 3 => 1,
        30 when job.Role == 2 => 2,
        30 when job.Role == 3 => 3,
        31 when job.Role == 3 => 4,
        33 => 5,
        32 => 6,
        _ => -1
    };

    private unsafe void DrawGearsetContextMenu(byte id, int category)
    {
        using var popup = ImRaii.ContextPopupItem($"##gearsetContext{id}");
        if (!popup)
        {
            return;
        }

        var module = RaptureGearsetModule.Instance();
        var gearset = module->GetGearset(id);
        var agent = AgentGearSet.Instance();
        if (agent == null || gearset == null)
        {
            return;
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.LinkGlamour"), "", false, UIState.Instance()->IsUnlockLinkUnlocked(15) && TerritoryInfo.Instance()->InSanctuary))
        {
            AgentMiragePrismMiragePlate.Instance()->OpenForGearset(id, gearset->GlamourSetLink, (ushort)AgentCharaCard.Instance()->AddonId);
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.UnlinkGlamour"), "", false, gearset->GlamourSetLink != 0))
        {
            module->LinkGlamourPlate(id, 0);
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.EditPortrait"), "", false, AgentBannerEditor.Instance()->IsActivatable()))
        {
            agent->OpenBannerEditorForGearset(id);
        }

        var previous = -1;
        var next = -1;
        for (byte candidateID = 0; candidateID < 100; candidateID++)
        {
            var candidate = module->GetGearset(candidateID);
            if (candidate == null || !candidate->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) ||
                !LuminaGetter.TryGetRow<ClassJob>(candidate->ClassJob, out var job) || GetGearsetCategory(job) != category)
            {
                continue;
            }

            if (candidateID < id)
            {
                previous = candidateID;
            }
            else if (candidateID > id && next == -1)
            {
                next = candidateID;
            }
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.GearsetMoveUp"), "", false, previous >= 0))
        {
            agent->ReassignGearsetId(id, previous);
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.GearsetMoveDown"), "", false, next >= 0))
        {
            agent->ReassignGearsetId(id, next);
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.GearsetRename")))
        {
            agent->OpenRenameDialog(id);
        }

        if (ImGui.MenuItem(OmniLoc.Get("Feature.MultiToolbar.GearsetDelete"), "", false, module->CurrentGearsetIndex != id))
        {
            agent->OpenDeleteDialog(id);
        }
    }

    private unsafe void DrawDurabilityPopup()
    {
        var manager = InventoryManager.Instance();
        var container = manager is null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.EquipmentUnavailable"));
            return;
        }

        var headerRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var bisLabel = OmniLoc.Get("BestInSlotGearTitle");
        var bisSize = OmniControls.CompactButtonSize(bisLabel);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.Equipment"));
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(headerRight - bisSize.X, ImGui.GetCursorScreenPos().Y));
        if (OmniControls.SmallButton($"{bisLabel}##equipmentBestInSlot", false, bisSize))
        {
            ExecuteCommand("/omni BIS配装");
            ClosePopup();
        }

        Span<int> slots = stackalloc int[container->Size];
        var count = 0;
        for (var index = 0; index < container->Size; index++)
        {
            var item = container->GetInventorySlot(index);
            if (item == null || item->ItemId == 0)
            {
                continue;
            }

            var position = count;
            while (position > 0 && GetItemSpiritbond(container->GetInventorySlot(slots[position - 1])) < GetItemSpiritbond(item))
            {
                slots[position] = slots[position - 1];
                position--;
            }

            slots[position] = index;
            count++;
        }

        for (var index = 0; index < count; index++)
        {
            var item = container->GetInventorySlot(slots[index]);
            if (LuminaGetter.TryGetRow<Item>(item->GetBaseItemId(), out var row))
            {
                DrawInventoryMenuRow($"equipment{slots[index]}", row.Name.ExtractText(), (uint)row.Icon,
                    $"{GetItemDurability(item)}% / {GetItemSpiritbond(item)}%");
            }
        }

        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiToolbar.InventoryActions"));
        uint darkMatter = 0;
        foreach (var itemID in DarkMatterItemIds)
        {
            if (LocalPlayerState.GetItemCount(itemID) > 0)
            {
                darkMatter = itemID;
            }
        }

        foreach (var actionId in DurabilityActionIds)
        {
            if (!LuminaGetter.TryGetRow<GeneralAction>(actionId, out var action) || !IsGeneralActionUnlocked(actionId))
            {
                continue;
            }

            var actionManager = ActionManager.Instance();
            var available = actionManager != null && actionManager->GetActionStatus(ActionType.GeneralAction, actionId) == 0 &&
                            (actionId != 6 || darkMatter != 0);
            var detail = actionId == 6 && darkMatter != 0 && LuminaGetter.TryGetRow<Item>(darkMatter, out var matter)
                ? $"{matter.Name.ExtractText()} x {LocalPlayerState.GetItemCount(darkMatter)}"
                : "";
            using var disabled = ImRaii.Disabled(!available);
            if (DrawInventoryMenuRow($"action{actionId}", action.Name.ExtractText(), (uint)action.Icon, detail) && available)
            {
                actionManager->UseAction(ActionType.GeneralAction, actionId);
            }
        }
    }

    private static bool CanReadRetainers() =>
        GameState.HomeWorld != 0 && GameState.CurrentWorld == GameState.HomeWorld &&
        !DService.Instance().Condition[ConditionFlag.BoundByDuty] &&
        !DService.Instance().Condition[ConditionFlag.BoundByDuty56] &&
        !DService.Instance().Condition[ConditionFlag.BoundByDuty95];

    private static unsafe void OnInventoryWidgetPopupOpened(MultiToolbarWidgetType type)
    {
        if (type != MultiToolbarWidgetType.RetainerList || !CanReadRetainers())
        {
            return;
        }

        var manager = RetainerManager.Instance();
        if (manager != null)
        {
            manager->RequestVenturesTimers();
        }
    }

    private static unsafe void OpenRepairWindow()
    {
        var manager = ActionManager.Instance();
        if (manager != null && IsGeneralActionUnlocked(6) && manager->GetActionStatus(ActionType.GeneralAction, 6) == 0)
        {
            manager->UseAction(ActionType.GeneralAction, 6);
        }
    }

    private unsafe void DrawRetainerPopup()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.RetainersLoading"));
            return;
        }

        var rowHeight = MathF.Max(ImGui.GetFrameHeight(), GetPopupRowIconSize()) + ImGui.GetStyle().CellPadding.Y * 2f;
        using var table = ImRaii.Table(
            "##multiToolbarRetainers", 6,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings);
        if (!table)
        {
            return;
        }

        var jobWidth = GetPopupRowIconSize() + ImGui.CalcTextSize("100").X + ImGui.GetStyle().ItemSpacing.X * 2f;
        var ventureWidth = MathF.Max(ImGui.CalcTextSize("00:00:00").X,
            ImGui.CalcTextSize(OmniLoc.Get("Feature.MultiToolbar.RetainerCompleted")).X) + ImGui.GetStyle().CellPadding.X * 2f;
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerJob"), ImGuiTableColumnFlags.WidthFixed, MathF.Max(ScaleToolbar(60f), jobWidth));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerName"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerGil"), ImGuiTableColumnFlags.WidthFixed, ScaleToolbar(108f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerInventory"), ImGuiTableColumnFlags.WidthFixed, ScaleToolbar(44f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerMarket"), ImGuiTableColumnFlags.WidthFixed, ScaleToolbar(44f));
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.MultiToolbar.RetainerVenture"), ImGuiTableColumnFlags.WidthFixed, MathF.Max(ScaleToolbar(80f), ventureWidth));
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerJob"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerName"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerGil"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerInventory"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerMarket"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.MultiToolbar.RetainerVenture"));
        var found = false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (uint index = 0; index < 10; index++)
        {
            var retainer = manager->GetRetainerBySortedIndex(index);
            if (retainer == null || retainer->RetainerId == 0 || !retainer->Available)
            {
                continue;
            }

            found = true;
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();
            if (retainer->ClassJob != 0 && ImageHelper.GetGameIcon(LuminaWrapper.GetJobIcon(retainer->ClassJob)) is { } texture)
            {
                ImGuiOm.TextImage(retainer->Level.ToString(), texture.Handle, new Vector2(GetPopupRowIconSize()));
            }
            else
            {
                OmniControls.TableTextCentered("-", rowHeight);
            }

            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(retainer->NameString, rowHeight);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(FormatCurrency(retainer->Gil), rowHeight);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(retainer->ItemCount.ToString(), rowHeight);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(retainer->MarketItemCount.ToString(), rowHeight);
            ImGui.TableNextColumn();
            var remaining = TimeSpan.FromSeconds(Math.Max(0, retainer->VentureComplete - now));
            var venture = retainer->VentureComplete == 0 ? "-" :
                retainer->VentureComplete <= now ? OmniLoc.Get("Feature.MultiToolbar.RetainerCompleted") :
                $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
            OmniControls.TableTextCentered(venture, rowHeight);
        }

        if (!found)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.RetainersEmpty"));
        }
    }

    private unsafe void DrawCurrenciesPopup()
    {
        DrawCurrencyGroup("", [1, 29, 21072, 20, 21, 22]);
        DrawCurrencyGroup(OmniLoc.Get("Feature.MultiToolbar.CurrencyHunt"), [27, 10307, 26533]);
        OmniControls.SectionLabel(OmniLoc.Get("Feature.MultiToolbar.CurrencyTomestones"));
        foreach (var tomestone in LuminaGetter.Get<TomestonesItem>())
        {
            if (tomestone.Tomestones.RowId is 2 or 3)
            {
                DrawCurrencyRow(tomestone.Item.RowId, tomestone.Tomestones.RowId == 3);
            }
        }

        DrawCurrencyRow(28);
        DrawCurrencyGroup("PvP", [25, 36656]);
        DrawCurrencyGroup(OmniLoc.Get("Feature.MultiToolbar.CurrencyCraftingGathering"), [33913, 33914, 41784, 41785, 28063]);
        DrawCurrencyGroup(OmniLoc.Get("Feature.MultiToolbar.CurrencyOther"), [26807]);
    }

    private void DrawCurrencyGroup(string title, ReadOnlySpan<uint> itemIds)
    {
        if (!string.IsNullOrEmpty(title))
        {
            OmniControls.SectionLabel(title);
        }

        foreach (var itemID in itemIds)
        {
            if (itemID is >= 20 and <= 22 && itemID - 19 != (uint)LocalPlayerState.GrandCompany)
            {
                continue;
            }

            DrawCurrencyRow(itemID);
        }
    }

    private unsafe void DrawCurrencyRow(uint itemID, bool weekly = false)
    {
        if (!LuminaGetter.TryGetRow<Item>(itemID, out var item))
        {
            return;
        }

        var count = LocalPlayerState.GetItemCount(itemID);
        var cap = itemID == 1 ? 0 : item.StackSize;
        if (itemID is >= 20 and <= 22 &&
            LuminaGetter.TryGetRow<GrandCompanyRank>(PlayerState.Instance()->GetGrandCompanyRank(), out var rank))
        {
            cap = rank.MaxSeals;
        }

        var amount = cap == 0 ? FormatCurrency(count) : $"{count:N0} / {cap:N0}";
        if (weekly && InventoryManager.Instance() is not null)
        {
            amount += $" ({InventoryManager.Instance()->GetWeeklyAcquiredTomestoneCount():N0} / {InventoryManager.GetLimitedTomestoneWeeklyLimit():N0})";
        }

        if (DrawInventoryMenuRow($"currency{itemID}", item.Name.ExtractText(), (uint)item.Icon, amount,
                config.TrackedCurrencyID == itemID, drawFrame: !ItemDataService.IsCurrency(item)))
        {
            config.TrackedCurrencyID = config.TrackedCurrencyID == itemID ? 0 : itemID;
            saveConfig();
        }
    }

    private bool DrawInventoryMenuRow(string id, string label, uint iconID, string value, bool selected = false, bool drawFrame = true)
    {
        using var scope = ImRaii.PushId(id);
        var height = MathF.Max(ImGui.GetFrameHeight(), GetPopupRowIconSize());
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var clicked = ImGui.Selectable("##row", selected, ImGuiSelectableFlags.DontClosePopups, new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();
        var left = origin.X;
        if (iconID != 0)
        {
            var iconSize = GetPopupRowIconSize();
            FramedGameIcon.Draw(drawList, iconID, new Vector2(left, origin.Y + (height - iconSize) / 2),
                new Vector2(iconSize), drawFrame: drawFrame);
            left += iconSize + ImGui.GetStyle().ItemInnerSpacing.X;
        }

        var textY = origin.Y + (height - ImGui.GetTextLineHeight()) / 2;
        var color = ImGui.GetColorU32(ImGuiCol.Text);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var valueX = origin.X + width - valueWidth;
        drawList.PushClipRect(new Vector2(left, origin.Y), new Vector2(valueX - ImGui.GetStyle().ItemSpacing.X, origin.Y + height), true);
        drawList.AddText(new Vector2(left, textY), color, label);
        drawList.PopClipRect();
        drawList.AddText(new Vector2(valueX, textY), color, value);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(label);
        }

        return clicked;
    }

    private static string GetCurrentGearsetLabel()
    {
        unsafe
        {
            var module = RaptureGearsetModule.Instance();
            if (module is null || module->CurrentGearsetIndex < 0)
            {
                return OmniLoc.Get("Feature.MultiToolbar.WidgetGearsetSwitcher");
            }

            var id = (byte)module->CurrentGearsetIndex;
            var gearset = module->GetGearset(id);
            if (gearset is null)
            {
                return OmniLoc.Get("Feature.MultiToolbar.WidgetGearsetSwitcher");
            }

            var name = gearset->NameString;
            return string.IsNullOrWhiteSpace(name) ? string.Format(OmniLoc.Get("Feature.MultiToolbar.GearsetNumber"), id + 1) : name;
        }
    }

    private static unsafe uint GetCurrentGearsetIcon() =>
        LocalPlayerState.ClassJob == 0 ? 0 : LuminaWrapper.GetJobIcon(LocalPlayerState.ClassJob, ClassJobIconType.Normal);

    private static string GetDurabilityLabel()
    {
        var snapshot = ReadEquipmentSnapshot();
        return $"{snapshot.Durability}% / {snapshot.Spiritbond}%";
    }

    private static uint GetDurabilityIcon()
    {
        var durability = ReadEquipmentSnapshot().Durability;
        return durability <= 10 ? 60074U : durability <= 30 ? 60073U : 61512U;
    }

    private static uint GetCurrencyIcon(uint itemID)
    {
        return LuminaGetter.TryGetRow<Item>(itemID, out var row) ? (uint)row.Icon : 0;
    }

    private static string FormatCurrency(uint value) => value.ToString("N0");

    private static unsafe EquipmentSnapshot ReadEquipmentSnapshot()
    {
        var manager = InventoryManager.Instance();
        var container = manager is null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded)
        {
            return new(0, 0, 0);
        }

        var durability = 200;
        var spiritbond = 0;
        var count = 0;
        for (var index = 0; index < container->Size; index++)
        {
            var item = container->GetInventorySlot(index);
            if (item is null || item->ItemId == 0)
            {
                continue;
            }

            durability = Math.Min(durability, GetItemDurability(item));
            spiritbond = Math.Max(spiritbond, GetItemSpiritbond(item));
            count++;
        }

        return count == 0 ? new(0, 0, 0) : new(durability, spiritbond, count);
    }

    private static unsafe int GetItemDurability(InventoryItem* item) =>
        Math.Clamp(item->Condition * 100 / 30000, 0, 200);

    private static unsafe int GetItemSpiritbond(InventoryItem* item) =>
        Math.Clamp(item->SpiritbondOrCollectability / 100, 0, 100);

    private readonly record struct EquipmentSnapshot(int Durability, int Spiritbond, int Count);
}
