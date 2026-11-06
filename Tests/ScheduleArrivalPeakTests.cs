using System;
using System.Collections.Generic;

using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        // -------------------------------------------------------------
        // Schedule-derived arrival peak (PHP) for the Deplaning Profile
        // -------------------------------------------------------------
        private static void RunScheduleArrivalPeak()
        {
            Console.WriteLine("== Schedule arrival peak (PHP) ==");

            // 1. Two banks: 10:00/10:20 heavier than 14:00; the rolling
            //    window covering both morning legs wins.
            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Peak",
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 600, 100),
                    MakePeakLeg(2, 620, 50),
                    MakePeakLeg(3, 840, 80)
                }
            };

            ScheduleArrivalPeakResult peak =
                ScheduleArrivalPeakCalculator.Calculate(schedule);

            Check(peak.PeakPassengers == 150,
                $"Peak hour sums the morning bank ({peak.PeakPassengers} == 150)");
            Check(peak.PeakWindowStartMinute == 600,
                $"Peak window starts at 10:00 ({peak.PeakWindowStartMinute} == 600)");
            Check(peak.ArrivalLegsUsed == 3 && peak.ArrivalLegsExcluded == 0,
                $"All arrival legs contributed ({peak.ArrivalLegsUsed} used, {peak.ArrivalLegsExcluded} excluded)");

            // 2. Rolling window: 10:00 (100) + 10:55 (50) peak at 150; the
            //    11:05 leg (30) only joins a later, smaller window.
            FlightScheduleResult rolling = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 600, 100),
                    MakePeakLeg(2, 655, 50),
                    MakePeakLeg(3, 665, 30)
                }
            };

            ScheduleArrivalPeakResult rollingPeak =
                ScheduleArrivalPeakCalculator.Calculate(rolling);

            Check(rollingPeak.PeakPassengers == 150,
                $"Rolling window captures 10:00-10:59 ({rollingPeak.PeakPassengers} == 150)");
            Check(rollingPeak.PeakWindowStartMinute == 600,
                $"Rolling peak window starts at 10:00 ({rollingPeak.PeakWindowStartMinute} == 600)");

            // 3. Seats x load factor fallback when PAX is missing.
            FlightScheduleResult seats = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 600, null, seats: 180, loadFactor: 0.8),
                    MakePeakLeg(2, 610, null, seats: 150),
                    MakePeakLeg(3, 620, null, seats: 200, loadFactor: 1.4),
                    MakePeakLeg(4, 630, null, seats: 100, loadFactor: -0.5)
                }
            };

            ScheduleArrivalPeakResult seatPeak =
                ScheduleArrivalPeakCalculator.Calculate(seats);

            Check(seatPeak.PeakPassengers == 144 + 150 + 200 + 100,
                $"Seats x load factor fallback covers all four legs ({seatPeak.PeakPassengers} == 594)");
            Check(seatPeak.ArrivalLegsUsed == 4 && seatPeak.ArrivalLegsExcluded == 0,
                "Seat-derived legs count as used, not excluded");

            // 4. Exclusions: missing time, no pax and no seats, negative pax.
            FlightScheduleResult excluded = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 600, 100),
                    MakePeakLeg(2, 610, 50, hasTime: false),
                    MakePeakLeg(3, 620, null),
                    MakePeakLeg(4, 630, -5),
                    MakePeakLeg(5, 640, 80)
                }
            };

            ScheduleArrivalPeakResult excludedPeak =
                ScheduleArrivalPeakCalculator.Calculate(excluded);

            Check(excludedPeak.PeakPassengers == 180,
                $"Only timed, countable legs sum into the peak ({excludedPeak.PeakPassengers} == 180)");
            Check(excludedPeak.ArrivalLegsExcluded == 3,
                $"Missing time, missing counts and negative pax are excluded ({excludedPeak.ArrivalLegsExcluded} == 3)");
            Check(excludedPeak.ArrivalLegsUsed == 2,
                $"Timed, countable legs are counted as used ({excludedPeak.ArrivalLegsUsed} == 2)");

            // 5. Freight and departure legs never contribute, and are not
            //    counted as excluded; unknown sector still counts.
            FlightScheduleResult services = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 600, 120, service: LegServiceType.Freight),
                    MakePeakLeg(2, 610, 90, service: LegServiceType.Other),
                    MakePeakLeg(3, 620, 60, direction: LegDirection.Departure),
                    MakePeakLeg(4, 630, 40, sector: LegSector.Unknown)
                }
            };

            ScheduleArrivalPeakResult servicePeak =
                ScheduleArrivalPeakCalculator.Calculate(services);

            Check(servicePeak.PeakPassengers == 40,
                $"Freight, other-service and departure legs are ignored ({servicePeak.PeakPassengers} == 40)");
            Check(servicePeak.ArrivalLegsUsed == 1,
                $"The unknown-sector arrival still contributes ({servicePeak.ArrivalLegsUsed} == 1)");
            Check(servicePeak.ArrivalLegsExcluded == 0,
                "Non-passenger legs are not counted as excluded");

            // 6. Midnight span: 23:55 day 0 and 00:05 day 1 share one window
            //    on the continuous clock.
            FlightScheduleResult midnight = new FlightScheduleResult
            {
                Legs = new List<FlightLeg>
                {
                    MakePeakLeg(1, 1435, 60),
                    MakePeakLeg(2, 5, 40, dayOffset: 1)
                }
            };

            ScheduleArrivalPeakResult midnightPeak =
                ScheduleArrivalPeakCalculator.Calculate(midnight);

            Check(midnightPeak.PeakPassengers == 100,
                $"Windows span midnight on the continuous clock ({midnightPeak.PeakPassengers} == 100)");
            Check(midnightPeak.PeakWindowStartMinute == 1435,
                $"Midnight window starts 23:55 ({midnightPeak.PeakWindowStartMinute} == 1435)");

            // 7. Null and empty schedules report no peak.
            ScheduleArrivalPeakResult nullResult =
                ScheduleArrivalPeakCalculator.Calculate((FlightScheduleResult)null);

            Check(nullResult.PeakPassengers == 0 &&
                  nullResult.PeakWindowStartMinute == -1,
                "A null schedule reports no peak and no window");

            ScheduleArrivalPeakResult emptyResult =
                ScheduleArrivalPeakCalculator.Calculate(new FlightScheduleResult());

            Check(emptyResult.PeakPassengers == 0 &&
                  emptyResult.PeakWindowStartMinute == -1,
                "An empty schedule reports no peak and no window");

            // 8. Every arrival leg excluded: peak stays zero.
            ScheduleArrivalPeakResult allExcluded =
                ScheduleArrivalPeakCalculator.Calculate(new FlightScheduleResult
                {
                    Legs = new List<FlightLeg>
                    {
                        MakePeakLeg(1, 600, 100, hasTime: false)
                    }
                });

            Check(allExcluded.PeakPassengers == 0 &&
                  allExcluded.PeakWindowStartMinute == -1 &&
                  allExcluded.ArrivalLegsExcluded == 1,
                "A schedule with only unusable arrivals reports an empty peak");
        }

        private static FlightLeg MakePeakLeg(
            int row,
            int minute,
            int? pax,
            int? seats = null,
            double? loadFactor = null,
            LegDirection direction = LegDirection.Arrival,
            LegSector sector = LegSector.Domestic,
            LegServiceType service = LegServiceType.Passenger,
            bool hasTime = true,
            int dayOffset = 0)
        {
            return new FlightLeg
            {
                Id = $"P{row}",
                SourceRow = row,
                Direction = direction,
                MinutesFromMidnight = minute % 1440,
                DayOffset = dayOffset,
                HasTime = hasTime,
                PaxRaw = pax,
                SeatsRaw = seats,
                LoadFactor = loadFactor,
                SectorRaw = sector,
                ServiceType = service
            };
        }
    }
}
