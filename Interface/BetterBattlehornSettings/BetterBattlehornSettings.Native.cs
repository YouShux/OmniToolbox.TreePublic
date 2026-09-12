using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using OmniToolbox.Host;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterBattlehornSettings
{
    // 国服图鉴右键设置调用：原生检查通过后提交全部三个 XBMPet RowId；不直接写槽位或发送命令。
    private static readonly CompSig SetPetsSignature = new(
        "48 89 5C 24 08 57 48 83 EC 50 45 33 C9 48 8B DA 48 8B F9 45 33 C0");
    private static readonly CompSig GetPetSignature = new(
        "80 FA 03 72 03 32 C0 C3 0F B6 C2 0F B6 84 08 84 01 00 00 C3");
    private static readonly CompSig ReceivePetsSignature = new(
        "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B F1 88 91 84 01 00 00 44 88 81 85 01 00 00 44 88 89 86 01 00 00");
    private static readonly CompSig IsCapturedSignature = new(
        "8D 42 FF 4C 8B C9 83 F8 31 77 24 44 8D 42 FF 41 C1 E8 03 41 83 F8 07 73 16 8D 4A FF BA 01 00 00 00 83 E1 07 D3 E2 43 84 54 08 70 0F 95 C0 C3 32 C0 C3");
    private SetPetsDelegate? setPets;
    private static readonly CompSig PetLevelSignature = new(
        "66 85 D2 74 13 0F B7 C2 FF C8 83 F8 32 73 09 0F B6 84 08 6C 04 00 00 C3 32 C0 C3");
    private static readonly CompSig PetExperienceSignature = new(
        "66 85 D2 74 13 0F B7 C2 FF C8 83 F8 32 73 09 0F B6 84 08 9E 04 00 00 C3 32 C0 C3");
    private GetPetProgressDelegate? getPetLevel;
    private GetPetProgressDelegate? getPetExperience;
    private static readonly CompSig LoadProgressHandlerSignature = new(
        "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 48 89 7C 24 20 41 54 41 56 41 57 48 83 EC 50 8B EA 4D 8B E0 C1 ED 10 8B FA 4C 8B F1");
    private LoadProgressHandlerDelegate? loadProgressHandler;
    private bool progressHandlerLoadAttempted;
    private long nextProgressCheck;
    private long progressDeadline;
    private bool progressRequested;
    private bool progressLoaded;
    private long nextDirectProgressRequest;
    private GetPetDelegate? getPet;
    private IsCapturedDelegate? isCaptured;
    private Hook<ReceivePetsDelegate>? receivePetsHook;
    private byte[]? pendingPets;
    private long pendingDeadline;
    private byte[]? acknowledgedPets;
    private int requestID;
    private nint pendingManager;
    private ulong characterID;
    private string status = string.Empty;

    private void OnUpdate(IFramework _)
    {
        if (!IsRunning)
        {
            return;
        }
        var id = DService.Instance().ObjectTable.LocalPlayer == null ? 0 : LocalPlayerState.ContentID;
        if (id != characterID)
        {
            characterID = id;
            pendingPets = null;
            acknowledgedPets = null;
            status = string.Empty;
            nextProgressCheck = Environment.TickCount64 + 2_000;
            progressDeadline = 0;
            progressRequested = false;
            progressLoaded = false;
            progressHandlerLoadAttempted = false;
            Revision++;
            window?.ResetSelection();
        }

        UpdateProgressData();
        UpdateWindow();

        if (pendingPets == null)
        {
            return;
        }

        if (acknowledgedPets != null)
        {
            var current = ReadPets();
            var matches = acknowledgedPets.AsSpan().SequenceEqual(pendingPets) &&
                          current != null && current.AsSpan().SequenceEqual(pendingPets);
            status = OmniLoc.Get(matches
                ? "Feature.Battlehorn.Applied" : "Feature.Battlehorn.Rejected");
            if (matches)
            {
                RefreshNotebook();
            }
            pendingPets = null;
            acknowledgedPets = null;
        }
        else if (Environment.TickCount64 >= pendingDeadline)
        {
            status = OmniLoc.Get("Feature.Battlehorn.Timeout");
            DalamudServices.PluginLog.Warning("[Battlehorn] Timeout #{Request}; target={Target}; current={Current}.",
                requestID, FormatPets(pendingPets), FormatPets(ReadPets()));
            pendingPets = null;
        }
    }

    private byte[]? ReadPets()
    {
        if (!IsRunning || characterID == 0 || getPet == null)
        {
            return null;
        }
        var manager = ActionManager.Instance();
        return manager == null ? null : [getPet(manager, 0), getPet(manager, 1), getPet(manager, 2)];
    }

    internal byte[]? CurrentPets => ReadPets();

    private void UpdateProgressData()
    {
        if (!notebookVisible && !progressRequested)
        {
            return;
        }
        var now = Environment.TickCount64;
        if (now < nextProgressCheck)
        {
            return;
        }
        nextProgressCheck = now + 500;
        if (characterID == 0)
        {
            return;
        }
        if (DService.Instance().Condition[ConditionFlag.BetweenAreas] ||
            DService.Instance().Condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }
        var framework = EventFramework.Instance();
        if (framework == null)
        {
            return;
        }
        var handler = framework->GetEventHandlerById(0xB03D0u);
        if (handler == null && !progressHandlerLoadAttempted && loadProgressHandler != null)
        {
            if (framework->LoadState != 6)
            {
                return;
            }
            // 处理器通过原生工厂注册，由 EventHandlerModule 持有和释放。
            progressHandlerLoadAttempted = true;
            handler = loadProgressHandler(&framework->EventHandlerModule, 0xB03D0u, 0);
            if (handler == null)
            {
                DalamudServices.PluginLog.Warning("[Battlehorn] 无法初始化魔兽等级数据处理器。");
            }
            // 等待原生初始化完成后再请求数据。
            nextProgressCheck = now + 1_000;
            return;
        }
        if (handler == null)
        {
            return;
        }
        var dataState = *(int*)((byte*)handler + 0x468);
        // 0x468 为永久兽级和经验的加载状态，原生回包将其置为 2。
        if (dataState == 2)
        {
            if (!progressLoaded)
            {
                progressLoaded = true;
                progressDeadline = 0;
                RefreshNotebook();
            }
            return;
        }
        progressLoaded = false;
        if (!progressRequested && notebookVisible)
        {
            if (!CanApply(out _) || framework->EventState1.EventId.Id != 0 || framework->EventState2.EventId.Id != 0)
            {
                return;
            }
            progressRequested = true;
            if (now < nextDirectProgressRequest)
            {
                return;
            }
            nextDirectProgressRequest = now + 30_000;
            progressDeadline = now + 15_000;
            // 复用 NPC 的查询命令，不修改处理器状态，也不模拟 NPC 事件。
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.QuestCommand4000);
        }
        if (progressDeadline != 0 && now >= progressDeadline)
        {
            progressDeadline = 0;
            DalamudServices.PluginLog.Warning(
                "[Battlehorn] Direct progress request timed out after 15 seconds; nativeState={State}; no automatic retry.",
                *(int*)((byte*)handler + 0xD08));
        }
    }

    internal string GetPetProgress(byte pet)
    {
        if (pet == 0)
        {
            return string.Empty;
        }
        return TryGetPetProgress(pet, out var level, out var experience)
            ? level == 25 ? $"Lv.{level}  --/--" : $"Lv.{level}  {experience}/100"
            : "--";
    }

    private bool TryGetPetProgress(byte pet, out byte level, out byte experience)
    {
        level = experience = 0;
        if (!IsRunning || pet == 0 || characterID == 0 || characterID != LocalPlayerState.ContentID ||
            getPetLevel == null || getPetExperience == null)
        {
            return false;
        }
        var framework = EventFramework.Instance();
        // 此处理器提供永久兽级和 0～100 的经验进度，不是同步后的等级。
        var handler = framework == null ? null : framework->GetEventHandlerById(0xB03D0u);
        if (handler == null || *(int*)((byte*)handler + 0x468) != 2)
        {
            return false;
        }
        level = getPetLevel(handler, pet);
        experience = getPetExperience(handler, pet);
        return level is > 0 and <= 25 && experience <= 100;
    }

    private static void RefreshNotebook()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId(AgentId.XBMMonsterNotebook);
        if (agent != null && agent->IsAgentActive())
        {
            // 0x5E、0x5F 分别触发列表和详情重绘；0x60 只刷新兽笛子窗口。
            *((byte*)agent + 0x5E) = 1;
            *((byte*)agent + 0x5F) = 1;
        }
    }

    private bool CanApply(out AgentInterface* agent)
    {
        agent = null;
        if (!IsRunning)
        {
            return false;
        }
        var module = AgentModule.Instance();
        agent = module == null ? null : module->GetAgentByInternalId(AgentId.XBMMonsterNotebook);
        var player = DService.Instance().ObjectTable.LocalPlayer;
        // 0x58 为图鉴操作限制状态；0x5C 由捕获列表响应置位，与现有国服捕获状态适配一致。
        return pendingPets == null && player != null && player.ClassJob.RowId == 43 &&
               characterID != 0 && characterID == LocalPlayerState.ContentID &&
               !player.IsCasting && !DService.Instance().Condition[ConditionFlag.InCombat] &&
               !DService.Instance().Condition[ConditionFlag.BetweenAreas] &&
               !DService.Instance().Condition[ConditionFlag.BetweenAreas51] &&
               agent != null && agent->IsAgentActive() && *(int*)((byte*)agent + 0x58) == 0 &&
               *((byte*)agent + 0x5C) != 0;
    }

    internal void Apply(BattlehornPreset preset)
    {
        if (!IsRunning)
        {
            return;
        }
        if (!CanApply(out var agent) || setPets == null || isCaptured == null ||
            !Presets.Contains(preset) || !ValidPets(preset.Pets))
        {
            status = OmniLoc.Get("Feature.Battlehorn.Unavailable");
            DalamudServices.PluginLog.Warning(
                "[Battlehorn] Apply blocked; target={Target}; busy={Busy}; job={Job}; agentActive={Active}; state={State}; capturedReady={Ready}.",
                FormatPets(preset.Pets), pendingPets != null, DService.Instance().ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0,
                agent != null && agent->IsAgentActive(), agent == null ? -1 : *(int*)((byte*)agent + 0x58),
                agent != null && *((byte*)agent + 0x5C) != 0);
            return;
        }

        foreach (var pet in preset.Pets)
        {
            if (pet != 0 && !isCaptured(agent, pet))
            {
                status = OmniLoc.Get("Feature.Battlehorn.NotCaptured");
                DalamudServices.PluginLog.Warning("[Battlehorn] Apply blocked; uncapturedPet={Pet}.", pet);
                return;
            }
        }

        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return;
        }
        var before = ReadPets();
        if (before != null && before.AsSpan().SequenceEqual(preset.Pets))
        {
            status = OmniLoc.Get("Feature.Battlehorn.Applied");
            RefreshNotebook();
            return;
        }
        pendingPets = (byte[])preset.Pets.Clone();
        acknowledgedPets = null;
        pendingManager = (nint)manager;
        requestID++;
        pendingDeadline = Environment.TickCount64 + 5_000;
        fixed (byte* pets = pendingPets)
        {
            var accepted = setPets(manager, pets);
            if (!accepted)
            {
                pendingPets = null;
                status = OmniLoc.Get("Feature.Battlehorn.Rejected");
                return;
            }
        }
        status = OmniLoc.Get("Feature.Battlehorn.Applying");
    }

    private void OnReceivePets(ActionManager* manager, byte first, byte second, byte third)
    {
        receivePetsHook!.Original(manager, first, second, third);
        if (pendingPets != null && (nint)manager == pendingManager)
        {
            acknowledgedPets = [first, second, third];
        }
    }

    private static string FormatPets(byte[]? pets) => pets == null ? "unavailable" : string.Join(",", pets);

    private bool ValidPets(byte[]? pets)
    {
        if (pets is not { Length: 3 })
        {
            return false;
        }
        for (var i = 0; i < pets.Length; i++)
        {
            if (pets[i] == 0)
            {
                continue;
            }
            if (!entries.ContainsKey(pets[i]))
            {
                return false;
            }
            for (var j = 0; j < i; j++)
            {
                if (pets[i] == pets[j])
                {
                    return false;
                }
            }
        }
        return true;
    }

    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetPetsDelegate(ActionManager* manager, byte* pets);
    private delegate byte GetPetDelegate(ActionManager* manager, byte slot);
    private delegate byte GetPetProgressDelegate(FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler* handler, ushort pet);
    private delegate FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler* LoadProgressHandlerDelegate(
        EventHandlerModule* module, uint eventID, nint luaActor);
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsCapturedDelegate(AgentInterface* agent, uint petID);
    private delegate void ReceivePetsDelegate(ActionManager* manager, byte first, byte second, byte third);
}
