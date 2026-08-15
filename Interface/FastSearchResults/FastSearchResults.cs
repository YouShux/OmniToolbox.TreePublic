using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class FastSearchResults(
    FastSearchResultsConfig config,
    Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("FastSearchResultsTitle"),
        Description = OmniLoc.Get("FastSearchResultsDescription"),
        Category = ModuleCategory.Interface
    };

    private static readonly CompSig ItemSearchUpdateSignature = new(
        "E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 80 BB ?? ?? ?? ?? ?? 0F 85");

    private static readonly CompSig ItemSearchPushFoundItemsSignature = new(
        "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 89 9C 24 ?? ?? ?? ?? 41 2B C9");

    private static readonly CompSig RecipeSearchIterateSignature = new(
        "80 B9 ?? ?? ?? ?? ?? 74 27 8B 81 ?? ?? ?? ?? 41 B8");

    private static readonly CompSig SendInventoryRefreshSignature = new(
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B DA 48 8B F1 33 D2 0F B7 FA");

    private static readonly InventoryType[] FreeCompanyInventories =
    [
        InventoryType.FreeCompanyPage1,
        InventoryType.FreeCompanyPage2,
        InventoryType.FreeCompanyPage3,
        InventoryType.FreeCompanyPage4,
        InventoryType.FreeCompanyPage5,
        InventoryType.FreeCompanyCrystals
    ];

    private readonly byte[] quickSearchLabel = Encoding.UTF8.GetBytes(
        $"{OmniLoc.Get("Feature.FastSearchResults.QuickSearch")}\0");

    private readonly byte[] partialSearchLabel = Encoding.UTF8.GetBytes(
        $"{LuminaWrapper.GetAddonText(3136)}\0");

    private FastSearchIndex? searchIndex;
    private FeatureLifetime? runtimeLifetime;
    private Hook<AgentItemSearchUpdateDelegate>? itemSearchUpdateHook;
    private Hook<RecipeSearchDelegate>? recipeSearchHook;
    private Hook<RecipeSearchIterateDelegate>? recipeSearchIterateHook;
    private Hook<SendInventoryRefreshDelegate>? sendInventoryRefreshHook;
    private AgentItemSearchPushFoundItemsDelegate? pushFoundItems;
    private bool useLocalRecipeResults;
    private bool freeCompanySessionActive;
    private int freeCompanyPreloadIndex;
    private long nextInventoryRequestTick;
    private long defaultPageDeadlineTick;
    private long nextDefaultPageAttemptTick;

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            searchIndex = new();
            pushFoundItems = ItemSearchPushFoundItemsSignature.GetDelegate<AgentItemSearchPushFoundItemsDelegate>();

            itemSearchUpdateHook = ItemSearchUpdateSignature.GetHook<AgentItemSearchUpdateDelegate>(OnItemSearchUpdate);
            lifetime.Add(itemSearchUpdateHook.Dispose);
            itemSearchUpdateHook.Enable();

            recipeSearchHook = DService.Instance().Hook.HookFromMemberFunction(
                typeof(AgentRecipeNote.MemberFunctionPointers),
                nameof(AgentRecipeNote.MemberFunctionPointers.SearchRecipe),
                (RecipeSearchDelegate)OnRecipeSearch);
            lifetime.Add(recipeSearchHook.Dispose);
            recipeSearchHook.Enable();

            recipeSearchIterateHook = RecipeSearchIterateSignature.GetHook<RecipeSearchIterateDelegate>(OnRecipeSearchIterate);
            lifetime.Add(recipeSearchIterateHook.Dispose);
            recipeSearchIterateHook.Enable();

            sendInventoryRefreshHook = SendInventoryRefreshSignature.GetHook<SendInventoryRefreshDelegate>(OnSendInventoryRefresh);
            lifetime.Add(sendInventoryRefreshHook.Dispose);
            sendInventoryRefreshHook.Enable();

            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, "ItemSearch", OnItemSearchAddon);
            addonEvents.Register(AddonEvent.PostRefresh, "ItemSearch", OnItemSearchAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, "ItemSearch", OnItemSearchAddon);
            addonEvents.Register(AddonEvent.PostSetup, "FreeCompanyChest", OnFreeCompanyChestAddon);
            addonEvents.Register(AddonEvent.PreFinalize, "FreeCompanyChest", OnFreeCompanyChestAddon);

            DalamudServices.PluginInterface.UiBuilder.Draw += DrawFreeCompanyDefaultPage;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawFreeCompanyDefaultPage);
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 50))
            {
                throw new InvalidOperationException("Fast-search update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            useLocalRecipeResults = true;
            runtimeLifetime = lifetime;

            if (AddonHelper.TryGetByName("ItemSearch", out AddonItemSearch* itemSearchAddon))
            {
                UpdateItemSearchLabel(itemSearchAddon);
            }

            if (AddonHelper.TryGetByName("FreeCompanyChest", out AtkUnitBase* freeCompanyAddon) &&
                freeCompanyAddon->IsVisible)
            {
                StartFreeCompanySession();
            }
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                runtimeLifetime = null;
                ClearNativeState();
                searchIndex?.Dispose();
                searchIndex = null;
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            try
            {
                if (AddonHelper.TryGetByName("ItemSearch", out AddonItemSearch* addon))
                {
                    UpdateItemSearchLabel(addon, partialSearchLabel);
                }
            }
            finally
            {
                ClearNativeState();
                searchIndex?.Dispose();
                searchIndex = null;
            }
        }
    }

    private void OnItemSearchUpdate(AgentItemSearch* agent)
    {
        if (agent == null ||
            searchIndex == null ||
            agent->StringData == null ||
            agent->ItemBuffer == null ||
            !agent->IsPartialSearching ||
            agent->IsItemPushPending)
        {
            itemSearchUpdateHook!.Original(agent);
            return;
        }

        try
        {
            agent->ItemCount = (uint)searchIndex.CopyMarketItemIds(
                agent->StringData->SearchParam.ToString(),
                new Span<uint>(agent->ItemBuffer, 100));
            pushFoundItems!(agent);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Fast market-item search failed.");
            itemSearchUpdateHook!.Original(agent);
        }
    }

    private void OnRecipeSearch(AgentRecipeNote* agent, Utf8String* text, byte mode, bool pushHistory)
    {
        if (agent == null || text == null || searchIndex == null || agent->RecipeSearchProcessing)
        {
            recipeSearchHook!.Original(agent, text, mode, pushHistory);
            return;
        }

        List<uint> recipeIds;
        try
        {
            recipeIds = searchIndex.SearchRecipeIds(text->ToString());
            useLocalRecipeResults = true;
        }
        catch (Exception ex)
        {
            useLocalRecipeResults = false;
            DalamudServices.PluginLog.Warning(ex, "Fast recipe search failed.");
            recipeSearchHook!.Original(agent, text, mode, pushHistory);
            return;
        }

        recipeSearchHook!.Original(agent, text, mode, pushHistory);
        if (agent->SearchContext == null)
        {
            useLocalRecipeResults = false;
            return;
        }

        agent->SearchResults.Clear();
        agent->SearchResults.AddRangeCopy(recipeIds);
        agent->SearchContext->CanIterate = false;
        agent->SearchContext->IsComplete = true;
    }

    private void OnRecipeSearchIterate(RecipeSearchContext* context)
    {
        if (!useLocalRecipeResults)
        {
            recipeSearchIterateHook!.Original(context);
        }
    }

    private bool OnSendInventoryRefresh(InventoryManager* manager, int inventoryType)
    {
        var type = (InventoryType)inventoryType;
        if (!IsFreeCompanyInventory(type))
        {
            return sendInventoryRefreshHook!.Original(manager, inventoryType);
        }

        try
        {
            InventoryCommand.Request(type);
            return true;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Fast free-company inventory refresh failed.");
            return sendInventoryRefreshHook!.Original(manager, inventoryType);
        }
    }

    private void OnItemSearchAddon(AddonEvent _, AddonArgs args) =>
        UpdateItemSearchLabel((AddonItemSearch*)args.Addon.Address);

    private void UpdateItemSearchLabel(AddonItemSearch* addon) =>
        UpdateItemSearchLabel(addon, quickSearchLabel);

    private static void UpdateItemSearchLabel(AddonItemSearch* addon, byte[] label)
    {
        var textNode = addon == null || addon->PartialSearchCheckBox == null
            ? null
            : addon->PartialSearchCheckBox->AtkComponentButton.ButtonTextNode;
        if (textNode == null)
        {
            return;
        }

        fixed (byte* labelPointer = label)
        {
            textNode->SetText(labelPointer);
        }
    }

    private void OnFreeCompanyChestAddon(AddonEvent eventType, AddonArgs _)
    {
        if (eventType == AddonEvent.PostSetup)
        {
            StartFreeCompanySession();
        }
        else
        {
            ResetFreeCompanySession();
        }
    }

    private void StartFreeCompanySession()
    {
        var now = Environment.TickCount64;
        freeCompanySessionActive = true;
        freeCompanyPreloadIndex = 0;
        nextInventoryRequestTick = now;
        defaultPageDeadlineTick = GetDefaultFreeCompanyPage() == InventoryType.Invalid
            ? 0
            : now + 2_000;
        nextDefaultPageAttemptTick = now;
    }

    private void ResetFreeCompanySession()
    {
        freeCompanySessionActive = false;
        freeCompanyPreloadIndex = 0;
        nextInventoryRequestTick = 0;
        defaultPageDeadlineTick = 0;
        nextDefaultPageAttemptTick = 0;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!freeCompanySessionActive ||
            !AddonHelper.TryGetByName("FreeCompanyChest", out AtkUnitBase* addon) ||
            !addon->IsVisible)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (freeCompanyPreloadIndex < FreeCompanyInventories.Length && now >= nextInventoryRequestTick)
        {
            try
            {
                InventoryCommand.Request(FreeCompanyInventories[freeCompanyPreloadIndex]);
                freeCompanyPreloadIndex++;
                nextInventoryRequestTick = now + 250;
            }
            catch (Exception ex)
            {
                freeCompanyPreloadIndex = FreeCompanyInventories.Length;
                DalamudServices.PluginLog.Warning(ex, "Fast free-company inventory preload failed.");
            }
        }

        if (defaultPageDeadlineTick == 0)
        {
            return;
        }

        var defaultPage = GetDefaultFreeCompanyPage();
        if (defaultPage == InventoryType.Invalid ||
            TryGetCurrentFreeCompanyPage(addon, out var currentPage) && currentPage == defaultPage ||
            now >= defaultPageDeadlineTick)
        {
            defaultPageDeadlineTick = 0;
            return;
        }

        if (now < nextDefaultPageAttemptTick)
        {
            return;
        }

        TrySelectFreeCompanyPage(addon, defaultPage);
        nextDefaultPageAttemptTick = now + 120;
    }

    private void DrawFreeCompanyDefaultPage()
    {
        if (!freeCompanySessionActive ||
            !AddonHelper.TryGetByName("FreeCompanyChest", out AtkUnitBase* addon) ||
            !addon->IsVisible)
        {
            return;
        }

        var hasCurrentPage = TryGetCurrentFreeCompanyPage(addon, out var currentPage);
        var isDefaultPage = hasCurrentPage && currentPage == GetDefaultFreeCompanyPage();
        ImGui.SetNextWindowPos(
            new(addon->X + OmniTheme.Scale(240f), addon->Y + OmniTheme.Scale(25f)),
            ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        try
        {
            var flags = ImGuiWindowFlags.NoDecoration |
                        ImGuiWindowFlags.NoSavedSettings |
                        ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoNav |
                        ImGuiWindowFlags.NoScrollbar |
                        ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoFocusOnAppearing |
                        ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.NoBackground |
                        ImGuiWindowFlags.AlwaysAutoResize;
            if (!ImGui.Begin("##fastSearchFreeCompanyDefaultPage", flags))
            {
                ImGui.End();
                return;
            }

            try
            {
                if (OmniControls.Checkbox("##fastSearchFreeCompanyDefaultPageToggle", ref isDefaultPage))
                {
                    config.DefaultFreeCompanyPage = isDefaultPage && hasCurrentPage
                        ? currentPage
                        : InventoryType.Invalid;
                    saveConfig();
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.FastSearchResults.DefaultPage"));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(OmniLoc.Get("Feature.FastSearchResults.DefaultPage.Help"));
                }
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private InventoryType GetDefaultFreeCompanyPage()
    {
        var page = config.DefaultFreeCompanyPage;
        if (page == InventoryType.FreeCompanyCrystals)
        {
            return page;
        }

        var index = (int)page - (int)InventoryType.FreeCompanyPage1;
        return index is >= 0 and <= 4 ? page : InventoryType.Invalid;
    }

    private static bool TryGetCurrentFreeCompanyPage(AtkUnitBase* addon, out InventoryType page)
    {
        page = InventoryType.Invalid;
        if (addon == null || addon->AtkValues == null || addon->AtkValuesCount < 3)
        {
            return false;
        }

        page = addon->AtkValues[1].UInt != 0
            ? InventoryType.FreeCompanyCrystals
            : (InventoryType)((int)InventoryType.FreeCompanyPage1 + addon->AtkValues[2].UInt);
        return IsFreeCompanyInventory(page);
    }

    private static bool TrySelectFreeCompanyPage(AtkUnitBase* addon, InventoryType page)
    {
        var button = addon == null
            ? null
            : (AtkComponentRadioButton*)addon->GetComponentByNodeId(
                page == InventoryType.FreeCompanyCrystals
                    ? 15u
                    : (uint)(10 + (int)page - (int)InventoryType.FreeCompanyPage1));
        var ownerNode = button == null ? null : button->AtkComponentButton.AtkComponentBase.OwnerNode;
        var eventData = ownerNode == null ? null : ownerNode->AtkResNode.AtkEventManager.Event;
        if (eventData == null)
        {
            return false;
        }

        addon->ReceiveEvent(eventData->State.EventType, (int)eventData->Param, eventData);
        return true;
    }

    private static bool IsFreeCompanyInventory(InventoryType type)
    {
        if (type == InventoryType.FreeCompanyCrystals)
        {
            return true;
        }

        var index = (int)type - (int)InventoryType.FreeCompanyPage1;
        return index is >= 0 and <= 4;
    }

    private void ClearNativeState()
    {
        ResetFreeCompanySession();
        useLocalRecipeResults = false;
        pushFoundItems = null;
        itemSearchUpdateHook = null;
        recipeSearchHook = null;
        recipeSearchIterateHook = null;
        sendInventoryRefreshHook = null;
    }

    private delegate void AgentItemSearchUpdateDelegate(AgentItemSearch* agent);

    private delegate void AgentItemSearchPushFoundItemsDelegate(AgentItemSearch* agent);

    private delegate void RecipeSearchDelegate(
        AgentRecipeNote* agent,
        Utf8String* text,
        byte mode,
        bool pushHistory);

    private delegate void RecipeSearchIterateDelegate(RecipeSearchContext* context);

    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool SendInventoryRefreshDelegate(InventoryManager* manager, int inventoryType);
}

[Serializable]
public sealed class FastSearchResultsConfig
{
    public InventoryType DefaultFreeCompanyPage { get; set; } = InventoryType.Invalid;
}
