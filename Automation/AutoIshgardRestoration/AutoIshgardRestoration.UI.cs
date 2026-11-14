using OmniToolbox.UI.Widgets;
using System.Globalization;
using OmenTools.Dalamud;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private bool windowOpen;

    private bool windowCollapsed;

    private readonly OmniWindowSize panelSize;

    private bool windowFocusRequested;

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
        using var style = new OmniStyleScope();
        var runLabel = OmniLoc.Get(this, running || startPending
            ? "Feature.AutoIshgardRestoration.UI.Stop" : "Feature.AutoIshgardRestoration.UI.Start");
        var compactLabel = OmniLoc.Get(this, panelSize.IsCompact
            ? "Feature.AutoIshgardRestoration.UI.Expand" : "Feature.AutoIshgardRestoration.UI.Collapse");
        var buttonSize = OmniAutomationPanel.MeasureButtons(runLabel, compactLabel);
        OmniAutomationPanel.ApplySize(panelSize, new(720f, 480f), new(320f, 240f), windowCollapsed, Title, buttonSize);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground;
        OmniWindowChrome.SetNextWindowPosition("###AutoIshgardRestorationWindow");
        if (windowFocusRequested)
        {
            ImGui.SetNextWindowFocus();
            windowFocusRequested = false;
        }
        if (!OmniWindowChrome.Begin("###AutoIshgardRestorationWindow", flags, windowCollapsed, panelSize.IsCompact))
        {
            ImGui.End();
            return;
        }

        try
        {
            panelSize.SaveAfterResize();
            DrawWindowContents(ImGui.GetWindowPos(), ImGui.GetWindowSize(), buttonSize, runLabel, compactLabel);
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
        Vector2 buttonSize,
        string runLabel,
        string compactLabel
    )
    {
        var (framePosition, frameSize) = OmniWindowChrome.GetFrameBounds(windowPosition, windowSize, windowCollapsed);
        var chrome = OmniAutomationPanel.DrawChrome(
            framePosition,
            frameSize, windowCollapsed,
            Title,
            "##collapseAutoIshgardRestoration",
            "##closeAutoIshgardRestoration");

        if (chrome.CloseClicked)
        {
            windowOpen = false;
            return;
        }
        if (chrome.ApplyCollapse(ref windowCollapsed) || windowCollapsed) return;

        var layout = OmniAutomationPanel.Arrange(framePosition, frameSize, buttonSize, showCollapseButton: true, minimumSelectorWidth: 0f, compact: panelSize.IsCompact);
        var (runClicked, compactClicked) = OmniAutomationPanel.DrawButtons(layout, runLabel, running || startPending, compactLabel);
        if (panelSize.ApplyCompact(compactClicked)) return;
        if (runClicked)
            ToggleProduction();

        if (panelSize.IsCompact) return;

        ImGui.SetCursorScreenPos(layout.ContentPosition);
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding,
            OmniTheme.Scale(OmniTheme.Tokens.WindowPadding));
        using var content = ImRaii.Child(
            "##autoIshgardWindowContent",
            layout.ContentSize,
            false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.HorizontalScrollbar);
        if (!content)
        {
            return;
        }

        DrawConfigurationContents();
    }

    private void DrawConfigurationContents()
    {
        OmniAutomationPanel.DrawStatusLine(status,
            string.IsNullOrWhiteSpace(lastError) ? null :
                string.Format(CultureInfo.InvariantCulture, OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.Error"), lastError),
            showStatusLabel: true);

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
            var jobWidth = OmniTextMeasurements.Measure(jobText).X + spacing + ImGui.GetStyle().CellPadding.X * 2f;
            var ticketLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.TicketThreshold");
            var ticketWidth = OmniTextMeasurements.Measure(ticketLabel).X + inputWidth + spacing * 2f + ImGui.GetStyle().CellPadding.X * 2f;
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
            var checkboxWidth = MathF.Max(OmniTextMeasurements.Measure(stopLabel).X, OmniTextMeasurements.Measure(buyLabel).X) +
                                OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + spacing;
            var thresholdWidth = MathF.Max(
                OmniTextMeasurements.Measure(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThreshold")).X,
                OmniTextMeasurements.Measure(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThreshold")).X) +
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
                        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThresholdHelp"), referenceText: OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.StopThreshold"));
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
                        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThresholdHelp"), referenceText: OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.ScripThreshold"));
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
    }

    private void DrawScripPurchaseSettings()
    {
        EnsureScripCatalog();
        var selected = GetSelectedScripProduct();
        var quantityLabel = OmniLoc.Get(this, "Feature.AutoIshgardRestoration.UI.PurchaseQuantity");
        var quantityWidth = OmniTextMeasurements.Measure(quantityLabel).X + OmniTheme.Scale(72f) +
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
            OmniControls.HelpIcon(quantityHelp, referenceText: quantityLabel);
        }
        if (!string.IsNullOrEmpty(scripCatalogError)) ImGui.TextWrapped(scripCatalogError);
    }

    private void OpenConfigurationWindow()
    {
        if (!windowOpen)
        {
            windowCollapsed = false;
            panelSize.IsCompact = true;
        }
        windowOpen = true;
        windowFocusRequested = true;
    }
}
