using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Diagnostics
{
    /// <summary>
    /// Resolves a service type from a user label or from the history keys the
    /// facility solver writes on the passengers it processed.
    /// </summary>
    public static class DiagnosticsServiceKindParser
    {
        public static DiagnosticsServiceKind Parse(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return DiagnosticsServiceKind.Unknown;

            string key = Normalize(label);

            if (key.Length == 0)
                return DiagnosticsServiceKind.Unknown;

            if (key.StartsWith("terminalentry", StringComparison.Ordinal) ||
                key == "entry" ||
                key.StartsWith("entryqueue", StringComparison.Ordinal))
                return DiagnosticsServiceKind.TerminalEntry;

            if (key.StartsWith("checkin", StringComparison.Ordinal) ||
                key.StartsWith("checkin", StringComparison.Ordinal) ||
                key == "check" ||
                key.StartsWith("checkincounter", StringComparison.Ordinal))
                return DiagnosticsServiceKind.CheckIn;

            if (key.StartsWith("bagdrop", StringComparison.Ordinal) ||
                key.StartsWith("baggage", StringComparison.Ordinal) ||
                key == "bag")
                return DiagnosticsServiceKind.BagDrop;

            if (key.StartsWith("security", StringComparison.Ordinal) ||
                key.StartsWith("securityscreening", StringComparison.Ordinal) ||
                key == "sec")
                return DiagnosticsServiceKind.Security;

            if (key.StartsWith("emigration", StringComparison.Ordinal) ||
                key.StartsWith("outboundimmigration", StringComparison.Ordinal) ||
                key == "emg")
                return DiagnosticsServiceKind.Emigration;

            if (key.StartsWith("immigration", StringComparison.Ordinal) ||
                key.StartsWith("inboundimmigration", StringComparison.Ordinal) ||
                key == "imm")
                return DiagnosticsServiceKind.Immigration;

            if (key.StartsWith("customshand", StringComparison.Ordinal) ||
                key == "customs" ||
                key.StartsWith("handbaggage", StringComparison.Ordinal) ||
                key == "chb")
                return DiagnosticsServiceKind.Customs;

            if (key.StartsWith("verification", StringComparison.Ordinal) ||
                key.StartsWith("passengerverification", StringComparison.Ordinal) ||
                key == "verify" ||
                key == "ver")
                return DiagnosticsServiceKind.Verification;

            if (key.StartsWith("dwell", StringComparison.Ordinal) ||
                key.StartsWith("departurelounge", StringComparison.Ordinal) ||
                key.StartsWith("retail", StringComparison.Ordinal) ||
                key.StartsWith("lounge", StringComparison.Ordinal))
                return DiagnosticsServiceKind.Dwell;

            if (key.StartsWith("boarding", StringComparison.Ordinal) ||
                key.StartsWith("gate", StringComparison.Ordinal))
                return DiagnosticsServiceKind.Boarding;

            if (key.StartsWith("transferdesk", StringComparison.Ordinal) ||
                key == "desk" ||
                key.StartsWith("transfercounter", StringComparison.Ordinal))
                return DiagnosticsServiceKind.TransferDesk;

            if (key.StartsWith("baggagerecheck", StringComparison.Ordinal) ||
                key.StartsWith("recheck", StringComparison.Ordinal) ||
                key.StartsWith("bagrecheck", StringComparison.Ordinal))
                return DiagnosticsServiceKind.BaggageRecheck;

            return DiagnosticsServiceKind.Unknown;
        }

        /// <summary>
        /// Infers the service kind from the history key present on every
        /// processed passenger. This works for raw QueueResults, which are what
        /// the facility components expose on their Queue Results output.
        /// </summary>
        public static DiagnosticsServiceKind InferFromQueue(QueueResults queue)
        {
            if (queue == null || queue.ProcessedPassengers == null)
                return DiagnosticsServiceKind.Unknown;

            List<Passenger> processed = queue.ProcessedPassengers
                .Where(p => p != null)
                .ToList();

            if (processed.Count == 0)
                return DiagnosticsServiceKind.Unknown;

            Dictionary<FacilityType, int> counts = new Dictionary<FacilityType, int>();

            foreach (Passenger passenger in processed)
            {
                if (passenger.History == null)
                    continue;

                foreach (FacilityType key in passenger.History.Keys)
                {
                    if (!counts.ContainsKey(key))
                        counts[key] = 0;

                    counts[key]++;
                }
            }

            if (counts.Count == 0)
                return DiagnosticsServiceKind.Unknown;

            // Prefer a key that every processed passenger carries. A result
            // normally writes exactly one facility history, so the intersection
            // identifies the facility that actually ran.
            List<FacilityType> common = counts
                .Where(pair => pair.Value == processed.Count)
                .Select(pair => pair.Key)
                .ToList();

            if (common.Count == 1)
                return FromFacilityType(common[0]);

            if (common.Count > 1)
            {
                // A merged result is unusual. Prefer the most specific service
                // over the transfer-only aliases.
                foreach (FacilityType candidate in new[]
                {
                    FacilityType.CheckIn,
                    FacilityType.TerminalEntry,
                    FacilityType.BagDrop,
                    FacilityType.Security,
                    FacilityType.Emigration,
                    FacilityType.Immigration,
                    FacilityType.Customs,
                    FacilityType.PassengerVerification,
                    FacilityType.DepartureLounge,
                    FacilityType.Boarding,
                    FacilityType.TransferDesk,
                    FacilityType.BaggageRecheck
                })
                {
                    if (common.Contains(candidate))
                        return FromFacilityType(candidate);
                }
            }

            FacilityType mostCommon = counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => pair.Key)
                .First();

            return FromFacilityType(mostCommon);
        }

        public static DiagnosticsServiceKind Resolve(QueueResults queue, string label)
        {
            DiagnosticsServiceKind fromQueue = InferFromQueue(queue);
            DiagnosticsServiceKind fromLabel = Parse(label);

            if (fromQueue != DiagnosticsServiceKind.Unknown)
                return fromQueue;

            return fromLabel;
        }

        public static DiagnosticsServiceKind FromFacilityType(FacilityType type)
        {
            switch (type)
            {
                case FacilityType.TerminalEntry:
                    return DiagnosticsServiceKind.TerminalEntry;
                case FacilityType.CheckIn:
                    return DiagnosticsServiceKind.CheckIn;
                case FacilityType.BagDrop:
                    return DiagnosticsServiceKind.BagDrop;
                case FacilityType.Security:
                    return DiagnosticsServiceKind.Security;
                case FacilityType.Emigration:
                    return DiagnosticsServiceKind.Emigration;
                case FacilityType.Immigration:
                    return DiagnosticsServiceKind.Immigration;
                case FacilityType.Customs:
                    return DiagnosticsServiceKind.Customs;
                case FacilityType.PassengerVerification:
                    return DiagnosticsServiceKind.Verification;
                case FacilityType.DepartureLounge:
                    return DiagnosticsServiceKind.Dwell;
                case FacilityType.Boarding:
                    return DiagnosticsServiceKind.Boarding;
                case FacilityType.TransferDesk:
                    return DiagnosticsServiceKind.TransferDesk;
                case FacilityType.BaggageRecheck:
                    return DiagnosticsServiceKind.BaggageRecheck;
                default:
                    return DiagnosticsServiceKind.Unknown;
            }
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }
    }
}
