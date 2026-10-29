using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities.CheckIn;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunFlightHourDemand()
        {
            Console.WriteLine("== Flight-hour passenger demand ==");

            PassengerProfile earlyProfile = MakeProfile(
                "Early", 100.0, 180.0, 180.0);

            PassengerGenerationResult domesticResult = GenerateFlightBasis(
                MakeFlightBasisGroup(
                    DHPType.DomesticDeparture, 80, earlyProfile),
                seed: 314);

            Check(domesticResult.Passengers.Count == 80,
                "Flight-hour DHD demand produces the expected passenger count");
            Check(domesticResult.Passengers.All(p =>
                    p.ScheduledDepartureTime >= 0.0 &&
                    p.ScheduledDepartureTime < 60.0),
                "Departure STD is sampled inside its demand hour");
            Check(domesticResult.Passengers.All(p =>
                    Math.Abs(p.ArrivalTime - (p.ScheduledDepartureTime - 180.0)) < 1e-9),
                "Departure arrival is placed relative to STD and profile advance");
            Check(domesticResult.Passengers.All(p =>
                    Math.Abs(p.CheckInOpenTime -
                        (p.ScheduledDepartureTime - 120.0)) < 1e-9 &&
                    Math.Abs(p.CheckInLastAdmissionTime -
                        (p.ScheduledDepartureTime - 60.0)) < 1e-9),
                "Domestic passengers carry STD-relative check-in window times");
            Check(domesticResult.Passengers.All(p => p.ArrivedBeforeCheckInOpen),
                "Early passengers are flagged while waiting for domestic check-in to open");
            Check(domesticResult.Passengers.All(p =>
                    p.DemandStepIndex == 0 && p.DemandHourIndex == 0 &&
                    p.IsDesignStep && !p.IsWarmUp && p.WaveId == -1),
                "Flight-hour passengers retain flight-hour tags without synthetic warm-up");
            Check(domesticResult.ArrivalSimulations.Any(s =>
                    s.StepStartMinute < 0.0 && s.Histogram.Sum() == 80),
                "Arrival histogram preserves departures before minute zero");

            PassengerGenerationResult internationalResult = GenerateFlightBasis(
                MakeFlightBasisGroup(
                    DHPType.InternationalDeparture,
                    20,
                    MakeProfile("International", 100.0, 240.0, 240.0)),
                seed: 42);

            Check(internationalResult.Passengers.All(p =>
                    Math.Abs(p.CheckInOpenTime -
                        (p.ScheduledDepartureTime - 180.0)) < 1e-9),
                "International check-in opens STD-180");

            List<string> overrideWarnings = new List<string>();
            CheckInWindowRules overriddenWindows = new CheckInWindowRules
            {
                Overrides = new List<CheckInWindowOverride>
                {
                    new CheckInWindowOverride
                    {
                        Airline = "AI",
                        FlightNumber = "101",
                        OpenMinutesBeforeDeparture = 300.0,
                        LastAdmissionMinutesBeforeDeparture = 240.0
                    }
                }
            };

            PassengerGenerationResult overrideResult = GenerateFlightBasis(
                MakeFlightBasisGroup(
                    DHPType.DomesticDeparture,
                    10,
                    MakeProfile("Override", 100.0, 180.0, 180.0)),
                seed: 26,
                checkInWindows: overriddenWindows,
                warn: overrideWarnings.Add);

            Check(overrideResult.Passengers.All(p =>
                    Math.Abs(p.CheckInOpenTime -
                        (p.ScheduledDepartureTime - 120.0)) < 1e-9) &&
                  overrideWarnings.Any(w => w.Contains("overrides are not applied")),
                "Flight-hour demand warns and uses sector defaults instead of airline overrides");

            PassengerGenerationResult arrivalResult = GenerateFlightBasis(
                MakeFlightBasisGroup(
                    DHPType.DomesticArrival,
                    30,
                    MakeProfile("Arrival", 100.0, 60.0, 150.0)),
                seed: 99,
                arrivalDelay: new ArrivalProcessDelay(15.0, 15.0));

            Check(arrivalResult.Passengers.All(p =>
                    p.ArrivalTime == p.ScheduledArrivalTime + 15.0),
                "Arrival passengers reach the arrival process at STA plus delay");
            Check(arrivalResult.Passengers.All(p =>
                    p.ScheduledDepartureTime == 0.0 && !p.IsWarmUp),
                "Arrival passengers have STA but no synthetic STD or warm-up tag");

            PassengerGenerationResult lateResult = GenerateFlightBasis(
                MakeFlightBasisGroup(
                    DHPType.DomesticDeparture,
                    12,
                    MakeProfile("Late", 100.0, 0.0, 0.0)),
                seed: 21);

            Check(lateResult.Passengers.All(p => p.IsLateForCheckIn),
                "Passengers arriving after last admission remain present and flagged");

            CheckInSolver solver = new CheckInSolver();
            CheckInResults solved = solver.Solve(new CheckInInputs
            {
                Passengers = lateResult.Passengers,
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 5.0,
                    StandardDeviation = 0.0
                },
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 7
            });

            Check(solved.QueueResults.ProcessedPassengers.Count == 0,
                "Late flight-hour passengers are excluded from check-in service");
        }

        private static void RunCheckInOpeningGate()
        {
            Console.WriteLine("== Check-in opening gate ==");

            Passenger early = new Passenger
            {
                Id = 1,
                DHPType = DHPType.DomesticDeparture,
                Channel = "Counter",
                ArrivalTime = 50.0,
                QueueArrivalTime = 50.0,
                CheckInOpenTime = 100.0,
                CheckInLastAdmissionTime = 200.0,
                ArrivedBeforeCheckInOpen = true
            };

            CheckInSolver solver = new CheckInSolver();
            CheckInResults solved = solver.Solve(new CheckInInputs
            {
                Passengers = new List<Passenger> { early },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 5.0,
                    StandardDeviation = 0.0
                },
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 1 },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 2
            });

            Passenger served = solved.QueueResults.ProcessedPassengers.Single();

            Check(served.StartServiceTime >= early.CheckInOpenTime,
                "Check-in service cannot start before the opening time");
            Check(Math.Abs(served.WaitTime) < 1e-9,
                "Waiting in the hall before opening is not counted as queue wait");
            Check(Math.Abs(early.ArrivalTime - 50.0) < 1e-9,
                "The check-in opening gate does not mutate the upstream passenger");
        }

        private static PassengerGenerationResult GenerateFlightBasis(
            PassengerGroup group,
            int seed,
            ArrivalProcessDelay arrivalDelay = null,
            CheckInWindowRules checkInWindows = null,
            Action<string> warn = null)
        {
            return new MultiIntervalPassengerGenerator().Generate(
                new List<PassengerGroup> { group },
                new WalkingProfile
                {
                    AverageSpeed = 1.4,
                    StandardDeviation = 0.2
                },
                new WaveSettings(),
                seed,
                ShowUpProfile.Mix(),
                warn: warn,
                checkInWindows: checkInWindows ?? CheckInWindowRules.CreateDefault(),
                arrivalDelay: arrivalDelay ?? new ArrivalProcessDelay(0.0, 0.0));
        }

        private static PassengerGroup MakeFlightBasisGroup(
            DHPType type,
            int demand,
            PassengerProfile profile)
        {
            return new PassengerGroup
            {
                DHP = new DHP
                {
                    Type = type,
                    DemandBasis = DemandTimeBasis.FlightHour,
                    DesignHourDemand = demand,
                    DemandFactors = new List<double> { 100.0 },
                    AnalysisPeriodHours = 1,
                    DesignHourIndex = 0,
                    WarmUpEnabled = true,
                    WarmUpMinutes = 60,
                    WarmUpFactor = 100.0
                },
                Profiles = new List<PassengerProfile> { profile }
            };
        }
    }
}
