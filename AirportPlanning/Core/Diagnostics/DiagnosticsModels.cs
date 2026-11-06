using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Diagnostics
{
    /// <summary>
    /// How badly a finding matters. Info never changes the overall status.
    /// </summary>
    public enum DiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// Display status for a check, a service row or the whole report.
    /// NotWired means there was nothing to check, not that the check passed.
    /// </summary>
    public enum DiagnosticStatus
    {
        Pass = 0,
        Info = 1,
        Warn = 2,
        Fail = 3,
        NotWired = 4
    }

    public enum DiagnosticCategory
    {
        Coverage = 0,
        Schedule = 1,
        Generation = 2,
        Transfer = 3,
        Service = 4,
        Passenger = 5,
        Gate = 6,
        Sizing = 7
    }

    /// <summary>
    /// A passenger-processing stage the diagnostics can reconcile.
    /// </summary>
    public enum DiagnosticsServiceKind
    {
        Unknown = 0,
        TerminalEntry = 1,
        CheckIn = 2,
        BagDrop = 3,
        Security = 4,
        Emigration = 5,
        Immigration = 6,
        Verification = 7,
        Dwell = 8,
        Boarding = 9,
        TransferDesk = 10,
        BaggageRecheck = 11,
        Customs = 12
    }

    /// <summary>
    /// One reconciliation finding. Expected/actual are optional because many
    /// findings are population or wiring issues rather than numeric variances.
    /// </summary>
    public class DiagnosticFinding
    {
        public string Code { get; set; } = string.Empty;

        public DiagnosticCategory Category { get; set; }

        public DiagnosticSeverity Severity { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Metric { get; set; } = string.Empty;

        public double? Expected { get; set; }

        public double? Actual { get; set; }

        public double? Variance { get; set; }

        public string Unit { get; set; } = string.Empty;

        public string ServiceLabel { get; set; } = string.Empty;

        public string Scenario { get; set; } = string.Empty;

        public List<int> PassengerIds { get; set; } = new List<int>();

        public List<string> FlightLegIds { get; set; } = new List<string>();

        public List<string> Details { get; set; } = new List<string>();

        public DiagnosticStatus Status
        {
            get
            {
                switch (Severity)
                {
                    case DiagnosticSeverity.Error:
                        return DiagnosticStatus.Fail;
                    case DiagnosticSeverity.Warning:
                        return DiagnosticStatus.Warn;
                    default:
                        return DiagnosticStatus.Info;
                }
            }
        }

        public string Describe()
        {
            string text = "[" + Status.ToDisplayLabel() + "] " + Code + " - " + Title;

            if (!string.IsNullOrWhiteSpace(Message))
                text += Environment.NewLine + "  " + Message;

            if (Expected.HasValue || Actual.HasValue)
            {
                text += Environment.NewLine + "  ";
                text += "Expected " + FormatNumber(Expected);
                text += ", actual " + FormatNumber(Actual);

                if (Variance.HasValue)
                    text += ", variance " + FormatSigned(Variance.Value) +
                            (string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit);
                else if (!string.IsNullOrEmpty(Unit))
                    text += " " + Unit;
            }

            if (PassengerIds != null && PassengerIds.Count > 0)
                text += Environment.NewLine + "  Passenger IDs: " + string.Join(", ", PassengerIds);

            if (FlightLegIds != null && FlightLegIds.Count > 0)
                text += Environment.NewLine + "  Flight legs: " + string.Join(", ", FlightLegIds);

            if (Details != null)
            {
                foreach (string detail in Details.Where(d => !string.IsNullOrWhiteSpace(d)))
                    text += Environment.NewLine + "  " + detail;
            }

            return text;
        }

        private static string FormatNumber(double? value)
        {
            if (!value.HasValue)
                return "n/a";

            return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatSigned(double value)
        {
            return (value > 0 ? "+" : string.Empty) +
                   value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// One processing stage's reconciliation. Counts are passenger records,
    /// not journeys: one transfer journey has a separate arrival and departure
    /// record, and each is counted on the leg that actually uses the service.
    /// </summary>
    public class DiagnosticsServiceRow
    {
        public string Label { get; set; } = string.Empty;

        public DiagnosticsServiceKind Kind { get; set; }

        public string KindLabel => Kind.ToLabel();

        /// <summary>Passenger records presented to the stage.</summary>
        public int Incoming { get; set; }

        /// <summary>Incoming records to which the service applies (expected + bypassed).</summary>
        public int Applicable { get; set; }

        /// <summary>Records the independent rule expects the stage to serve.</summary>
        public int Expected { get; set; }

        /// <summary>Records the stage reported as processed, warm-up included.</summary>
        public int Processed { get; set; }

        /// <summary>Processed records flagged as warm-up.</summary>
        public int WarmUpProcessed { get; set; }

        /// <summary>Processed records excluding warm-up.</summary>
        public int NonWarmUpProcessed { get; set; }

        /// <summary>Incoming records that a rule or passenger flag deliberately excludes.</summary>
        public int Bypassed { get; set; }

        /// <summary>Incoming records to which the service does not apply at all.</summary>
        public int NotApplicable { get; set; }

        /// <summary>Expected records that the stage did not process.</summary>
        public int MissingExpected { get; set; }

        /// <summary>Processed records that the independent rule did not expect at this stage.</summary>
        public int ExtraProcessed { get; set; }

        /// <summary>Processed records without this facility's history record.</summary>
        public int MissingHistory { get; set; }

        /// <summary>Incoming records carrying this facility's history without being processed now.</summary>
        public int HistoryOnUnprocessed { get; set; }

        /// <summary>Duplicate passenger IDs inside the processed list.</summary>
        public int DuplicateProcessed { get; set; }

        /// <summary>Physical processors reported by the result, when known.</summary>
        public int PhysicalResources { get; set; }

        /// <summary>Staffed processors reported by the result, when known.</summary>
        public int ActiveResources { get; set; }

        public int ResourcesUsed { get; set; }

        public int PoolCount { get; set; }

        /// <summary>Sum of pool passenger counts (warm-up excluded).</summary>
        public int PoolPassengers { get; set; }

        public double PeakQueue { get; set; }

        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public List<string> Notes { get; set; } = new List<string>();

        public List<int> MissingPassengerIds { get; set; } = new List<int>();

        public List<int> ExtraPassengerIds { get; set; } = new List<int>();

        /// <summary>
        /// Expected records grouped by channel, with the missing subset. Only
        /// populated for stages where the channel mix explains the result.
        /// </summary>
        public List<DiagnosticsBreakdownRow> Channels { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Expected records grouped by online/offline and bag flags.</summary>
        public List<DiagnosticsBreakdownRow> PassengerMix { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Expected records grouped by departure type.</summary>
        public List<DiagnosticsBreakdownRow> DepartureTypes { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Expected records grouped by origin/transfer journey.</summary>
        public List<DiagnosticsBreakdownRow> Journeys { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>
        /// Records deliberately excluded from the expected set, such as late
        /// check-ins and explicit transfer-rule bypasses.
        /// </summary>
        public List<DiagnosticsBreakdownRow> Exceptions { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>
        /// Bag Drop stage breakdown, or null for other stages.
        /// </summary>
        public DiagnosticsBagDropBreakdown BagDrop { get; set; }
    }

    /// <summary>
    /// One slice of a service row: a channel, passenger mix, departure type or
    /// journey. Expected is the population slice; Missing is the part of it the
    /// stage did not process. PassengerIds stays empty unless the slice is a
    /// missing subset, which keeps the aggregate rows short.
    /// </summary>
    public class DiagnosticsBreakdownRow
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>Records in this slice when it is a plain count, not an expected set.</summary>
        public int Count { get; set; }

        /// <summary>Expected records in this slice.</summary>
        public int Expected { get; set; }

        /// <summary>Expected records in this slice that were processed.</summary>
        public int Processed { get; set; }

        /// <summary>Expected records in this slice that were not processed.</summary>
        public int Missing { get; set; }

        /// <summary>Processed records in this slice that carry the service history.</summary>
        public int Recorded { get; set; }

        /// <summary>Expected records in this slice on a channel that cannot serve them.</summary>
        public int RoutingMismatch { get; set; }

        public List<int> PassengerIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Bag Drop is a standalone stage. These counts reconcile the separate
    /// bag-drop requirement (Web/Kiosk bag carriers and rule-required
    /// transfers) with the Bag Drop history the stage writes.
    /// </summary>
    public class DiagnosticsBagDropBreakdown
    {
        /// <summary>True when at least one record requires bag drop or carries its history.</summary>
        public bool Active { get; set; }

        /// <summary>Expected Bag Drop records.</summary>
        public int Expected { get; set; }

        /// <summary>Eligible Bag Drop records. Every eligible record is correctly placed.</summary>
        public int OnChannel { get; set; }

        /// <summary>Expected Bag Drop records carrying a Bag Drop history record.</summary>
        public int Recorded { get; set; }

        /// <summary>Expected Bag Drop records without a Bag Drop history record.</summary>
        public int Missing { get; set; }

        /// <summary>Always zero: Bag Drop no longer depends on the primary channel.</summary>
        public int RoutingMismatch { get; set; }

        /// <summary>Bag-drop records excluded because they were late for Check-In.</summary>
        public int LateBypassed { get; set; }

        public List<int> MissingPassengerIds { get; set; } = new List<int>();

        public List<int> RecordedPassengerIds { get; set; } = new List<int>();

        public List<int> RoutingMismatchPassengerIds { get; set; } = new List<int>();

        /// <summary>Expected bag-drop records by channel.</summary>
        public List<DiagnosticsBreakdownRow> Channels { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Routing-mismatch records by channel.</summary>
        public List<DiagnosticsBreakdownRow> RoutingMismatchChannels { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Missing bag-drop records by channel.</summary>
        public List<DiagnosticsBreakdownRow> MissingChannels { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Missing bag-drop records by online/offline and bag flags.</summary>
        public List<DiagnosticsBreakdownRow> MissingMix { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Missing bag-drop records by departure type.</summary>
        public List<DiagnosticsBreakdownRow> MissingDepartureTypes { get; set; } =
            new List<DiagnosticsBreakdownRow>();

        /// <summary>Missing bag-drop records by origin/transfer journey.</summary>
        public List<DiagnosticsBreakdownRow> MissingJourneys { get; set; } =
            new List<DiagnosticsBreakdownRow>();
    }

    /// <summary>
    /// Demand versus tagging for one transfer scenario. Tagged counts only
    /// outbound departure legs, matching TransferDemandResult. Arrival legs are
    /// reported separately because they are separate passenger records.
    /// </summary>
    public class DiagnosticsTransferRow
    {
        public string Scenario { get; set; } = string.Empty;

        public int Requested { get; set; }

        public int FeasibleMaximum { get; set; }

        public int Effective { get; set; }

        /// <summary>Tagged outbound count recorded on the demand result.</summary>
        public int Tagged { get; set; }

        /// <summary>Tagged outbound count recomputed from the population.</summary>
        public int TaggedPopulation { get; set; }

        public int ArrivalLegs { get; set; }

        public int DepartureLegs { get; set; }

        public int Shortfall { get; set; }

        public int Excess { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Rule demand versus actual service history for one transfer service.
    /// The counts are taken from the tagged transfer records, not from every
    /// passenger at the facility.
    /// </summary>
    public class DiagnosticsTransferServiceRow
    {
        public string Service { get; set; } = string.Empty;

        public DiagnosticsServiceKind Kind { get; set; }

        public int Applicable { get; set; }

        public int Required { get; set; }

        public int Bypassed { get; set; }

        public int Unspecified { get; set; }

        /// <summary>Transfer records carrying the corresponding facility history.</summary>
        public int Processed { get; set; }

        public int MissingRequired { get; set; }

        /// <summary>Transfer records served although their rule explicitly bypasses the service.</summary>
        public int ServedBypassed { get; set; }

        /// <summary>Transfer records served with no explicit transfer rules.</summary>
        public int ProcessedUnspecified { get; set; }

        /// <summary>True when a facility result or passenger history can audit this service.</summary>
        public bool ResultWired { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// One transfer flow/service at its busiest rolling 60-minute window.
    /// </summary>
    public class DiagnosticsPeakRow
    {
        public string Flow { get; set; } = string.Empty;

        public string Service { get; set; } = string.Empty;

        public DiagnosticsServiceKind Kind { get; set; }

        public int StartMinute { get; set; }

        public string Window { get; set; } = string.Empty;

        /// <summary>Transfer records in the window (required + bypassed + unspecified).</summary>
        public int Peak { get; set; }

        public int Required { get; set; }

        public int Bypassed { get; set; }

        public int Unspecified { get; set; }

        public int Processed { get; set; }

        public int MissingRequired { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// A single passenger record that needs investigation. Passenger IDs are
    /// the fastest way to find the record in a passenger filter or CSV export.
    /// </summary>
    public class DiagnosticsPassengerIssue
    {
        public int PassengerId { get; set; }

        public string FlightLegId { get; set; } = string.Empty;

        public string Airline { get; set; } = string.Empty;

        public string FlightNumber { get; set; } = string.Empty;

        public string DhpType { get; set; } = string.Empty;

        public string Journey { get; set; } = string.Empty;

        public string Scenario { get; set; } = string.Empty;

        /// <summary>Service channel the record was routed on (Counter, BagDrop, Web...).</summary>
        public string Channel { get; set; } = string.Empty;

        public bool UsesOnlineCheckIn { get; set; }

        public bool HasCheckedBag { get; set; }

        public string Code { get; set; } = string.Empty;

        public DiagnosticSeverity Severity { get; set; }

        public string Message { get; set; } = string.Empty;

        public double? Expected { get; set; }

        public double? Actual { get; set; }

        public string Describe()
        {
            string flight = string.Join(" ", new[] { Airline, FlightNumber }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            string text = "PAX " + PassengerId;
            if (!string.IsNullOrWhiteSpace(flight))
                text += " | " + flight;
            if (!string.IsNullOrWhiteSpace(FlightLegId))
                text += " | leg " + FlightLegId;
            if (!string.IsNullOrWhiteSpace(DhpType))
                text += " | " + DhpType;
            if (!string.IsNullOrWhiteSpace(Journey))
                text += " | " + Journey;
            if (!string.IsNullOrWhiteSpace(Scenario))
                text += " | " + Scenario;
            if (!string.IsNullOrWhiteSpace(Channel))
                text += " | channel " + Channel;

            text += " | [" + Code + "] " + Message;

            if (Expected.HasValue || Actual.HasValue)
            {
                text += " (expected " +
                        (Expected.HasValue
                            ? Expected.Value.ToString("0.###", CultureInfo.InvariantCulture)
                            : "n/a") +
                        ", actual " +
                        (Actual.HasValue
                            ? Actual.Value.ToString("0.###", CultureInfo.InvariantCulture)
                            : "n/a") + ")";
            }

            return text;
        }
    }

    /// <summary>
    /// One count definition, so the reader can see what a number means
    /// without opening the source.
    /// </summary>
    public class DiagnosticsDefinitionRow
    {
        public string Label { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }

    public class DiagnosticsGateRow
    {
        public string Label { get; set; } = string.Empty;

        public int Expected { get; set; }

        public int Actual { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public string Note { get; set; } = string.Empty;
    }

    public class DiagnosticsSizingRow
    {
        public string Label { get; set; } = string.Empty;

        public int PeakDemand { get; set; }

        public int ProcessorCount { get; set; }

        public double NetArea { get; set; }

        public double GrossArea { get; set; }

        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// The complete diagnostics result. The table outputs are text because
    /// Grasshopper panels and CSV exports are the primary consumers.
    /// </summary>
    public class DiagnosticsReport
    {
        public DiagnosticStatus OverallStatus { get; set; } = DiagnosticStatus.Pass;

        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }

        public int InfoCount { get; set; }

        public int PopulationCount { get; set; }

        public int ServiceResultCount { get; set; }

        public int WarmUpCount { get; set; }

        public int DesignStepCount { get; set; }

        public int OutboundTransferLegs { get; set; }

        public int InboundTransferLegs { get; set; }

        public string PopulationSource { get; set; } = "none";

        public List<DiagnosticFinding> Findings { get; set; } = new List<DiagnosticFinding>();

        public List<DiagnosticsServiceRow> Services { get; set; } = new List<DiagnosticsServiceRow>();

        public List<DiagnosticsTransferRow> Transfers { get; set; } = new List<DiagnosticsTransferRow>();

        public List<DiagnosticsTransferServiceRow> TransferServices { get; set; } =
            new List<DiagnosticsTransferServiceRow>();

        public List<DiagnosticsPeakRow> Peaks { get; set; } = new List<DiagnosticsPeakRow>();

        public List<DiagnosticsPassengerIssue> PassengerIssues { get; set; } =
            new List<DiagnosticsPassengerIssue>();

        public List<DiagnosticsDefinitionRow> Definitions { get; set; } =
            new List<DiagnosticsDefinitionRow>();

        public List<DiagnosticsGateRow> GateRows { get; set; } = new List<DiagnosticsGateRow>();

        public List<DiagnosticsSizingRow> SizingRows { get; set; } = new List<DiagnosticsSizingRow>();

        public List<string> CoverageLines { get; set; } = new List<string>();

        public List<string> SummaryLines()
        {
            return DiagnosticsFormatter.CreateSummaryLines(this);
        }

        public List<string> IssueLines(bool includeInfo = false)
        {
            return DiagnosticsFormatter.CreateIssueLines(this, includeInfo);
        }

        public List<string> ServiceTableLines()
        {
            return DiagnosticsFormatter.CreateServiceTableLines(this);
        }

        public List<string> TransferTableLines()
        {
            return DiagnosticsFormatter.CreateTransferTableLines(this);
        }

        public List<string> TransferServiceTableLines()
        {
            return DiagnosticsFormatter.CreateTransferServiceTableLines(this);
        }

        public List<string> PeakTableLines()
        {
            return DiagnosticsFormatter.CreatePeakTableLines(this);
        }

        public List<string> PassengerIssueLines()
        {
            return DiagnosticsFormatter.CreatePassengerIssueLines(this);
        }

        public List<string> DefinitionLines()
        {
            return DiagnosticsFormatter.CreateDefinitionLines(this);
        }

        public List<string> GateCheckLines()
        {
            return DiagnosticsFormatter.CreateGateCheckLines(this);
        }

        public List<string> SizingCheckLines()
        {
            return DiagnosticsFormatter.CreateSizingCheckLines(this);
        }

        public string ToCsv()
        {
            return DiagnosticsFormatter.CreateCsv(this);
        }

        public override string ToString()
        {
            return string.Join(Environment.NewLine, SummaryLines());
        }
    }

    /// <summary>
    /// Neutral input for one facility/service result. The Grasshopper wrapper
    /// maps typed facility results into this shape so the calculator stays
    /// free of Grasshopper and facility-component dependencies.
    /// </summary>
    public class DiagnosticsServiceInput
    {
        public string Label { get; set; } = string.Empty;

        public DiagnosticsServiceKind Kind { get; set; } = DiagnosticsServiceKind.Unknown;

        public QueueResults Queue { get; set; }

        /// <summary>
        /// Optional explicit stage population. When empty, the queue's full
        /// output population is used, which is the same set of records that
        /// entered the stage before the processed ones advanced.
        /// </summary>
        public List<Passenger> Passengers { get; set; }

        public int? PhysicalResources { get; set; }

        public int? ActiveResources { get; set; }

        public int? EligiblePassengers { get; set; }

        public int? RequiredGates { get; set; }

        public double? GateCapacity { get; set; }

        public double? Utilization { get; set; }

        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Neutral input for one facility sizing result.
    /// </summary>
    public class DiagnosticsSizingInput
    {
        public string Label { get; set; } = string.Empty;

        public int PeakDemand { get; set; }

        public int ProcessorCount { get; set; }

        public double NetArea { get; set; }

        public double GrossArea { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class DiagnosticsInput
    {
        public FlightScheduleResult Schedule { get; set; }

        public PassengerGenerationResult Generation { get; set; }

        /// <summary>
        /// Generated or post-facility population used for transfer, schedule
        /// and passenger checks. Service checks use each service's own stage
        /// population when one is available.
        /// </summary>
        public List<Passenger> Passengers { get; set; }

        public TransferDemandResult TransferDemand { get; set; }

        public List<DiagnosticsServiceInput> Services { get; set; } =
            new List<DiagnosticsServiceInput>();

        public List<GateSizingResult> GateResults { get; set; } =
            new List<GateSizingResult>();

        public List<DiagnosticsSizingInput> SizingResults { get; set; } =
            new List<DiagnosticsSizingInput>();

        /// <summary>Maximum passenger IDs or passenger issues listed per check.</summary>
        public int MaxPassengerIds { get; set; } = 100;
    }

    public static class DiagnosticsStatusExtensions
    {
        public static string ToDisplayLabel(this DiagnosticStatus status)
        {
            switch (status)
            {
                case DiagnosticStatus.Fail:
                    return "FAIL";
                case DiagnosticStatus.Warn:
                    return "WARN";
                case DiagnosticStatus.Info:
                    return "INFO";
                case DiagnosticStatus.NotWired:
                    return "NOT WIRED";
                default:
                    return "PASS";
            }
        }

        public static int ToRank(this DiagnosticStatus status)
        {
            switch (status)
            {
                case DiagnosticStatus.Fail:
                    return 4;
                case DiagnosticStatus.Warn:
                    return 3;
                case DiagnosticStatus.NotWired:
                    return 2;
                case DiagnosticStatus.Info:
                    return 1;
                default:
                    return 0;
            }
        }

        public static DiagnosticStatus Worst(this DiagnosticStatus left, DiagnosticStatus right)
        {
            return left.ToRank() >= right.ToRank() ? left : right;
        }
    }

    public static class DiagnosticsLabelExtensions
    {
        public static string ToLabel(this DiagnosticsServiceKind kind)
        {
            switch (kind)
            {
                case DiagnosticsServiceKind.TerminalEntry:
                    return "Terminal Entry";
                case DiagnosticsServiceKind.CheckIn:
                    return "Check-In";
                case DiagnosticsServiceKind.BagDrop:
                    return "Bag Drop";
                case DiagnosticsServiceKind.Security:
                    return "Security";
                case DiagnosticsServiceKind.Emigration:
                    return "Emigration";
                case DiagnosticsServiceKind.Immigration:
                    return "Immigration";
                case DiagnosticsServiceKind.Verification:
                    return "Verification";
                case DiagnosticsServiceKind.Dwell:
                    return "Dwell";
                case DiagnosticsServiceKind.Boarding:
                    return "Boarding";
                case DiagnosticsServiceKind.TransferDesk:
                    return "Transfer Desk";
                case DiagnosticsServiceKind.BaggageRecheck:
                    return "Baggage Recheck";
                case DiagnosticsServiceKind.Customs:
                    return "Customs Hand Baggage";
                default:
                    return "Unknown";
            }
        }

        public static FacilityType? ToFacilityType(this DiagnosticsServiceKind kind)
        {
            switch (kind)
            {
                case DiagnosticsServiceKind.TerminalEntry:
                    return FacilityType.TerminalEntry;
                case DiagnosticsServiceKind.CheckIn:
                    return FacilityType.CheckIn;
                case DiagnosticsServiceKind.BagDrop:
                    return FacilityType.BagDrop;
                case DiagnosticsServiceKind.Security:
                    return FacilityType.Security;
                case DiagnosticsServiceKind.Emigration:
                    return FacilityType.Emigration;
                case DiagnosticsServiceKind.Immigration:
                    return FacilityType.Immigration;
                case DiagnosticsServiceKind.Verification:
                    return FacilityType.PassengerVerification;
                case DiagnosticsServiceKind.Dwell:
                    return FacilityType.DepartureLounge;
                case DiagnosticsServiceKind.Boarding:
                    return FacilityType.Boarding;
                case DiagnosticsServiceKind.TransferDesk:
                    return FacilityType.TransferDesk;
                case DiagnosticsServiceKind.BaggageRecheck:
                    return FacilityType.BaggageRecheck;
                case DiagnosticsServiceKind.Customs:
                    return FacilityType.Customs;
                default:
                    return null;
            }
        }
    }
}
