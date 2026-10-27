using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Walking;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunScheduleGeneratorTiming()
        {
            Console.WriteLine("== Schedule generator timing ==");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Timing",
                Legs = new List<FlightLeg>
                {
                    MakeLeg("L1", LegDirection.Departure, 720, 100, LegSector.Domestic)
                }
            };

            PassengerProfile economy = MakeScheduleProfile(
                "Economy", 60.0, 60.0, 120.0, ShowUpProfile.Mix());
            PassengerProfile business = MakeScheduleProfile(
                "Business", 40.0, 30.0, 90.0, ShowUpProfile.Mix());

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                MakeScheduleGroup(DHPType.DomesticDeparture, economy, business)
            };

            SchedulePassengerGenerationResult result = GenerateFromSchedule(
                schedule, groups, seed: 12, applyWalk: false, walkMetres: 0.0,
                arrivalDelay: new ArrivalProcessDelay(0.0, 30.0));

            Check(result.Passengers.Count == 100,
                $"Schedule PAX are generated exactly ({result.Passengers.Count} == 100)");
            Check(result.DemandByType[DHPType.DomesticDeparture] == 100,
                "All passengers are tagged Domestic Departure");

            int economyCount = result.Passengers.Count(p => p.ProfileName == "Economy");
            int businessCount = result.Passengers.Count(p => p.ProfileName == "Business");

            Check(economyCount == 60, $"Economy share is 60% ({economyCount})");
            Check(businessCount == 40, $"Business share is 40% ({businessCount})");

            Check(result.Passengers.All(p => Math.Abs(p.ScheduledDepartureTime - 720.0) < 1e-9),
                "Every passenger carries the flight's actual STD");

            bool economyInWindow = result.Passengers
                .Where(p => p.ProfileName == "Economy")
                .All(p => p.ArrivalTime >= 600.0 - 1e-9 && p.ArrivalTime <= 660.0 + 1e-9);

            bool businessInWindow = result.Passengers
                .Where(p => p.ProfileName == "Business")
                .All(p => p.ArrivalTime >= 630.0 - 1e-9 && p.ArrivalTime <= 690.0 + 1e-9);

            Check(economyInWindow,
                "Economy arrivals span STD-120 to STD-60");
            Check(businessInWindow,
                "Business arrivals span STD-90 to STD-30");
            Check(result.Passengers.All(p => !string.IsNullOrEmpty(p.FlightLegId)),
                "Flight-leg identity survives onto every passenger");
            Check(result.DesignStepIndex >= 0,
                $"A busiest generated hour is selected ({result.DesignStepIndex})");
            Check(result.ArrivalSimulations.Count > 0,
                "Schedule generation emits arrival simulations");

            SchedulePassengerGenerationResult repeat = GenerateFromSchedule(
                schedule, groups, seed: 12, applyWalk: false, walkMetres: 0.0,
                arrivalDelay: new ArrivalProcessDelay(0.0, 30.0));

            bool identical = repeat.Passengers.Count == result.Passengers.Count;

            for (int i = 0; identical && i < repeat.Passengers.Count; i++)
            {
                identical = Math.Abs(
                    repeat.Passengers[i].ArrivalTime -
                    result.Passengers[i].ArrivalTime) < 1e-9;
            }

            Check(identical, "Same seed reproduces schedule-driven arrivals");
        }

        private static void RunScheduleDemandPeaksWithoutDhp()
        {
            Console.WriteLine("== Schedule demand peaks without DHP ==");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Peaks",
                Legs = new List<FlightLeg>
                {
                    MakeLeg("DD", LegDirection.Departure, 720, 100, LegSector.Domestic),
                    MakeLeg("DA", LegDirection.Arrival, 1080, 50, LegSector.Domestic),
                    MakeLeg("ID", LegDirection.Departure, 720, 40, LegSector.International),
                    MakeLeg("IA", LegDirection.Arrival, 1080, 20, LegSector.International)
                }
            };

            PassengerProfile profile = MakeScheduleProfile(
                "Economy", 100.0, 60.0, 120.0, ShowUpProfile.Mix());

            // Deliberately no DHP: the group type alone must be enough.
            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                MakeTypedScheduleGroup(DHPType.DomesticDeparture, profile),
                MakeTypedScheduleGroup(DHPType.DomesticArrival, profile),
                MakeTypedScheduleGroup(DHPType.InternationalDeparture, profile),
                MakeTypedScheduleGroup(DHPType.InternationalArrival, profile)
            };

            SchedulePassengerGenerationResult result = GenerateFromSchedule(
                schedule, groups, seed: 15, applyWalk: false, walkMetres: 0.0,
                arrivalDelay: new ArrivalProcessDelay(0.0, 0.0));

            Check(result.Passengers.Count == 210,
                $"Groups without a DHP still generate every PAX ({result.Passengers.Count} == 210)");

            Check(result.DomesticDeparturePeak == 100,
                $"DD peak is schedule PAX ({result.DomesticDeparturePeak} == 100)");
            Check(result.DomesticDeparturePeakAt == 665,
                $"DD peak window starts 11:05 for a 12:00 flight ({result.DomesticDeparturePeakAt})");
            Check(result.DomesticArrivalPeak == 50,
                $"DA peak is schedule PAX ({result.DomesticArrivalPeak} == 50)");
            Check(result.DomesticArrivalPeakAt == 1025,
                $"DA peak window starts 17:05 for an 18:00 flight ({result.DomesticArrivalPeakAt})");
            Check(result.InternationalDeparturePeak == 40,
                $"ID peak is schedule PAX ({result.InternationalDeparturePeak} == 40)");
            Check(result.InternationalDeparturePeakAt == 665,
                $"ID peak window starts 11:05 for a 12:00 flight ({result.InternationalDeparturePeakAt})");
            Check(result.InternationalArrivalPeak == 20,
                $"IA peak is schedule PAX ({result.InternationalArrivalPeak} == 20)");
            Check(result.InternationalArrivalPeakAt == 1025,
                $"IA peak window starts 17:05 for an 18:00 flight ({result.InternationalArrivalPeakAt})");
            Check(result.CombinedPeak == 140,
                $"Combined peak counts both directions in one window ({result.CombinedPeak} == 140)");
            Check(result.CombinedPeakAt == 665,
                $"Combined peak window starts 11:05 for the 12:00 bank ({result.CombinedPeakAt})");

            Check(result.ScheduleRolling.Count > 0,
                "Raw schedule rolling series is populated");
            Check(result.PassengerRolling.Count > 0,
                "Generated-passenger rolling series is populated");
            Check(result.ScheduleRolling.Any(s =>
                    s.Minute == 720 && s.DomesticDeparture == 100),
                "Raw schedule series carries the flight at its STD");
            Check(result.DesignStepIndex == 11,
                $"Default design hour follows the schedule peak window ({result.DesignStepIndex} == 11)");
        }

        private static void RunScheduleGeneratorCheckInAndArrivals()
        {
            Console.WriteLine("== Schedule generator check-in windows / arrivals ==");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Windows",
                Legs = new List<FlightLeg>
                {
                    MakeLeg("D1", LegDirection.Departure, 720, 70, LegSector.Domestic),
                    MakeLeg("A1", LegDirection.Arrival, 600, 20, LegSector.International)
                }
            };

            PassengerProfile econ = MakeScheduleProfile(
                "Economy", 100.0, 60.0, 240.0, ShowUpProfile.Mix());

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                MakeScheduleGroup(DHPType.DomesticDeparture, econ),
                MakeScheduleGroup(DHPType.InternationalArrival, econ)
            };

            SchedulePassengerGenerationResult result = GenerateFromSchedule(
                schedule, groups, seed: 3, applyWalk: true, walkMetres: 1000.0,
                arrivalDelay: new ArrivalProcessDelay(5.0, 25.0));

            List<Passenger> departures = result.Passengers
                .Where(p => p.DHPType == DHPType.DomesticDeparture)
                .ToList();

            List<Passenger> arrivals = result.Passengers
                .Where(p => p.DHPType == DHPType.InternationalArrival)
                .ToList();

            Check(departures.Count == 70, $"Departure count follows schedule PAX ({departures.Count})");
            Check(arrivals.Count == 20, $"Arrival count follows schedule PAX ({arrivals.Count})");

            Check(departures.All(p => Math.Abs(p.CheckInOpenTime - 600.0) < 1e-9),
                "Domestic check-in opens at STD-120");
            Check(departures.All(p => Math.Abs(p.CheckInLastAdmissionTime - 660.0) < 1e-9),
                "Domestic last admission is STD-60");
            Check(departures.Any(p => p.ArrivedBeforeCheckInOpen),
                "Early terminal arrivals are flagged before check-in opens");
            Check(departures.Any(p => p.IsLateForCheckIn),
                "Walking past last admission is flagged late, not silently moved");
            Check(result.LateCheckInCount == departures.Count(p => p.IsLateForCheckIn),
                "Late count matches the passenger flags");
            Check(result.BeforeCheckInOpenCount == departures.Count(p => p.ArrivedBeforeCheckInOpen),
                "Before-open count matches the passenger flags");

            Check(arrivals.All(p => Math.Abs(p.ScheduledArrivalTime - 600.0) < 1e-9),
                "Arrival passengers carry STA");
            Check(arrivals.All(p => p.ArrivalTime >= 605.0 - 1e-9 &&
                                    p.ArrivalTime <= 625.0 + 1e-9),
                "Arrival passengers are placed by the explicit STA delay assumption");
            Check(arrivals.All(p => p.ScheduledDepartureTime == 0.0),
                "Arrival passengers do not receive a synthetic departure time");
        }

        private static void RunScheduleCheckInLastAdmission()
        {
            Console.WriteLine("== Schedule check-in last admission ==");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "LastAdmission",
                Legs = new List<FlightLeg>
                {
                    MakeLeg("L1", LegDirection.Departure, 720, 5, LegSector.Domestic)
                }
            };

            PassengerProfile profile = MakeScheduleProfile(
                "Economy", 100.0, 60.0, 120.0, ShowUpProfile.Mix());

            SchedulePassengerGenerationResult generated = GenerateFromSchedule(
                schedule,
                new List<PassengerGroup>
                {
                    MakeScheduleGroup(DHPType.DomesticDeparture, profile)
                },
                seed: 4, applyWalk: true, walkMetres: 10000.0,
                arrivalDelay: new ArrivalProcessDelay());

            Check(generated.Passengers.All(p => p.IsLateForCheckIn),
                "A walk beyond last admission flags every passenger late");

            AirportPlanning.Facilities.CheckIn.CheckInSolver solver =
                new AirportPlanning.Facilities.CheckIn.CheckInSolver();

            AirportPlanning.Facilities.CheckIn.CheckInResults solved = solver.Solve(
                new AirportPlanning.Facilities.CheckIn.CheckInInputs
                {
                    Passengers = generated.Passengers.Select(p => p.Clone()).ToList(),
                    ServiceProfile = new ServiceProfile
                    {
                        MeanServiceTime = 2.0,
                        StandardDeviation = 0.1
                    },
                    Channels = new List<string>(),
                    PhysicalResources = new List<int> { 5 },
                    OptimizeResources = false,
                    Seed = 2,
                    StaffingPercentage = 100.0
                });

            Check(solved.QueueResults.ProcessedPassengers.Count == 0,
                "Late passengers are not served at check-in");
            Check(solver.Warnings.Any(w => w.Contains("after the flight's last-admission")),
                "The check-in solver reports the excluded late passengers");
        }

        private static void RunScheduleFallbacksAndRules()
        {
            Console.WriteLine("== Schedule fallbacks / check-in overrides / size defaults ==");

            FlightLeg overrideLeg = MakeLeg(
                "O1", LegDirection.Departure, 720, 10, LegSector.Domestic);
            overrideLeg.Airline = "AI";

            CheckInWindowRules rules = new CheckInWindowRules();

            rules.Overrides.Add(new CheckInWindowOverride
            {
                Airline = "AI",
                OpenMinutesBeforeDeparture = 150.0,
                LastAdmissionMinutesBeforeDeparture = 45.0
            });

            CheckInWindow window = rules.Resolve(overrideLeg);

            Check(Math.Abs(window.OpensAt - 570.0) < 1e-9 &&
                  Math.Abs(window.LastAdmissionAt - 675.0) < 1e-9,
                "Airline override replaces the sector check-in window");

            FlightLeg fallbackLeg = MakeLeg(
                "S1", LegDirection.Departure, 720, 0, LegSector.Domestic);
            fallbackLeg.PaxRaw = null;
            fallbackLeg.SeatsRaw = 180;
            fallbackLeg.LoadFactor = 0.5;

            FlightScheduleResult fallbackSchedule = new FlightScheduleResult
            {
                ScheduleName = "Fallback",
                Legs = new List<FlightLeg> { fallbackLeg }
            };

            PassengerProfile profile = MakeScheduleProfile(
                "Economy", 100.0, 60.0, 120.0, ShowUpProfile.Mix());

            SchedulePassengerGenerationResult fallback = GenerateFromSchedule(
                fallbackSchedule,
                new List<PassengerGroup>
                {
                    MakeScheduleGroup(DHPType.DomesticDeparture, profile)
                },
                seed: 9, applyWalk: false, walkMetres: 0.0,
                arrivalDelay: new ArrivalProcessDelay());

            Check(fallback.Passengers.Count == 90,
                $"Missing PAX falls back to seats x load factor ({fallback.Passengers.Count} == 90)");

            Check(DeplaningProfile.BandForMppa(8.0) == AirportSizeBand.Small,
                "8 mppa selects the Small deplaning preset");
            Check(DeplaningProfile.BandForMppa(20.0) == AirportSizeBand.Medium,
                "20 mppa selects the Medium deplaning preset");
            Check(DeplaningProfile.BandForMppa(30.0) == AirportSizeBand.Large,
                "30 mppa selects the Large deplaning preset");
            Check(DeplaningProfile.BandForMppa(0.0) == AirportSizeBand.Medium,
                "Unknown mppa falls back to Medium");

            DeplaningProfile large = DeplaningProfile.FromAirportSize(30.0);
            Check(Math.Abs(large.W15 - 0.35) < 1e-9 &&
                  Math.Abs(large.W30 - 0.50) < 1e-9,
                "Large size preset uses 35/50/100");

            Random random = new Random(1);

            for (int i = 0; i < 200; i++)
            {
                double position = ShowUpProfile.Mix().SampleNormalizedElapsed(random);

                if (position < 0.0 || position > 1.0)
                {
                    Check(false, "Show-up sampling stays within the normalized interval");
                    break;
                }
            }

            Check(true, "Show-up sampling returns normalized positions");
        }

        private static SchedulePassengerGenerationResult GenerateFromSchedule(
            FlightScheduleResult schedule,
            List<PassengerGroup> groups,
            int seed,
            bool applyWalk,
            double walkMetres,
            ArrivalProcessDelay arrivalDelay)
        {
            return new SchedulePassengerGenerator().Generate(
                schedule,
                groups,
                new WalkingProfile { AverageSpeed = 1.4, StandardDeviation = 0.2 },
                seed,
                PassengerArrivalBasis.TerminalEntry,
                applyWalk,
                walkMetres,
                new CheckInWindowRules(),
                arrivalDelay,
                ShowUpProfile.Mix());
        }

        private static PassengerGroup MakeScheduleGroup(
            DHPType type,
            params PassengerProfile[] profiles)
        {
            return new PassengerGroup
            {
                DHP = new DHP
                {
                    Type = type,
                    DesignHourDemand = 0,
                    AnalysisPeriodHours = 24,
                    DesignHourIndex = 0
                },
                Profiles = profiles.ToList()
            };
        }

        private static PassengerGroup MakeTypedScheduleGroup(
            DHPType type,
            params PassengerProfile[] profiles)
        {
            return new PassengerGroup
            {
                Type = type,
                Profiles = profiles.ToList()
            };
        }

        private static PassengerProfile MakeScheduleProfile(
            string name,
            double percentage,
            double minAdvance,
            double maxAdvance,
            ShowUpProfile showUp)
        {
            return new PassengerProfile
            {
                Name = name,
                Percentage = percentage,
                Journey = PassengerJourney.Origin,
                OnlineCheckInProbability = 0.2,
                CheckedBagProbability = 0.4,
                MaleProbability = 0.5,
                MinAdvanceMinutes = minAdvance,
                MaxAdvanceMinutes = maxAdvance,
                DepartureShowUp = showUp
            };
        }

        private static FlightLeg MakeLeg(
            string id,
            LegDirection direction,
            int minutes,
            int pax,
            LegSector sector)
        {
            return new FlightLeg
            {
                Id = id,
                LinkId = id,
                Direction = direction,
                HasTime = true,
                MinutesFromMidnight = minutes,
                DayOffset = 0,
                Airline = "AI",
                FlightNumber = "101",
                SectorRaw = sector,
                ServiceType = LegServiceType.Passenger,
                PaxRaw = pax,
                SourceRow = 8
            };
        }
    }
}
