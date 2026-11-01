using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OmenTools.Info.Lumina;
using HouseTerritory = OmenTools.Interop.Game.ExecuteCommand.Implementations.HousingCommand.HouseTerritory;

namespace OmniToolbox.Modules;

public sealed partial class PartyFinderHousingTravel
{
    internal static class PartyFinderHousingAddressParser
    {
        internal static IReadOnlyList<Address> Parse
        (
            string text,
            uint   fallbackWorldID
        )
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var normalized = new StringBuilder(text.Length);
            foreach (var character in text.Normalize(NormalizationForm.FormKC))
            {
                normalized.Append(char.GetUnicodeCategory(character) == UnicodeCategory.DashPunctuation ||
                                  character is '−' or '~' ? '-' : character);
            }

            var addresses = new List<Address>();
            var worldID = fallbackWorldID;
            foreach (Match match in TokenPattern.Matches(normalized.ToString()))
            {
                if (match.Groups["reset"].Success)
                {
                    worldID = fallbackWorldID;
                    continue;
                }

                if (match.Groups["world"].Success)
                {
                    worldID = WorldAliases[match.Value];
                    continue;
                }

                var ward = int.Parse(match.Groups["ward"].Value, CultureInfo.InvariantCulture);
                var plot = int.Parse(match.Groups["plot"].Value, CultureInfo.InvariantCulture);
                if (ward is < 1 or > 30 || plot is < 1 or > 60 || !Sheets.CNWorlds.ContainsKey(worldID))
                    continue;

                var address = new Address(worldID, (uint)Territories[match.Groups["territory"].Value], ward, plot);
                if (!addresses.Contains(address))
                    addresses.Add(address);
            }

            return addresses;
        }

        internal readonly record struct Address
        (
            uint WorldID,
            uint TerritoryID,
            int  Ward,
            int  Plot
        );

        private static readonly Dictionary<string, HouseTerritory> Territories = new(StringComparer.Ordinal)
        {
            ["海雾村"] = HouseTerritory.Mist,
            ["海雾"] = HouseTerritory.Mist,
            ["海都"] = HouseTerritory.Mist,
            ["海"] = HouseTerritory.Mist,
            ["薰衣草苗圃"] = HouseTerritory.LavenderBeds,
            ["薰衣草"] = HouseTerritory.LavenderBeds,
            ["苗圃"] = HouseTerritory.LavenderBeds,
            ["森都"] = HouseTerritory.LavenderBeds,
            ["森"] = HouseTerritory.LavenderBeds,
            ["高脚孤丘"] = HouseTerritory.Goblet,
            ["孤丘"] = HouseTerritory.Goblet,
            ["沙都"] = HouseTerritory.Goblet,
            ["沙"] = HouseTerritory.Goblet,
            ["白银乡"] = HouseTerritory.Shirogane,
            ["白银"] = HouseTerritory.Shirogane,
            ["白"] = HouseTerritory.Shirogane,
            ["穹顶皓天"] = HouseTerritory.Empyreum,
            ["天穹街"] = HouseTerritory.Empyreum,
            ["天穹"] = HouseTerritory.Empyreum,
            ["雪都"] = HouseTerritory.Empyreum,
            ["雪"] = HouseTerritory.Empyreum
        };

        private static readonly Dictionary<string, uint> WorldAliases = Sheets.CNWorlds.Values
            .SelectMany(world => new[] { world.Name.ToString(), world.Name.ToString()[..2] }
                .Distinct()
                .Select(name => (Name: name, ID: world.RowId)))
            .GroupBy(world => world.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().ID, StringComparer.Ordinal);

        private static readonly Regex TokenPattern = new(
            $@"(?<territory>{string.Join('|', Territories.Keys.OrderByDescending(name => name.Length).Select(Regex.Escape))})" +
            @"\s*(?:第\s*)?(?<ward>[0-9]{1,2})\s*(?:区\s*(?:第\s*)?(?<plot>[0-9]{1,2})\s*(?:号|(?=[\s\p{P}]|$))|[-#]\s*(?<plot>[0-9]{1,2})(?:\s*号)?)" +
            @"(?![0-9]|\s*[-/.]\s*[0-9]|\s*(?:[wWkK]|万|千|元|金币))" +
            $@"|(?<world>{string.Join('|', WorldAliases.Keys.OrderByDescending(name => name.Length).Select(Regex.Escape))})" +
            @"|(?<reset>[;\r\n。])",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    }
}
