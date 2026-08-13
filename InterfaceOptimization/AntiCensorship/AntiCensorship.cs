using System.Drawing;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AntiCensorship(AntiCensorshipConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AntiCensorshipTitle"),
        Description = OmniLoc.Get("AntiCensorshipDescription"),
        Category = ModuleCategory.Interface
    };

    private static readonly CompSig GetFilteredUtf8StringSig = new(
        "48 89 74 24 ?? 57 48 83 EC ?? 48 83 79 ?? ?? 48 8B FA 48 8B F1 0F 84 ?? ?? ?? ?? 48 89 5C 24");
    private static readonly CompSig VulgarInstanceOffsetSig = new(
        "48 8B 81 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B D3");
    private static readonly CompSig LocalMessageDisplaySig = new(
        "40 53 48 83 EC ?? 48 8D 99 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 48 8B 0D");
    private static readonly CompSig PartyFinderMessageDisplaySig = new(
        "48 89 5C 24 ?? 57 48 83 EC ?? 48 8D 99 ?? ?? ?? ?? 48 8B F9 48 8B CB E8");
    private static readonly CompSig LookingForGroupConditionReceiveEventSig = new(
        "E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CB E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CB E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 48 8B CE");
    private static readonly CompSig TextInputReceiveEventSig = new(
        "4C 8B DC 55 53 57 41 54 41 57 49 8D AB ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 48 8B 9D");

    [ThreadStatic]
    private static bool allowFilterCall;

    private FeatureLifetime? runtimeLifetime;
    private AntiCensorshipProcessor? processor;
    private Hook<GetFilteredUtf8StringDelegate>? getFilteredUtf8StringHook;
    private Hook<LocalMessageDisplayDelegate>? localMessageDisplayHook;
    private Hook<PartyFinderMessageDisplayDelegate>? partyFinderMessageDisplayHook;
    private Hook<LookingForGroupConditionReceiveEventDelegate>? lookingForGroupConditionReceiveEventHook;
    private Hook<TextInputReceiveDelegate>? textInputReceiveEventHook;
    private int vulgarInstanceOffset;

    private delegate void GetFilteredUtf8StringDelegate(nint vulgarInstance, Utf8String* text);

    private delegate nint LocalMessageDisplayDelegate(nint context, Utf8String* source);

    private delegate nint PartyFinderMessageDisplayDelegate(nint context, Utf8String* source);

    private delegate byte LookingForGroupConditionReceiveEventDelegate(nint context, AtkValue* values);

    private delegate void TextInputReceiveDelegate(
        AtkComponentTextInput* textInput,
        AtkEventType eventType,
        int eventParam,
        AtkEvent* atkEvent,
        AtkEventData* atkEventData);

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var autoHandle = config.EnableAutoHandle;
        if (OmniControls.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.AutoHandle")}##antiCensorshipAutoHandle", ref autoHandle))
        {
            config.EnableAutoHandle = autoHandle;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(60f));
        var separator = config.Separator.ToString();
        if (OmniControls.InputText(
                $"{OmniLoc.Get("Feature.AntiCensorship.Separator")}##antiCensorshipSeparator",
                ref separator,
                8))
        {
            separator = separator.Trim();
            config.Separator = string.IsNullOrWhiteSpace(separator) || separator == "*"
                ? '.'
                : separator[0];
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.AntiCensorship.AutoHandle.Help"));
        ImGui.SameLine(0f, OmniTheme.ContentGap());
        var coloring = config.EnableColoring;
        if (OmniControls.Checkbox($"{OmniLoc.Get("Feature.AntiCensorship.Coloring")}##antiCensorshipColoring", ref coloring))
        {
            config.EnableColoring = coloring;
            changed = true;
        }

        ImGui.SameLine();
        var colorID = config.HighlightColor;
        if (UIColorPicker.Draw("antiCensorship", ref colorID, KnownColor.Red.ToVector4()))
        {
            config.HighlightColor = colorID;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.AntiCensorship.Coloring.Help"));
        if (!changed)
        {
            return false;
        }

        processor?.Clear();
        return true;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var offsetAddress = VulgarInstanceOffsetSig.ScanText();
            if (offsetAddress == nint.Zero)
            {
                throw new InvalidOperationException("Anti-censorship vulgar instance offset signature was not found.");
            }

            vulgarInstanceOffset = *(int*)(offsetAddress + 3);
            getFilteredUtf8StringHook = EnableHook<GetFilteredUtf8StringDelegate>(
                GetFilteredUtf8StringSig,
                GetFilteredUtf8StringDetour,
                lifetime);
            processor = new(config, GetFilteredString);
            lifetime.Add(processor.Clear);
            localMessageDisplayHook = EnableHook<LocalMessageDisplayDelegate>(
                LocalMessageDisplaySig,
                LocalMessageDisplayDetour,
                lifetime);
            partyFinderMessageDisplayHook = EnableHook<PartyFinderMessageDisplayDelegate>(
                PartyFinderMessageDisplaySig,
                PartyFinderMessageDisplayDetour,
                lifetime);
            lookingForGroupConditionReceiveEventHook = EnableHook<LookingForGroupConditionReceiveEventDelegate>(
                LookingForGroupConditionReceiveEventSig,
                LookingForGroupConditionReceiveEventDetour,
                lifetime);
            textInputReceiveEventHook = EnableHook<TextInputReceiveDelegate>(
                TextInputReceiveEventSig,
                TextInputReceiveEventDetour,
                lifetime);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                ClearRuntimeReferences();
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            ClearRuntimeReferences();
        }
    }

    private static Hook<T> EnableHook<T>(CompSig signature, T detour, FeatureLifetime lifetime) where T : Delegate
    {
        var hook = signature.GetHook(detour);
        lifetime.Add(hook.Dispose);
        hook.Enable();
        return hook;
    }

    private void GetFilteredUtf8StringDetour(nint vulgarInstance, Utf8String* text)
    {
        if (allowFilterCall || vulgarInstance == nint.Zero || text == null)
        {
            getFilteredUtf8StringHook!.Original(vulgarInstance, text);
        }
    }

    private nint LocalMessageDisplayDetour(nint context, Utf8String* source)
    {
        HighlightMessage(source);
        return localMessageDisplayHook!.Original(context, source);
    }

    private nint PartyFinderMessageDisplayDetour(nint context, Utf8String* source)
    {
        HighlightMessage(source);
        return partyFinderMessageDisplayHook!.Original(context, source);
    }

    private byte LookingForGroupConditionReceiveEventDetour(nint context, AtkValue* values)
    {
        if (config.EnableAutoHandle && processor is not null)
        {
            try
            {
                if (values != null && values->Int == 15 && values[1].String.Value != null)
                {
                    var original = SeString.Parse(values[1].String.Value);
                    if (!string.IsNullOrWhiteSpace(original.TextValue))
                    {
                        var handled = new SeString(original.Payloads);
                        processor.Bypass(ref handled);
                        if (!string.Equals(handled.TextValue, original.TextValue, StringComparison.Ordinal))
                        {
                            processor.RememberAutoHandledHighlight(handled, original);
                            var encoded = handled.EncodeWithNullTerminator();
                            values[1].SetManagedString(encoded);
                            var addon = AddonHelper.GetByName("LookingForGroupCondition");
                            var textInput = addon == null
                                ? null
                                : (AtkComponentTextInput*)addon->GetComponentByNodeId(22);
                            if (textInput != null)
                            {
                                textInput->SetText(encoded);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Debug(ex, "Anti-censorship party finder text processing failed.");
            }
        }

        return lookingForGroupConditionReceiveEventHook!.Original(context, values);
    }

    private void TextInputReceiveEventDetour(
        AtkComponentTextInput* textInput,
        AtkEventType eventType,
        int eventParam,
        AtkEvent* atkEvent,
        AtkEventData* atkEventData)
    {
        textInputReceiveEventHook!.Original(textInput, eventType, eventParam, atkEvent, atkEventData);
        if (!config.EnableAutoHandle || processor is null || eventType != AtkEventType.FocusStop || textInput == null)
        {
            return;
        }

        try
        {
            var addon = textInput->OwnerAddon;
            if (addon == null)
            {
                addon = textInput->ContainingAddon2;
            }

            if (addon == null || !string.Equals(addon->NameString, "ChatLog", StringComparison.Ordinal))
            {
                return;
            }

            var original = SeString.Parse(textInput->EvaluatedString);
            var handled = new SeString(original.Payloads);
            processor.Bypass(ref handled);
            if (string.Equals(handled.TextValue, original.TextValue, StringComparison.Ordinal))
            {
                return;
            }

            processor.RememberAutoHandledHighlight(handled, original);
            textInput->SetText(handled.EncodeWithNullTerminator());
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Debug(ex, "Anti-censorship chat input processing failed.");
        }
    }

    private void HighlightMessage(Utf8String* source)
    {
        if (!config.EnableColoring || processor is null || source == null)
        {
            return;
        }

        try
        {
            var text = SeString.Parse(source->AsSpan());
            if (!processor.TryApplyAutoHandledHighlight(ref text))
            {
                processor.Highlight(ref text);
            }

            source->SetString(text.EncodeWithNullTerminator());
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Debug(ex, "Anti-censorship message highlighting failed.");
        }
    }

    private string GetFilteredString(string text)
    {
        var framework = Framework.Instance();
        if (framework == null || getFilteredUtf8StringHook is null || vulgarInstanceOffset == 0)
        {
            return text;
        }

        var vulgarInstance = *(nint*)((byte*)framework + vulgarInstanceOffset);
        if (vulgarInstance == nint.Zero)
        {
            return text;
        }

        var utf8String = Utf8String.FromString(text);
        try
        {
            allowFilterCall = true;
            getFilteredUtf8StringHook.Original(vulgarInstance, utf8String);
            return utf8String->ToString();
        }
        finally
        {
            allowFilterCall = false;
            utf8String->Dtor(true);
        }
    }

    private void ClearRuntimeReferences()
    {
        runtimeLifetime = null;
        processor = null;
        getFilteredUtf8StringHook = null;
        localMessageDisplayHook = null;
        partyFinderMessageDisplayHook = null;
        lookingForGroupConditionReceiveEventHook = null;
        textInputReceiveEventHook = null;
        vulgarInstanceOffset = 0;
    }
}

[Serializable]
public sealed class AntiCensorshipConfig
{
    public bool EnableColoring { get; set; }
    public bool EnableAutoHandle { get; set; } = true;
    public int HighlightColor { get; set; } = 17;
    public char Separator { get; set; } = '.';
}
