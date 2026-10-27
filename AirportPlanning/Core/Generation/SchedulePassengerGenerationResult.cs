using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Generation
{
    /// <summary>One passenger leg's contribution to schedule generation.</summary>
    public class ScheduleFlightDemand
    {
        public FlightLeg Leg { get; set; }

        public DHPType Type { get; set; }

        public int PassengerCount { get; set; }

        public int GeneratedCount { get; set; }

        public string Issue { get; set; } = string.Empty;
    }

    /// <summary>Passenger demand counted in one schedule hour.</summary>
    public class ScheduleHourDemand
    {
        public int Hour { get; set; }

        public int Count { get; set; }

        public Dictionary<DHPType, int> ByType { get; set; } =
            new Dictionary<DHPType, int>();

        public string Label
        {
            get
            {
                int wrapped = ((Hour % 24) + 24) % 24;
                return $"{wrapped:00}:00-{(wrapped + 1) % 24:00}:00";
            }
        }
    }

    /// <summary>One rolling 60-minute series point at a 5-minute step.</summary>
    public class ScheduleRollingSeries
    {
        public int Minute { get; set; }

        public int DomesticDeparture { get; set; }

        public int DomesticArrival { get; set; }

        public int InternationalDeparture { get; set; }

        public int InternationalArrival { get; set; }

        public int Combined => DomesticDeparture + DomesticArrival +
            InternationalDeparture + InternationalArrival;

        public int TotalDepartures => DomesticDeparture + InternationalDeparture;

        public int TotalArrivals => DomesticArrival + InternationalArrival;
    }

    /// <summary>
    /// Result of schedule-driven generation. It extends the existing result so
    /// downstream components can consume it unchanged, and adds the schedule
    /// diagnostics the planner needs to judge the input.
    /// </summary>
    public class SchedulePassengerGenerationResult : PassengerGenerationResult
    {
        public int ScheduleLegCount { get; set; }

        public int PassengerLegCount { get; set; }

        public int FlightsWithPassengers { get; set; }

        public int GeneratedPassengerCount { get; set; }

        public int DesignStepIndex { get; set; } = -1;

        public int PeakHourDemand { get; set; }

        /// <summary>
        /// Four-group peak demands, computed from the schedule itself using
        /// rolling 60-minute windows stepped every 5 minutes. These are the
        /// default design-hour values; the planner may override them.
        /// </summary>
        public int DomesticDeparturePeak { get; set; }

        public int DomesticArrivalPeak { get; set; }

        public int InternationalDeparturePeak { get; set; }

        public int InternationalArrivalPeak { get; set; }

        public int CombinedPeak { get; set; }

        /// <summary>
        /// Minute of the day each four-group peak falls at. Used to label the
        /// design window and to compare peaks across groups.
        /// </summary>
        public int DomesticDeparturePeakAt { get; set; }

        public int DomesticArrivalPeakAt { get; set; }

        public int InternationalDeparturePeakAt { get; set; }

        public int InternationalArrivalPeakAt { get; set; }

        public int CombinedPeakAt { get; set; }

        public int LateCheckInCount { get; set; }

        public int BeforeCheckInOpenCount { get; set; }

        public int ArrivalLegsWithoutGroup { get; set; }

        public int DepartureLegsWithoutGroup { get; set; }

        public Dictionary<DHPType, int> DemandByType { get; set; } =
            new Dictionary<DHPType, int>();

        public List<ScheduleHourDemand> DemandByHour { get; set; } =
            new List<ScheduleHourDemand>();

        /// <summary>
        /// Rolling 60-minute demand measured at each flight's STD/STA — the
        /// raw schedule view, 5-minute steps.
        /// </summary>
        public List<ScheduleRollingSeries> ScheduleRolling { get; set; } =
            new List<ScheduleRollingSeries>();

        /// <summary>
        /// Rolling 60-minute demand measured at generated passenger arrival
        /// times — after show-up, walking and arrival-process timing.
        /// </summary>
        public List<ScheduleRollingSeries> PassengerRolling { get; set; } =
            new List<ScheduleRollingSeries>();

        public List<ScheduleFlightDemand> FlightDemands { get; set; } =
            new List<ScheduleFlightDemand>();

        public List<string> GenerationWarnings { get; set; } =
            new List<string>();

        public List<string> CreateSummaryLines()
        {
            var lines = new List<string>
            {
                $"Schedule legs: {ScheduleLegCount}",
                $"Passenger legs: {PassengerLegCount}",
                $"Flights with generated passengers: {FlightsWithPassengers}",
                $"Passengers generated: {GeneratedPassengerCount}",
                $"DD peak: {DomesticDeparturePeak} at {FormatMinute(DomesticDeparturePeakAt)}",
                $"DA peak: {DomesticArrivalPeak} at {FormatMinute(DomesticArrivalPeakAt)}",
                $"ID peak: {InternationalDeparturePeak} at {FormatMinute(InternationalDeparturePeakAt)}",
                $"IA peak: {InternationalArrivalPeak} at {FormatMinute(InternationalArrivalPeakAt)}",
                $"Combined peak: {CombinedPeak} at {FormatMinute(CombinedPeakAt)}",
                $"Design hour: {(DesignStepIndex >= 0 ? FormatHour(DesignStepIndex) : "n/a")}",
                $"Late after check-in close: {LateCheckInCount}",
                $"Arrived before check-in opened: {BeforeCheckInOpenCount}"
            };

            foreach (DHPType type in Enum.GetValues(typeof(DHPType)))
            {
                DemandByType.TryGetValue(type, out int count);
                lines.Add($"{type}: {count}");
            }

            if (DepartureLegsWithoutGroup > 0 || ArrivalLegsWithoutGroup > 0)
            {
                lines.Add($"Legs skipped for a missing group: " +
                          $"{DepartureLegsWithoutGroup} departure, " +
                          $"{ArrivalLegsWithoutGroup} arrival");
            }

            return lines;
        }

        public static string FormatHour(int hour)
        {
            int wrapped = ((hour % 24) + 24) % 24;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:00-{1:00}:00",
                wrapped,
                (wrapped + 1) % 24);
        }

        public static string FormatMinute(int minuteOfDay)
        {
            int day = (int)Math.Floor(minuteOfDay / 1440.0);
            int m = minuteOfDay - day * 1440;
            int hh = m / 60;
            int mm = m % 60;
            string clock = $"{hh:00}:{mm:00}";
            return day == 0 ? clock : (day > 0 ? $"{clock}+{day}d" : $"{clock}-{Math.Abs(day)}d");
        }
    }
}
