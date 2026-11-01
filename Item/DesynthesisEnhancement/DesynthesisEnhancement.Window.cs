using System.Diagnostics.CodeAnalysis;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Config;

namespace OmniToolbox.TreePublic;

public sealed partial class DesynthesisEnhancement
{
    internal sealed class DesynthesisEnhancementWindow : NativeAddon
    {
        private readonly DesynthesisEnhancementConfig config;
        private readonly Action                       saveConfig;
        private readonly Action                       toggleBatch;
        private readonly Func<bool>                   isRunning;
        private TextButtonNode?                       startButton;
        private bool                                  displayedRunning;

        [SetsRequiredMembers]
        public DesynthesisEnhancementWindow
        (
            DesynthesisEnhancementConfig config,
            Action                       saveConfig,
            Action                       toggleBatch,
            Func<bool>                   isRunning
        )
        {
            this.config           = config;
            this.saveConfig       = saveConfig;
            this.toggleBatch      = toggleBatch;
            this.isRunning        = isRunning;
            InternalName          = "OmniDesynthesisEnhancement";
            Title                 = OmniLoc.Get("DesynthesisEnhancementTitle");
            Subtitle              = string.Empty;
            Size                  = new(340f, 150f);
            ContentPadding        = new(8f, 4f);
            RememberClosePosition = false;
            CreateWindowNode      = static () => new WindowNode { ShowCloseButton = false };
            RespectCloseAll       = false;
            DisableClose          = true;
            EnableContextMenu     = false;
        }

        protected override unsafe void OnSetup
        (
            AtkUnitBase*   addon,
            Span<AtkValue> atkValueSpan
        )
        {
            if (DalamudServices.IsUnloading)
                return;

            var skipHQ = CreateCheckbox("Feature.DesynthesisEnhancement.SkipHQ", config.SkipHQ, value => config.SkipHQ = value);
            skipHQ.Width = MathF.Max(110f, skipHQ.Width);
            var confirm = CreateCheckbox("Feature.DesynthesisEnhancement.ConfirmWhenManual", config.ConfirmWhenManual,
                value => config.ConfirmWhenManual = value);
            var normalQuality = CreateCheckbox("Feature.DesynthesisEnhancement.GuaranteeNQ", config.GuaranteeNQ,
                value => config.GuaranteeNQ = value);
            var width = MathF.Ceiling(MathF.Max(skipHQ.Width + 12f + confirm.Width, normalQuality.Width));
            var position = ContentStartPosition;
            skipHQ.Position        = position;
            confirm.Position       = position + new Vector2(skipHQ.Width + 12f, 0f);
            normalQuality.Position = position + new Vector2(0f, 32f);
            displayedRunning       = isRunning();
            startButton            = new()
            {
                Position = position + new Vector2(0f, 64f),
                Size = new(width, 28f),
                String = OmniLoc.Get(displayedRunning ? "Feature.DesynthesisEnhancement.Stop" : "Feature.DesynthesisEnhancement.Start"),
                OnClick = toggleBatch
            };
            startButton.AttachNode(this);
            SetWindowSize(Size + new Vector2(width - ContentSize.X, 96f - ContentSize.Y));
            if (AddonHelper.TryGetByName("SalvageItemSelector", out var selector) && selector->IsAddonAndNodesReady())
                SyncPosition(selector);
        }

        protected override unsafe void OnDraw(AtkUnitBase* addon)
        {
            if (DalamudServices.IsUnloading)
                return;

            if (!AddonHelper.TryGetByName("SalvageItemSelector", out var selector) ||
                !selector->IsAddonAndNodesReady() || !selector->IsVisible)
            {
                Close();
                return;
            }

            SyncPosition(selector);
            var running = isRunning();
            if (startButton is not null && displayedRunning != running)
            {
                displayedRunning   = running;
                startButton.String = OmniLoc.Get(running ? "Feature.DesynthesisEnhancement.Stop" : "Feature.DesynthesisEnhancement.Start");
            }
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon) => startButton = null;

        internal unsafe void SyncPosition(AtkUnitBase* selector)
        {
            if (!DalamudServices.IsUnloading && InternalAddon != null)
                OmniNativeWindowAttachment.SyncBeside(this, InternalAddon, selector, 6f, 0f, preferRight: true);
        }

        private CheckboxNode CreateCheckbox
        (
            string       key,
            bool         isChecked,
            Action<bool> onChanged
        )
        {
            var text = OmniLoc.Get(key);
            var checkbox = new CheckboxNode
            {
                Height = 24f,
                String = text,
                IsChecked = isChecked
            };
            checkbox.OnClick = value =>
            {
                onChanged(value);
                saveConfig();
            };
            checkbox.Width   = MathF.Ceiling(checkbox.Height + checkbox.Label.GetTextDrawSize(text, false).X + 4f);
            checkbox.AttachNode(this);
            return checkbox;
        }
    }
}
