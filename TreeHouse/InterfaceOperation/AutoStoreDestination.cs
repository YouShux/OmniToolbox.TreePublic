using System.Globalization;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal enum AutoStoreDestinationKind
{
    Saddlebag,
    FreeCompanyChest,
    GlamourDresser,
    Armoire,
    Retainer
}

internal readonly record struct AutoStoreDestination(
    AutoStoreDestinationKind Kind,
    int RetainerIndex = -1)
{
    public static bool TryParse(string text, out AutoStoreDestination destination)
    {
        text = text.Trim();
        if (Contains(text, "陆行鸟背包") ||
            Contains(text, "陸行鳥背包") ||
            Contains(text, "陆行鸟鞍囊") ||
            Contains(text, "陸行鳥鞍囊") ||
            Contains(text, "鞍囊") ||
            Contains(text, "鸟包") ||
            Contains(text, "鳥包") ||
            Contains(text, "saddlebag"))
        {
            destination = new(AutoStoreDestinationKind.Saddlebag);
            return true;
        }

        if (Contains(text, "部队箱") || Contains(text, "部隊箱") || Contains(text, "free company chest"))
        {
            destination = new(AutoStoreDestinationKind.FreeCompanyChest);
            return true;
        }

        if (Contains(text, "投影台") || Contains(text, "投影臺") || Contains(text, "glamour dresser"))
        {
            destination = new(AutoStoreDestinationKind.GlamourDresser);
            return true;
        }

        if (Contains(text, "收藏柜") || Contains(text, "收藏櫃") || Contains(text, "armoire"))
        {
            destination = new(AutoStoreDestinationKind.Armoire);
            return true;
        }

        if (!Contains(text, "雇员") && !Contains(text, "雇員") && !Contains(text, "retainer"))
        {
            destination = default;
            return false;
        }

        var numberStart = -1;
        var numberLength = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                if (numberStart >= 0)
                {
                    break;
                }

                continue;
            }

            if (numberStart < 0)
            {
                numberStart = index;
            }

            numberLength++;
        }

        if (numberStart < 0)
        {
            destination = new(AutoStoreDestinationKind.Retainer);
            return true;
        }

        if (int.TryParse(
                text.AsSpan(numberStart, numberLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var retainerIndex) &&
            retainerIndex is >= 1 and <= 10)
        {
            destination = new(AutoStoreDestinationKind.Retainer, retainerIndex - 1);
            return true;
        }

        destination = default;
        return false;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
