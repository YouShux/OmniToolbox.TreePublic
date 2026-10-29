using System.Globalization;
using OmenTools.Dalamud;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private bool windowOpen;

    private bool windowExpanded;

    private bool windowCollapsed;

    private bool windowSizePending;

    private float lastWindowScale;

    private Vector2 lastViewportSize;

    public override bool DrawSettings()
    {
        if (OmniControls.SmallButton(Title, false)) OpenConfigurationWindow();
        return false;
    }

    private void DrawConfigurationWindow()
    {
        if (!windowOpen)
        {
            return;
        }

        using var font = Fonts.GetUIFont().Push();
        using var placement = new OmniWindowPlacement.WindowScope("###AutoIshgardRestorationWindow");
        using var style = new ComicStyleScope();
        var inset = OmniTheme.WindowInset();
        var frameInset = OmniTheme.ChromeFrameInset();
        var buttonSize = Vector2.Max(
            OmniControls.CompactButtonSize(OmniLoc.Get(this, running || startPending
                ? "Feature.AutoIshgardRestoration.UI.Stop" : "Feature.AutoIshgardRestoration.UI.Start"), 80f),
            OmniControls.CompactButtonSize(OmniLoc.Get(this, windowExpanded
                ? "Feature.AutoIshgardRestoration.UI.Collapse" : "Feature.AutoIshgardRestoration.UI.Expand"), 80f));
        var compactControlsWidth = buttonSize.X * 2f + OmniTheme.Scale(8f) +
                                   (inset + frameInset) * 2f;
        var compactWidth = MathF.Max(compactControlsWidth,
            ImGui.CalcTextSize(Title).X +
            OmniTheme.TitleExpandIconRight() * 2f + inset * 2f);
        var compactSize = new Vector2(compactWidth,
            OmniTheme.TitleBarHeight() + buttonSize.Y + (inset + frameInset) * 2f);
        if (!windowExpanded && !windowCollapsed && !string.IsNullOrWhiteSpace(lastError))
        {
            var errorWidth = MathF.Max(
                1f, OmniTheme.ClampWindowSize(compactSize).X - (inset + frameInset) * 2f);
            compactSize.Y += ImGui.CalcTextSize(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Error"), lastError), false, errorWidth).Y +
                             ImGui.GetStyle().ItemSpacing.Y;
        }

        var viewportSize = OmniWindowPlacement.GetWorkArea("###AutoIshgardRestorationWindow").Size;
        var applyWindowSize = !windowExpanded || windowCollapsed || windowSizePending ||
                              MathF.Abs(lastWindowScale - OmniTheme.ScaleValue) > 0.001f ||
                              lastViewportSize != viewportSize;
        var targetSize = windowExpanded
            ? OmniTheme.Scale(config.ExpandedWindowSize)
            : compactSize;
        if (windowCollapsed)
            targetSize = OmniTheme.CollapsedWindowSize(windowExpanded ? targetSize.X : compactWidth);
        if (windowExpanded && !windowCollapsed)
            ImGui.SetNextWindowSizeConstraints(OmniTheme.ClampWindowSize(OmniTheme.Scale(new Vector2(320f, 240f))),
                viewportSize);
        if (applyWindowSize)
        {
            ImGui.SetNextWindowSize(OmniTheme.ClampWindowSize(targetSize), ImGuiCond.Always);
            windowSizePending = false;
        }
        lastWindowScale  = OmniTheme.ScaleValue;
        lastViewportSize = viewportSize;
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground;
        if (!windowExpanded || windowCollapsed) flags |= ImGuiWindowFlags.NoResize;
        OmniWindowChrome.SetNextWindowPosition("###AutoIshgardRestorationWindow");
        if (!ImGui.Begin("###AutoIshgardRestorationWindow", flags))
        {
            ImGui.End();
            return;
        }

        try
        {
            if (windowExpanded && !windowCollapsed && !applyWindowSize && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var actualSize = OmniTheme.PreserveWindowSize(ImGui.GetWindowSize(), targetSize);
                var size = OmniTheme.Unscale(actualSize);
                if (float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0f && size.Y > 0f &&
                    (MathF.Abs(actualSize.X - targetSize.X) > 1f || MathF.Abs(actualSize.Y - targetSize.Y) > 1f))
                {
                    config.ExpandedWindowSize = size;
                    saveConfig();
                }
            }
            DrawWindowContents(ImGui.GetWindowPos(), ImGui.GetWindowSize(), buttonSize);
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawWindowContents
    (
        Vector2 windowPosition,
        Vector2 windowSize,
        Vector2 buttonSize
    )
    {
        var (framePosition, frameSize) = OmniWindowChrome.GetFrameBounds(windowPosition, windowSize, windowCollapsed);
        var chrome = OmniWindowChrome.Draw(
            framePosition,
            frameSize,
            windowCollapsed,
            Title,
            "##collapseAutoIshgardRestoration",
            "##closeAutoIshgardRestoration");
        if (chrome.ToggleCollapse)
        {
            windowCollapsed   = !windowCollapsed;
            windowSizePending = true;
        }

        if (chrome.CloseClicked)
        {
            windowOpen = false;
        }

        if (!windowOpen || windowCollapsed || chrome.ToggleCollapse)
        {
            return;
        }

        var (bodyPosition, bodySize) = OmniWindowChrome.GetContentBounds(framePosition, frameSize);
        if (windowExpanded)
            OmniControls.DrawPanelBackground(bodyPosition, bodySize, OmniTheme.Tokens.Surface);
        var controlPadding = windowExpanded ? OmniTheme.Scale(OmniTheme.Tokens.WindowPadding) : Vector2.Zero;
        ImGui.SetCursorScreenPos(bodyPosition + controlPadding);
        DrawWindowActionRow(MathF.Max(1f, bodySize.X - controlPadding.X * 2f), buttonSize);
        if (windowSizePending)
            return;
        if (!windowExpanded)
        {
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                using var wrap = ImRaii.TextWrapPos(ImGui.GetCursorPosX() + bodySize.X);
                ImGui.TextUnformatted(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Error"), lastError));
            }

            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(bodyPosition.X, ImGui.GetCursorScreenPos().Y + controlPadding.Y));
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding,
            OmniTheme.Scale(OmniTheme.Tokens.WindowPadding));
        var remainingHeight = MathF.Max(1f, bodyPosition.Y + bodySize.Y - ImGui.GetCursorScreenPos().Y);
        using var content = ImRaii.Child(
            "##autoIshgardWindowContent",
            new Vector2(bodySize.X, remainingHeight),
            false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.HorizontalScrollbar);
        if (!content)
        {
            return;
        }

        DrawConfigurationContents();
    }

    private void DrawWindowActionRow
    (
        float   availableWidth,
        Vector2 buttonSize
    )
    {
        var rowPosition = ImGui.GetCursorScreenPos();
        var primaryLabel = OmniLoc.Get(this, running || startPending
            ? "Feature.AutoIshgardRestoration.UI.Stop" : "Feature.AutoIshgardRestoration.UI.Start");
        var secondaryLabel = OmniLoc.Get(this, windowExpanded
            ? "Feature.AutoIshgardRestoration.UI.Collapse" : "Feature.AutoIshgardRestoration.UI.Expand");
        var spacing = OmniTheme.Scale(8f);
        var secondaryWidth = MathF.Min(buttonSize.X, MathF.Max(1f, (availableWidth - spacing) * 0.5f));
        var primarySize = new Vector2(MathF.Max(1f, availableWidth - secondaryWidth - spacing), buttonSize.Y);
        var secondarySize = new Vector2(secondaryWidth, buttonSize.Y);
        if (OmniControls.SmallButton(
                primaryLabel,
                running || startPending,
                primarySize))
        {
            ToggleProduction();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X + MathF.Max(0f, availableWidth - secondarySize.X),
            rowPosition.Y));
        if (OmniControls.SmallButton(secondaryLabel, false, secondarySize))
        {
            windowExpanded ^= true;
            windowSizePending = true;
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X,
            rowPosition.Y + buttonSize.Y + ImGui.GetStyle().ItemSpacing.Y));
    }

    private void DrawConfigurationContents()
    {
        var selected = FindRecipe(config.SelectedRecipeID);
        var playerState = DalamudServices.PlayerState;
        var jobID = playerState.IsLoaded ? playerState.ClassJob.RowId : 0;
        var level = playerState.IsLoaded ? playerState.Level : (short)0;
        var jobText = playerState.IsLoaded
            ? string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.CurrentJob"), GetJobName(jobID), level)
            : OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.NotCraftingJob");
        var inputWidth = OmniTheme.Scale(72f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        using (ImRaii.Disabled(running || startPending))
        {
            var jobWidth = ImGui.CalcTextSize(jobText).X + spacing + ImGui.GetStyle().CellPadding.X * 2f;
            var ticketLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.TicketThreshold");
            var ticketWidth = ImGui.CalcTextSize(ticketLabel).X + inputWidth + spacing * 2f + ImGui.GetStyle().CellPadding.X * 2f;
            var productionWidth = MathF.Max(ImGui.GetContentRegionAvail().X, jobWidth + ticketWidth + OmniTheme.Scale(180f));
            using (var table = ImRaii.Table("##ishgardProductionSettings", 3, ImGuiTableFlags.SizingStretchSame,
                       new Vector2(productionWidth, 0f)))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("##job", ImGuiTableColumnFlags.WidthFixed, jobWidth);
                    ImGui.TableSetupColumn("##recipe", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("##tickets", ImGuiTableColumnFlags.WidthFixed, ticketWidth);
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(jobText);

                    ImGui.TableNextColumn();
                    var preview = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.SwitchCraftingJob");
                    if (jobID is >= 8 and <= 15)
                        preview = selected is { } value && value.JobID == jobID
                            ? FormatRecipe(value) : OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.SelectRecipe");
                    if (OmniControls.BeginCombo("##ishgardRecipe", preview,
                            MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
                    {
                        foreach (var recipe in Recipes)
                        {
                            if (recipe.JobID != jobID)
                                continue;

                            var available = recipe.Level <= level;
                            using var disabled = ImRaii.Disabled(!available);
                            if (OmniControls.RoundedSelectable(
                                    FormatRecipe(recipe),
                                    config.SelectedRecipeID == recipe.RecipeID,
                                    size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)) && available)
                            {
                                config.SelectedRecipeID = recipe.RecipeID;
                                selected                = recipe;
                                saveConfig();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.TableNextColumn();
                    if (OmniControls.InputInt($"{ticketLabel}##ticketThreshold", ref config.TicketThreshold,
                            inputWidth))
                        config.TicketThreshold = Math.Clamp(config.TicketThreshold, 1, 10);
                    if (ImGui.IsItemDeactivatedAfterEdit()) saveConfig();
                }
            }

            var stopLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.AutoStopScrips");
            var buyLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.AutoBuyScrips");
            var checkboxWidth = MathF.Max(ImGui.CalcTextSize(stopLabel).X, ImGui.CalcTextSize(buyLabel).X) +
                                OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + spacing;
            var thresholdWidth = MathF.Max(
                ImGui.CalcTextSize(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThreshold")).X,
                ImGui.CalcTextSize(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThreshold")).X) +
                inputWidth + OmniControls.HelpIconSize().X + spacing * 3f;
            var policyWidth = MathF.Max(ImGui.GetContentRegionAvail().X,
                (checkboxWidth + thresholdWidth) * 2f + ImGui.GetStyle().CellPadding.X * 8f);
            using (var table = ImRaii.Table("##ishgardScripPolicy", 4, ImGuiTableFlags.SizingStretchSame,
                       new Vector2(policyWidth, 0f)))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("##stopMode", ImGuiTableColumnFlags.WidthFixed, checkboxWidth);
                    ImGui.TableSetupColumn("##stopThreshold", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("##purchaseMode", ImGuiTableColumnFlags.WidthFixed, checkboxWidth);
                    ImGui.TableSetupColumn("##purchaseThreshold", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextColumn();
                    var autoStop = !config.AutoBuyScrips;
                    if (OmniControls.Checkbox(stopLabel, ref autoStop))
                    {
                        config.AutoBuyScrips = !autoStop;
                        saveConfig();
                    }

                    ImGui.TableNextColumn();
                    using (ImRaii.Disabled(config.AutoBuyScrips))
                    {
                        if (OmniControls.InputInt($"{OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThreshold")}##scripStopThreshold",
                                ref config.ScripStopThreshold, inputWidth))
                            config.ScripStopThreshold = Math.Clamp(config.ScripStopThreshold, 1, 10000);
                        if (ImGui.IsItemDeactivatedAfterEdit()) saveConfig();
                        ImGui.SameLine();
                        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThresholdHelp"));
                    }

                    ImGui.TableNextColumn();
                    if (OmniControls.Checkbox(buyLabel, ref config.AutoBuyScrips))
                        saveConfig();

                    ImGui.TableNextColumn();
                    using (ImRaii.Disabled(!config.AutoBuyScrips))
                    {
                        if (OmniControls.InputInt($"{OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThreshold")}##scripThreshold",
                                ref config.ScripThreshold, inputWidth))
                            config.ScripThreshold = Math.Clamp(config.ScripThreshold, 1, 10000);
                        if (ImGui.IsItemDeactivatedAfterEdit()) saveConfig();
                        ImGui.SameLine();
                        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThresholdHelp"));
                    }
                }
            }

            using (ImRaii.Disabled(!config.AutoBuyScrips))
                DrawScripPurchaseSettings();
        }

        ImGui.Separator();
        var balance = ReadSkybuildersScrips();
        var pendingScrips = selected is { } current ? ReadInventory(current.ItemID).PendingScrips : 0;
        ImGui.TextWrapped(string.Format(CultureInfo.InvariantCulture,
            OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripBalance"), balance,
            Math.Min(10000L, balance + (long)pendingScrips)));

        UpdateVoucherCount(DateTime.UtcNow);
        ImGui.TextUnformatted(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.VoucherCount"), FormatVoucherCount(voucherCount, voucherLimit)));
        ImGui.TextWrapped(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Status"), status));
        if (!string.IsNullOrWhiteSpace(lastError))
            ImGui.TextWrapped(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Error"), lastError));
    }

    private void DrawScripPurchaseSettings()
    {
        EnsureScripCatalog();
        var selected = GetSelectedScripProduct();
        var quantityLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseQuantity");
        var quantityWidth = ImGui.CalcTextSize(quantityLabel).X + OmniTheme.Scale(72f) +
                            OmniControls.HelpIconSize().X + ImGui.GetStyle().ItemSpacing.X * 3f +
                            ImGui.GetStyle().CellPadding.X * 2f;
        var purchaseWidth = MathF.Max(ImGui.GetContentRegionAvail().X, quantityWidth + OmniTheme.Scale(180f));
        using var table = ImRaii.Table("##ishgardPurchaseSettings", 2, ImGuiTableFlags.SizingStretchSame,
            new Vector2(purchaseWidth, 0f));
        if (!table) return;
        ImGui.TableSetupColumn("##product", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##quantity", ImGuiTableColumnFlags.WidthFixed, quantityWidth);
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseItem"));
        ImGui.SameLine();
        var popupHeight = MathF.Max(1f, MathF.Min(OmniTheme.Scale(360f), OmniWindowPlacement.GetWorkArea("###AutoIshgardRestorationWindow").Size.Y * 0.8f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(0f, popupHeight), new Vector2(float.MaxValue, popupHeight));
        var preview = selected is null ? OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.SelectPurchaseItem")
            : string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ProductPrice"), selected.Name, selected.Price);
        if (OmniControls.BeginCombo("##ishgardPurchaseItem", preview, MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
        {
            ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            var searchChanged = OmniControls.InputTextWithHint(
                "##scripProductSearch", OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.SearchItem"), ref scripProductSearch, 128,
                ImGui.GetContentRegionAvail().X);
            ImGui.Separator();
            var productSelected = false;
            using (var results = ImRaii.Child("##scripProductResults",
                       new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y))))
            {
                if (results)
                {
                    if (searchChanged) ImGui.SetScrollY(0f);
                    var hasMatches = false;
                    var search = scripProductSearch.Trim();
                    ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                    foreach (var product in scripProducts)
                    {
                        if (!product.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                        hasMatches = true;
                        if (OmniControls.RoundedSelectable(string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ProductPrice"), product.Name, product.Price) + $"##{product.ShopID}-{product.ItemID}",
                                selected == product, size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)))
                        {
                            config.ScripShopID   = product.ShopID;
                            config.ScripItemID   = product.ItemID;
                            selected             = product;
                            config.ScripQuantity = Math.Min(Math.Max(1, config.ScripQuantity), Math.Max(1, GetConfiguredScripLimit(product)));
                            saveConfig();
                            productSelected = true;
                        }
                    }
                    if (!hasMatches) ImGui.TextDisabled(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.NoMatchingItems"));
                }
            }
            if (productSelected) ImGui.CloseCurrentPopup();
            ImGui.EndCombo();
        }

        ImGui.TableNextColumn();
        var limit = selected is null ? 0 : GetConfiguredScripLimit(selected);
        var quantity = limit == 0 ? 0 : Math.Clamp(config.ScripQuantity, 1, limit);
        using (ImRaii.Disabled(limit == 0))
        {
            if (OmniControls.InputInt($"{quantityLabel}##scripQuantity", ref quantity, OmniTheme.Scale(72f)) && limit > 0)
                config.ScripQuantity = Math.Clamp(quantity, 1, limit);
            if (ImGui.IsItemDeactivatedAfterEdit()) saveConfig();
            ImGui.SameLine();
            var quantityHelp = string.Format(CultureInfo.InvariantCulture,
                OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseLimit"), limit);
            if (selected is not null)
                quantityHelp += "\n" + string.Format(CultureInfo.InvariantCulture,
                    OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseCost"), (long)quantity * selected.Price);
            OmniControls.HelpIcon(quantityHelp);
        }
        if (!string.IsNullOrEmpty(scripCatalogError)) ImGui.TextWrapped(scripCatalogError);
    }

    private void OpenConfigurationWindow()
    {
        windowOpen        = true;
        windowExpanded    = false;
        windowCollapsed   = false;
        windowSizePending = true;
    }
}
