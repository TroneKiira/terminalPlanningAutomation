using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Area;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.TerminalEntry;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Sizers;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunTerminalEntryQueueAndRouting()
        {
            Console.WriteLine("== Terminal entry queue / routing ==");

            List<Passenger> passengers = BuildTerminalEntryPopulation(
                departureCount: 300,
                arrivalCount: 50,
                spanMinutes: 30,
                walkMinutes: 2.0);

            List<double> sourceArrivals = passengers
                .Select(p => p.ArrivalTime)
                .ToList();

            TerminalEntryInputs input = new TerminalEntryInputs
            {
                Passengers = passengers,
                ServiceTimeProvider = BuildTerminalEntryServiceTimeMap(),
                DigiYatraSharePercentage = 40.0,
                ManualPhysicalPositions = 2,
                DigiYatraPhysicalPositions = 1,
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 7
            };

            TerminalEntrySolver solver = new TerminalEntrySolver();
            TerminalEntryResults result = solver.Solve(input);
            QueueResults queue = result.QueueResults;

            Check(queue.ProcessedPassengers.Count == 300,
                $"Only the 300 departures are served ({queue.ProcessedPassengers.Count})");
            Check(queue.Passengers.Count == 350,
                $"All 350 passengers leave the stage ({queue.Passengers.Count})");

            int digiYatra = queue.ProcessedPassengers.Count(p =>
                string.Equals(p.TerminalEntryRoute, TerminalEntrySolver.DigiYatraRoute, StringComparison.OrdinalIgnoreCase));
            int manual = queue.ProcessedPassengers.Count(p =>
                string.Equals(p.TerminalEntryRoute, TerminalEntrySolver.ManualRoute, StringComparison.OrdinalIgnoreCase));

            Check(digiYatra == 120, $"40% route to DigiYatra ({digiYatra})");
            Check(manual == 180, $"60% route to manual verification ({manual})");

            Check(queue.Passengers
                .Where(p => p.DHPType == DHPType.DomesticArrival)
                .All(p => string.IsNullOrEmpty(p.TerminalEntryRoute)),
                "Arrival passengers bypass terminal entry");

            Check(queue.ProcessedPassengers.All(p => p.History.ContainsKey(FacilityType.TerminalEntry)),
                "Every served passenger records a terminal-entry visit");

            Check(queue.ProcessedPassengers.All(p =>
                    Math.Abs(p.ArrivalTime - (p.EndServiceTime + 2.0)) < 1e-9),
                "Entry service is followed by the entry-to-check-in walk");

            Check(queue.ProcessedPassengers.All(p =>
                    string.Equals(p.Channel, "Counter", StringComparison.OrdinalIgnoreCase)),
                "Check-in routing survives the temporary entry-route channel");

            Check(passengers.All(p => p.History.Count == 0),
                "The upstream passenger population is not mutated");

            Check(passengers.Select(p => p.ArrivalTime).SequenceEqual(sourceArrivals),
                "The upstream arrival times are not mutated");

            Check(result.PhysicalPositions == 3,
                $"Fixed positions total 3 ({result.PhysicalPositions})");
            Check(result.PoolNames.Count == 2 &&
                  result.PoolNames.Contains(TerminalEntrySolver.ManualRoute) &&
                  result.PoolNames.Contains(TerminalEntrySolver.DigiYatraRoute),
                "Manual and DigiYatra pools are reported");
            Check(result.Wait95PerPool.Count == 2,
                $"Wait95 is reported per entry pool ({result.Wait95PerPool.Count})");
            Check(result.AverageWaitPerPool.Count == 2 &&
                  result.MaxWaitPerPool.Count == 2 &&
                  result.Wait95ListPerPool.Count == 2 &&
                  result.PeakQueuePerPool.Count == 2,
                "Average, maximum, Wait95 and peak queue are reported per entry pool");
            Check(result.MaxWaitPerPool.All(w => w <= queue.MaxWait + 1e-9) &&
                  result.PeakQueuePerPool.All(q => q <= queue.PeakQueueLength),
                "Per-pool extremes never exceed the facility-wide values");
            Check(result.PoolNames.All(name => result.Wait95PerPool.ContainsKey(name)),
                "Every reported pool has its own Wait95 entry");
        }

        private static void RunTerminalEntryScheduleTiming()
        {
            Console.WriteLine("== Terminal entry schedule timing ==");

            Passenger departure = CreateTerminalEntryPassenger(
                id: 1,
                type: DHPType.DomesticDeparture,
                arrivalTime: 105.0,
                walkMinutes: 5.0);
            departure.FlightLegId = "F1";
            departure.TerminalEntryArrivalTime = 100.0;
            departure.CheckInOpenTime = 0.0;
            departure.CheckInLastAdmissionTime = 200.0;

            Passenger incompatible = CreateTerminalEntryPassenger(
                id: 2,
                type: DHPType.DomesticDeparture,
                arrivalTime: 150.0,
                walkMinutes: 0.0);
            incompatible.FlightLegId = "F2";
            incompatible.TerminalEntryArrivalTime = double.NaN;

            // A generated passenger with a stored checkpoint time whose clock
            // has already been advanced (for example by a walking segment).
            Passenger generated = CreateTerminalEntryPassenger(
                id: 3,
                type: DHPType.InternationalDeparture,
                arrivalTime: 120.0,
                walkMinutes: 0.0);
            generated.ArrivalTime = 160.0;
            generated.QueueArrivalTime = 160.0;

            TerminalEntrySolver solver = new TerminalEntrySolver();
            TerminalEntryResults result = solver.Solve(new TerminalEntryInputs
            {
                Passengers = new List<Passenger> { departure, incompatible, generated },
                ServiceTimeProvider = BuildTerminalEntryServiceTimeMap(),
                DigiYatraSharePercentage = 0.0,
                ManualPhysicalPositions = 1,
                DigiYatraPhysicalPositions = 1,
                OptimizeResources = false,
                Seed = 1
            });

            Check(result.QueueResults.ProcessedPassengers.Count == 2,
                "The timed schedule and generated passengers are served");
            Check(departure.History.Count == 0 && incompatible.History.Count == 0,
                "The incompatible schedule passenger is passed through untouched");

            Passenger served = result.QueueResults.ProcessedPassengers
                .First(p => p.Id == departure.Id);
            Check(served.StartServiceTime >= 100.0,
                $"Queueing starts at the terminal-entry time ({Math.Round(served.StartServiceTime, 2)})");
            Check(Math.Abs(served.ArrivalTime - (served.EndServiceTime + 5.0)) < 1e-9,
                "The post-entry clock includes the entry-to-check-in walk");
            Check(!served.ArrivedBeforeCheckInOpen,
                "The advanced clock updates the check-in opening flag");

            Passenger generatedServed = result.QueueResults.ProcessedPassengers
                .First(p => p.Id == generated.Id);
            Check(generatedServed.StartServiceTime >= 120.0 &&
                  generatedServed.StartServiceTime < 160.0,
                $"A generated passenger queues at their stored checkpoint time ({Math.Round(generatedServed.StartServiceTime, 2)})");
            Check(solver.Warnings.Any(w => w.Contains("no terminal-entry timestamp")),
                "A schedule passenger without an entry timestamp is warned about");
        }

        private static void RunTerminalEntryTransfers()
        {
            Console.WriteLine("== Terminal entry transfer bypass ==");

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 10; i++)
            {
                passengers.Add(CreateTerminalEntryPassenger(
                    id: 1000 + i,
                    type: i % 2 == 0
                        ? DHPType.DomesticDeparture
                        : DHPType.InternationalDeparture,
                    arrivalTime: 60.0 + i * 3.0,
                    walkMinutes: 2.0));
            }

            for (int i = 0; i < 5; i++)
            {
                Passenger transfer = CreateTerminalEntryPassenger(
                    id: 2000 + i,
                    type: DHPType.DomesticDeparture,
                    arrivalTime: 80.0 + i * 4.0,
                    walkMinutes: 2.0);
                transfer.Journey = PassengerJourney.Transfer;
                transfer.TransferScenario = TransferScenario.DomesticToDomestic;
                passengers.Add(transfer);
            }

            passengers.Add(CreateTerminalEntryPassenger(
                id: 3000,
                type: DHPType.InternationalArrival,
                arrivalTime: 90.0,
                walkMinutes: 0.0));

            TerminalEntrySolver solver = new TerminalEntrySolver();
            TerminalEntryResults result = solver.Solve(new TerminalEntryInputs
            {
                Passengers = passengers,
                ServiceTimeProvider = BuildTerminalEntryServiceTimeMap(),
                DigiYatraSharePercentage = 40.0,
                ManualPhysicalPositions = 2,
                DigiYatraPhysicalPositions = 1,
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 7
            });

            List<Passenger> processed = result.QueueResults.ProcessedPassengers;
            List<Passenger> output = result.QueueResults.Passengers;

            Check(processed.Count == 10,
                $"Only the 10 originating departures use terminal entry ({processed.Count})");
            Check(processed.All(p => p.Journey != PassengerJourney.Transfer),
                "No transfer passenger is served by an entry pool");

            Check(result.TransfersBypassingEntry == 5,
                $"All 5 transfer departures bypass terminal entry ({result.TransfersBypassingEntry})");
            Check(output.Count == passengers.Count,
                "Transfer and arrival passengers still leave the stage on the output population");

            List<Passenger> transferOutput = output
                .Where(p => p.Journey == PassengerJourney.Transfer)
                .ToList();
            Check(transferOutput.Count == 5 &&
                  transferOutput.All(p =>
                      string.IsNullOrEmpty(p.TerminalEntryRoute) &&
                      !p.History.ContainsKey(FacilityType.TerminalEntry)),
                "Transfer passengers keep no entry route or terminal-entry record");
            Check(transferOutput.All(p =>
                    Math.Abs(p.ArrivalTime - passengers
                        .First(source => source.Id == p.Id).ArrivalTime) < 1e-9),
                "Transfer passenger arrival times are not reset to a terminal checkpoint");

            int digiYatra = processed.Count(p =>
                string.Equals(
                    p.TerminalEntryRoute,
                    TerminalEntrySolver.DigiYatraRoute,
                    StringComparison.OrdinalIgnoreCase));
            Check(digiYatra == 4,
                $"The DigiYatra share applies to the 10 originating departures only ({digiYatra})");

            Check(TerminalEntrySolver.RequiresTerminalEntry(
                    passengers.First(p => p.Id == 1000)) &&
                  !TerminalEntrySolver.RequiresTerminalEntry(
                    passengers.First(p => p.Id == 2000)) &&
                  !TerminalEntrySolver.RequiresTerminalEntry(
                    passengers.First(p => p.Id == 3000)),
                "Terminal entry requires an originating departure");
        }

        private static void RunTerminalEntryOptimizer()
        {
            Console.WriteLine("== Terminal entry optimizer ==");

            TerminalEntryResults first = SolveTerminalEntryOptimized(seed: 21);
            TerminalEntryResults second = SolveTerminalEntryOptimized(seed: 21);

            Check(first.SearchOutcomes.Count == 2,
                $"Both entry pools were searched ({first.SearchOutcomes.Count})");
            Check(first.SearchOutcomes.All(o => o.Success),
                "Both entry pools meet their MQT");
            Check(first.QueueResults.Wait95 <= 2.0 + 1e-6,
                $"Whole-run Wait95 meets the slowest pool MQT ({Math.Round(first.QueueResults.Wait95, 2)})");
            Check(first.PhysicalPositions == second.PhysicalPositions &&
                  first.PhysicalPerPool.SequenceEqual(second.PhysicalPerPool),
                "The same seed reproduces the same entry capacity");
            Check(first.PhysicalPositions >= 1 && first.ActivePositions >= 1,
                "The optimized entry stage keeps at least one staffed position");
        }

        private static void RunTerminalEntryPoolPlans()
        {
            Console.WriteLine("== Terminal entry pool plans ==");

            TerminalEntryInputs input = BuildTerminalEntryInput(
                BuildTerminalEntryPopulation(200, 0, 20, 0.0),
                seed: 5);
            input.DigiYatraSharePercentage = 50.0;
            input.OptimizeResources = false;
            input.PoolPlans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = TerminalEntrySolver.ManualRoute,
                    PhysicalResources = 4,
                    TargetWait95 = 1.0,
                    Range = new ResourceSearchRange(1, 10)
                },
                new PoolPlan
                {
                    Channel = TerminalEntrySolver.DigiYatraRoute,
                    PhysicalResources = 2,
                    TargetWait95 = 0.5,
                    Range = new ResourceSearchRange(1, 10)
                }
            };

            TerminalEntryResults result = new TerminalEntrySolver().Solve(input);

            Check(result.PhysicalPerPool.SequenceEqual(new[] { 4, 2 }),
                $"Pool plans supply the fixed positions ({string.Join(",", result.PhysicalPerPool)})");
            Check(result.NominalPerPool.SequenceEqual(new[] { 4, 2 }),
                "Pool plans are retained as the nominal benchmark");
            Check(result.PoolNames.SequenceEqual(new[]
                {
                    TerminalEntrySolver.ManualRoute,
                    TerminalEntrySolver.DigiYatraRoute
                }),
                "Pool plans keep their configured identity and order");
        }

        private static void RunTerminalEntrySizing()
        {
            Console.WriteLine("== Terminal entry facility sizing ==");

            TerminalEntryStandards standards = new TerminalEntryStandards
            {
                QueueAreaPerPassenger = 1.5,
                Width = 2.0,
                Depth = 3.0,
                SupportPercent = 10.0,
                CirculationPercent = 0.0
            };

            FacilitySizingContext context = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 5,
                    ActiveProcessorCount = 4,
                    PeakQueue = 20,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = standards
            };

            FacilitySizingResult result = new TerminalEntrySizer().SizeFacility(context);

            Check(Math.Abs(result.QueueArea - 30.0) < 1e-9,
                $"Queue area uses the entry peak ({result.QueueArea})");
            Check(Math.Abs(result.ProcessingArea - 30.0) < 1e-9,
                $"Processing area uses the installed positions ({result.ProcessingArea})");
            Check(Math.Abs(result.SupportArea - 3.0) < 1e-9,
                $"Support area follows the processing area ({result.SupportArea})");
            Check(result.EquipmentCounts.TryGetValue("Terminal Entry Positions", out int positions) &&
                  positions == 5,
                "The sizer reports the installed entry positions");

            FacilityAreaStandardSet areaStandards = new FacilityAreaStandardSet
            {
                DefaultQueueSpacePerPax = 1.2,
                CirculationPercent = 0.0
            };

            areaStandards.Units.Add(new FacilityUnitStandard
            {
                Key = TerminalEntrySolver.ManualRoute,
                AreaPerUnit = 4.0,
                QueueSpacePerPax = 1.5
            });

            areaStandards.Units.Add(new FacilityUnitStandard
            {
                Key = TerminalEntrySolver.DigiYatraRoute,
                AreaPerUnit = 2.0,
                QueueSpacePerPax = 1.0
            });

            FacilitySizingContext routedContext = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 4,
                    ActiveProcessorCount = 4,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = standards,
                AreaStandards = areaStandards,
                UseRoutedPoolAreas = true,
                PoolNames = new List<string>
                {
                    TerminalEntrySolver.ManualRoute,
                    TerminalEntrySolver.DigiYatraRoute
                },
                PhysicalPerPool = new List<int> { 3, 1 },
                ActivePerPool = new List<int> { 3, 1 },
                PeakQueueByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { TerminalEntrySolver.ManualRoute, 20 },
                    { TerminalEntrySolver.DigiYatraRoute, 4 }
                },
                GlobalPeakQueue = 24
            };

            FacilitySizingResult routed = new TerminalEntrySizer().SizeFacility(routedContext);

            Check(Math.Abs(routed.QueueArea - (20 * 1.5 + 4 * 1.0)) < 1e-9,
                $"Routed entry queue area follows each pool's peak ({routed.QueueArea})");
            Check(Math.Abs(routed.ProcessingArea - (3 * 4.0 + 1 * 2.0)) < 1e-9,
                $"Routed entry processing area follows each pool's unit standard ({routed.ProcessingArea})");
            Check(routed.EquipmentCounts.TryGetValue(
                      TerminalEntrySolver.ManualRoute + " units (installed)", out int manualUnits) &&
                  manualUnits == 3,
                "Routed sizing reports the manual entry units");
        }

        private static TerminalEntryResults SolveTerminalEntryOptimized(int seed)
        {
            TerminalEntryInputs input = BuildTerminalEntryInput(
                BuildTerminalEntryPopulation(300, 0, 30, 0.0),
                seed: seed);
            input.DigiYatraSharePercentage = 50.0;
            input.OptimizeResources = true;
            input.ResourceDomain = new ResourceSearchRange(1, 100);
            input.PoolPlans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = TerminalEntrySolver.ManualRoute,
                    PhysicalResources = 1,
                    TargetWait95 = 2.0,
                    Range = new ResourceSearchRange(1, 100)
                },
                new PoolPlan
                {
                    Channel = TerminalEntrySolver.DigiYatraRoute,
                    PhysicalResources = 1,
                    TargetWait95 = 1.0,
                    Range = new ResourceSearchRange(1, 100)
                }
            };

            return new TerminalEntrySolver().Solve(input);
        }

        private static TerminalEntryInputs BuildTerminalEntryInput(
            List<Passenger> passengers,
            int seed)
        {
            return new TerminalEntryInputs
            {
                Passengers = passengers,
                ServiceTimeProvider = BuildTerminalEntryServiceTimeMap(),
                DigiYatraSharePercentage = 50.0,
                ManualPhysicalPositions = 2,
                DigiYatraPhysicalPositions = 1,
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = seed
            };
        }

        private static ServiceTimeMap BuildTerminalEntryServiceTimeMap()
        {
            return new ServiceTimeMap
            {
                Default = new ServiceProfile
                {
                    MeanServiceTime = TerminalEntrySolver.DefaultManualServiceTimeSeconds / 60.0
                },
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = TerminalEntrySolver.ManualRoute,
                        Profile = new ServiceProfile
                        {
                            MeanServiceTime = TerminalEntrySolver.DefaultManualServiceTimeSeconds / 60.0
                        }
                    },
                    new ServiceTimeEntry
                    {
                        Key = TerminalEntrySolver.DigiYatraRoute,
                        Profile = new ServiceProfile
                        {
                            MeanServiceTime = TerminalEntrySolver.DefaultDigiYatraServiceTimeSeconds / 60.0
                        }
                    }
                }
            };
        }

        private static List<Passenger> BuildTerminalEntryPopulation(
            int departureCount,
            int arrivalCount,
            double spanMinutes,
            double walkMinutes)
        {
            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < departureCount; i++)
            {
                Passenger passenger = CreateTerminalEntryPassenger(
                    i,
                    i % 2 == 0 ? DHPType.DomesticDeparture : DHPType.InternationalDeparture,
                    spanMinutes * i / Math.Max(1, departureCount),
                    walkMinutes);
                passengers.Add(passenger);
            }

            for (int i = 0; i < arrivalCount; i++)
            {
                Passenger passenger = CreateTerminalEntryPassenger(
                    departureCount + i,
                    i % 2 == 0 ? DHPType.DomesticArrival : DHPType.InternationalArrival,
                    spanMinutes * i / Math.Max(1, arrivalCount),
                    0.0);
                passengers.Add(passenger);
            }

            return passengers;
        }

        private static Passenger CreateTerminalEntryPassenger(
            int id,
            DHPType type,
            double arrivalTime,
            double walkMinutes)
        {
            return new Passenger
            {
                Id = id,
                PassengerType = type == DHPType.DomesticArrival ||
                                type == DHPType.DomesticDeparture
                    ? PassengerType.Domestic
                    : PassengerType.International,
                DHPType = type,
                Channel = "Counter",
                ArrivalTime = arrivalTime,
                QueueArrivalTime = arrivalTime,
                TerminalEntryArrivalTime = arrivalTime,
                TerminalEntryWalkMinutes = walkMinutes,
                ScheduledDepartureTime = 300.0,
                ShowUpSignature = "test"
            };
        }

    }
}
