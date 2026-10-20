using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ObjectTable = OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal readonly record struct BetterGlamourItemSearchResult(uint ItemID, string Name, uint IconID);

    internal enum BetterGlamourEditorItemKind
    {
        Item,
        Hairstyle,
        Glasses
    }

    internal sealed record BetterGlamourEditorRow(
        string PartName,
        BetterGlamourPreset Preset,
        BetterGlamourItem? Item,
        bool IsGlasses,
        bool IsHairstyle,
        bool IsDyeSelected,
        Action? OpenDye,
        Action OnChanged,
        Action<BetterGlamourEditorListItemNode>? OnSearchFocused = null,
        Action<BetterGlamourEditorListItemNode, string>? OnSearchChanged = null,
        Action? OnSearchClosed = null)
    {
        internal BetterGlamourEditorItemKind Kind => IsHairstyle
            ? BetterGlamourEditorItemKind.Hairstyle
            : IsGlasses
                ? BetterGlamourEditorItemKind.Glasses
                : BetterGlamourEditorItemKind.Item;
    }

    internal sealed record BetterGlamourDyeGridRow(
        List<GlamourDyeOption> Options,
        byte SelectedID,
        Action<GlamourDyeOption> OnSelected,
        Action<BetterGlamourDyeColorNode, string> ShowTooltip,
        Action HideTooltip);

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
