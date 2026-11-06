using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Analysis;
using AirportPlanning.Core.Diagnostics;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.Customs;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunCustomsTests()
        {
            Console.WriteLine("== Customs hand baggage ==");

            RunCustomsEligibilityTests();
            RunCustomsSolverTests();
            RunCustomsDeterminismTests();
            RunCustomsOptimizerTests();
            RunCustomsNarrativeTests();
            RunCustomsDiagnosticsTests();
        }

        // -------------------------------------------------------------
        // Eligibility: who enters the country and therefore clears customs.
        // -------------------------------------------------------------
        private static void RunCustomsEligibilityTests()
        {
            Console.WriteLine("-- Customs eligibility --");

            Check(CustomsSolver.RequiresCustoms(CusPassenger(
                    1, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Destination, null, 1.0)),
                "An originating international arrival clears customs");

            Check(CustomsSolver.RequiresCustoms(CusPassenger(
                    2, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Transfer, TransferScenario.InternationalToDomestic, 1.0)),
                "An International→Domestic transfer enters the country and clears customs");

            Check(!CustomsSolver.RequiresCustoms(CusPassenger(
                    3, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Transfer, TransferScenario.InternationalToInternational, 1.0)),
                "An International→International transfer stays airside and bypasses customs");

            Check(!CustomsSolver.RequiresCustoms(CusPassenger(
                    4, DHPType.DomesticArrival, PassengerType.Domestic,
                    PassengerJourney.Destination, null, 1.0)),
                "A domestic arrival bypasses customs");

            Check(!CustomsSolver.RequiresCustoms(CusPassenger(
                    5, DHPType.DomesticArrival, PassengerType.Domestic,
                    PassengerJourney.Transfer, TransferScenario.DomesticToInternational, 1.0)),
                "A domestic-to-international transfer bypasses arrival customs");

            Check(!CustomsSolver.RequiresCustoms(CusPassenger(
                    6, DHPType.InternationalDeparture, PassengerType.International,
                    PassengerJourney.Origin, null, 1.0)),
                "An international departure does not use arrival customs");

            Check(!CustomsSolver.RequiresCustoms(null),
                "A null passenger is not eligible for customs");

            Check(FacilityName.Canonicalize("Customs") == FacilityName.Customs &&
                  FacilityName.Canonicalize("Customs Hand Baggage") == FacilityName.Customs &&
                  FacilityName.Canonicalize("Hand Baggage Screening") == FacilityName.Customs &&
                  !FacilityName.Matches("Customs", "Immigration"),
                "Customs tags canonicalize without colliding with Immigration");
            Check(FacilityScopes.Customs == FacilityName.Customs,
                "The Customs scope and canonical name agree");
        }

        // -------------------------------------------------------------
        // Solver: automatic filtering and one shared pool.
        // -------------------------------------------------------------
        private static void RunCustomsSolverTests()
        {
            Console.WriteLine("-- Customs solver filtering --");

            List<Passenger> passengers = CusCohort();
            CustomsSolver solver = new CustomsSolver();

            CustomsResults result = solver.Solve(new CustomsInputs
            {
                Passengers = passengers,
                ServiceProfile = CusServiceProfile(),
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            HashSet<int> expectedIds = new HashSet<int> { 6000, 6001, 6002, 6100, 6101 };
            HashSet<int> processedIds = new HashSet<int>(
                result.QueueResults.ProcessedPassengers.Select(p => p.Id));

            Check(processedIds.SetEquals(expectedIds),
                $"Customs serves 3 international arrivals + 2 International→Domestic transfers ({processedIds.Count})");

            Check(result.QueueResults.ProcessedPassengers.All(p =>
                    p.History.ContainsKey(FacilityType.Customs)),
                "Every processed passenger carries a Customs history record");

            Check(result.QueueResults.Passengers.Count == passengers.Count,
                $"The output population preserves every passenger ({result.QueueResults.Passengers.Count})");

            Check(passengers.Where(p => !expectedIds.Contains(p.Id)).All(p =>
                    !p.History.ContainsKey(FacilityType.Customs)),
                "Domestic arrivals, departures and International→International transfers pass through untouched");

            Check(result.PoolNames.Count == 1 && result.PoolNames[0] == "Shared",
                "Customs runs as one shared pool when no channels are wired");

            Check(result.QueueResults.ProcessedPassengers
                    .Where(p => p.TransferScenario == TransferScenario.InternationalToDomestic)
                    .All(p => p.AssignedPool == "Shared"),
                "International→Domestic transfers share the main customs pool (no transfer segregation)");

            Check(result.PhysicalPositions == 2 && result.ActivePositions == 2,
                $"Physical and active positions are reported ({result.PhysicalPositions}/{result.ActivePositions})");

            Check(result.QueueResults.ProcessedPassengers.All(p =>
                {
                    FacilityRecord record = p.History[FacilityType.Customs];

                    return record != null &&
                           Math.Abs(record.StartServiceTime - p.StartServiceTime) < 1e-9 &&
                           Math.Abs(record.EndServiceTime - p.EndServiceTime) < 1e-9 &&
                           Math.Abs(record.WaitTime - p.WaitTime) < 1e-9 &&
                           record.EndServiceTime >= record.StartServiceTime &&
                           Math.Abs(record.WaitTime - (record.StartServiceTime - record.ArrivalTime)) < 1e-9;
                }),
                "Customs history matches the simulated service times and waits");

            Check(solver.Warnings.Count == 0,
                "A wired service profile produces no customs warnings");

            // A channel filter must never admit an ineligible passenger. The
            // whole cohort shares the "Arrival" channel, including the
            // International→International transfers and domestic arrivals.
            CustomsResults wired = new CustomsSolver().Solve(new CustomsInputs
            {
                Passengers = passengers,
                ServiceProfile = CusServiceProfile(),
                Channels = new List<string> { "Arrival" },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Channel = "Arrival",
                        PhysicalResources = 3,
                        TargetWait95 = 10.0,
                        Range = new ResourceSearchRange(1, 6)
                    }
                },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(wired.QueueResults.ProcessedPassengers.Count == 5 &&
                  wired.QueueResults.ProcessedPassengers.All(p => expectedIds.Contains(p.Id)),
                "Wiring a channel cannot admit passengers who bypass customs");
            Check(wired.PhysicalPositions == 3 &&
                  wired.PoolNames.Count == 1 &&
                  wired.PoolNames[0] == "Arrival",
                "A pool plan supplies the customs pool identity and fixed positions");
        }

        // -------------------------------------------------------------
        // Repeated runs with the same seed stay deterministic.
        // -------------------------------------------------------------
        private static void RunCustomsDeterminismTests()
        {
            Console.WriteLine("-- Customs determinism --");

            CustomsResults first = new CustomsSolver().Solve(CusInput());
            CustomsResults second = new CustomsSolver().Solve(CusInput());

            List<Passenger> firstProcessed = first.QueueResults.ProcessedPassengers
                .OrderBy(p => p.Id).ToList();
            List<Passenger> secondProcessed = second.QueueResults.ProcessedPassengers
                .OrderBy(p => p.Id).ToList();

            Check(firstProcessed.Count == secondProcessed.Count &&
                  firstProcessed.Zip(secondProcessed, (a, b) =>
                      a.Id == b.Id &&
                      Math.Abs(a.ServiceTime - b.ServiceTime) < 1e-9 &&
                      Math.Abs(a.StartServiceTime - b.StartServiceTime) < 1e-9 &&
                      Math.Abs(a.EndServiceTime - b.EndServiceTime) < 1e-9).All(equal => equal),
                "Two Customs runs with the same seed produce identical service histories");

            Check(Math.Abs(first.QueueResults.Wait95 - second.QueueResults.Wait95) < 1e-9,
                "Two Customs runs with the same seed produce the same Wait95");
        }

        // -------------------------------------------------------------
        // Optimizer: shared-pool sizing honours the resource domain.
        // -------------------------------------------------------------
        private static void RunCustomsOptimizerTests()
        {
            Console.WriteLine("-- Customs optimizer --");

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 60; i++)
            {
                passengers.Add(CusPassenger(
                    7000 + i, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Destination, null, 1.0 + i * 0.5));
            }

            CustomsSolver solver = new CustomsSolver();
            CustomsResults result = solver.Solve(new CustomsInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 2.0,
                    StandardDeviation = 0.0
                },
                PhysicalResources = new List<int> { 1 },
                TargetWait95 = new List<double> { 5.0 },
                ResourceDomain = new ResourceSearchRange(1, 10),
                OptimizeResources = true,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Check(result.PhysicalPositions >= 1 && result.PhysicalPositions <= 10,
                $"Customs optimizer stays inside the resource domain ({result.PhysicalPositions})");
            Check(result.SearchOutcomes != null && result.SearchOutcomes.Count == 1,
                "The shared customs pool reports one optimizer search outcome");
            Check(result.QueueResults.Wait95 <= 5.0 + 1e-6 ||
                  result.PhysicalPositions == 10,
                $"The optimizer meets the target wait or reaches the search limit ({result.QueueResults.Wait95:0.00} min)");
        }

        // -------------------------------------------------------------
        // Narration: customs appears for entering-country arrivals only.
        // -------------------------------------------------------------
        private static void RunCustomsNarrativeTests()
        {
            Console.WriteLine("-- Customs narration --");

            Passenger intlArrival = CusPassenger(
                8000, DHPType.InternationalArrival, PassengerType.International,
                PassengerJourney.Destination, null, 1.0);
            CusWriteHistory(intlArrival, 1.0);

            List<string> intlNarrative = PassengerNarrativeBuilder.BuildNarration(intlArrival);

            Check(intlNarrative.Any(l =>
                    l.Contains("Customs Hand Baggage") && l.Contains("served")),
                "An international arrival narrates its customs visit with times");

            Passenger airside = CusPassenger(
                8001, DHPType.InternationalArrival, PassengerType.International,
                PassengerJourney.Transfer, TransferScenario.InternationalToInternational, 1.0);

            List<string> airsideNarrative = PassengerNarrativeBuilder.BuildNarration(airside);

            Check(!airsideNarrative.Any(l => l.Contains("Customs Hand Baggage")),
                "An International→International transfer is not offered a customs stage");

            List<string> explicitAirside = PassengerNarrativeBuilder.BuildNarration(
                airside, new[] { "Customs Hand Baggage" });

            Check(explicitAirside.Any(l =>
                    l.Contains("Customs Hand Baggage") && l.Contains("stay airside")),
                "When the customs stage is narrated explicitly, the airside bypass reason is named");

            Passenger domesticArrival = CusPassenger(
                8002, DHPType.DomesticArrival, PassengerType.Domestic,
                PassengerJourney.Destination, null, 1.0);

            List<string> domesticNarrative = PassengerNarrativeBuilder.BuildNarration(domesticArrival);

            Check(!domesticNarrative.Any(l => l.Contains("Customs Hand Baggage")),
                "A domestic arrival is not offered a customs stage");
        }

        // -------------------------------------------------------------
        // Diagnostics: the history key identifies customs.
        // -------------------------------------------------------------
        private static void RunCustomsDiagnosticsTests()
        {
            Console.WriteLine("-- Customs diagnostics --");

            CustomsResults result = new CustomsSolver().Solve(CusInput());

            Check(DiagnosticsServiceKindParser.Parse("Customs Hand Baggage") ==
                  DiagnosticsServiceKind.Customs &&
                  DiagnosticsServiceKindParser.Parse("Customs") ==
                  DiagnosticsServiceKind.Customs,
                "Customs labels resolve to the diagnostics service kind");

            Check(DiagnosticsServiceKindParser.InferFromQueue(result.QueueResults) ==
                  DiagnosticsServiceKind.Customs,
                "The customs history key is inferred from Queue Results");

            Passenger intlArrival = CusPassenger(
                8100, DHPType.InternationalArrival, PassengerType.International,
                PassengerJourney.Destination, null, 1.0);
            Passenger airside = CusPassenger(
                8101, DHPType.InternationalArrival, PassengerType.International,
                PassengerJourney.Transfer, TransferScenario.InternationalToInternational, 1.0);

            Check(ServiceEligibility.Evaluate(
                      intlArrival, DiagnosticsServiceKind.Customs).IsEligible,
                "Diagnostics expects an international arrival at customs");
            Check(!ServiceEligibility.Evaluate(
                      airside, DiagnosticsServiceKind.Customs).IsEligible,
                "Diagnostics does not expect an International→International transfer at customs");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private static CustomsInputs CusInput()
        {
            return new CustomsInputs
            {
                Passengers = CusCohort(),
                ServiceProfile = CusServiceProfile(),
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            };
        }

        private static List<Passenger> CusCohort()
        {
            TransferScenarioRules entryRules = new TransferScenarioRules
            {
                Scenario = TransferScenario.InternationalToDomestic,
                HasProcessingRules = true,
                MinimumConnectionMinutes = 0.0,
                MaximumConnectionMinutes = 1440.0,
                RequiresImmigration = true
            };

            List<Passenger> passengers = new List<Passenger>();

            // Originating international arrivals: entering the country.
            for (int i = 0; i < 3; i++)
            {
                passengers.Add(CusPassenger(
                    6000 + i, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Destination, null, 1.0 + i * 0.25));
            }

            // Domestic arrivals: stay landside, never clear customs.
            for (int i = 0; i < 2; i++)
            {
                passengers.Add(CusPassenger(
                    6003 + i, DHPType.DomesticArrival, PassengerType.Domestic,
                    PassengerJourney.Destination, null, 1.0 + i * 0.25));
            }

            // International→Domestic transfers: enter the country.
            for (int i = 0; i < 2; i++)
            {
                Passenger transfer = CusPassenger(
                    6100 + i, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Transfer, TransferScenario.InternationalToDomestic,
                    2.0 + i * 0.25);
                transfer.TransferProcessing = entryRules.Clone();
                passengers.Add(transfer);
            }

            // International→International transfers: remain airside.
            for (int i = 0; i < 2; i++)
            {
                passengers.Add(CusPassenger(
                    6110 + i, DHPType.InternationalArrival, PassengerType.International,
                    PassengerJourney.Transfer, TransferScenario.InternationalToInternational,
                    2.0 + i * 0.25));
            }

            // A domestic arrival on an onward international connection.
            passengers.Add(CusPassenger(
                6112, DHPType.DomesticArrival, PassengerType.Domestic,
                PassengerJourney.Transfer, TransferScenario.DomesticToInternational, 3.0));

            // A departing passenger cannot reach arrival customs.
            passengers.Add(CusPassenger(
                6200, DHPType.InternationalDeparture, PassengerType.International,
                PassengerJourney.Origin, null, 1.0));

            return passengers;
        }

        private static Passenger CusPassenger(
            int id,
            DHPType dhp,
            PassengerType type,
            PassengerJourney journey,
            TransferScenario? scenario,
            double arrival)
        {
            return new Passenger
            {
                Id = id,
                Channel = "Arrival",
                ProfileName = "Customs",
                DHPType = dhp,
                PassengerType = type,
                Journey = journey,
                TransferScenario = scenario,
                ArrivalTime = arrival,
                QueueArrivalTime = arrival
            };
        }

        private static ServiceProfile CusServiceProfile()
        {
            return new ServiceProfile
            {
                MeanServiceTime = 1.0,
                StandardDeviation = 0.0
            };
        }

        private static void CusWriteHistory(Passenger passenger, double arrival)
        {
            passenger.ArrivalTime = arrival;
            passenger.StartServiceTime = arrival + 1.0;
            passenger.EndServiceTime = arrival + 2.0;
            passenger.WaitTime = 1.0;
            passenger.AssignedCounter = 1;
            passenger.AssignedPool = "Shared";

            passenger.History[FacilityType.Customs] = new FacilityRecord
            {
                ArrivalTime = arrival,
                StartServiceTime = arrival + 1.0,
                EndServiceTime = arrival + 2.0,
                WaitTime = 1.0,
                AssignedResource = 1,
                PoolName = "Shared"
            };
        }
    }
}
