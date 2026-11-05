using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Canonical facility names and tag matching for pool plans and
    /// service-time rows.
    ///
    /// A row's <c>Facility</c> tag decides which facility consumes it. Tags
    /// are matched leniently: case, spaces, hyphens and the planning-table
    /// spellings ("Security Check", "Departure Emigration", …) all resolve to
    /// the canonical name, so a row is never silently dropped because of
    /// spelling. A tag that matches nothing is reported by the resolver, not
    /// ignored.
    /// </summary>
    public static class FacilityName
    {
        /// <summary>Canonical name for terminal entry (manual + DigiYatra).</summary>
        public const string TerminalEntry = "Terminal Entry";

        /// <summary>Canonical name for check-in (counters, kiosks).</summary>
        public const string CheckIn = "Check-In";

        /// <summary>Canonical name for the bag-drop stage.</summary>
        public const string BagDrop = "Bag Drop";

        /// <summary>Canonical name for security screening.</summary>
        public const string Security = "Security";

        /// <summary>Canonical name for outbound passport control.</summary>
        public const string Emigration = "Emigration";

        /// <summary>Canonical name for inbound passport control.</summary>
        public const string Immigration = "Immigration";

        /// <summary>Canonical name for the transfer desk.</summary>
        public const string TransferDesk = "Transfer Desk";

        /// <summary>Canonical name for baggage recheck.</summary>
        public const string BaggageRecheck = "Baggage Recheck";

        /// <summary>Every canonical name, for warning text.</summary>
        public static readonly string[] All =
        {
            TerminalEntry, CheckIn, BagDrop, Security, Emigration,
            Immigration, TransferDesk, BaggageRecheck
        };

        /// <summary>Normalized alias -> canonical name.</summary>
        private static readonly Dictionary<string, string> Aliases = BuildAliases();

        private static Dictionary<string, string> BuildAliases()
        {
            Dictionary<string, string> aliases =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Add(string canonical, params string[] spellings)
            {
                foreach (string spelling in spellings)
                {
                    string key = Normalize(spelling);

                    if (!string.IsNullOrEmpty(key) && !aliases.ContainsKey(key))
                        aliases[key] = canonical;
                }
            }

            Add(TerminalEntry, "Terminal Entry", "Terminal Entry Gates", "TerminalEntry",
                "Entry");
            Add(CheckIn, "Check-In", "Check In", "CheckIn", "Check-in Area",
                "Check In Area", "Departure CheckIn");
            Add(BagDrop, "Bag Drop", "BagDrop", "Baggage Drop",
                "Self-service Bag Drop", "Check-in Baggage");
            Add(Security, "Security", "Security Check", "Security Screening",
                "Security Checkpoint", "Security Check - ATRS", "Security Checkpoint ATRS");
            Add(Emigration, "Emigration", "Departure Emigration",
                "Outbound Passport Control", "Outbound Immigration");
            Add(Immigration, "Immigration", "Arrival Immigration",
                "Inbound Passport Control", "Arrival Passport Control");
            Add(TransferDesk, "Transfer Desk", "Transfers Desk", "TransferDesk",
                "Transfer Counter");
            Add(BaggageRecheck, "Baggage Recheck", "Bag Recheck", "BaggageRecheck",
                "Transfer Baggage Recheck", "Baggage Re-Check");

            return aliases;
        }

        /// <summary>
        /// Resolves a user tag to its canonical facility name, or null when the
        /// tag matches nothing. Matching ignores case, spaces, hyphens and
        /// underscores, so "check in area" and "CheckIn" both hit Check-In.
        /// </summary>
        public static string Canonicalize(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            string key = Normalize(tag);

            return key != null && Aliases.TryGetValue(key, out string canonical)
                ? canonical
                : null;
        }

        /// <summary>
        /// True when the tag identifies the facility, leniently: canonical
        /// names, planning-table spellings and casing variants all count.
        /// </summary>
        public static bool Matches(string tag, string facility)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(facility))
                return false;

            string canonicalTag = Canonicalize(tag);
            string canonicalFacility = Canonicalize(facility);

            return canonicalTag != null && canonicalFacility != null &&
                   string.Equals(canonicalTag, canonicalFacility, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The valid names, formatted for a warning message.</summary>
        public static string DescribeValid()
        {
            return string.Join(", ", All);
        }

        private static string Normalize(string tag)
        {
            if (tag == null)
                return null;

            char[] buffer = new char[tag.Length];
            int count = 0;

            foreach (char c in tag)
            {
                if (char.IsLetterOrDigit(c))
                    buffer[count++] = char.ToLowerInvariant(c);
            }

            return count == 0 ? null : new string(buffer, 0, count);
        }
    }
}
