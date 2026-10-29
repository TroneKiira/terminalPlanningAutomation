using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// Reads the compact comma-separated lounge-profile table used by the
    /// Grasshopper definition. Percentage-point values and fractions are both
    /// accepted, and profiles are keyed by Annex 14 code.
    /// </summary>
    public static class GateLoungeProfileTableParser
    {
        public static Dictionary<string, GateLoungeProfile> Parse(IEnumerable<string> lines)
        {
            Dictionary<string, GateLoungeProfile> profiles = new Dictionary<string, GateLoungeProfile>(
                StringComparer.OrdinalIgnoreCase);

            List<string[]> rows = (lines ?? Enumerable.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .SelectMany(line => line.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(line => line.Split(','))
                .Where(cells => cells.Length > 1)
                .ToList();

            if (rows.Count == 0)
                return profiles;

            List<Tuple<int, string>> codeColumns = new List<Tuple<int, string>>();
            string[] header = rows[0];
            for (int column = 1; column < header.Length; column++)
            {
                string value = new string((header[column] ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                    .ToUpperInvariant();
                if (value.StartsWith("CODE", StringComparison.Ordinal))
                    value = value.Substring(4);

                if (value.Length == 1 && "ABCDEF".IndexOf(value[0]) >= 0)
                {
                    profiles[value] = new GateLoungeProfile { Code = value };
                    codeColumns.Add(Tuple.Create(column, value));
                }
            }

            foreach (string[] row in rows.Skip(1))
            {
                string label = new string((row[0] ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                    .ToLowerInvariant();
                string field = ProfileField(label);
                if (field == null)
                    continue;

                foreach (Tuple<int, string> column in codeColumns)
                {
                    if (column.Item1 >= row.Length || !double.TryParse(
                        row[column.Item1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double value))
                        continue;

                    GateLoungeProfile profile = profiles[column.Item2];
                    switch (field)
                    {
                        case "seats": profile.Seats = Math.Max(0, (int)Math.Round(value)); break;
                        case "load": profile.LoadFactor = Share(value); break;
                        case "seated": profile.SeatedPct = Share(value); break;
                        case "standing": profile.StandingPct = Share(value); break;
                        case "queue": profile.BoardingQueuePct = Share(value); break;
                        case "nextseat": profile.NextSeatAllowancePct = Share(value); break;
                        case "open": profile.OpenLoungeReductionPct = Share(value); break;
                        case "podiums": profile.PodiumsPerLounge = Math.Max(0, (int)Math.Round(value)); break;
                    }
                }
            }

            return profiles;
        }

        private static string ProfileField(string label)
        {
            if (label.Contains("averageseats") || label == "seats") return "seats";
            if (label.Contains("loadfactor")) return "load";
            if (label.StartsWith("seated")) return "seated";
            if (label.StartsWith("standing")) return "standing";
            if (label.Contains("nextseat")) return "nextseat";
            if (label.Contains("openlounge")) return "open";
            if (label.Contains("boardingpodium")) return "podiums";
            if (label.Contains("boardingqueue")) return "queue";
            return null;
        }

        private static double Share(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            double share = value > 1.0 ? value / 100.0 : value;
            return Math.Min(1.0, Math.Max(0.0, share));
        }
    }
}
