using System.Linq;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal static class AutoSortItemsPanel
{
    public static bool Draw(AutoSortItemsConfig config)
    {
        AutoSortItems.EnsureRules(config);
        var changed = false;
        using (var optionsTable = ImRaii.Table(
                   "##autoSortItemsOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (optionsTable)
            {
                ImGui.TableSetupColumn("##sortArmouryOnJobChange", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##autoMerge", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##sendChat", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##sendNotification", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var sortArmouryOnJobChange = config.SortArmouryOnJobChange;
                if (OmniControls.Checkbox(
                        OmniLoc.Get("Feature.AutoSortItems.SortArmouryOnJobChange"),
                        ref sortArmouryOnJobChange))
                {
                    config.SortArmouryOnJobChange = sortArmouryOnJobChange;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var autoMerge = config.AutoMerge;
                if (OmniControls.Checkbox(OmniLoc.Get("Feature.AutoSortItems.AutoMerge"), ref autoMerge))
                {
                    config.AutoMerge = autoMerge;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var sendChat = config.SendChat;
                if (OmniControls.Checkbox(OmniLoc.Get("Feature.AutoSortItems.SendChat"), ref sendChat))
                {
                    config.SendChat = sendChat;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var sendNotification = config.SendNotification;
                if (OmniControls.Checkbox(
                        OmniLoc.Get("Feature.AutoSortItems.SendNotification"),
                        ref sendNotification))
                {
                    config.SendNotification = sendNotification;
                    changed = true;
                }
            }
        }

        var rowContentHeight = MathF.Max(
            OmniTheme.CheckboxSize(),
            MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        var deleteLabel = OmniLoc.Get("Feature.AutoSortItems.DeleteRule");
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var tabLabel = OmniLoc.Get("Feature.AutoSortItems.Tab");
        var tabWidth = OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(tabLabel).X;
        var rules = config.Rules!;
        var enableColumnWidth = OmniTheme.CheckboxSize() + ImGui.GetStyle().CellPadding.X * 2f;
        var visibleRowCount = Math.Min(
            6,
            rules.Count + rules
                .Where(rule => rule.Category is not null)
                .Select(rule => rule.Category!)
                .Distinct(StringComparer.Ordinal)
                .Count());
        var tableHeight = OmniTheme.SmallButtonSize().Y +
                          rowContentHeight * visibleRowCount +
                          ImGui.GetStyle().CellPadding.Y * (visibleRowCount + 1) * 2f +
                          ImGui.GetStyle().FrameBorderSize * 2f;
        using var table = ImRaii.Table(
            "##autoSortItemsRules",
            4,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, tableHeight));
        if (table)
        {
            ImGui.TableSetupColumn("##enabled", ImGuiTableColumnFlags.WidthFixed, enableColumnWidth);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Category"),
                ImGuiTableColumnFlags.WidthStretch,
                1f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Condition"),
                ImGuiTableColumnFlags.WidthStretch,
                1.6f);
            ImGui.TableSetupColumn(
                OmniLoc.Get("Feature.AutoSortItems.Column.Actions"),
                ImGuiTableColumnFlags.WidthFixed,
                MathF.Max(deleteSize.X, tabWidth) + ImGui.GetStyle().CellPadding.X * 2f);
            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow();
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
            var allEnabled = rules.Any(rule => rule.Category is not null) &&
                             rules.Where(rule => rule.Category is not null).All(rule => rule.Enabled);
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
            if (OmniControls.Checkbox("##allEnabled", ref allEnabled))
            {
                SetAllCategoriesEnabled(config, allEnabled);
                changed = true;
            }
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Category"));
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Condition"));
            OmniControls.TableHeader(OmniLoc.Get("Feature.AutoSortItems.Column.Actions"));

            var removeIndex = -1;
            var drawnRules = new HashSet<int>();
            foreach (var category in rules
                         .Where(rule => rule.Category is not null)
                         .Select(rule => rule.Category!)
                         .Distinct(StringComparer.Ordinal)
                         .ToArray())
            {
                DrawCategoryHeader(config, category, rowContentHeight, ref changed);
                for (var index = 0; index < rules.Count; index++)
                {
                    if (rules[index].Category == category)
                    {
                        DrawRule(config, index, rowContentHeight, deleteSize, ref removeIndex, ref changed);
                        drawnRules.Add(index);
                    }
                }
            }

            for (var index = 0; index < rules.Count; index++)
            {
                if (!drawnRules.Contains(index))
                {
                    DrawRule(config, index, rowContentHeight, deleteSize, ref removeIndex, ref changed);
                }
            }

            if (removeIndex >= 0)
            {
                rules.RemoveAt(removeIndex);
                changed = true;
            }
        }

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.AutoSortItems.AddRule"), false))
        {
            config.Rules!.Add(new());
            changed = true;
        }

        return changed;
    }

    private static void DrawCategoryHeader(
        AutoSortItemsConfig config,
        string category,
        float rowContentHeight,
        ref bool changed)
    {
        ImGui.PushID(category);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableSetColumnIndex(0);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var categoryLabel = GetRuleOptionLabel(category, AutoSortItems.Categories);
        var enabled = IsCategoryEnabled(config, category);
        OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
        if (OmniControls.Checkbox($"##{category}Enabled", ref enabled))
        {
            SetCategoryEnabled(config, category, enabled);
            changed = true;
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        OmniControls.CenterTableItem(
            new Vector2(ImGui.CalcTextSize(categoryLabel).X, ImGui.GetFrameHeight()),
            rowContentHeight);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(categoryLabel);

        ImGui.TableSetColumnIndex(2);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableSetColumnIndex(3);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        if (AutoSortItems.SupportsTab(category))
        {
            var tabLabel = OmniLoc.Get("Feature.AutoSortItems.Tab");
            var tab = GetCategoryHeader(config, category).Tab;
            OmniControls.CenterTableItem(
                new Vector2(
                    OmniTheme.CheckboxSize() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(tabLabel).X,
                    OmniTheme.CheckboxSize()),
                rowContentHeight);
            if (OmniControls.Checkbox($"{tabLabel}##tab", ref tab))
            {
                SetCategoryTab(config, category, tab);
                changed = true;
            }
        }

        ImGui.PopID();
    }

    private static void DrawRule(
        AutoSortItemsConfig config,
        int index,
        float rowContentHeight,
        Vector2 deleteSize,
        ref int removeIndex,
        ref bool changed)
    {
        var rule = config.Rules![index];
        ImGui.PushID(index);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        var categoryWidth = ImGui.GetContentRegionAvail().X;
        OmniControls.CenterTableItem(new Vector2(categoryWidth, ImGui.GetFrameHeight()), rowContentHeight);
        var rowChanged = DrawRuleCombo("category", rule.Category, AutoSortItems.Categories, categoryWidth, out var category);

        ImGui.TableNextColumn();
        var conditionSpacing = ImGui.GetStyle().ItemSpacing.X;
        var conditionWidth = MathF.Max(1f, (ImGui.GetContentRegionAvail().X - conditionSpacing) * 0.5f);
        OmniControls.CenterTableItem(
            new Vector2(conditionWidth * 2f + conditionSpacing, ImGui.GetFrameHeight()),
            rowContentHeight);
        rowChanged |= DrawRuleCombo("condition", rule.Condition, AutoSortItems.Conditions, conditionWidth, out var condition);
        ImGui.SameLine(0f, conditionSpacing);
        rowChanged |= DrawRuleCombo("order", rule.Order, AutoSortItems.Orders, conditionWidth, out var order);
        if (rowChanged)
        {
            config.Rules[index] = rule with
            {
                Enabled = string.Equals(category, rule.Category, StringComparison.Ordinal)
                    ? rule.Enabled
                    : IsCategoryEnabled(config, category),
                Category = category,
                Condition = condition,
                Order = order
            };
            changed = true;
        }

        ImGui.TableNextColumn();
        OmniControls.CenterTableItem(deleteSize, rowContentHeight);
        if (OmniControls.SmallButton(
                $"{OmniLoc.Get("Feature.AutoSortItems.DeleteRule")}##delete",
                false,
                deleteSize))
        {
            removeIndex = index;
        }

        ImGui.PopID();
    }

    private static AutoSortItemsCategoryHeader GetCategoryHeader(AutoSortItemsConfig config, string category) =>
        config.CategoryHeaders!.FirstOrDefault(header => header.Category == category) ??
        new(category, category is "inventory" or "saddlebag" or "rightsaddlebag");

    private static bool IsCategoryEnabled(AutoSortItemsConfig config, string? category) =>
        category is null ||
        !config.Rules!.Any(rule => string.Equals(rule.Category, category, StringComparison.Ordinal)) ||
        config.Rules!.Any(rule =>
            rule.Enabled && string.Equals(rule.Category, category, StringComparison.Ordinal));

    private static void SetCategoryEnabled(AutoSortItemsConfig config, string category, bool enabled)
    {
        for (var index = 0; index < config.Rules!.Count; index++)
        {
            if (string.Equals(config.Rules[index].Category, category, StringComparison.Ordinal))
            {
                config.Rules[index] = config.Rules[index] with { Enabled = enabled };
            }
        }
    }

    private static void SetAllCategoriesEnabled(AutoSortItemsConfig config, bool enabled)
    {
        for (var index = 0; index < config.Rules!.Count; index++)
        {
            if (config.Rules[index].Category is not null)
            {
                config.Rules[index] = config.Rules[index] with { Enabled = enabled };
            }
        }
    }

    private static void SetCategoryTab(AutoSortItemsConfig config, string category, bool tab)
    {
        for (var index = 0; index < config.CategoryHeaders!.Count; index++)
        {
            if (config.CategoryHeaders[index].Category == category)
            {
                config.CategoryHeaders[index] = new(category, tab);
                return;
            }
        }

        config.CategoryHeaders.Add(new(category, tab));
    }

    private static string GetRuleOptionLabel(string category, IReadOnlyList<AutoSortItems.RuleOption> options) =>
        options.FirstOrDefault(option => option.Key == category) is { } option
            ? OmniLoc.Get(option.LocalizationKey)
            : OmniLoc.Get("Feature.AutoSortItems.Unset");

    private static bool DrawRuleCombo(
        string id,
        string? current,
        IReadOnlyList<AutoSortItems.RuleOption> options,
        float width,
        out string? selected)
    {
        selected = current;
        var preview = options.FirstOrDefault(option => option.Key == current) is { } selectedOption
            ? OmniLoc.Get(selectedOption.LocalizationKey)
            : OmniLoc.Get("Feature.AutoSortItems.Unset");
        if (!OmniControls.BeginCombo($"##{id}", preview, width))
        {
            return false;
        }

        var changed = false;
        foreach (var option in options)
        {
            if (ImGui.Selectable(OmniLoc.Get(option.LocalizationKey), current == option.Key))
            {
                selected = option.Key;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

}
