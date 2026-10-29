using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ObjectTable = OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal readonly record struct BetterGlamourItemSearchResult
    (
        uint                        ItemID,
        string                      Name,
        uint                        IconID,
        uint                        EquipLevel = 0,
        BetterGlamourEditorItemKind Kind = BetterGlamourEditorItemKind.Item
    );

    internal enum BetterGlamourEditorItemKind
    {
        Item,
        Hairstyle,
        Glasses
    }

    internal sealed record BetterGlamourEditorRow
    (
        string                      PartName,
        BetterGlamourPreset         Preset,
        BetterGlamourItem?          Item,
        BetterGlamourEditorItemKind Kind,
        Action                      OpenItem,
        Action<int>?                OpenDye = null,
        Action<bool>?               SetHighlights = null,
        CustomizeData?              HairAppearance = null,
        Action<int>?                ClearDye = null,
        Action?                     ClearItem = null,
        Action?                     UndoItem = null,
        Action<int>?                UndoDye = null,
        Action?                     SwapDyes = null
    );

    internal sealed record BetterGlamourEditorPair
    (
        BetterGlamourEditorRow Left,
        BetterGlamourEditorRow Right
    );

    internal sealed record BetterGlamourDyeGridRow
    (
        List<GlamourDyeOption>    Options,
        byte                      SelectedID,
        Action<GlamourDyeOption>  OnSelected,
        Action<GlamourDyeOption>? ContextRequested = null,
        byte                      OriginalID = 0
    );

    internal sealed class OmniPreviewContext
    {
        public required IDalamudPluginInterface PluginInterface { get; init; }

        public required IAddonLifecycle AddonLifecycle { get; init; }

        public required ITextureReadbackProvider TextureReadback { get; init; }

        public required IFramework Framework { get; init; }

        public required IClientState ClientState { get; init; }

        public required ObjectTable ObjectTable { get; init; }

        public required ICondition Condition { get; init; }

        public required IGameGui GameGUI { get; init; }

        public required IDataManager DataManager { get; init; }

    }
}
