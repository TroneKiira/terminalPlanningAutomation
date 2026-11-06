using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Emigration;
using AirportPlanning.Facilities.Immigration;
using AirportPlanning.Facilities.Security;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunTransferServiceConsistencyTests()
        {
            Console.WriteLine("== Transfer service usage / downstream consistency ==");

            RunCheckInTransferConsistency();
            RunCheckInBagDropChannelRuleConsistency();
            RunBagDropTransferConsistency();
            RunSecurityTransferConsistency();
            RunEmigrationTransferConsistency();
            RunImmigrationTransferConsistency();
            RunTransferRuleMatrixConsistency();
            RunTransferServiceDeterminism();
            RunAggregateTransferServiceConsistency();
            RunScheduleTransferServiceConsistency();
            RunUnspecifiedTransferRuleWarning();
            RunTransferBagDropChannelRouting();
            RunScheduleTransferBagDropChannelRouting();
        }

        // -------------------------------------------------------------
        // Check-In: a rule-based bypass must never enter a physical pool.
        // -------------------------------------------------------------
        private static void RunCheckInTransferConsistency()
        {
            Console.WriteLine("-- Check-In transfer bypass consistency --");

            TransferScenarioRules required = TscRules(checkIn: true, security: true);
            TransferScenarioRules bypassed = TscRules(checkIn: false, security: true);

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 5; i++)
            {
                passengers.Add(TscPassenger(
                    1000 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Origin, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 5; i++)
            {
                passengers.Add(TscPassenger(
                    1010 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Web", PassengerJourney.Origin, null, null,
                    online: true, arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    1100 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, required,
                    online: true, arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    1110 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, bypassed,
                    online: false, arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    1120 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, null,
                    online: false, arrival: 2.0 + i * 0.2));
            }

            passengers.Add(TscPassenger(
                1130, DHPType.DomesticDeparture, PassengerType.Domestic,
                "Counter", PassengerJourney.Transfer,
                TransferScenario.DomesticToDomestic, required,
                online: true, late: true, arrival: 30.0));

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            Check(usage[TransferService.CheckIn].Required == 3 &&
                  usage[TransferService.CheckIn].Bypassed == 2 &&
                  usage[TransferService.CheckIn].Unspecified == 2,
                $"Check-In usage partitions required/bypassed/unspecified ({usage[TransferService.CheckIn].Describe()})");

            CheckInResults wired = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expected = p =>
                p != null &&
                TscIsDeparture(p) &&
                !p.IsLateForCheckIn &&
                string.Equals(p.Channel, "Counter", StringComparison.OrdinalIgnoreCase) &&
                !TransferServiceUsageCalculator.IsBypassed(p, TransferService.CheckIn);

            TscAssertService(
                "Check-In with wired channels",
                TransferService.CheckIn,
                FacilityType.CheckIn,
                passengers,
                wired.QueueResults.ProcessedPassengers,
                wired.QueueResults.Passengers,
                expected);

            Check(wired.QueueResults.ProcessedPassengers.Count == 9,
                $"Check-In serves 5 origins + 2 required + 2 unspecified transfers ({wired.QueueResults.ProcessedPassengers.Count})");
            Check(wired.QueueResults.ProcessedPassengers.All(p =>
                    p.Journey != PassengerJourney.Transfer ||
                    p.TransferProcessing == null ||
                    p.TransferProcessing.RequiresCheckIn),
                "No transfer that bypasses check-in is present in the served set");
            Check(wired.QueueResults.ProcessedPassengers.All(p => p.Id != 1130),
                "A required transfer past last admission is not served at check-in");
            Check(wired.QueueResults.ProcessedPassengers.Count(p =>
                    TransferServiceUsageCalculator.IsRequired(p, TransferService.CheckIn) &&
                    !p.IsLateForCheckIn) ==
                  usage[TransferService.CheckIn].Required - 1,
                $"Check-In serves every required transfer that passes last admission ({usage[TransferService.CheckIn].Required - 1} of {usage[TransferService.CheckIn].Required})");
            Check(new CheckInSolver().Warnings != null,
                "Check-In solver exposes its warnings");

            CheckInSolver lateSolver = new CheckInSolver();
            lateSolver.Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });
            Check(lateSolver.Warnings.Any(w => w.Contains("last-admission")),
                "Check-In warns that required transfer(s) missed last admission instead of counting a bypass");

            CheckInResults shared = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(TscIds(shared.QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(wired.QueueResults.ProcessedPassengers)),
                "Check-In bypasses are identical with a shared pool and with wired channels");
            Check(shared.QueueResults.ProcessedPassengers.Count == 9,
                $"Shared Check-In pool also excludes bypasses ({shared.QueueResults.ProcessedPassengers.Count})");
        }

        /// <summary>
        /// Check-In and Bag Drop are separate stages. Check-In serves the
        /// check-in visit only; Web and Kiosk passengers with a checked bag
        /// continue to Bag Drop, while a counter accepts the bag itself.
        /// </summary>
        private static void RunCheckInBagDropChannelRuleConsistency()
        {
            Console.WriteLine("-- Check-In / Bag Drop stage consistency --");

            List<Passenger> passengers = new List<Passenger>
            {
                TscPassenger(
                    2001, DHPType.DomesticDeparture, PassengerType.Domestic, "Counter",
                    PassengerJourney.Transfer, TransferScenario.DomesticToDomestic,
                    TscRules(checkIn: true), online: true, bag: true, arrival: 1.0),
                TscPassenger(
                    2002, DHPType.DomesticDeparture, PassengerType.Domestic, "Web",
                    PassengerJourney.Transfer, TransferScenario.DomesticToDomestic,
                    TscRules(bagDrop: true), online: true, bag: true, arrival: 1.1),
                TscPassenger(
                    2003, DHPType.DomesticDeparture, PassengerType.Domestic, "Web",
                    PassengerJourney.Transfer, TransferScenario.DomesticToDomestic,
                    TscRules(), online: true, bag: true, arrival: 1.2),
                TscPassenger(
                    2004, DHPType.DomesticDeparture, PassengerType.Domestic, "Counter",
                    PassengerJourney.Origin, null, null, arrival: 1.3),
                TscPassenger(
                    2005, DHPType.DomesticDeparture, PassengerType.Domestic, "Web",
                    PassengerJourney.Origin, null, null, online: true, arrival: 1.4),
                TscPassenger(
                    2006, DHPType.DomesticDeparture, PassengerType.Domestic, "Web",
                    PassengerJourney.Origin, null, null, online: true, bag: true, arrival: 1.5),
                TscPassenger(
                    2007, DHPType.DomesticDeparture, PassengerType.Domestic, "Counter",
                    PassengerJourney.Origin, null, null, online: true, bag: true, arrival: 1.6),
                TscPassenger(
                    2008, DHPType.DomesticDeparture, PassengerType.Domestic, "Counter",
                    PassengerJourney.Transfer, TransferScenario.DomesticToDomestic,
                    TscRules(bagDrop: true), online: true, bag: true, arrival: 1.7),
                TscPassenger(
                    2009, DHPType.DomesticDeparture, PassengerType.Domestic, "Counter",
                    PassengerJourney.Origin, null, null, bag: true, arrival: 1.8)
            };

            CheckInResults result = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 1 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            HashSet<int> served = TscIds(result.QueueResults.ProcessedPassengers);

            Check(served.Contains(2001),
                "A transfer that requires check-in is served at Check-In");
            Check(served.Contains(2004) && served.Contains(2007) && served.Contains(2009),
                "Counter passengers are served at Check-In");
            Check(!served.Contains(2002) && !served.Contains(2008),
                "A bag-drop-only transfer is not served at Check-In");
            Check(!served.Contains(2003),
                "A transfer that bypasses both services is not served");
            Check(!served.Contains(2005) && !served.Contains(2006),
                "Web passengers never enter a physical Check-In pool");

            Check(result.QueueResults.ProcessedPassengers.All(p =>
                    p.History.ContainsKey(FacilityType.CheckIn)),
                "Every served record carries the Check-In history");
            Check(result.QueueResults.ProcessedPassengers.All(p =>
                    !p.History.ContainsKey(FacilityType.BagDrop)),
                "Check-In never records a separate Bag Drop visit");

            BagDropResults bagDrop = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = result.QueueResults.Passengers,
                ServiceProfile = TscServiceProfile(),
                PhysicalResources = new List<int> { 1 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            HashSet<int> dropped = TscIds(bagDrop.QueueResults.ProcessedPassengers);

            Check(dropped.SetEquals(new[] { 2002, 2006, 2008 }),
                $"Bag Drop serves Web bag carriers and rule-required transfers ({string.Join(", ", dropped.OrderBy(id => id))})");
            Check(!dropped.Contains(2007) && !dropped.Contains(2009),
                "Counter bag carriers have their bag accepted at check-in");
            Check(bagDrop.QueueResults.ProcessedPassengers.All(p =>
                    p.History.ContainsKey(FacilityType.BagDrop)),
                "The Bag Drop stage records its own visit");
        }

        // -------------------------------------------------------------
        // Bag Drop: same channel-filter defect and same explicit-bypass rule.
        // -------------------------------------------------------------
        private static void RunBagDropTransferConsistency()
        {
            Console.WriteLine("-- Bag Drop transfer bypass consistency --");

            TransferScenarioRules required = TscRules(bagDrop: true);
            TransferScenarioRules bypassed = TscRules(bagDrop: false);

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 4; i++)
            {
                passengers.Add(TscPassenger(
                    2000 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Web", PassengerJourney.Origin, null, null,
                    online: true, bag: true, arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 3; i++)
            {
                passengers.Add(TscPassenger(
                    2010 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Web", PassengerJourney.Origin, null, null,
                    online: true, bag: false, arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    2020 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Origin, null, null,
                    online: false, bag: true, arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    2100 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Kiosk", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, required,
                    online: false, bag: true, arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    2110 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Kiosk", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, bypassed,
                    online: false, bag: true, arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    2120 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Web", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, null,
                    online: true, bag: true, arrival: 2.0 + i * 0.2));
            }

            Passenger alreadyDropped = TscPassenger(
                2130, DHPType.DomesticDeparture, PassengerType.Domestic,
                "Web", PassengerJourney.Transfer,
                TransferScenario.DomesticToDomestic, required,
                online: true, bag: true, arrival: 30.0);
            alreadyDropped.History[FacilityType.BagDrop] = new FacilityRecord
            {
                ArrivalTime = 1.0,
                StartServiceTime = 1.0,
                EndServiceTime = 1.1,
                WaitTime = 0.0,
                AssignedResource = 1
            };
            passengers.Add(alreadyDropped);

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            Check(usage[TransferService.BagDrop].Required == 3 &&
                  usage[TransferService.BagDrop].Bypassed == 2 &&
                  usage[TransferService.BagDrop].Unspecified == 2,
                $"Bag Drop usage partitions required/bypassed/unspecified ({usage[TransferService.BagDrop].Describe()})");

            BagDropSolver solver = new BagDropSolver();
            BagDropResults result = solver.Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "BagDrop" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 6,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expected = p =>
                p != null &&
                TscIsDeparture(p) &&
                p.RequiresSeparateBagDrop &&
                !TransferServiceUsageCalculator.IsBypassed(p, TransferService.BagDrop) &&
                !p.History.ContainsKey(FacilityType.BagDrop);

            TscAssertService(
                "Bag Drop with wired channels",
                TransferService.BagDrop,
                FacilityType.BagDrop,
                passengers,
                result.QueueResults.ProcessedPassengers,
                result.QueueResults.Passengers,
                expected);

            Check(result.QueueResults.ProcessedPassengers.Count == 8,
                $"Bag Drop serves 4 origins + 2 required + 2 unspecified transfers ({result.QueueResults.ProcessedPassengers.Count})");
            Check(result.QueueResults.ProcessedPassengers.All(p =>
                    p.Journey != PassengerJourney.Transfer ||
                    p.TransferProcessing == null ||
                    p.TransferProcessing.RequiresBagDrop),
                "No transfer that bypasses bag drop is present in the served set");
            Check(result.QueueResults.ProcessedPassengers.Count(p =>
                    TransferServiceUsageCalculator.IsRequired(p, TransferService.BagDrop)) ==
                  usage[TransferService.BagDrop].Required - 1,
                $"Bag Drop serves every required transfer that has not already dropped a bag ({usage[TransferService.BagDrop].Required - 1} of {usage[TransferService.BagDrop].Required})");
            Check(solver.Warnings.Any(w => w.Contains("already carry a Bag Drop record")),
                "Bag Drop reports passengers already served by an earlier Bag Drop stage");

            BagDropResults shared = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 6,
                StaffingPercentage = 100.0
            });

            Check(TscIds(shared.QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(result.QueueResults.ProcessedPassengers)),
                "Bag Drop bypasses are identical with a shared pool and with wired channels");
        }

        // -------------------------------------------------------------
        // Security: integrated, separate, fallback, and bypass.
        // -------------------------------------------------------------
        private static void RunSecurityTransferConsistency()
        {
            Console.WriteLine("-- Security transfer bypass consistency --");

            TransferScenarioRules required = TscRules(security: true);
            TransferScenarioRules bypassed = TscRules(security: false);
            TransferScenarioRules separate = TscRules(
                security: true,
                securityRouting: TransferFacilityRouting.Separate);

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    3000 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Origin, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            passengers.Add(TscPassenger(
                3002, DHPType.InternationalArrival, PassengerType.International,
                "Arrival", PassengerJourney.Destination, null, null,
                arrival: 1.4));

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    3100 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, required,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    3110 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, bypassed,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    3120 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, null,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 4; i++)
            {
                passengers.Add(TscPassenger(
                    3130 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, separate,
                    arrival: 2.0 + i * 0.2));
            }

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            Check(usage[TransferService.Security].Required == 6 &&
                  usage[TransferService.Security].Bypassed == 2 &&
                  usage[TransferService.Security].Unspecified == 2,
                $"Security usage partitions required/bypassed/unspecified ({usage[TransferService.Security].Describe()})");

            List<PoolPlan> plans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = "Counter",
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                },
                new PoolPlan
                {
                    Channel = "Transfer Security",
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                }
            };

            SecurityResults configured = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter", "Transfer Security" },
                PoolPlans = plans,
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expected = p =>
                p != null &&
                TscIsDeparture(p) &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Security);

            TscAssertService(
                "Security with a dedicated transfer pool",
                TransferService.Security,
                FacilityType.Security,
                passengers,
                configured.QueueResults.ProcessedPassengers,
                configured.QueueResults.Passengers,
                expected);

            Check(configured.QueueResults.ProcessedPassengers.Count == 10,
                $"Security serves 2 origins + 2 required + 2 unspecified + 4 separate transfers ({configured.QueueResults.ProcessedPassengers.Count})");
            Check(configured.QueueResults.ProcessedPassengers.Count(p =>
                    TransferServiceUsageCalculator.IsRequired(p, TransferService.Security)) ==
                  usage[TransferService.Security].Required,
                $"Security serves every transfer reported as Required by TUSE ({usage[TransferService.Security].Required} of {usage[TransferService.Security].Required})");
            Check(configured.QueueResults.ProcessedPassengers
                    .Where(p => p.TransferProcessing != null &&
                                p.TransferProcessing.SecurityRouting == TransferFacilityRouting.Separate)
                    .All(p => p.AssignedPool == "Transfer Security"),
                "Separately-routed transfers use the dedicated security pool");
            Check(configured.QueueResults.Passengers
                    .Where(p => p.TransferProcessing != null)
                    .All(p => string.Equals(p.Channel, "Counter", StringComparison.OrdinalIgnoreCase)),
                "Security restores the original channel after separate routing");

            SecuritySolver fallbackSolver = new SecuritySolver();
            SecurityResults fallback = fallbackSolver.Solve(new SecurityInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Channel = "Counter",
                        PhysicalResources = 1,
                        TargetWait95 = 10.0,
                        Range = new ResourceSearchRange(1, 4)
                    }
                },
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(fallback.QueueResults.ProcessedPassengers.Count == 10,
                $"Separate security routing falls back to the main pool instead of dropping transfers ({fallback.QueueResults.ProcessedPassengers.Count})");
            Check(fallback.QueueResults.ProcessedPassengers.All(p =>
                    p.TransferProcessing == null ||
                    p.TransferProcessing.SecurityRouting != TransferFacilityRouting.Separate ||
                    p.AssignedPool != "Transfer Security"),
                "Fallback transfers are not assigned to the unconfigured transfer pool");
            Check(fallbackSolver.Warnings.Any(w => w.Contains("no matching pool")),
                "Security fallback warns that the transfer pool is not configured");
        }

        // -------------------------------------------------------------
        // Emigration: same fallback rule on the e-gate/counter split.
        // -------------------------------------------------------------
        private static void RunEmigrationTransferConsistency()
        {
            Console.WriteLine("-- Emigration transfer bypass consistency --");

            TransferScenarioRules required = TscRules(emigration: true);
            TransferScenarioRules bypassed = TscRules(emigration: false);
            TransferScenarioRules separate = TscRules(
                emigration: true,
                emigrationRouting: TransferFacilityRouting.Separate);

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 3; i++)
            {
                passengers.Add(TscPassenger(
                    4000 + i, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Origin, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    4003 + i, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Origin, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    4100 + i, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, required,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    4110 + i, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, bypassed,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    4120 + i, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, null,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    4130 + i, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, separate,
                    arrival: 2.0 + i * 0.2));
            }

            passengers.Add(TscPassenger(
                4140, DHPType.DomesticDeparture, PassengerType.Domestic,
                "Counter", PassengerJourney.Transfer,
                TransferScenario.DomesticToDomestic, required,
                arrival: 3.0));

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            Check(usage[TransferService.Emigration].Required == 4 &&
                  usage[TransferService.Emigration].Bypassed == 2 &&
                  usage[TransferService.Emigration].Unspecified == 2,
                $"Emigration usage partitions required/bypassed/unspecified ({usage[TransferService.Emigration].Describe()})");
            Check(!TransferServiceUsageCalculator.AppliesTo(
                    TransferService.Emigration, passengers.First(p => p.Id == 4140)),
                "A domestic transfer is outside emigration applicability even with an emigration rule");

            List<PoolPlan> plans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = EmigrationSolver.EGatePool,
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                },
                new PoolPlan
                {
                    Channel = EmigrationSolver.CounterPool,
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                },
                new PoolPlan
                {
                    Channel = "Transfer Emigration",
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                }
            };

            EmigrationResults configured = new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                EGateSharePercentage = 50.0,
                Channels = new List<string>
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool,
                    "Transfer Emigration"
                },
                PoolPlans = plans,
                OptimizeResources = false,
                Seed = 8,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expected = p =>
                p != null &&
                p.PassengerType == PassengerType.International &&
                TscIsDeparture(p) &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Emigration);

            TscAssertService(
                "Emigration with a dedicated transfer pool",
                TransferService.Emigration,
                FacilityType.Emigration,
                passengers,
                configured.QueueResults.ProcessedPassengers,
                configured.QueueResults.Passengers,
                expected);

            Check(configured.QueueResults.ProcessedPassengers.Count == 9,
                $"Emigration serves 3 origins + 2 required + 2 unspecified + 2 separate transfers ({configured.QueueResults.ProcessedPassengers.Count})");
            Check(configured.QueueResults.ProcessedPassengers.Count(p =>
                    TransferServiceUsageCalculator.IsRequired(p, TransferService.Emigration)) ==
                  usage[TransferService.Emigration].Required,
                $"Emigration serves every transfer reported as Required by TUSE ({usage[TransferService.Emigration].Required} of {usage[TransferService.Emigration].Required})");
            Check(configured.QueueResults.ProcessedPassengers
                    .Where(p => p.TransferProcessing != null &&
                                p.TransferProcessing.EmigrationRouting == TransferFacilityRouting.Separate)
                    .All(p => p.AssignedPool == "Transfer Emigration"),
                "Separately-routed transfers use the dedicated emigration pool");

            EmigrationSolver fallbackSolver = new EmigrationSolver();
            EmigrationResults fallback = fallbackSolver.Solve(new EmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                EGateSharePercentage = 50.0,
                Channels = new List<string>
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool
                },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Channel = EmigrationSolver.EGatePool,
                        PhysicalResources = 1,
                        TargetWait95 = 10.0,
                        Range = new ResourceSearchRange(1, 4)
                    },
                    new PoolPlan
                    {
                        Channel = EmigrationSolver.CounterPool,
                        PhysicalResources = 1,
                        TargetWait95 = 10.0,
                        Range = new ResourceSearchRange(1, 4)
                    }
                },
                OptimizeResources = false,
                Seed = 8,
                StaffingPercentage = 100.0
            });

            Check(fallback.QueueResults.ProcessedPassengers.Count == 9,
                $"Separate emigration routing falls back to the main hall instead of dropping transfers ({fallback.QueueResults.ProcessedPassengers.Count})");
            Check(fallbackSolver.Warnings.Any(w => w.Contains("no matching pool")),
                "Emigration fallback warns that the transfer pool is not configured");
        }

        // -------------------------------------------------------------
        // Immigration: arrival-side applicability and fallback.
        // -------------------------------------------------------------
        private static void RunImmigrationTransferConsistency()
        {
            Console.WriteLine("-- Immigration transfer bypass consistency --");

            TransferScenarioRules required = TscRules(immigration: true);
            TransferScenarioRules bypassed = TscRules(immigration: false);
            TransferScenarioRules separate = TscRules(
                immigration: true,
                immigrationRouting: TransferFacilityRouting.Separate);

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 3; i++)
            {
                passengers.Add(TscPassenger(
                    5000 + i, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Destination, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    5003 + i, DHPType.DomesticArrival, PassengerType.Domestic,
                    "Arrival", PassengerJourney.Destination, null, null,
                    arrival: 1.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    5100 + i, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToDomestic, required,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    5110 + i, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToInternational, bypassed,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    5120 + i, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToDomestic, null,
                    arrival: 2.0 + i * 0.2));
            }

            for (int i = 0; i < 2; i++)
            {
                passengers.Add(TscPassenger(
                    5130 + i, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToDomestic, separate,
                    arrival: 2.0 + i * 0.2));
            }

            passengers.Add(TscPassenger(
                5140, DHPType.DomesticArrival, PassengerType.Domestic,
                "Arrival", PassengerJourney.Transfer,
                TransferScenario.DomesticToDomestic, required,
                arrival: 3.0));

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            Check(usage[TransferService.Immigration].Required == 4 &&
                  usage[TransferService.Immigration].Bypassed == 2 &&
                  usage[TransferService.Immigration].Unspecified == 2,
                $"Immigration usage partitions required/bypassed/unspecified ({usage[TransferService.Immigration].Describe()})");
            Check(!TransferServiceUsageCalculator.AppliesTo(
                    TransferService.Immigration, passengers.First(p => p.Id == 5140)),
                "A domestic arrival is outside immigration applicability even with an immigration rule");

            List<PoolPlan> plans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = "Arrival",
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                },
                new PoolPlan
                {
                    Channel = "Transfer Immigration",
                    PhysicalResources = 1,
                    TargetWait95 = 10.0,
                    Range = new ResourceSearchRange(1, 4)
                }
            };

            ImmigrationResults configured = new ImmigrationSolver().Solve(new ImmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Arrival", "Transfer Immigration" },
                PoolPlans = plans,
                OptimizeResources = false,
                Seed = 9,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expected = p =>
                p != null &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Immigration);

            TscAssertService(
                "Immigration with a dedicated transfer pool",
                TransferService.Immigration,
                FacilityType.Immigration,
                passengers,
                configured.QueueResults.ProcessedPassengers,
                configured.QueueResults.Passengers,
                expected);

            Check(configured.QueueResults.ProcessedPassengers.Count == 9,
                $"Immigration serves 3 origins + 2 required + 2 unspecified + 2 separate transfers ({configured.QueueResults.ProcessedPassengers.Count})");
            Check(configured.QueueResults.ProcessedPassengers.Count(p =>
                    TransferServiceUsageCalculator.IsRequired(p, TransferService.Immigration)) ==
                  usage[TransferService.Immigration].Required,
                $"Immigration serves every transfer reported as Required by TUSE ({usage[TransferService.Immigration].Required} of {usage[TransferService.Immigration].Required})");
            Check(configured.QueueResults.ProcessedPassengers
                    .Where(p => p.TransferProcessing != null &&
                                p.TransferProcessing.ImmigrationRouting == TransferFacilityRouting.Separate)
                    .All(p => p.AssignedPool == "Transfer Immigration"),
                "Separately-routed transfers use the dedicated immigration pool");

            ImmigrationSolver fallbackSolver = new ImmigrationSolver();
            ImmigrationResults fallback = fallbackSolver.Solve(new ImmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Arrival" },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Channel = "Arrival",
                        PhysicalResources = 1,
                        TargetWait95 = 10.0,
                        Range = new ResourceSearchRange(1, 4)
                    }
                },
                OptimizeResources = false,
                Seed = 9,
                StaffingPercentage = 100.0
            });

            Check(fallback.QueueResults.ProcessedPassengers.Count == 9,
                $"Separate immigration routing falls back to the main hall instead of dropping transfers ({fallback.QueueResults.ProcessedPassengers.Count})");
            Check(fallbackSolver.Warnings.Any(w => w.Contains("no matching pool")),
                "Immigration fallback warns that the transfer pool is not configured");
        }

        // -------------------------------------------------------------
        // Rule matrix: applicability by scenario and journey leg.
        // -------------------------------------------------------------
        private static void RunTransferRuleMatrixConsistency()
        {
            Console.WriteLine("-- Transfer service rule matrix --");

            List<Passenger> passengers = new List<Passenger>();
            int id = 6000;

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                TransferScenarioRules allTrue = TscRules(
                    checkIn: true, bagDrop: true, security: true,
                    immigration: true, emigration: true,
                    transferDesk: true, baggageRecheck: true);
                allTrue.Scenario = scenario;

                TransferScenarioRules allFalse = TscRules();
                allFalse.Scenario = scenario;

                DHPType departureType = TscDepartureType(scenario);
                DHPType arrivalType = TscArrivalType(scenario);
                PassengerType departureMarket = TscMarket(departureType);
                PassengerType arrivalMarket = TscMarket(arrivalType);

                passengers.Add(TscPassenger(
                    id++, departureType, departureMarket, "Counter",
                    PassengerJourney.Transfer, scenario, allTrue, arrival: id * 0.01));
                passengers.Add(TscPassenger(
                    id++, arrivalType, arrivalMarket, "Arrival",
                    PassengerJourney.Transfer, scenario, allTrue, arrival: id * 0.01));
                passengers.Add(TscPassenger(
                    id++, departureType, departureMarket, "Counter",
                    PassengerJourney.Transfer, scenario, allFalse, arrival: id * 0.01));
                passengers.Add(TscPassenger(
                    id++, arrivalType, arrivalMarket, "Arrival",
                    PassengerJourney.Transfer, scenario, allFalse, arrival: id * 0.01));
                passengers.Add(TscPassenger(
                    id++, departureType, departureMarket, "Counter",
                    PassengerJourney.Transfer, scenario, null, arrival: id * 0.01));
                passengers.Add(TscPassenger(
                    id++, arrivalType, arrivalMarket, "Arrival",
                    PassengerJourney.Transfer, scenario, null, arrival: id * 0.01));
            }

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);

            Check(usage[TransferService.CheckIn].Required == 4 &&
                  usage[TransferService.CheckIn].Bypassed == 4 &&
                  usage[TransferService.CheckIn].Unspecified == 4,
                $"Check-In matrix partitions 4/4/4 departure records ({usage[TransferService.CheckIn].Describe()})");
            Check(usage[TransferService.BagDrop].Required == 4 &&
                  usage[TransferService.BagDrop].Bypassed == 4 &&
                  usage[TransferService.BagDrop].Unspecified == 4,
                "Bag Drop matrix matches Check-In applicability");
            Check(usage[TransferService.Security].Required == 4 &&
                  usage[TransferService.Security].Bypassed == 4 &&
                  usage[TransferService.Security].Unspecified == 4,
                "Security matrix matches departure applicability");
            Check(usage[TransferService.Emigration].Required == 2 &&
                  usage[TransferService.Emigration].Bypassed == 2 &&
                  usage[TransferService.Emigration].Unspecified == 2,
                $"Emigration only counts international departure transfers ({usage[TransferService.Emigration].Describe()})");
            Check(usage[TransferService.Immigration].Required == 2 &&
                  usage[TransferService.Immigration].Bypassed == 2 &&
                  usage[TransferService.Immigration].Unspecified == 2,
                $"Immigration only counts international arrival transfers ({usage[TransferService.Immigration].Describe()})");
            Check(usage[TransferService.TransferDesk].Required == 4 &&
                  usage[TransferService.TransferDesk].Bypassed == 4 &&
                  usage[TransferService.TransferDesk].Unspecified == 4,
                "Transfer desk applies to every arrival transfer record");
            Check(usage[TransferService.BaggageRecheck].Required == 4 &&
                  usage[TransferService.BaggageRecheck].Bypassed == 4 &&
                  usage[TransferService.BaggageRecheck].Unspecified == 4,
                "Baggage recheck applies to every arrival transfer record");

            foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
            {
                TransferServiceUsage entry = usage[service];
                Check(entry.Applicable == entry.Required + entry.Bypassed + entry.Unspecified,
                    $"{service.ToLabel()} usage is an exact partition ({entry.Applicable})");
            }

            Passenger trueD2D = passengers.First(p =>
                p.TransferScenario == TransferScenario.DomesticToDomestic &&
                p.DHPType == DHPType.DomesticDeparture &&
                p.TransferProcessing != null &&
                p.TransferProcessing.RequiresCheckIn);
            Check(TransferServiceUsageCalculator.UsesService(trueD2D, TransferService.CheckIn) &&
                  TransferServiceUsageCalculator.UsesService(trueD2D, TransferService.Security) &&
                  !TransferServiceUsageCalculator.UsesService(trueD2D, TransferService.Emigration) &&
                  !TransferServiceUsageCalculator.UsesService(trueD2D, TransferService.Immigration) &&
                  !TransferServiceUsageCalculator.UsesService(trueD2D, TransferService.TransferDesk),
                "All-true D→D departure uses departure services but not arrival/international services");

            Passenger trueI2D = passengers.First(p =>
                p.TransferScenario == TransferScenario.InternationalToDomestic &&
                p.DHPType == DHPType.InternationalArrival &&
                p.TransferProcessing != null &&
                p.TransferProcessing.RequiresImmigration);
            Check(TransferServiceUsageCalculator.UsesService(trueI2D, TransferService.Immigration) &&
                  TransferServiceUsageCalculator.UsesService(trueI2D, TransferService.TransferDesk) &&
                  !TransferServiceUsageCalculator.UsesService(trueI2D, TransferService.CheckIn) &&
                  !TransferServiceUsageCalculator.UsesService(trueI2D, TransferService.Emigration),
                "All-true I→D arrival uses immigration/desk but not departure services");

            Passenger falseD2D = passengers.First(p =>
                p.TransferScenario == TransferScenario.DomesticToDomestic &&
                p.DHPType == DHPType.DomesticDeparture &&
                p.TransferProcessing != null &&
                !p.TransferProcessing.RequiresCheckIn);
            Check(!TransferServiceUsageCalculator.UsesService(falseD2D, TransferService.CheckIn) &&
                  !TransferServiceUsageCalculator.UsesService(falseD2D, TransferService.Security) &&
                  TransferServiceUsageCalculator.IsBypassed(falseD2D, TransferService.CheckIn),
                "All-false rules bypass departure services");

            Passenger unspecifiedD2D = passengers.First(p =>
                p.TransferScenario == TransferScenario.DomesticToDomestic &&
                p.DHPType == DHPType.DomesticDeparture &&
                p.TransferProcessing == null);
            Check(TransferServiceUsageCalculator.IsUnspecified(unspecifiedD2D, TransferService.CheckIn) &&
                  TransferServiceUsageCalculator.UsesService(unspecifiedD2D, TransferService.CheckIn) &&
                  TransferServiceUsageCalculator.UsesService(unspecifiedD2D, TransferService.Security) &&
                  !TransferServiceUsageCalculator.UsesService(unspecifiedD2D, TransferService.BagDrop),
                "Unspecified transfers follow normal passenger behaviour instead of a bypass");
        }

        // -------------------------------------------------------------
        // Determinism: repeated solves must return the same cohorts.
        // -------------------------------------------------------------
        private static void RunTransferServiceDeterminism()
        {
            Console.WriteLine("-- Transfer service deterministic cohorts --");

            TransferScenarioRules required = TscRules(
                checkIn: true, bagDrop: true, security: true,
                immigration: true, emigration: true);
            TransferScenarioRules bypassed = TscRules();

            List<Passenger> passengers = new List<Passenger>
            {
                TscPassenger(7000, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Origin, null, null, arrival: 1.0),
                TscPassenger(7001, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Origin, null, null, arrival: 1.2),
                TscPassenger(7002, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Destination, null, null, arrival: 1.4),
                TscPassenger(7100, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, required,
                    online: true, bag: true, arrival: 2.0),
                TscPassenger(7101, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, required,
                    online: true, bag: true, arrival: 2.2),
                TscPassenger(7110, DHPType.DomesticDeparture, PassengerType.Domestic,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToDomestic, bypassed,
                    arrival: 2.4),
                TscPassenger(7111, DHPType.InternationalDeparture, PassengerType.International,
                    "Counter", PassengerJourney.Transfer,
                    TransferScenario.DomesticToInternational, bypassed,
                    arrival: 2.6),
                TscPassenger(7120, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToDomestic, required,
                    arrival: 3.0),
                TscPassenger(7121, DHPType.InternationalArrival, PassengerType.International,
                    "Arrival", PassengerJourney.Transfer,
                    TransferScenario.InternationalToInternational, bypassed,
                    arrival: 3.2)
            };

            Func<CheckInResults> solveCheckIn = () => new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter", "BagDrop" },
                PhysicalResources = new List<int> { 2, 1 },
                TargetWait95 = new List<double> { 10.0, 10.0 },
                OptimizeResources = false,
                Seed = 10,
                StaffingPercentage = 100.0
            });

            Check(TscIds(solveCheckIn().QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(solveCheckIn().QueueResults.ProcessedPassengers)),
                "Check-In returns the same served cohort on repeated runs");

            Func<BagDropResults> solveBagDrop = () => new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "BagDrop" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 10,
                StaffingPercentage = 100.0
            });

            Check(TscIds(solveBagDrop().QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(solveBagDrop().QueueResults.ProcessedPassengers)),
                "Bag Drop returns the same served cohort on repeated runs");

            Func<SecurityResults> solveSecurity = () => new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 10,
                StaffingPercentage = 100.0
            });

            Check(TscIds(solveSecurity().QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(solveSecurity().QueueResults.ProcessedPassengers)),
                "Security returns the same served cohort on repeated runs");

            Func<EmigrationResults> solveEmigration = () => new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                EGateSharePercentage = 50.0,
                Channels = new List<string>
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool
                },
                PhysicalResources = new List<int> { 1, 1 },
                TargetWait95 = new List<double> { 10.0, 10.0 },
                OptimizeResources = false,
                Seed = 10,
                StaffingPercentage = 100.0
            });

            Check(TscIds(solveEmigration().QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(solveEmigration().QueueResults.ProcessedPassengers)),
                "Emigration returns the same served cohort on repeated runs");

            Func<ImmigrationResults> solveImmigration = () => new ImmigrationSolver().Solve(new ImmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Arrival" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 10,
                StaffingPercentage = 100.0
            });

            Check(TscIds(solveImmigration().QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(solveImmigration().QueueResults.ProcessedPassengers)),
                "Immigration returns the same served cohort on repeated runs");
        }

        // -------------------------------------------------------------
        // Aggregate/PHP: the user's 10% example, scaled to 100 passengers.
        // -------------------------------------------------------------
        private static void RunAggregateTransferServiceConsistency()
        {
            Console.WriteLine("-- Aggregate transfer bypass consistency --");

            List<Passenger> population = Enumerable.Range(1, 100)
                .Select(id => new Passenger
                {
                    Id = id,
                    PassengerType = PassengerType.Domestic,
                    DHPType = DHPType.DomesticDeparture,
                    Journey = PassengerJourney.Origin,
                    Channel = DefaultChannelAssigner.CounterChannel,
                    UsesOnlineCheckIn = false,
                    HasCheckedBag = true,
                    ProfileName = "Aggregate transfer",
                    ArrivalTime = 1.0 + id * 0.05,
                    QueueArrivalTime = 1.0 + id * 0.05
                })
                .ToList();

            TransferDemandProfile profile = new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                DomesticToDomesticPercent = 100.0
            };

            TransferScenarioRules rules =
                profile.ScenarioRules[TransferScenario.DomesticToDomestic];
            rules.HasProcessingRules = true;
            rules.RequiresCheckIn = false;
            rules.RequiresBagDrop = false;
            rules.RequiresSecurityScreening = false;
            rules.RequiresEmigration = false;
            rules.RequiresImmigration = false;
            rules.RequiresTransferDesk = false;
            rules.RequiresBaggageRecheck = false;

            TransferDemandResult tagged =
                TransferPassengerTagger.ApplyAggregate(population, profile);

            Check(tagged.EffectiveDomesticTransferPassengers == 10,
                $"Aggregate tagging creates 10% of 100 departures ({tagged.EffectiveDomesticTransferPassengers})");
            Check(tagged.ServiceUsage[TransferService.CheckIn].Bypassed == 10 &&
                  tagged.ServiceUsage[TransferService.CheckIn].Required == 0 &&
                  tagged.ServiceUsage[TransferService.CheckIn].Unspecified == 0,
                "Aggregate Check-In usage reports all 10 transfers as rule-based bypasses");
            Check(tagged.ServiceUsage[TransferService.Security].Bypassed == 10,
                "Aggregate Security usage reports all 10 transfers as rule-based bypasses");

            CheckInResults checkIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = population,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(checkIn.QueueResults.ProcessedPassengers.Count == 90,
                $"Check-In serves 90 origins and no bypassed transfer ({checkIn.QueueResults.ProcessedPassengers.Count})");
            Check(checkIn.QueueResults.ProcessedPassengers.All(p =>
                    p.Journey != PassengerJourney.Transfer),
                "No aggregate transfer enters the check-in queue when the rule bypasses check-in");

            SecurityResults security = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = population,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(security.QueueResults.ProcessedPassengers.Count == 90,
                $"Security also excludes the 10 rule-based bypasses ({security.QueueResults.ProcessedPassengers.Count})");

            CheckInResults shared = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = population,
                ServiceProfile = TscServiceProfile(),
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(shared.QueueResults.ProcessedPassengers.Count == 90,
                $"Shared Check-In pool excludes the same aggregate bypasses ({shared.QueueResults.ProcessedPassengers.Count})");

            Passenger arrival = new Passenger
            {
                Id = 9000,
                DHPType = DHPType.InternationalArrival,
                Journey = PassengerJourney.Destination,
                UsesOnlineCheckIn = false
            };
            Check(!arrival.RequiresCheckIn && !arrival.RequiresBagDrop,
                "Arrival records never report a check-in or bag-drop requirement");
        }

        // -------------------------------------------------------------
        // Design Day: the usage report and all facility solvers agree.
        // -------------------------------------------------------------
        private static void RunScheduleTransferServiceConsistency()
        {
            Console.WriteLine("-- Design Day transfer service consistency --");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "TSC design day"
            };

            for (int i = 0; i < 10; i++)
            {
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-ARR-D-{i}", LegDirection.Arrival, 60 + i * 10, 20,
                    LegSector.Domestic, "LHR", "DEL"));
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-ARR-I-{i}", LegDirection.Arrival, 60 + i * 10, 20,
                    LegSector.International, "LHR", "DXB"));
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-DEP-D-{i}", LegDirection.Departure, 600 + i * 10, 20,
                    LegSector.Domestic, "SIN", "BOM"));
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-DEP-I-{i}", LegDirection.Departure, 600 + i * 10, 20,
                    LegSector.International, "SIN", "DOH"));
            }

            PassengerProfile scheduleProfile = MakeScheduleProfile(
                "TSC schedule", 100.0, 60.0, 120.0, ShowUpProfile.Mix());
            scheduleProfile.OnlineCheckInProbability = 0.0;
            scheduleProfile.CheckedBagProbability = 0.0;

            TransferDemandProfile profile = new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                InternationalTransferPercent = 10.0,
                DomesticToDomesticPercent = 50.0,
                InternationalToDomesticPercent = 50.0,
                DomesticToInternationalPercent = 50.0,
                InternationalToInternationalPercent = 50.0
            };

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                TransferScenarioRules rules = profile.ScenarioRules[scenario];
                rules.HasProcessingRules = true;
                rules.RequiresCheckIn = false;
                rules.RequiresBagDrop = false;
                rules.RequiresSecurityScreening = false;
                rules.RequiresEmigration = false;
                rules.RequiresImmigration = false;
                rules.RequiresTransferDesk = false;
                rules.RequiresBaggageRecheck = false;
            }

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                MakeTypedScheduleGroup(DHPType.DomesticDeparture, scheduleProfile),
                MakeTypedScheduleGroup(DHPType.InternationalDeparture, scheduleProfile)
            };

            SchedulePassengerGenerationResult generated = new SchedulePassengerGenerator().Generate(
                schedule,
                groups,
                new WalkingProfile { AverageSpeed = 1.35, StandardDeviation = 0.0 },
                seed: 12,
                transferDemand: profile);

            int outgoingTransfers = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Transfer && TscIsDeparture(p));
            int incomingTransfers = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Transfer && TscIsArrival(p));
            int originDepartures = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Origin && TscIsDeparture(p));
            int originInternationalDepartures = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Origin &&
                p.DHPType == DHPType.InternationalDeparture);
            int originInternationalArrivals = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Destination &&
                p.DHPType == DHPType.InternationalArrival);
            int internationalOutgoingTransfers = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Transfer &&
                p.DHPType == DHPType.InternationalDeparture);
            int internationalIncomingTransfers = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Transfer &&
                p.DHPType == DHPType.InternationalArrival);

            Check(generated.TransferDemand.EffectiveTransferPassengers == 40 &&
                  generated.TransferDemand.TaggedTransferPassengers == 40,
                $"Design Day tags all 40 outbound transfers ({generated.TransferDemand.TaggedTransferPassengers})");
            Check(outgoingTransfers == 40 && incomingTransfers == 0,
                $"Only outbound journey legs are tagged ({outgoingTransfers} outbound, {incomingTransfers} inbound)");
            Check(generated.TransferDemand.ServiceUsage[TransferService.CheckIn].Bypassed == 40,
                "Design Day Check-In usage counts every outbound transfer as a bypass");
            Check(generated.TransferDemand.ServiceUsage[TransferService.Security].Bypassed == 40,
                "Design Day Security usage counts every outbound transfer as a bypass");
            Check(generated.TransferDemand.ServiceUsage[TransferService.Emigration].Bypassed ==
                  internationalOutgoingTransfers,
                "Design Day Emigration usage only counts international outbound transfers");
            Check(generated.TransferDemand.ServiceUsage[TransferService.Immigration].Bypassed == 0 &&
                  internationalIncomingTransfers == 0,
                "Design Day Immigration usage has no inbound transfer records");
            Check(generated.TransferDemand.ServiceUsage[TransferService.TransferDesk].Bypassed == 0 &&
                  generated.TransferDemand.ServiceUsage[TransferService.BaggageRecheck].Bypassed == 0,
                "Design Day arrival services have no inbound transfer records");

            CheckInResults checkIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expectedCheckIn = p =>
                p != null &&
                TscIsDeparture(p) &&
                !p.IsLateForCheckIn &&
                !TransferServiceUsageCalculator.IsBypassed(p, TransferService.CheckIn);

            TscAssertService(
                "Design Day Check-In",
                TransferService.CheckIn,
                FacilityType.CheckIn,
                generated.Passengers,
                checkIn.QueueResults.ProcessedPassengers,
                checkIn.QueueResults.Passengers,
                expectedCheckIn);

            Check(checkIn.QueueResults.ProcessedPassengers.Count == originDepartures,
                $"Design Day Check-In serves originating departures only ({checkIn.QueueResults.ProcessedPassengers.Count} of {originDepartures})");
            Check(checkIn.QueueResults.ProcessedPassengers.All(p =>
                    p.Journey != PassengerJourney.Transfer),
                "No Design Day transfer is queued at Check-In when its rule bypasses check-in");

            CheckInResults sharedCheckIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                PhysicalResources = new List<int> { 3 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Check(TscIds(sharedCheckIn.QueueResults.ProcessedPassengers).SetEquals(
                    TscIds(checkIn.QueueResults.ProcessedPassengers)),
                "Design Day Check-In bypasses are identical in shared and wired pools");

            BagDropResults bagDrop = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 2 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Check(bagDrop.QueueResults.ProcessedPassengers.Count == 0,
                $"Design Day Bag Drop serves nobody when every transfer bypasses it and origins carry no bags ({bagDrop.QueueResults.ProcessedPassengers.Count})");

            SecurityResults security = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expectedSecurity = p =>
                p != null &&
                TscIsDeparture(p) &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Security);

            TscAssertService(
                "Design Day Security",
                TransferService.Security,
                FacilityType.Security,
                generated.Passengers,
                security.QueueResults.ProcessedPassengers,
                security.QueueResults.Passengers,
                expectedSecurity);

            Check(security.QueueResults.ProcessedPassengers.Count == originDepartures,
                $"Design Day Security serves originating departures only ({security.QueueResults.ProcessedPassengers.Count})");

            EmigrationResults emigration = new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                EGateSharePercentage = 50.0,
                Channels = new List<string>
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool
                },
                PhysicalResources = new List<int> { 2, 2 },
                TargetWait95 = new List<double> { 10.0, 10.0 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expectedEmigration = p =>
                p != null &&
                p.PassengerType == PassengerType.International &&
                TscIsDeparture(p) &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Emigration);

            TscAssertService(
                "Design Day Emigration",
                TransferService.Emigration,
                FacilityType.Emigration,
                generated.Passengers,
                emigration.QueueResults.ProcessedPassengers,
                emigration.QueueResults.Passengers,
                expectedEmigration);

            Check(emigration.QueueResults.ProcessedPassengers.Count ==
                  originInternationalDepartures,
                $"Design Day Emigration serves originating international departures only ({emigration.QueueResults.ProcessedPassengers.Count})");

            ImmigrationResults immigration = new ImmigrationSolver().Solve(new ImmigrationInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Func<Passenger, bool> expectedImmigration = p =>
                p != null &&
                TransferServiceUsageCalculator.UsesService(p, TransferService.Immigration);

            TscAssertService(
                "Design Day Immigration",
                TransferService.Immigration,
                FacilityType.Immigration,
                generated.Passengers,
                immigration.QueueResults.ProcessedPassengers,
                immigration.QueueResults.Passengers,
                expectedImmigration);

            Check(immigration.QueueResults.ProcessedPassengers.Count == 0 &&
                  originInternationalArrivals == 0,
                $"Design Day Immigration has no arrival passengers to serve ({immigration.QueueResults.ProcessedPassengers.Count})");
        }

        // -------------------------------------------------------------
        // Missing rules are not bypasses: they follow normal behaviour.
        // -------------------------------------------------------------
        private static void RunUnspecifiedTransferRuleWarning()
        {
            Console.WriteLine("-- Unspecified transfer rules remain normal behaviour --");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "TSC unspecified"
            };

            for (int i = 0; i < 5; i++)
            {
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-U-ARR-{i}", LegDirection.Arrival, 60 + i * 10, 20,
                    LegSector.Domestic, "LHR", "DEL"));
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-U-DEP-{i}", LegDirection.Departure, 600 + i * 10, 20,
                    LegSector.Domestic, "SIN", "BOM"));
            }

            PassengerProfile scheduleProfile = MakeScheduleProfile(
                "TSC unspecified", 100.0, 60.0, 120.0, ShowUpProfile.Mix());
            scheduleProfile.OnlineCheckInProbability = 0.0;
            scheduleProfile.CheckedBagProbability = 0.0;

            TransferDemandProfile profile = new TransferDemandProfile
            {
                DomesticTransferPercent = 50.0,
                DomesticToDomesticPercent = 100.0
            };
            profile.ScenarioRules[TransferScenario.DomesticToDomestic].HasProcessingRules = false;

            SchedulePassengerGenerationResult generated = new SchedulePassengerGenerator().Generate(
                schedule,
                new List<PassengerGroup>
                {
                    MakeTypedScheduleGroup(DHPType.DomesticDeparture, scheduleProfile)
                },
                new WalkingProfile { AverageSpeed = 1.35, StandardDeviation = 0.0 },
                seed: 14,
                transferDemand: profile);

            int taggedOutbound = generated.Passengers.Count(p =>
                p.Journey == PassengerJourney.Transfer && TscIsDeparture(p));
            Check(taggedOutbound == 50,
                $"Half of the 100 outbound passengers are tagged transfers ({taggedOutbound})");
            Check(generated.TransferDemand.ServiceUsage[TransferService.CheckIn].Unspecified == 50 &&
                  generated.TransferDemand.ServiceUsage[TransferService.CheckIn].Bypassed == 0,
                "Transfers without processing rules are unspecified, not bypassed");
            Check(generated.TransferDemand.Warnings.Any(w =>
                    w.Contains("no explicit processing rules")),
                "The generator warns that tagged transfers have no explicit processing rules");

            CheckInResults checkIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = TscServiceProfile(),
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 15,
                StaffingPercentage = 100.0
            });

            Check(checkIn.QueueResults.ProcessedPassengers.Count == 100,
                $"Unspecified transfers follow normal check-in behaviour ({checkIn.QueueResults.ProcessedPassengers.Count})");
            Check(checkIn.QueueResults.ProcessedPassengers.Count(p =>
                    p.Journey == PassengerJourney.Transfer) == 50,
                "All 50 unspecified outbound transfers keep normal check-in service");
        }

        // -------------------------------------------------------------
        // Transfer bag-drop routing: tagging must mark the separate Bag Drop
        // requirement without replacing the passenger's primary channel.
        // -------------------------------------------------------------
        private static void RunTransferBagDropChannelRouting()
        {
            Console.WriteLine("-- Transfer bag-drop channel routing --");

            TransferDemandProfile profile = new TransferDemandProfile
            {
                DomesticTransferPercent = 12.5,
                InternationalToDomesticPercent = 100.0
            };
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].HasProcessingRules = true;
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].RequiresCheckIn = true;
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].RequiresBagDrop = true;

            ProfileChannelMap routing = new ProfileChannelMap
            {
                DefaultChannel = DefaultChannelAssigner.CounterChannel,
                Entries =
                {
                    new ProfileChannelEntry
                    {
                        ProfileName = "Transfer routing",
                        Shares =
                        {
                            new ChannelShare { Channel = "Counter", Share = 0.75 },
                            new ChannelShare { Channel = "Kiosk", Share = 0.25 },
                            new ChannelShare { Channel = "Bag-Drop", Share = 0.5 }
                        }
                    }
                }
            };

            Check(routing.BagDropRoutingChannel == "Bag-Drop",
                "The legacy bag-drop name is still discoverable");
            Check(routing.GetChannels().Contains("Web") &&
                  !routing.GetChannels().Contains("Bag-Drop"),
                "A legacy BagDrop share is normalized to Web as the primary channel");

            List<Passenger> MakePopulation()
            {
                return Enumerable.Range(1, 40)
                    .Select(id => new Passenger
                    {
                        Id = id,
                        PassengerType = PassengerType.Domestic,
                        DHPType = DHPType.DomesticDeparture,
                        Journey = PassengerJourney.Origin,
                        Channel = id % 4 == 0 ? "Kiosk" : "Counter",
                        UsesOnlineCheckIn = false,
                        HasCheckedBag = true,
                        ProfileName = "Transfer routing",
                        ArrivalTime = 1.0 + id * 0.05,
                        QueueArrivalTime = 1.0 + id * 0.05
                    })
                    .ToList();
            }

            // Tagging marks the separate requirement; the primary channel stays.
            List<Passenger> routed = MakePopulation();

            TransferDemandResult tagged =
                TransferPassengerTagger.ApplyAggregate(routed, profile, routing);

            List<Passenger> transfers = routed
                .Where(p => p.Journey == PassengerJourney.Transfer)
                .ToList();

            Check(transfers.Count == 5,
                $"Aggregate tagging tags 5 International→Domestic transfers ({transfers.Count})");
            Check(transfers.All(p => p.RequiresBagDrop && p.RequiresSeparateBagDrop),
                "Every tagged transfer rule requires the separate Bag Drop stage");
            Check(transfers.All(p =>
                    string.Equals(p.Channel, "Counter", StringComparison.Ordinal) ||
                    string.Equals(p.Channel, "Kiosk", StringComparison.Ordinal)),
                $"A tagged transfer keeps its primary channel ({string.Join(", ", transfers.Select(p => p.Channel).Distinct())})");
            Check(transfers.All(p => !DefaultChannelAssigner.IsBagDropChannel(p.Channel)),
                "Bag Drop is never assigned as a primary channel");

            // Idempotence: re-tagging an already-marked population is stable.
            TransferPassengerTagger.ApplyAggregate(routed, profile, routing);
            Check(transfers.All(p => p.RequiresSeparateBagDrop),
                "Re-tagging keeps the separate Bag Drop requirement");

            // The legacy enforcement flag no longer rewrites primary channels.
            routing.EnforceBagDropRouting = false;
            List<Passenger> byShares = MakePopulation();

            TransferPassengerTagger.ApplyAggregate(byShares, profile, routing);

            List<Passenger> shareTransfers = byShares
                .Where(p => p.Journey == PassengerJourney.Transfer)
                .ToList();
            Check(shareTransfers.All(p => !DefaultChannelAssigner.IsBagDropChannel(p.Channel)) &&
                  shareTransfers.All(p =>
                      string.Equals(p.Channel, "Counter", StringComparison.Ordinal) ||
                      string.Equals(p.Channel, "Kiosk", StringComparison.Ordinal)),
                "The legacy enforcement flag leaves the primary channels untouched");
        }

        // -------------------------------------------------------------
        // Schedule mode: the generator must mark the separate Bag Drop
        // requirement while leaving the primary channel intact.
        // -------------------------------------------------------------
        private static void RunScheduleTransferBagDropChannelRouting()
        {
            Console.WriteLine("-- Schedule transfer bag-drop channel routing --");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "TSC bag-drop routing"
            };

            for (int i = 0; i < 5; i++)
            {
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-R-ARR-I-{i}", LegDirection.Arrival, 60 + i * 10, 20,
                    LegSector.International, "LHR", "DXB"));
                schedule.Legs.Add(MakeTransferLeg(
                    $"TSC-R-DEP-D-{i}", LegDirection.Departure, 600 + i * 10, 20,
                    LegSector.Domestic, "SIN", "BOM"));
            }

            foreach (FlightLeg leg in schedule.Legs)
                leg.Terminal = "T1";

            PassengerProfile scheduleProfile = MakeScheduleProfile(
                "TSC routing", 100.0, 60.0, 120.0, ShowUpProfile.Mix());
            scheduleProfile.OnlineCheckInProbability = 0.0;
            scheduleProfile.CheckedBagProbability = 0.0;

            TransferDemandProfile profile = new TransferDemandProfile
            {
                DomesticTransferPercent = 10.0,
                InternationalToDomesticPercent = 100.0
            };
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].HasProcessingRules = true;
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].RequiresCheckIn = true;
            profile.ScenarioRules[TransferScenario.InternationalToDomestic].RequiresBagDrop = true;

            ProfileChannelMap routing = new ProfileChannelMap
            {
                DefaultChannel = DefaultChannelAssigner.CounterChannel,
                Entries =
                {
                    new ProfileChannelEntry
                    {
                        ProfileName = "TSC routing",
                        Shares =
                        {
                            new ChannelShare { Channel = "Counter", Share = 1.0 }
                        }
                    },
                    // A bag-drop desk exists in the routing table, held on a
                    // profile that the schedule groups do not use.
                    new ProfileChannelEntry
                    {
                        ProfileName = "TSC unused",
                        Shares =
                        {
                            new ChannelShare { Channel = "Bag-Drop", Share = 1.0 }
                        }
                    }
                }
            };

            SchedulePassengerGenerationResult generated = new SchedulePassengerGenerator().Generate(
                schedule,
                new List<PassengerGroup>
                {
                    MakeTypedScheduleGroup(DHPType.DomesticDeparture, scheduleProfile),
                    MakeTypedScheduleGroup(DHPType.InternationalDeparture, scheduleProfile)
                },
                new WalkingProfile { AverageSpeed = 1.35, StandardDeviation = 0.0 },
                seed: 17,
                channelAssigner: routing,
                transferDemand: profile);

            List<Passenger> outbound = generated.Passengers
                .Where(p => p.Journey == PassengerJourney.Transfer &&
                            (p.DHPType == DHPType.DomesticDeparture ||
                             p.DHPType == DHPType.InternationalDeparture))
                .ToList();

            Check(outbound.Count == 10,
                $"Schedule transfer tagging tags 10 International→Domestic departure legs ({outbound.Count})");
            Check(outbound.All(p => p.RequiresBagDrop && p.RequiresSeparateBagDrop),
                "Every scheduled transfer rule requires the separate Bag Drop stage");
            Check(outbound.All(p => string.Equals(p.Channel, "Counter", StringComparison.Ordinal)),
                $"The generator keeps the primary Counter channel ({string.Join(", ", outbound.Select(p => p.Channel).Distinct())})");

            List<Passenger> origins = generated.Passengers
                .Where(p => p.Journey == PassengerJourney.Origin)
                .ToList();

            Check(origins.All(p => string.Equals(p.Channel, "Counter", StringComparison.Ordinal)),
                "Origin departures keep their share-routed channel");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private static TransferScenarioRules TscRules(
            bool checkIn = false,
            bool bagDrop = false,
            bool security = false,
            bool immigration = false,
            bool emigration = false,
            bool transferDesk = false,
            bool baggageRecheck = false,
            TransferFacilityRouting securityRouting = TransferFacilityRouting.Integrated,
            TransferFacilityRouting immigrationRouting = TransferFacilityRouting.Integrated,
            TransferFacilityRouting emigrationRouting = TransferFacilityRouting.Integrated)
        {
            return new TransferScenarioRules
            {
                Scenario = TransferScenario.DomesticToDomestic,
                HasProcessingRules = true,
                MinimumConnectionMinutes = 0.0,
                MaximumConnectionMinutes = 1440.0,
                RequiresCheckIn = checkIn,
                RequiresBagDrop = bagDrop,
                RequiresSecurityScreening = security,
                RequiresImmigration = immigration,
                RequiresEmigration = emigration,
                RequiresTransferDesk = transferDesk,
                RequiresBaggageRecheck = baggageRecheck,
                SecurityRouting = securityRouting,
                ImmigrationRouting = immigrationRouting,
                EmigrationRouting = emigrationRouting
            };
        }

        private static Passenger TscPassenger(
            int id,
            DHPType dhp,
            PassengerType type,
            string channel,
            PassengerJourney journey,
            TransferScenario? scenario,
            TransferScenarioRules rules,
            bool online = false,
            bool bag = false,
            bool late = false,
            double arrival = 0.0)
        {
            double time = arrival > 0.0 ? arrival : 1.0 + id * 0.001;

            return new Passenger
            {
                Id = id,
                Channel = channel,
                DHPType = dhp,
                Journey = journey,
                TransferScenario = scenario,
                TransferProcessing = rules != null ? rules.Clone() : null,
                PassengerType = type,
                ProfileName = "TSC",
                UsesOnlineCheckIn = online,
                HasCheckedBag = bag,
                IsLateForCheckIn = late,
                ArrivalTime = time,
                QueueArrivalTime = time
            };
        }

        private static ServiceProfile TscServiceProfile()
        {
            return new ServiceProfile
            {
                MeanServiceTime = 1.0,
                StandardDeviation = 0.0
            };
        }

        private static bool TscIsDeparture(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticDeparture ||
                    passenger.DHPType == DHPType.InternationalDeparture);
        }

        private static bool TscIsArrival(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticArrival ||
                    passenger.DHPType == DHPType.InternationalArrival);
        }

        private static DHPType TscDepartureType(TransferScenario scenario)
        {
            return scenario == TransferScenario.DomesticToDomestic ||
                   scenario == TransferScenario.InternationalToDomestic
                ? DHPType.DomesticDeparture
                : DHPType.InternationalDeparture;
        }

        private static DHPType TscArrivalType(TransferScenario scenario)
        {
            return scenario == TransferScenario.DomesticToDomestic ||
                   scenario == TransferScenario.DomesticToInternational
                ? DHPType.DomesticArrival
                : DHPType.InternationalArrival;
        }

        private static PassengerType TscMarket(DHPType dhp)
        {
            return dhp == DHPType.InternationalDeparture ||
                   dhp == DHPType.InternationalArrival
                ? PassengerType.International
                : PassengerType.Domestic;
        }

        private static HashSet<int> TscIds(IEnumerable<Passenger> passengers)
        {
            return new HashSet<int>(
                (passengers ?? Enumerable.Empty<Passenger>())
                    .Where(p => p != null)
                    .Select(p => p.Id));
        }

        private static void TscAssertService(
            string label,
            TransferService service,
            FacilityType facility,
            IList<Passenger> input,
            IList<Passenger> processed,
            IList<Passenger> output,
            Func<Passenger, bool> expectedUses)
        {
            HashSet<int> expectedIds = TscIds(input.Where(expectedUses));
            HashSet<int> processedIds = TscIds(processed);
            HashSet<int> bypassedIds = TscIds(input.Where(p =>
                TransferServiceUsageCalculator.IsBypassed(p, service)));

            Check(processedIds.SetEquals(expectedIds),
                $"{label}: served cohort matches service eligibility " +
                $"({processedIds.Count} served, {expectedIds.Count} expected)");
            Check(!processedIds.Overlaps(bypassedIds),
                $"{label}: none of the {bypassedIds.Count} rule-based bypasses are served");
            Check(TscIds(output).SetEquals(TscIds(input)) &&
                  output.Count == input.Count,
                $"{label}: the complete population is passed through exactly once");

            List<Passenger> processedOutput = output
                .Where(p => p != null && processedIds.Contains(p.Id))
                .ToList();
            Check(processedOutput.All(p => p.History.ContainsKey(facility)),
                $"{label}: every served passenger carries a {facility} record");

            List<Passenger> bypassedOutput = output
                .Where(p => p != null && bypassedIds.Contains(p.Id))
                .ToList();
            Check(bypassedOutput.All(p => !p.History.ContainsKey(facility)),
                $"{label}: bypassed passengers carry no {facility} record");
        }
    }
}
