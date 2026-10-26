using System.Globalization;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class LandLotteryCountdown : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Interface
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "LandLotteryCountdownTitle",
            ["Description"] = "LandLotteryCountdownDescription",
            ["Feature.LandLotteryCountdown.Remaining"] = "Feature.LandLotteryCountdown.Remaining"
        });

    protected override void OnStart(FeatureLifetime activation) {
        activation.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreRefresh,
            ADDON_NAME,
            OnContentsInfoPreRefresh));
        DalamudServices.AddonLifecycle.RegisterListener(
            AddonEvent.PreRefresh,
            ADDON_NAME,
            OnContentsInfoPreRefresh);
    }

    private static void OnContentsInfoPreRefresh(AddonEvent _, AddonArgs args)
    {
        if (args is not AddonRefreshArgs refreshArgs ||
            refreshArgs.AtkValues == 0 ||
            refreshArgs.AtkValueCount == 0)
        {
            return;
        }

        var values = (AtkValue*)refreshArgs.AtkValues;
        for (uint index = 0; index + 1 < refreshArgs.AtkValueCount; index++)
        {
            if (values[index].Type != AtkValueType.Int ||
                values[index].Int != HOUSING_LOTTERY_ROW)
            {
                continue;
            }

            ref var text = ref values[index + 1];
            if (text.Type is not (AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString) ||
                text.String.Value == null)
            {
                return;
            }

            var displayText = text.String.ToString();
            if (string.IsNullOrEmpty(displayText))
            {
                return;
            }

            var remaining = GetRemainingTime(DateTime.UtcNow);
            var suffix = string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.LandLotteryCountdown.Remaining"),
                (int)remaining.TotalHours,
                remaining.Minutes);
            text.SetManagedString($"{displayText}  {suffix}");
            return;
        }
    }

    private static TimeSpan GetRemainingTime(DateTime now)
    {
        var periodStart = RecentEntryPeriodStart;
        var periodDays = ENTRY_PERIOD_DAYS;
        while (true)
        {
            var periodEnd = periodStart.AddDays(periodDays);
            if (periodEnd > now)
            {
                return periodEnd - now;
            }

            periodStart = periodEnd;
            periodDays = periodDays == ENTRY_PERIOD_DAYS ? RESULTS_PERIOD_DAYS : ENTRY_PERIOD_DAYS;
        }
    }

    #region 常量

    private const string ADDON_NAME = "ContentsInfo";

    private const int HOUSING_LOTTERY_ROW = 18;

    private const int ENTRY_PERIOD_DAYS = 5;

    private const int RESULTS_PERIOD_DAYS = 4;

    private static readonly DateTime RecentEntryPeriodStart = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);

    #endregion
}
