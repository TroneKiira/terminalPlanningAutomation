using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>
    /// Direction of one leg: arriving into the airport, or departing from it.
    /// </summary>
    public enum LegDirection
    {
        Arrival,
        Departure
    }

    /// <summary>
    /// Broad service type, used to decide whether a leg belongs to a passenger
    /// process (check-in, baggage reclaim, security) or to stand occupancy only.
    /// Freight legs are retained because a stand still has to be found for them.
    /// </summary>
    public enum LegServiceType
    {
        Passenger,
        Freight,
        Other
    }

    /// <summary>
    /// Sector of one leg, as stated by the file.
    /// </summary>
    public enum LegSector
    {
        Unknown,
        Domestic,
        International
    }

    /// <summary>
    /// Where a leg's terminal value came from. Fallback terminals are an
    /// explicit assumption, not a workbook value, and must never be presented
    /// as if the file supplied them.
    /// </summary>
    public enum TerminalSource
    {
        None,
        Workbook,
        Fallback,
        Cargo
    }

    /// <summary>
    /// One parsed leg: what the file said, before reference tables are applied.
    /// A row containing both an arrival and a departure turnaround produces two
    /// legs with the same link ID.
    /// </summary>
    public class FlightLeg
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Shared by the arrival and departure of one aircraft turn.</summary>
        public string LinkId { get; set; } = string.Empty;

        /// <summary>The rotation/turn identifier from the file, when present.</summary>
        public string RotationId { get; set; } = string.Empty;

        public LegDirection Direction { get; set; }

        /// <summary>
        /// Calendar date when the workbook or the Base Date supplies one.
        /// Otherwise empty.
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// False when the time cell is blank, an error, or an uncached formula.
        /// A missing time must never be mistaken for a genuine midnight flight.
        /// </summary>
        public bool HasTime { get; set; }

        /// <summary>The time exactly as read from the workbook.</summary>
        public string TimeRaw { get; set; } = string.Empty;

        /// <summary>Minute of the operating day (0 through 1439).</summary>
        public int MinutesFromMidnight { get; set; }

        /// <summary>
        /// Additional whole days after the operating day. A linked departure
        /// after midnight is stored as 06:10 with DayOffset = 1.
        /// </summary>
        public int DayOffset { get; set; }

        public string Airline { get; set; } = string.Empty;

        public string FlightNumber { get; set; } = string.Empty;

        /// <summary>Exactly as written in the file; standardisation happens later.</summary>
        public string AircraftTypeRaw { get; set; } = string.Empty;

        /// <summary>The ICAO code letter as written in the file, when present.</summary>
        public string IcaoCodeRaw { get; set; } = string.Empty;

        /// <summary>Origin for an arrival, destination for a departure.</summary>
        public string Station { get; set; } = string.Empty;

        /// <summary>The via point written in the file, when present.</summary>
        public string Via { get; set; } = string.Empty;

        public LegSector SectorRaw { get; set; } = LegSector.Unknown;

        public LegServiceType ServiceType { get; set; } = LegServiceType.Other;

        public int? SeatsRaw { get; set; }

        public double? LoadFactor { get; set; }

        public int? PaxRaw { get; set; }

        public string Terminal { get; set; } = string.Empty;

        /// <summary>
        /// True when the terminal could not be resolved and no fallback was
        /// applied. This is not the same as an intentionally blank terminal.
        /// </summary>
        public bool TerminalUnresolved { get; set; }

        /// <summary>
        /// Workbook, Fallback, Cargo or None. A Fallback value is an explicit
        /// assumption rather than something read from the file.
        /// </summary>
        public TerminalSource TerminalSource { get; set; } = TerminalSource.None;

        public bool TerminalIsFallback => TerminalSource == TerminalSource.Fallback;

        public string Stand { get; set; } = string.Empty;

        public string Registration { get; set; } = string.Empty;

        public string Remarks { get; set; } = string.Empty;

        public List<LegIssue> Issues { get; set; } = new List<LegIssue>();

        public int SourceRow { get; set; }

        /// <summary>Minute on the continuous schedule clock, including day offset.</summary>
        public int TotalMinutesFromMidnight => DayOffset * 1440 + MinutesFromMidnight;

        public bool HasError => Issues.Any(i => string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase));

        public bool HasWarning => Issues.Any(i => string.Equals(i.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One problem found with one leg during parsing.
    /// </summary>
    public class LegIssue
    {
        public string Field { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Severity { get; set; } = "Warning";

        public override string ToString()
        {
            return $"{Field}: {Message}";
        }
    }

    /// <summary>
    /// Result of parsing one schedule file.
    /// </summary>
    public class FlightScheduleResult
    {
        public string ScheduleName { get; set; } = string.Empty;

        /// <summary>The sheet actually parsed, not merely the requested name.</summary>
        public string SheetName { get; set; } = string.Empty;

        public string SourceFile { get; set; } = string.Empty;

        public string BaseDate { get; set; } = string.Empty;

        /// <summary>
        /// Terminal applied to non-freight legs whose terminal is missing or
        /// unresolved. Empty means no fallback was configured.
        /// </summary>
        public string FallbackTerminal { get; set; } = string.Empty;

        public bool LinkedRows { get; set; }

        /// <summary>The row used for column headings.</summary>
        public int HeaderRow { get; set; }

        /// <summary>Number of data rows that contained at least one movement.</summary>
        public int SourceRowCount { get; set; }

        public List<FlightLeg> Legs { get; set; } = new List<FlightLeg>();

        public List<LegIssue> GlobalIssues { get; set; } = new List<LegIssue>();

        /// <summary>Resolved field-to-Excel-column bindings (for example ArrivalPax = L).</summary>
        public Dictionary<string, string> Columns { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int ArrivalCount => Legs.Count(l => l.Direction == LegDirection.Arrival);

        public int DepartureCount => Legs.Count(l => l.Direction == LegDirection.Departure);

        public int TotalLegCount => Legs.Count;

        public int WarningCount =>
            GlobalIssues.Count(i => i.Severity == "Warning") +
            Legs.Sum(l => l.Issues.Count(i => i.Severity == "Warning"));

        public int ErrorCount =>
            GlobalIssues.Count(i => i.Severity == "Error") +
            Legs.Sum(l => l.Issues.Count(i => i.Severity == "Error"));

        /// <summary>Number of reconstructed turnarounds (complete or lone legs).</summary>
        public int RotationCount => Rotations().Count;

        /// <summary>
        /// Resolved field-to-column bindings in physical sheet order, one line
        /// per field (for example "ArrivalPax = L"). This is the column map the
        /// parser actually used, including any caller overrides.
        /// </summary>
        public List<string> ColumnMapLines()
        {
            return Columns
                .OrderBy(pair => XlsxWorkbook.ColumnIndex(pair.Value))
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key} = {pair.Value}")
                .ToList();
        }

        /// <summary>
        /// Builds rotations from the link IDs assigned by the parser. In linked
        /// mode one row becomes one rotation. In unlinked mode every leg is its
        /// own rotation.
        /// </summary>
        public List<FlightRotation> Rotations()
        {
            return Legs
                .GroupBy(l => string.IsNullOrWhiteSpace(l.LinkId) ? l.Id : l.LinkId)
                .Select(group => new FlightRotation
                {
                    LinkId = group.Key,
                    Arrival = group.FirstOrDefault(l => l.Direction == LegDirection.Arrival),
                    Departure = group.FirstOrDefault(l => l.Direction == LegDirection.Departure)
                })
                .ToList();
        }

        public FlightScheduleSummary CreateSummary()
        {
            var arrivals = Legs.Where(l => l.Direction == LegDirection.Arrival).ToList();
            var departures = Legs.Where(l => l.Direction == LegDirection.Departure).ToList();

            return new FlightScheduleSummary
            {
                ScheduleName = ScheduleName,
                SheetName = SheetName,
                SourceFile = SourceFile,
                BaseDate = BaseDate,
                HeaderRow = HeaderRow,
                SourceRowCount = SourceRowCount,
                TotalLegs = TotalLegCount,
                ArrivalCount = ArrivalCount,
                DepartureCount = DepartureCount,
                RotationCount = RotationCount,
                CompleteRotationCount = Rotations().Count(r => r.IsComplete),
                LoneArrivalCount = Rotations().Count(r => r.IsLoneArrival),
                LoneDepartureCount = Rotations().Count(r => r.IsLoneDeparture),
                ArrivalPax = arrivals.Sum(l => l.PaxRaw ?? 0),
                DeparturePax = departures.Sum(l => l.PaxRaw ?? 0),
                ArrivalSeats = arrivals.Sum(l => l.SeatsRaw ?? 0),
                DepartureSeats = departures.Sum(l => l.SeatsRaw ?? 0),
                ArrivalDomestic = arrivals.Count(l => l.SectorRaw == LegSector.Domestic),
                ArrivalInternational = arrivals.Count(l => l.SectorRaw == LegSector.International),
                DepartureDomestic = departures.Count(l => l.SectorRaw == LegSector.Domestic),
                DepartureInternational = departures.Count(l => l.SectorRaw == LegSector.International),
                PassengerLegs = Legs.Count(l => l.ServiceType == LegServiceType.Passenger),
                FreightLegs = Legs.Count(l => l.ServiceType == LegServiceType.Freight),
                OtherServiceLegs = Legs.Count(l => l.ServiceType == LegServiceType.Other),
                LegsWithoutTime = Legs.Count(l => !l.HasTime),
                UnresolvedTerminals = Legs.Count(l => l.TerminalUnresolved),
                FallbackTerminalLegs = Legs.Count(l => l.TerminalSource == TerminalSource.Fallback),
                CargoTerminalLegs = Legs.Count(l =>
                    string.Equals(l.Terminal, "Cargo", StringComparison.OrdinalIgnoreCase)),
                FallbackTerminal = this.FallbackTerminal,
                WarningCount = WarningCount,
                ErrorCount = ErrorCount
            };
        }
    }

    /// <summary>
    /// One reconstructed aircraft turnaround.
    /// </summary>
    public class FlightRotation
    {
        public string LinkId { get; set; } = string.Empty;

        public FlightLeg Arrival { get; set; }

        public FlightLeg Departure { get; set; }

        /// <summary>The rotation/turn identifier supplied by the file, if any.</summary>
        public string RotationId =>
            !string.IsNullOrWhiteSpace(Arrival?.RotationId)
                ? Arrival.RotationId
                : Departure?.RotationId ?? string.Empty;

        public bool IsLoneArrival => Arrival != null && Departure == null;

        public bool IsLoneDeparture => Arrival == null && Departure != null;

        public bool IsComplete => Arrival != null && Departure != null;

        public string Station =>
            !string.IsNullOrWhiteSpace(Arrival?.Station)
                ? Arrival.Station
                : Departure?.Station ?? string.Empty;

        public LegSector SectorRaw =>
            Arrival?.SectorRaw ?? Departure?.SectorRaw ?? LegSector.Unknown;

        public string AircraftTypeRaw =>
            Arrival?.AircraftTypeRaw ?? Departure?.AircraftTypeRaw ?? string.Empty;

        public string IcaoCodeRaw =>
            Arrival?.IcaoCodeRaw ?? Departure?.IcaoCodeRaw ?? string.Empty;

        /// <summary>
        /// Ground time in minutes when both legs have usable times. Negative or
        /// missing information yields null rather than a guessed turn time.
        /// </summary>
        public int? TurnMinutes
        {
            get
            {
                if (Arrival == null || Departure == null || !Arrival.HasTime || !Departure.HasTime)
                    return null;

                int turn = Departure.TotalMinutesFromMidnight - Arrival.TotalMinutesFromMidnight;
                return turn >= 0 ? turn : (int?)null;
            }
        }
    }

    /// <summary>
    /// Typed totals for the Grasshopper summary output and for tests.
    /// </summary>
    public class FlightScheduleSummary
    {
        public string ScheduleName { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string BaseDate { get; set; } = string.Empty;
        public int HeaderRow { get; set; }
        public int SourceRowCount { get; set; }
        public int TotalLegs { get; set; }
        public int ArrivalCount { get; set; }
        public int DepartureCount { get; set; }
        public int RotationCount { get; set; }
        public int CompleteRotationCount { get; set; }
        public int LoneArrivalCount { get; set; }
        public int LoneDepartureCount { get; set; }
        public int ArrivalPax { get; set; }
        public int DeparturePax { get; set; }
        public int ArrivalSeats { get; set; }
        public int DepartureSeats { get; set; }
        public int ArrivalDomestic { get; set; }
        public int ArrivalInternational { get; set; }
        public int DepartureDomestic { get; set; }
        public int DepartureInternational { get; set; }
        public int PassengerLegs { get; set; }
        public int FreightLegs { get; set; }
        public int OtherServiceLegs { get; set; }
        public int LegsWithoutTime { get; set; }

        /// <summary>Legs with no usable terminal after any fallback was applied.</summary>
        public int UnresolvedTerminals { get; set; }

        /// <summary>Legs whose terminal came from the configured fallback.</summary>
        public int FallbackTerminalLegs { get; set; }

        /// <summary>Legs whose resolved terminal is Cargo (freight).</summary>
        public int CargoTerminalLegs { get; set; }

        /// <summary>The fallback terminal that was configured, or empty.</summary>
        public string FallbackTerminal { get; set; } = string.Empty;

        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }

        public int TotalPax => ArrivalPax + DeparturePax;
    }
}
