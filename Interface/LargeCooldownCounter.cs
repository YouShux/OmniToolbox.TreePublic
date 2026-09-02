using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class LargeCooldownCounter(LargeCooldownCounterConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("LargeCooldownCounterTitle"),
        Description = OmniLoc.Get("LargeCooldownCounterDescription"),
        Category = ModuleCategory.Interface
    };

    private static readonly FontType[] SupportedFonts =
    [
        FontType.Axis,
        FontType.MiedingerMed,
        FontType.Miedinger,
        FontType.TrumpGothic
    ];
    private static readonly Vector4 CooldownTextColor = Vector4.One;
    private static readonly Vector4 CooldownEdgeColor = new(0.2f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 InvalidTextColor = new(0.85f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 InvalidEdgeColor = new(0.34f, 0f, 0f, 1f);

    private FeatureLifetime? runtimeLifetime;
    private Hook<AddonActionBarBase.Delegates.UpdateHotbarSlot>? updateHotbarSlotHook;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##largeCooldownCounterSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##largeCooldownCounterFont", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##largeCooldownCounterFontSize", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##largeCooldownCounterUnused1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##largeCooldownCounterUnused2", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.LargeCooldownCounter.Font"));
        ImGui.SameLine();
        if (OmniControls.BeginCombo(
                "##largeCooldownCounterFont",
                GetFontName(config.Font),
                MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
        {
            foreach (var font in SupportedFonts)
            {
                if (ImGui.Selectable(GetFontName(font), config.Font == font))
                {
                    config.Font = font;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TableNextColumn();
        var fontSizeAdjust = config.FontSizeAdjust;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.LargeCooldownCounter.FontSizeAdjust"));
        ImGui.SameLine();
        if (OmniControls.SliderInt(
                "##largeCooldownCounterFontSizeAdjust",
                ref fontSizeAdjust,
                -15,
                30,
                "%d",
                MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
        {
            config.FontSizeAdjust = fontSizeAdjust;
            changed = true;
        }

        ImGui.TableNextColumn();
        ImGui.TableNextColumn();

        return changed;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            updateHotbarSlotHook = DService.Instance().Hook.HookFromAddress<AddonActionBarBase.Delegates.UpdateHotbarSlot>(
                AddonActionBarBase.MemberFunctionPointers.UpdateHotbarSlot,
                OnUpdateHotbarSlot);
            lifetime.Add(updateHotbarSlotHook.Dispose);
            updateHotbarSlotHook.Enable();
            runtimeLifetime = lifetime;
        }
        catch
        {
            updateHotbarSlotHook = null;
            lifetime.Dispose();
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
            updateHotbarSlotHook = null;
        }
    }

    private void OnUpdateHotbarSlot(
        AddonActionBarBase* addon,
        ActionBarSlot* slot,
        NumberArrayData* numberArray,
        StringArrayData* stringArray,
        int numberArrayIndex,
        int stringArrayIndex)
    {
        updateHotbarSlotHook!.Original(
            addon,
            slot,
            numberArray,
            stringArray,
            numberArrayIndex,
            stringArrayIndex);

        if (numberArray == null ||
            numberArray->IntArray == null ||
            numberArrayIndex < 0 ||
            numberArrayIndex + 2 >= numberArray->Size ||
            numberArray->IntArray[numberArrayIndex + 2] != 5 ||
            slot == null ||
            slot->ComponentDragDrop == null ||
            slot->ComponentDragDrop->AtkComponentIcon == null ||
            slot->ComponentDragDrop->AtkComponentIcon->FrameIcon == null)
        {
            return;
        }

        var cooldownTextNode = slot->ComponentDragDrop->AtkComponentIcon->FrameIcon->GetAsAtkTextNode();
        if (cooldownTextNode == null)
        {
            return;
        }

        var font = SupportedFonts.Contains(config.Font) ? config.Font : FontType.TrumpGothic;
        cooldownTextNode->SetAlignment(AlignmentType.Center);
        cooldownTextNode->SetFont(font);
        cooldownTextNode->FontSize = GetFontSize(font);

        if (cooldownTextNode->TextColor.B < 100)
        {
            ApplyColor(cooldownTextNode, InvalidTextColor, InvalidEdgeColor);
            return;
        }

        ApplyColor(cooldownTextNode, CooldownTextColor, CooldownEdgeColor);
    }

    private byte GetFontSize(FontType font)
    {
        var defaultSize = font switch
        {
            FontType.Axis => 18,
            FontType.MiedingerMed => 14,
            FontType.Miedinger => 15,
            _ => 24
        };
        return (byte)Math.Clamp(defaultSize + config.FontSizeAdjust * 2, 4, 255);
    }

    private static void ApplyColor(AtkTextNode* node, Vector4 textColor, Vector4 edgeColor)
    {
        node->TextColor.R = ToByte(textColor.X);
        node->TextColor.G = ToByte(textColor.Y);
        node->TextColor.B = ToByte(textColor.Z);
        node->TextColor.A = ToByte(textColor.W);
        node->EdgeColor.R = ToByte(edgeColor.X);
        node->EdgeColor.G = ToByte(edgeColor.Y);
        node->EdgeColor.B = ToByte(edgeColor.Z);
        node->EdgeColor.A = ToByte(edgeColor.W);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    private static string GetFontName(FontType font) => font switch
    {
        FontType.Axis => "Axis",
        FontType.MiedingerMed => "Miedinger Medium",
        FontType.Miedinger => "Miedinger",
        _ => "Trump Gothic"
    };

}

[Serializable]
public sealed class LargeCooldownCounterConfig
{
    public FontType Font { get; set; } = FontType.TrumpGothic;

    public int FontSizeAdjust { get; set; } = 3;
}
