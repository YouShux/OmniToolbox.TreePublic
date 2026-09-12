using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Collections;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterBattlehornSettings(
    BetterBattlehornSettingsConfig config,
    CollectionService collections,
    Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterBattlehornSettingsTitle"),
        Description = OmniLoc.Get("BetterBattlehornSettingsDescription"),
        Category = ModuleCategory.Interface,
        RequiresPrivateProvider = true,
        RequiresTestQualification = false,
        Commands =
        [
            new("Feature.Battlehorn.CommandDescription", "/omni 兽笛设置 <方案名>")
        ]
    };

    private const string AddonName = "XBMMonsterNotebook";
    private readonly Dictionary<uint, CollectionEntry> entries = [];
    private BattlehornSettingsNativeUI? window;
    private bool notebookVisible;
    internal bool IsRunning { get; private set; }
    internal int Revision { get; private set; }
    internal string Status => status;
    internal bool CanEdit => CanApply(out _);
    internal List<BattlehornPreset> Presets => config.Characters.TryGetValue(characterID, out var list) ? list : [];

    public override bool TryHandleCommand(string arguments)
    {
        if (!CanApply(out _))
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.Battlehorn.Unavailable"));
            return true;
        }
        var name = arguments.Trim();
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Apply(preset);
                return true;
            }
        }
        OmniNotifier.Chat(string.Format(OmniLoc.Get("Feature.Battlehorn.PresetNotFound"), name));
        return true;
    }

    protected override void OnEnable()
    {
        try
        {
            entries.Clear();
            foreach (var entry in collections.Get(CollectionType.Bestiary))
            {
                entries[entry.ID] = entry;
            }
            setPets = SetPetsSignature.GetDelegate<SetPetsDelegate>();
            getPet = GetPetSignature.GetDelegate<GetPetDelegate>();
            getPetLevel = PetLevelSignature.GetDelegate<GetPetProgressDelegate>();
            getPetExperience = PetExperienceSignature.GetDelegate<GetPetProgressDelegate>();
            loadProgressHandler = LoadProgressHandlerSignature.GetDelegate<LoadProgressHandlerDelegate>();
            isCaptured = IsCapturedSignature.GetDelegate<IsCapturedDelegate>();
            receivePetsHook = ReceivePetsSignature.GetHook<ReceivePetsDelegate>(OnReceivePets);
            receivePetsHook.Enable();
            window = new(this);
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, DetailAddonName, OnDetailAddon);
            DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, DetailAddonName, OnDetailAddon);
            if (!FrameworkManager.Instance().Reg(OnUpdate))
            {
                throw new InvalidOperationException("Battlehorn update registration failed.");
            }
            IsRunning = true;
        }
        catch
        {
            OnDisable();
            throw;
        }
    }

    protected override void OnDisable()
    {
        IsRunning = false;
        FrameworkManager.Instance().Unreg(OnUpdate);
        DalamudServices.AddonLifecycle.UnregisterListener(OnDetailAddon);
        DisposeDetailProgress();
        window?.Dispose();
        window = null;
        receivePetsHook?.Dispose();
        receivePetsHook = null;
        setPets = null;
        getPet = null;
        getPetLevel = null;
        getPetExperience = null;
        loadProgressHandler = null;
        progressHandlerLoadAttempted = false;
        nextProgressCheck = 0;
        progressDeadline = 0;
        progressRequested = false;
        progressLoaded = false;
        nextDirectProgressRequest = 0;
        isCaptured = null;
        pendingPets = null;
        acknowledgedPets = null;
        characterID = 0;
        notebookVisible = false;
        status = string.Empty;
    }

    private void UpdateWindow()
    {
        if (window == null)
        {
            return;
        }
        var notebook = AddonHelper.GetByName(AddonName);
        var visible = characterID != 0 && characterID == LocalPlayerState.ContentID &&
                      notebook != null && notebook->IsVisible;
        if (!visible)
        {
            if (notebookVisible)
            {
                window.Close();
            }
            notebookVisible = false;
            return;
        }
        if (!notebookVisible)
        {
            nextProgressCheck = 0;
            progressRequested = false;
            window.Open();
        }
        notebookVisible = true;
        AtkUnitBase* native = window;
        if (native == null || !window.IsOpen)
        {
            return;
        }
        if (MathF.Abs(native->Scale - notebook->Scale) > 0.001f)
        {
            native->SetScale(notebook->Scale / AtkUnitBase.GetGlobalUIScale(), true);
        }
        window.SyncTitleColor(notebook);
        var screen = AtkStage.Instance()->ScreenSize;
        var width = window.Size.X * native->Scale;
        var height = window.Size.Y * native->Scale;
        window.SetWindowPosition(new(
            Math.Clamp(notebook->GetX() - width - 6f, 0f, MathF.Max(0f, screen.Width - width)),
            Math.Clamp(notebook->GetY(), 0f, MathF.Max(0f, screen.Height - height))));
    }

    internal CollectionEntry? GetEntry(byte pet) => entries.GetValueOrDefault(pet);

    internal bool SavePreset(string name, BattlehornPreset? target)
    {
        if (!IsRunning)
        {
            return false;
        }
        if (!CanEdit || target != null && !Presets.Contains(target))
        {
            status = OmniLoc.Get("Feature.Battlehorn.Unavailable");
            DalamudServices.PluginLog.Warning("[Battlehorn] Save blocked; overwrite={Overwrite}.", target != null);
            return false;
        }
        name = name.Trim();
        if (!ValidName(name, target))
        {
            return false;
        }
        var pets = ReadPets();
        if (!ValidPets(pets))
        {
            DalamudServices.PluginLog.Warning("[Battlehorn] Save invalid current={Current}.", FormatPets(pets));
            return false;
        }
        if (target == null)
        {
            if (!config.Characters.TryGetValue(characterID, out var list))
            {
                config.Characters[characterID] = list = [];
            }
            target = new();
            list.Add(target);
        }
        target.Name = name;
        target.Pets = pets!;
        saveConfig();
        Revision++;
        status = OmniLoc.Get("Feature.Battlehorn.Saved");
        return true;
    }

    internal bool RenamePreset(BattlehornPreset preset, string name)
    {
        if (!IsRunning)
        {
            return false;
        }
        name = name.Trim();
        if (!Presets.Contains(preset) || !ValidName(name, preset))
        {
            return false;
        }
        preset.Name = name;
        saveConfig();
        Revision++;
        return true;
    }

    internal void DeletePreset(BattlehornPreset preset)
    {
        if (!IsRunning)
        {
            return;
        }
        if (Presets.Remove(preset))
        {
            saveConfig();
            Revision++;
            status = string.Empty;
        }
    }

    private bool ValidName(string name, BattlehornPreset? target)
    {
        if (name.Length != 0)
        {
            foreach (var other in Presets)
            {
                if (!ReferenceEquals(target, other) && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    status = OmniLoc.Get("Feature.Battlehorn.InvalidName");
                    return false;
                }
            }
            return true;
        }
        status = OmniLoc.Get("Feature.Battlehorn.InvalidName");
        return false;
    }
}
