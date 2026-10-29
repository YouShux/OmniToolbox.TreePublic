using System.Drawing;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class AntiCensorship(AntiCensorshipConfig config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "AntiCensorshipTitle",
            ["Description"] = "AntiCensorshipDescription",
            ["Feature.AntiCensorship.AutoHandle"] = "Feature.AntiCensorship.AutoHandle",
            ["Feature.AntiCensorship.AutoHandle.Help"] = "Feature.AntiCensorship.AutoHandle.Help",
            ["Feature.AntiCensorship.Coloring"] = "Feature.AntiCensorship.Coloring",
            ["Feature.AntiCensorship.Coloring.Help"] = "Feature.AntiCensorship.Coloring.Help",
            ["Feature.AntiCensorship.Separator"] = "Feature.AntiCensorship.Separator"
        });

    private static readonly CompSig GetFilteredUtf8StringSig = new(
        "48 89 74 24 ?? 57 48 83 EC ?? 48 83 79 ?? ?? 48 8B FA 48 8B F1 0F 84 ?? ?? ?? ?? 48 89 5C 24");
    private delegate void GetFilteredUtf8StringDelegate
    (
        nint        vulgarInstance,
        Utf8String* text
    );
    private Hook<GetFilteredUtf8StringDelegate>? GetFilteredUtf8StringHook;

    private static readonly CompSig VulgarInstanceOffsetSig = new(
        "48 8B 81 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B D3");

    private static readonly CompSig LocalMessageDisplaySig = new(
        "40 53 48 83 EC ?? 48 8D 99 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 48 8B 0D");
    private delegate nint LocalMessageDisplayDelegate
    (
        nint        context,
        Utf8String* source
    );
    private Hook<LocalMessageDisplayDelegate>? LocalMessageDisplayHook;

    private static readonly CompSig PartyFinderMessageDisplaySig = new(
        "48 89 5C 24 ?? 57 48 83 EC ?? 48 8D 99 ?? ?? ?? ?? 48 8B F9 48 8B CB E8");
    private delegate nint PartyFinderMessageDisplayDelegate
    (
        nint        context,
        Utf8String* source
    );
    private Hook<PartyFinderMessageDisplayDelegate>? PartyFinderMessageDisplayHook;

    private static readonly CompSig LookingForGroupConditionReceiveEventSig = new(
        "E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CB E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CB E8 ?? ?? ?? ?? 0F B6 F8 E9 ?? ?? ?? ?? 48 8B CE");
    private delegate byte LookingForGroupConditionReceiveEventDelegate
    (
        nint      context,
        AtkValue* values
    );
    private Hook<LookingForGroupConditionReceiveEventDelegate>? LookingForGroupConditionReceiveEventHook;

    private static readonly CompSig TextInputReceiveEventSig = new(
        "4C 8B DC 55 53 57 41 54 41 57 49 8D AB ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 48 8B 9D");
    private delegate void TextInputReceiveDelegate
    (
        AtkComponentTextInput* textInput,
        AtkEventType           eventType,
        int                    eventParam,
        AtkEvent*              atkEvent,
        AtkEventData*          atkEventData
    );
    private Hook<TextInputReceiveDelegate>? TextInputReceiveEventHook;

    [ThreadStatic]
    private static bool AllowFilterCall;

    private Processor? processor;
    private int        vulgarInstanceOffset;

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            ClearRuntimeReferences();
        });
        var offsetAddress = VulgarInstanceOffsetSig.ScanText();
        if (offsetAddress == nint.Zero)
        {
            throw new InvalidOperationException("Anti-censorship vulgar instance offset signature was not found.");
        }

        vulgarInstanceOffset      = *(int*)(offsetAddress + 3);
        GetFilteredUtf8StringHook = EnableHook<GetFilteredUtf8StringDelegate>(
            GetFilteredUtf8StringSig,
            GetFilteredUtf8StringDetour,
            lifetime);
        processor                 = new(config, GetFilteredString);
        lifetime.Add(processor.Clear);
        LocalMessageDisplayHook                  = EnableHook<LocalMessageDisplayDelegate>(
            LocalMessageDisplaySig,
            LocalMessageDisplayDetour,
            lifetime);
        PartyFinderMessageDisplayHook            = EnableHook<PartyFinderMessageDisplayDelegate>(
            PartyFinderMessageDisplaySig,
            PartyFinderMessageDisplayDetour,
            lifetime);
        LookingForGroupConditionReceiveEventHook = EnableHook<LookingForGroupConditionReceiveEventDelegate>(
            LookingForGroupConditionReceiveEventSig,
            LookingForGroupConditionReceiveEventDetour,
            lifetime);
        TextInputReceiveEventHook                = EnableHook<TextInputReceiveDelegate>(
            TextInputReceiveEventSig,
            TextInputReceiveEventDetour,
            lifetime);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var autoHandle = config.EnableAutoHandle;
        if (OmniControls.Checkbox($"{OmniLoc.Get(this, "Feature.AntiCensorship.AutoHandle")}##antiCensorshipAutoHandle", ref autoHandle))
        {
            config.EnableAutoHandle = autoHandle;
            changed                 = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(60f));
        var separator = config.Separator.ToString();
        if (OmniControls.InputText(
                $"{OmniLoc.Get(this, "Feature.AntiCensorship.Separator")}##antiCensorshipSeparator",
                ref separator,
                8))
        {
            separator        = separator.Trim();
            config.Separator = string.IsNullOrWhiteSpace(separator) || separator == "*"
                ? '.'
                : separator[0];
            processor?.Clear();
        }
        changed |= ImGui.IsItemDeactivatedAfterEdit();

        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AntiCensorship.AutoHandle.Help"));
        ImGui.SameLine(0f, OmniTheme.ContentGap());
        var coloring = config.EnableColoring;
        if (OmniControls.Checkbox($"{OmniLoc.Get(this, "Feature.AntiCensorship.Coloring")}##antiCensorshipColoring", ref coloring))
        {
            config.EnableColoring = coloring;
            changed               = true;
        }

        ImGui.SameLine();
        var color = UIColorPicker.Resolve(
            config.HighlightColor,
            config.UseCustomHighlightColor,
            config.HighlightColorValue,
            KnownColor.Red.ToVector4());
        if (UIColorPicker.Draw(
                "antiCensorship",
                ref color,
                ImGuiColorEditFlags.DisplayRgb))
        {
            config.HighlightColorValue     = color;
            config.UseCustomHighlightColor = true;
            processor?.Clear();
        }

        OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AntiCensorship.Coloring.Help"));
        if (!changed)
        {
            return false;
        }

        processor?.Clear();
        return true;
    }

    private static Hook<T> EnableHook<T>
    (
        CompSig         signature,
        T               detour,
        FeatureLifetime lifetime
    ) where T : Delegate
    {
        var hook = signature.GetHook(detour);
        lifetime.Add(hook.Dispose);
        lifetime.OnStopping(hook.Disable);
        hook.Enable();
        return hook;
    }

    private void GetFilteredUtf8StringDetour
    (
        nint        vulgarInstance,
        Utf8String* text
    )
    {
        if (AllowFilterCall || vulgarInstance == nint.Zero || text == null)
        {
            GetFilteredUtf8StringHook!.Original(vulgarInstance, text);
        }
    }

    private nint LocalMessageDisplayDetour
    (
        nint        context,
        Utf8String* source
    )
    {
        HighlightMessage(source);
        return LocalMessageDisplayHook!.Original(context, source);
    }

    private nint PartyFinderMessageDisplayDetour
    (
        nint        context,
        Utf8String* source
    )
    {
        HighlightMessage(source);
        return PartyFinderMessageDisplayHook!.Original(context, source);
    }

    private byte LookingForGroupConditionReceiveEventDetour
    (
        nint      context,
        AtkValue* values
    )
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
            catch (Exception)
            {
            }
        }

        return LookingForGroupConditionReceiveEventHook!.Original(context, values);
    }

    private void TextInputReceiveEventDetour
    (
        AtkComponentTextInput* textInput,
        AtkEventType           eventType,
        int                    eventParam,
        AtkEvent*              atkEvent,
        AtkEventData*          atkEventData
    )
    {
        TextInputReceiveEventHook!.Original(textInput, eventType, eventParam, atkEvent, atkEventData);
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
        catch (Exception)
        {
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
        catch (Exception)
        {
        }
    }

    private string GetFilteredString(string text)
    {
        var framework = Framework.Instance();
        if (framework == null || GetFilteredUtf8StringHook is null || vulgarInstanceOffset == 0)
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
            AllowFilterCall = true;
            GetFilteredUtf8StringHook.Original(vulgarInstance, utf8String);
            return utf8String->ToString();
        }
        finally
        {
            AllowFilterCall = false;
            utf8String->Dtor(true);
        }
    }

    private void ClearRuntimeReferences()
    {
        processor                                = null;
        GetFilteredUtf8StringHook                = null;
        LocalMessageDisplayHook                  = null;
        PartyFinderMessageDisplayHook            = null;
        LookingForGroupConditionReceiveEventHook = null;
        TextInputReceiveEventHook                = null;
        vulgarInstanceOffset                     = 0;
    }
}
