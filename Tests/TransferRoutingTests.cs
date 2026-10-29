using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.Emigration;
using AirportPlanning.Facilities.Immigration;
using AirportPlanning.Facilities.Security;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunTransferRoutingTests()
        {
            Console.WriteLine("== Transfer routing (shared vs dedicated pools) ==");

            RunRoutingModelTests();
            RunSecurityRoutingTests();
            RunEmigrationRoutingTests();
            RunImmigrationTests();
        }

        // -------------------------------------------------------------
        // Rules model: defaults, clone preservation, validation
        // -------------------------------------------------------------
        private static void RunRoutingModelTests()
        {
            TransferScenarioRules rules = new TransferScenarioRules();
            Check(rules.RequiresTransferDesk == false,
                "Transfer desk requirement defaults to off");
            Check(rules.SecurityRouting == TransferFacilityRouting.Integrated &&
                  rules.ImmigrationRouting == TransferFacilityRouting.Integrated &&
                  rules.EmigrationRouting == TransferFacilityRouting.Integrated,
                "All routing choices default to Integrated");

            rules.RequiresTransferDesk = true;
            rules.SecurityRouting = TransferFacilityRouting.Separate;
            rules.ImmigrationRouting = TransferFacilityRouting.Separate;
            rules.EmigrationRouting = TransferFacilityRouting.Separate;

            TransferScenarioRules clone = rules.Clone();
            Check(clone.RequiresTransferDesk &&
                  clone.SecurityRouting == TransferFacilityRouting.Separate &&
                  clone.ImmigrationRouting == TransferFacilityRouting.Separate &&
                  clone.EmigrationRouting == TransferFacilityRouting.Separate,
                "Cloning preserves the transfer desk flag and every routing choice");

            TransferDemandProfile profile = NewTransferRoutingProfile();
            profile.ScenarioRules[TransferScenario.DomesticToInternational].SecurityRouting =
                (TransferFacilityRouting)99;
            Check(profile.GetValidationErrors().Any(e => e.Contains("security routing")),
                "Profile validation rejects an out-of-range security routing value");
        }

        // -------------------------------------------------------------
        // Security: dedicated transfer pool vs shared hall vs bypass
        // -------------------------------------------------------------
        private static void RunSecurityRoutingTests()
        {
            /* 6 transfer passengers routed separately, 2 transfers routed
               integrated, 2 originating passengers. A dedicated
               "Transfer Security" pool is configured alongside Counter. */
            List<Passenger> passengers = new List<Passenger>();

            TransferScenarioRules separateRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic,
                security: true,
                securityRouting: TransferFacilityRouting.Separate);
            TransferScenarioRules integratedRules = NewRoutingRules(
                TransferScenario.DomesticToDomestic,
                security: true,
                securityRouting: TransferFacilityRouting.Integrated);

            for (int i = 0; i < 6; i++)
                passengers.Add(TransferDeparture(100 + i, separateRules, TransferScenario.DomesticToDomestic));

            for (int i = 0; i < 2; i++)
                passengers.Add(TransferDeparture(200 + i, integratedRules, TransferScenario.DomesticToDomestic));

            for (int i = 0; i < 2; i++)
                passengers.Add(OriginDeparture(300 + i));

            SecuritySolver solver = new SecuritySolver();
            SecurityResults result = solver.Solve(new SecurityInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter", "Transfer Security" },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "Counter", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 6) },
                    new PoolPlan { Channel = "Transfer Security", PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 6) }
                },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            List<Passenger> processed = result.QueueResults.ProcessedPassengers;
            Check(processed.Count == 10,
                $"Security serves integrated transfers, separate transfers and origin passengers ({processed.Count})");

            List<Passenger> output = result.QueueResults.Passengers;
            List<Passenger> separateServed = output
                .Where(p => p.TransferProcessing != null &&
                            p.TransferProcessing.SecurityRouting == TransferFacilityRouting.Separate)
                .ToList();

            Check(separateServed.Count == 6 &&
                  separateServed.All(p => p.AssignedPool == "Transfer Security"),
                "Separately-routed transfers are served by the dedicated transfer-security pool");

            Check(separateServed.All(p => p.Channel == "Counter"),
                "Separate routing restores the upstream check-in channel after the solve");

            List<Passenger> integratedServed = output
                .Where(p => p.TransferProcessing != null &&
                            p.TransferProcessing.SecurityRouting == TransferFacilityRouting.Integrated)
                .ToList();
            Check(integratedServed.Count == 2 &&
                  integratedServed.All(p => p.AssignedPool == "Counter"),
                "Integrated transfers share the main security pool");

            Check(result.PoolNames.Contains("Transfer Security") &&
                  result.PhysicalPerChannel[result.PoolNames.IndexOf("Transfer Security")] == 1,
                "The transfer-security pool reports its own keyed plan");

            Check(solver.Warnings.Any(w => w.Contains("separate") && w.Contains("Transfer Security")),
                "The solver reports how many transfers were routed separately");

            /* Without a configured transfer pool, separately-routed transfers
               fall back to the shared pool and the solver says so. */
            SecuritySolver fallbackSolver = new SecuritySolver();
            SecurityResults fallback = fallbackSolver.Solve(new SecurityInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferDeparture(400, separateRules, TransferScenario.DomesticToDomestic)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(fallback.QueueResults.ProcessedPassengers.Count == 1 &&
                  fallback.QueueResults.ProcessedPassengers[0].AssignedPool != "Transfer Security",
                "Separate routing degrades to the shared pool when no transfer pool is configured");
            Check(fallbackSolver.Warnings.Any(w => w.Contains("no matching pool")),
                "The fallback warns that separate routing had no matching pool");

            /* A transfer whose rules clear security as satisfied bypasses
               security entirely. */
            TransferScenarioRules bypassRules = NewRoutingRules(
                TransferScenario.InternationalToInternational,
                security: false,
                securityRouting: TransferFacilityRouting.Integrated);
            SecuritySolver bypassSolver = new SecuritySolver();
            SecurityResults bypass = bypassSolver.Solve(new SecurityInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferDeparture(500, bypassRules, TransferScenario.InternationalToInternational,
                        DHPType.InternationalDeparture, PassengerType.International)
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(bypass.QueueResults.ProcessedPassengers.Count == 0 &&
                  bypass.QueueResults.Passengers[0].History.Count == 0,
                "A transfer without a security requirement passes through untouched");
        }

        // -------------------------------------------------------------
        // Emigration: dedicated transfer pool excluded from the e-gate split
        // -------------------------------------------------------------
        private static void RunEmigrationRoutingTests()
        {
            TransferScenarioRules separateRules = NewRoutingRules(
                TransferScenario.DomesticToInternational,
                emigration: true,
                emigrationRouting: TransferFacilityRouting.Separate);

            List<Passenger> passengers = new List<Passenger>();
            for (int i = 0; i < 4; i++)
                passengers.Add(TransferDeparture(
                    600 + i, separateRules, TransferScenario.DomesticToInternational,
                    DHPType.InternationalDeparture, PassengerType.International));
            for (int i = 0; i < 4; i++)
                passengers.Add(OriginDeparture(700 + i, international: true));

            EmigrationSolver solver = new EmigrationSolver();
            EmigrationResults result = solver.Solve(new EmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                EGateSharePercentage = 50.0,
                Channels = new List<string>
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool,
                    "Transfer Emigration"
                },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = EmigrationSolver.EGatePool, PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = EmigrationSolver.CounterPool, PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = "Transfer Emigration", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) }
                },
                OptimizeResources = false,
                Seed = 17,
                StaffingPercentage = 100.0
            });

            List<Passenger> processed = result.QueueResults.ProcessedPassengers;
            Check(processed.Count == 8,
                $"Emigration serves separately-routed transfers and origin departures ({processed.Count})");

            List<Passenger> transferServed = processed
                .Where(p => p.TransferProcessing != null)
                .ToList();
            Check(transferServed.Count == 4 &&
                  transferServed.All(p => p.EmigrationRoute != EmigrationSolver.EGatePool &&
                                          p.EmigrationRoute != EmigrationSolver.CounterPool),
                "Separately-routed transfers stay out of the e-gate/counter split");

            Dictionary<string, int> byPool = processed
                .GroupBy(p => p.AssignedPool)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            Check(byPool.TryGetValue("Transfer Emigration", out int transferCount) && transferCount == 4,
                "Transfers are served by the dedicated transfer-emigration pool");
            Check(byPool.TryGetValue(EmigrationSolver.EGatePool, out int egate) && egate == 2 &&
                  byPool.TryGetValue(EmigrationSolver.CounterPool, out int counter) && counter == 2,
                "The e-gate share applies to the remaining origin population only");

            Check(result.QueueResults.Passengers.All(p => p.Channel == "Counter"),
                "Emigration restores the upstream check-in channel for every passenger");

            /* Integrated transfers (the default) still join the split. */
            TransferScenarioRules integratedRules = NewRoutingRules(
                TransferScenario.DomesticToInternational,
                emigration: true,
                emigrationRouting: TransferFacilityRouting.Integrated);
            List<Passenger> integratedPopulation = new List<Passenger>
            {
                TransferDeparture(800, integratedRules, TransferScenario.DomesticToInternational,
                    DHPType.InternationalDeparture, PassengerType.International)
            };

            EmigrationSolver integratedSolver = new EmigrationSolver();
            EmigrationResults integrated = integratedSolver.Solve(new EmigrationInputs
            {
                Passengers = integratedPopulation,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                EGateSharePercentage = 100.0,
                Channels = new List<string> { EmigrationSolver.EGatePool },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = EmigrationSolver.EGatePool, PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) }
                },
                OptimizeResources = false,
                Seed = 17,
                StaffingPercentage = 100.0
            });

            Check(integrated.QueueResults.ProcessedPassengers.Count == 1 &&
                  integrated.QueueResults.ProcessedPassengers[0].AssignedPool == EmigrationSolver.EGatePool,
                "Integrated transfers still clear the shared emigration hall");
        }

        // -------------------------------------------------------------
        // Immigration: arrivals plus I→D transfers, I→I passes through
        // -------------------------------------------------------------
        private static void RunImmigrationTests()
        {
            TransferScenarioRules entryRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic,
                immigration: true,
                immigrationRouting: TransferFacilityRouting.Integrated);
            TransferScenarioRules airsideRules = NewRoutingRules(
                TransferScenario.InternationalToInternational,
                immigration: false,
                immigrationRouting: TransferFacilityRouting.Integrated);

            List<Passenger> passengers = new List<Passenger>
            {
                // I→D transfer: enters the country, clears immigration.
                TransferArrival(900, entryRules, TransferScenario.InternationalToDomestic),
                // I→I transfer: stays airside, never reaches immigration.
                TransferArrival(901, airsideRules, TransferScenario.InternationalToInternational),
                // Originating international arrival: clears immigration.
                new Passenger
                {
                    Id = 902,
                    Channel = "Arrival",
                    DHPType = DHPType.InternationalArrival,
                    Journey = PassengerJourney.Destination,
                    PassengerType = PassengerType.International,
                    ProfileName = "Arrival",
                    ArrivalTime = 4.0,
                    QueueArrivalTime = 4.0
                },
                // Domestic departure: never clears immigration.
                OriginDeparture(903)
            };

            ImmigrationSolver solver = new ImmigrationSolver();
            ImmigrationResults result = solver.Solve(new ImmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string>(),
                OptimizeResources = false,
                Seed = 19,
                StaffingPercentage = 100.0
            });

            List<Passenger> processed = result.QueueResults.ProcessedPassengers;
            Check(processed.Count == 2 &&
                  processed.All(p => p.Id == 900 || p.Id == 902),
                $"Immigration serves the I→D transfer and the international arrival only ({processed.Count})");

            Check(result.QueueResults.Passengers
                    .First(p => p.Id == 901).History.Count == 0,
                "The I→I transfer passes through without an immigration record");
            Check(result.QueueResults.Passengers
                    .First(p => p.Id == 903).History.Count == 0,
                "The domestic departure passes through without an immigration record");

            /* A dedicated transfer-immigration pool takes the separately-routed
               flow out of the shared hall. */
            TransferScenarioRules separateRules = NewRoutingRules(
                TransferScenario.InternationalToDomestic,
                immigration: true,
                immigrationRouting: TransferFacilityRouting.Separate);

            ImmigrationSolver separateSolver = new ImmigrationSolver();
            ImmigrationResults separate = separateSolver.Solve(new ImmigrationInputs
            {
                Passengers = new List<Passenger>
                {
                    TransferArrival(950, separateRules, TransferScenario.InternationalToDomestic),
                    new Passenger
                    {
                        Id = 951,
                        Channel = "Arrival",
                        DHPType = DHPType.InternationalArrival,
                        Journey = PassengerJourney.Destination,
                        PassengerType = PassengerType.International,
                        ProfileName = "Arrival",
                        ArrivalTime = 1.0,
                        QueueArrivalTime = 1.0
                    }
                },
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Arrival", "Transfer Immigration" },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "Arrival", PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = "Transfer Immigration", PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) }
                },
                OptimizeResources = false,
                Seed = 19,
                StaffingPercentage = 100.0
            });

            Check(separate.QueueResults.ProcessedPassengers.Count == 2 &&
                  separate.QueueResults.ProcessedPassengers
                      .First(p => p.TransferProcessing != null).AssignedPool == "Transfer Immigration",
                "Separately-routed I→D transfers use the dedicated transfer-immigration pool");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private static TransferDemandProfile NewTransferRoutingProfile()
        {
            return new TransferDemandProfile();
        }

        private static TransferScenarioRules NewRoutingRules(
            TransferScenario scenario,
            bool security = false,
            TransferFacilityRouting securityRouting = TransferFacilityRouting.Integrated,
            bool immigration = false,
            TransferFacilityRouting immigrationRouting = TransferFacilityRouting.Integrated,
            bool emigration = false,
            TransferFacilityRouting emigrationRouting = TransferFacilityRouting.Integrated)
        {
            return new TransferScenarioRules
            {
                Scenario = scenario,
                HasProcessingRules = true,
                MinimumConnectionMinutes = 0.0,
                MaximumConnectionMinutes = 1440.0,
                RequiresSecurityScreening = security,
                SecurityRouting = securityRouting,
                RequiresImmigration = immigration,
                ImmigrationRouting = immigrationRouting,
                RequiresEmigration = emigration,
                EmigrationRouting = emigrationRouting
            };
        }

        /// <summary>A departing transfer passenger (the arrival leg has not yet
        /// been generated, so departures carry the scenario tags today).</summary>
        private static Passenger TransferDeparture(
            int id,
            TransferScenarioRules rules,
            TransferScenario scenario,
            DHPType dhp = DHPType.DomesticDeparture,
            PassengerType type = PassengerType.Domestic)
        {
            return new Passenger
            {
                Id = id,
                Channel = "Counter",
                DHPType = dhp,
                Journey = PassengerJourney.Transfer,
                TransferScenario = scenario,
                TransferProcessing = rules.Clone(),
                PassengerType = type,
                ProfileName = "Transfer",
                ArrivalTime = id * 0.2,
                QueueArrivalTime = id * 0.2
            };
        }

        /// <summary>An arriving transfer passenger (International→Domestic or
        /// International→International at the inbound leg).</summary>
        private static Passenger TransferArrival(
            int id,
            TransferScenarioRules rules,
            TransferScenario scenario)
        {
            return new Passenger
            {
                Id = id,
                Channel = "Arrival",
                DHPType = DHPType.InternationalArrival,
                Journey = PassengerJourney.Transfer,
                TransferScenario = scenario,
                TransferProcessing = rules.Clone(),
                PassengerType = PassengerType.International,
                ProfileName = "Transfer",
                ArrivalTime = id * 0.2,
                QueueArrivalTime = id * 0.2
            };
        }

        private static Passenger OriginDeparture(int id, bool international = false)
        {
            return new Passenger
            {
                Id = id,
                Channel = "Counter",
                DHPType = international ? DHPType.InternationalDeparture : DHPType.DomesticDeparture,
                Journey = PassengerJourney.Origin,
                PassengerType = international ? PassengerType.International : PassengerType.Domestic,
                ProfileName = "Economy",
                ArrivalTime = id * 0.2,
                QueueArrivalTime = id * 0.2
            };
        }
    }
}
