using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Analysis
{
    /// <summary>
    /// One facility visit in a passenger's journey, read exclusively from the
    /// passenger's <see cref="Passenger.History"/>.
    ///
    /// A visit exists exactly when the facility wrote a history record. There
    /// is no fallback to the passenger's mutable latest state, so a stage the
    /// passenger never queued at can never borrow another stage's times.
    /// </summary>
    public sealed class NarrativeEvent
    {
        public string Stage { get; set; }

        public FacilityType Facility { get; set; }

        /// <summary>True for the e-gate entry sub-event of a series security checkpoint.</summary>
        public bool IsEntryStage { get; set; }

        public double ArrivalTime { get; set; }
        public double StartServiceTime { get; set; }
        public double EndServiceTime { get; set; }
        public double WaitTime { get; set; }

        /// <summary>Server pool that served the visit, from the history record.</summary>
        public string PoolName { get; set; } = string.Empty;

        /// <summary>Processor route where the stage records one, e.g. "EGate".</summary>
        public string Route { get; set; } = string.Empty;

        public int Resource { get; set; }

        public double ServiceTime => EndServiceTime - StartServiceTime;
    }

    /// <summary>
    /// Builds a process-by-process journey narrative from passenger history.
    ///
    /// Design rules, driven by field failures where a narration printed the
    /// same verification times under Terminal Entry, Check-In and Emigration:
    ///
    ///   1. A stage is narrated with times only when its history record
    ///      exists. Every other stage of the canonical journey reads
    ///      "not visited" - never stale state from a later stage.
    ///   2. A series security checkpoint renders as two events: the e-gate
    ///      entry stage (from the record's entry fields) and the ATRS
    ///      screening stage (the record's main fields), in that order.
    ///   3. Events are emitted in chronological order of service start.
    ///   4. <see cref="ValidateChronology"/> fails a journey whose recorded
    ///      times do not fit together (start before arrival, end before
    ///      start, a stage starting before the previous one ends, waits that
    ///      exceed the recorded span).
    /// </summary>
    public static class PassengerNarrativeBuilder
    {
        /// <summary>
        /// The canonical departure-journey order, mirroring the wired chain:
        /// Terminal Entry -> Check-In -> Bag Drop -> Verification ->
        /// Emigration -> Security.
        /// </summary>
        public static readonly string[] DepartureJourneyStages =
        {
            "Terminal Entry",
            "Check-In",
            "Bag Drop",
            "Verification",
            "Emigration",
            "Security"
        };

        private const double Epsilon = 1e-9;

        private static readonly Dictionary<FacilityType, string> StageLabels =
            new Dictionary<FacilityType, string>
            {
                { FacilityType.TerminalEntry, "Terminal Entry" },
                { FacilityType.CheckIn, "Check-In" },
                { FacilityType.BagDrop, "Bag Drop" },
                { FacilityType.PassengerVerification, "Verification" },
                { FacilityType.Emigration, "Emigration" },
                { FacilityType.Security, "Security" },
                { FacilityType.Immigration, "Immigration" },
                { FacilityType.TransferDesk, "Transfer Desk" },
                { FacilityType.BaggageRecheck, "Baggage Recheck" },
                { FacilityType.DepartureLounge, "Departure Lounge" },
                { FacilityType.Boarding, "Boarding" }
            };

        /// <summary>Every visit the passenger's history records, chronologically.</summary>
        public static List<NarrativeEvent> CollectEvents(Passenger passenger)
        {
            List<NarrativeEvent> events = new List<NarrativeEvent>();

            if (passenger?.History == null)
                return events;

            foreach (KeyValuePair<FacilityType, FacilityRecord> pair in passenger.History)
            {
                FacilityRecord record = pair.Value;

                if (record == null)
                    continue;

                if (pair.Key == FacilityType.Security && record.HasEntryStage)
                {
                    events.Add(new NarrativeEvent
                    {
                        Stage = "Security — e-gate entry",
                        Facility = FacilityType.Security,
                        IsEntryStage = true,
                        ArrivalTime = record.EntryArrivalTime,
                        StartServiceTime = record.EntryStartServiceTime,
                        EndServiceTime = record.EntryEndServiceTime,
                        WaitTime = record.EntryWaitTime,
                        PoolName = record.EntryPoolName,
                        Route = passenger.SecurityEntryRoute,
                        Resource = record.EntryAssignedResource
                    });
                }

                string stage = StageLabels.TryGetValue(pair.Key, out string label)
                    ? label
                    : pair.Key.ToString();

                if (pair.Key == FacilityType.Security && record.HasEntryStage)
                    stage = "Security — ATRS screening";

                string route = pair.Key == FacilityType.TerminalEntry
                    ? passenger.TerminalEntryRoute
                    : pair.Key == FacilityType.Emigration
                        ? passenger.EmigrationRoute
                        : pair.Key == FacilityType.Security
                            ? passenger.SecurityScreeningRoute
                            : string.Empty;

                events.Add(new NarrativeEvent
                {
                    Stage = stage,
                    Facility = pair.Key,
                    ArrivalTime = record.ArrivalTime,
                    StartServiceTime = record.StartServiceTime,
                    EndServiceTime = record.EndServiceTime,
                    WaitTime = record.WaitTime,
                    PoolName = record.PoolName,
                    Route = route,
                    Resource = record.AssignedResource
                });
            }

            return events
                .OrderBy(e => e.StartServiceTime)
                .ThenBy(e => e.EndServiceTime)
                .ToList();
        }

        /// <summary>
        /// Narrates the canonical journey for one passenger. Stages without a
        /// history record read "not visited" with a REASON: either why the
        /// stage does not apply to this passenger (transfer bypass, web
        /// check-in, counter bag acceptance, wrong leg), or - the important
        /// diagnostic - "expected but missing", when the passenger's own data
        /// says the stage should have been visited but no history exists.
        /// Arrival-side stages (Immigration, Transfer Desk, Baggage Recheck)
        /// are appended when visited or expected, so nothing the passenger
        /// should have experienced is silently absent.
        /// </summary>
        public static List<string> BuildNarration(Passenger passenger)
        {
            List<string> stages = new List<string>(DepartureJourneyStages);

            if (passenger != null)
            {
                foreach (FacilityType facility in new[]
                {
                    FacilityType.Immigration,
                    FacilityType.TransferDesk,
                    FacilityType.BaggageRecheck
                })
                {
                    bool visited = passenger.History.ContainsKey(facility);
                    bool expected = DescribeExpectation(passenger, facility, out _);

                    if ((visited || expected) && !stages.Contains(StageLabels[facility]))
                        stages.Add(StageLabels[facility]);
                }

                foreach (FacilityType facility in new[]
                {
                    FacilityType.DepartureLounge,
                    FacilityType.Boarding
                })
                {
                    if (passenger.History.ContainsKey(facility) &&
                        !stages.Contains(StageLabels[facility]))
                    {
                        stages.Add(StageLabels[facility]);
                    }
                }
            }

            return BuildNarration(passenger, stages);
        }

        /// <summary>
        /// Narrates one passenger across the given stage order. A stage with
        /// several recorded events (series security) renders one line per
        /// event, in chronological order.
        /// </summary>
        public static List<string> BuildNarration(Passenger passenger, IEnumerable<string> stages)
        {
            List<string> lines = new List<string>
            {
                Header(passenger)
            };

            if (passenger == null)
                return lines;

            lines.AddRange(ContextLines(passenger));

            List<NarrativeEvent> events = CollectEvents(passenger);

            bool hasTerminalEntry = passenger.History.ContainsKey(FacilityType.TerminalEntry);
            string terminalEntryStage = StageLabels[FacilityType.TerminalEntry];

            int index = 0;

            foreach (string stage in stages ?? Array.Empty<string>())
            {
                index++;

                if (stage == terminalEntryStage)
                {
                    double walkMinutes = WalkMinutes(passenger);

                    if (hasTerminalEntry && walkMinutes > 0.0)
                    {
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "      walk carried from Terminal Entry: {0:0.00} min (data on the passenger; the next stage starts when service ends)",
                            walkMinutes));
                    }
                }

                List<NarrativeEvent> stageEvents = events
                    .Where(e => StageBelongsTo(e, stage, passenger))
                    .ToList();

                if (stageEvents.Count == 0)
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}] {1} — not visited — {2}",
                        index,
                        stage,
                        StageNote(passenger, stage)));

                    continue;
                }

                foreach (NarrativeEvent evt in stageEvents)
                    lines.Add(FormatVisit(index, evt));
            }

            return lines;
        }

        /// <summary>
        /// The narration as one multi-line string, exactly what a text output
        /// on the canvas shows. Lines are joined with "\n" so the text is
        /// identical across platforms and matches <see cref="BuildNarration"/>
        /// line for line.
        /// </summary>
        public static string ToText(Passenger passenger)
        {
            return string.Join("\n", BuildNarration(passenger));
        }

        /// <summary>
        /// Checks a passenger's recorded journey for impossible times. Returns
        /// one human-readable violation per problem; an empty list means the
        /// history fits together stage by stage.
        /// </summary>
        public static List<string> ValidateChronology(Passenger passenger)
        {
            List<string> violations = new List<string>();
            NarrativeEvent previous = null;

            foreach (NarrativeEvent evt in CollectEvents(passenger))
            {
                if (evt.StartServiceTime < evt.ArrivalTime - Epsilon)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: service starts at {1} min before the recorded arrival {2} min",
                        evt.Stage,
                        Minutes(evt.StartServiceTime),
                        Minutes(evt.ArrivalTime)));
                }

                if (evt.EndServiceTime < evt.StartServiceTime - Epsilon)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: service ends at {1} min before it starts at {2} min",
                        evt.Stage,
                        Minutes(evt.EndServiceTime),
                        Minutes(evt.StartServiceTime)));
                }

                if (evt.WaitTime < -Epsilon)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: negative wait {1}",
                        evt.Stage,
                        Seconds(evt.WaitTime)));
                }

                if (evt.WaitTime > evt.StartServiceTime - evt.ArrivalTime + Epsilon)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: wait {1} exceeds the {2} between arrival and service start",
                        evt.Stage,
                        Seconds(evt.WaitTime),
                        Seconds(evt.StartServiceTime - evt.ArrivalTime)));
                }

                if (previous != null && evt.StartServiceTime < previous.EndServiceTime - Epsilon)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: starts at {1} min before {2} ends at {3} min",
                        evt.Stage,
                        Minutes(evt.StartServiceTime),
                        previous.Stage,
                        Minutes(previous.EndServiceTime)));
                }

                previous = evt;
            }

            return violations;
        }

        // ---- helpers -------------------------------------------------------

        /// <summary>
        /// The note appended after "not visited" for one stage. Either the
        /// reason the stage does not apply to this passenger, or - when the
        /// passenger's own data says the stage should have happened - the
        /// "expected but missing" diagnostic.
        /// </summary>
        private static string StageNote(Passenger passenger, string stage)
        {
            FacilityType? facility = FacilityFromStage(stage);

            if (facility == null)
                return "no history for this stage";

            DescribeExpectation(passenger, facility.Value, out string note);

            bool expected = !string.IsNullOrEmpty(note) && note.EndsWith("(expected)", StringComparison.Ordinal);

            return expected
                ? "expected but missing — " + note.Substring(0, note.Length - "(expected)".Length) +
                  "check the facility wires, pool keys or the branch the passenger travelled"
                : note;
        }

        /// <summary>
        /// Decides whether a passenger of this description should carry a
        /// history record for one facility. Returns true when the stage is
        /// expected (and <paramref name="note"/> explains why, suffixed with
        /// the "(expected)" marker), false when the stage is legitimately
        /// bypassed or not applicable (the note is the reason).
        /// </summary>
        private static bool DescribeExpectation(Passenger passenger, FacilityType facility, out string note)
        {
            note = string.Empty;

            if (passenger == null)
                return false;

            switch (facility)
            {
                case FacilityType.TerminalEntry:
                    if (!IsDepartureLeg(passenger))
                    {
                        note = "arrival passengers do not pass the departure terminal entry";
                        return false;
                    }

                    if (passenger.Journey == PassengerJourney.Transfer)
                    {
                        note = "transfers are already airside and skip the terminal-entry pools";
                        return false;
                    }

                    note = "originating departures queue at the terminal-entry pools (expected)";
                    return true;

                case FacilityType.CheckIn:
                    if (!IsDepartureLeg(passenger))
                    {
                        note = "arrival passengers do not use check-in";
                        return false;
                    }

                    if (passenger.RequiresCheckIn)
                    {
                        note = "this passenger must present at a physical check-in processor (expected)";
                        return true;
                    }

                    if (passenger.TransferProcessing != null)
                    {
                        note = "the scenario rule marks check-in as not required";
                        return false;
                    }

                    note = DefaultChannelAssigner.IsWebChannel(passenger.Channel)
                        ? "web check-in has no queue"
                        : "online check-in; no processor visit is recorded";

                    return false;

                case FacilityType.BagDrop:
                    if (!IsDepartureLeg(passenger))
                    {
                        note = "arrival passengers do not use the bag drop";
                        return false;
                    }

                    if (passenger.RequiresSeparateBagDrop)
                    {
                        note = "the checked bag needs the separate bag-drop stage (expected)";
                        return true;
                    }

                    if (passenger.TransferProcessing != null)
                    {
                        note = "the scenario rule marks bag drop as not required";
                        return false;
                    }

                    if (!passenger.RequiresBagDrop)
                    {
                        note = "no checked baggage to drop";
                        return false;
                    }

                    note = "the bag is accepted at the check-in counter";
                    return false;

                case FacilityType.PassengerVerification:
                    note = "the wired journey feeds every passenger through the verification stage (expected)";
                    return true;

                case FacilityType.Emigration:
                    if (!IsDepartureLeg(passenger))
                    {
                        note = "arrival passengers do not use outbound emigration";
                        return false;
                    }

                    if (!IsInternational(passenger))
                    {
                        note = "domestic passengers bypass emigration";
                        return false;
                    }

                    if (TransferServiceUsageCalculator.UsesService(
                            passenger, TransferService.Emigration))
                    {
                        note = "international departures clear outbound passport control (expected)";
                        return true;
                    }

                    note = "the scenario rule marks emigration as not required (already airside)";
                    return false;

                case FacilityType.Security:
                    if (!IsDepartureLeg(passenger))
                    {
                        note = "arrival passengers do not use departure security";
                        return false;
                    }

                    if (TransferServiceUsageCalculator.UsesService(
                            passenger, TransferService.Security))
                    {
                        note = "departures clear the security checkpoint (expected)";
                        return true;
                    }

                    note = "the scenario rule marks security screening as not required";
                    return false;

                case FacilityType.Immigration:
                    if (!IsArrivalLeg(passenger))
                    {
                        note = "departure passengers do not use arrival immigration";
                        return false;
                    }

                    if (!IsInternational(passenger))
                    {
                        note = "domestic arrivals have no passport control";
                        return false;
                    }

                    if (TransferServiceUsageCalculator.UsesService(
                            passenger, TransferService.Immigration))
                    {
                        note = "international arrivals clear arrival passport control (expected)";
                        return true;
                    }

                    note = "the scenario rule marks immigration as not required";
                    return false;

                case FacilityType.TransferDesk:
                    if (!IsArrivalLeg(passenger) || passenger.Journey != PassengerJourney.Transfer)
                    {
                        note = "only arriving transfer passengers present at the transfer desk";
                        return false;
                    }

                    if (TransferServiceUsageCalculator.UsesService(
                            passenger, TransferService.TransferDesk))
                    {
                        note = "the scenario rule requires a transfer-desk presentation (expected)";
                        return true;
                    }

                    note = "the scenario rule does not require a transfer desk";
                    return false;

                case FacilityType.BaggageRecheck:
                    if (!IsArrivalLeg(passenger) || passenger.Journey != PassengerJourney.Transfer)
                    {
                        note = "only arriving transfer passengers recheck baggage";
                        return false;
                    }

                    if (TransferServiceUsageCalculator.UsesService(
                            passenger, TransferService.BaggageRecheck))
                    {
                        note = "the scenario rule requires baggage recheck (expected)";
                        return true;
                    }

                    note = "the scenario rule does not require baggage recheck";
                    return false;

                default:
                    return false;
            }
        }

        private static FacilityType? FacilityFromStage(string stage)
        {
            foreach (KeyValuePair<FacilityType, string> pair in StageLabels)
            {
                if (string.Equals(pair.Value, stage, StringComparison.Ordinal))
                    return pair.Key;
            }

            return null;
        }

        private static bool IsDepartureLeg(Passenger passenger)
        {
            return passenger.DHPType == DHPType.DomesticDeparture ||
                   passenger.DHPType == DHPType.InternationalDeparture;
        }

        private static bool IsArrivalLeg(Passenger passenger)
        {
            return passenger.DHPType == DHPType.DomesticArrival ||
                   passenger.DHPType == DHPType.InternationalArrival;
        }

        private static bool IsInternational(Passenger passenger)
        {
            return passenger.PassengerType == PassengerType.International ||
                   passenger.DHPType == DHPType.InternationalDeparture ||
                   passenger.DHPType == DHPType.InternationalArrival;
        }

        /// <summary>
        /// Journey context lines rendered under the header: the flight and its
        /// scheduled times for schedule-mode passengers, the demand window for
        /// PHP-mode passengers, the terminal-entry checkpoint arrival, the
        /// check-in window with late/early flags, and the transfer flow with
        /// its scenario-rule summary.
        /// </summary>
        private static List<string> ContextLines(Passenger passenger)
        {
            List<string> lines = new List<string>();

            bool hasFlightIdentity =
                !string.IsNullOrWhiteSpace(passenger.FlightLegId) ||
                !string.IsNullOrWhiteSpace(passenger.FlightNumber);

            bool hasScheduledDeparture =
                !double.IsNaN(passenger.ScheduledDepartureTime) &&
                passenger.ScheduledDepartureTime > 0.0;

            if (hasFlightIdentity || hasScheduledDeparture)
            {
                StringBuilder flight = new StringBuilder("  flight");

                if (!string.IsNullOrWhiteSpace(passenger.FlightNumber))
                    flight.Append(" '").Append(passenger.FlightNumber).Append('\'');

                if (!string.IsNullOrWhiteSpace(passenger.Airline))
                    flight.Append(" (").Append(passenger.Airline).Append(')');

                if (!string.IsNullOrWhiteSpace(passenger.FlightLegId))
                    flight.Append(" leg ").Append(passenger.FlightLegId);

                if (hasScheduledDeparture)
                    flight.Append(" departs at ").Append(Minutes(passenger.ScheduledDepartureTime));

                lines.Add(flight.ToString());
            }

            if (!double.IsNaN(passenger.ScheduledArrivalTime) &&
                passenger.ScheduledArrivalTime > 0.0)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  inbound flight arrives at {0}",
                    Minutes(passenger.ScheduledArrivalTime)));
            }

            if (passenger.DemandStepIndex >= 0)
            {
                StringBuilder window = new StringBuilder("  demand window step ")
                    .Append(passenger.DemandStepIndex);

                if (passenger.IsDesignStep)
                    window.Append(" (design hour)");

                if (passenger.IsWarmUp)
                    window.Append(" (warm-up)");

                if (passenger.WaveId >= 0)
                    window.Append(", wave ").Append(passenger.WaveId);

                lines.Add(window.ToString());
            }

            if (!double.IsNaN(passenger.TerminalEntryArrivalTime) &&
                !double.IsInfinity(passenger.TerminalEntryArrivalTime))
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  arrives at the terminal-entry checkpoint at {0}",
                    Minutes(passenger.TerminalEntryArrivalTime)));
            }

            if (!double.IsNaN(passenger.CheckInOpenTime) ||
                !double.IsNaN(passenger.CheckInLastAdmissionTime))
            {
                StringBuilder window = new StringBuilder("  check-in window:");

                if (!double.IsNaN(passenger.CheckInOpenTime))
                    window.Append(" opens ").Append(Minutes(passenger.CheckInOpenTime));

                if (!double.IsNaN(passenger.CheckInLastAdmissionTime))
                    window.Append(", last admission ").Append(Minutes(passenger.CheckInLastAdmissionTime));

                if (passenger.IsLateForCheckIn)
                    window.Append(" — LATE for check-in");
                else if (passenger.ArrivedBeforeCheckInOpen)
                    window.Append(" — arrives before opening");

                lines.Add(window.ToString());
            }

            if (passenger.Journey == PassengerJourney.Transfer)
            {
                string flow = passenger.TransferScenario.HasValue
                    ? passenger.TransferScenario.Value.ToFlowLabel()
                    : "unspecified flow";

                string rules = passenger.TransferProcessing == null
                    ? "no explicit scenario rules — normal passenger behaviour applies"
                    : DescribeScenarioRules(passenger);

                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  transfer flow: {0}; {1}",
                    flow,
                    rules));
            }

            return lines;
        }

        private static string DescribeScenarioRules(Passenger passenger)
        {
            List<string> required = new List<string>();
            List<string> bypassed = new List<string>();

            foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
            {
                if (TransferServiceUsageCalculator.IsRequired(passenger, service))
                    required.Add(service.ToLabel());
                else if (TransferServiceUsageCalculator.IsBypassed(passenger, service))
                    bypassed.Add(service.ToLabel());
            }

            if (required.Count == 0 && bypassed.Count == 0)
                return "scenario rules present but they touch no applicable service";

            StringBuilder text = new StringBuilder("scenario rules");

            if (required.Count > 0)
                text.Append(" require ").Append(string.Join(", ", required));

            if (bypassed.Count > 0)
            {
                if (required.Count > 0)
                    text.Append("; ");

                text.Append("bypass ").Append(string.Join(", ", bypassed));
            }

            return text.ToString();
        }

        private static bool StageBelongsTo(NarrativeEvent evt, string stage, Passenger passenger)
        {
            if (string.Equals(evt.Stage, stage, StringComparison.Ordinal))
                return true;

            // Both security series events belong to the canonical "Security" stage.
            if (string.Equals(stage, "Security", StringComparison.Ordinal) &&
                evt.Facility == FacilityType.Security)
            {
                return true;
            }

            // A stage label that is not canonical (e.g. an appended facility)
            // still matches its own label exactly; handled above.
            return false;
        }

        private static double WalkMinutes(Passenger passenger)
        {
            double walk = passenger.WalkingTime;

            if (!double.IsNaN(passenger.TerminalEntryWalkMinutes) &&
                !double.IsInfinity(passenger.TerminalEntryWalkMinutes))
            {
                walk = Math.Max(walk, Math.Max(0.0, passenger.TerminalEntryWalkMinutes));
            }

            return Math.Max(0.0, walk);
        }

        private static string Header(Passenger passenger)
        {
            if (passenger == null)
                return "Passenger: (none)";

            string bag = passenger.HasCheckedBag ? ", checked bag" : "";

            return string.Format(
                CultureInfo.InvariantCulture,
                "Passenger {0}: {1} {2}, profile '{3}', channel '{4}'{5}",
                passenger.Id,
                passenger.DHPType,
                passenger.Journey,
                passenger.ProfileName,
                passenger.Channel,
                bag);
        }

        private static string FormatVisit(int index, NarrativeEvent evt)
        {
            string route = string.IsNullOrEmpty(evt.Route) ? "" : $" route '{evt.Route}',";

            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1} — pool '{2}',{3} resource {4}: arrived {5}, waited {6}, service {7}, served {8} to {9}",
                index,
                evt.Stage,
                string.IsNullOrEmpty(evt.PoolName) ? "(none)" : evt.PoolName,
                route,
                evt.Resource,
                Minutes(evt.ArrivalTime),
                Seconds(evt.WaitTime),
                Seconds(evt.ServiceTime),
                Minutes(evt.StartServiceTime),
                Minutes(evt.EndServiceTime));
        }

        private static string Minutes(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture) + " min";
        }

        private static string Seconds(double minutes)
        {
            return (minutes * 60.0).ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }
    }
}
