using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AirportPlanning.Core.Analysis;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Emigration;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facilities.Security;
using AirportPlanning.Facilities.TerminalEntry;

namespace AirportPlanning.Tests
{
    /// <summary>
    /// Headless end-to-end journey regression tests.
    ///
    /// The fixture mirrors the wiring of gh/aviation_update_1.ghx:
    ///
    ///   Terminal Entry -> Check-In -> Bag Drop -> Verification
    ///       Domestic  ----------------------------------\
    ///       International -> Emigration -----------------+-> Security (EGate -> ATRS series)
    ///
    /// and asserts, per passenger: which stages were visited (history-backed),
    /// exact stage-time arithmetic, pool identities, the service-time source,
    /// and strict chronology. The narration tests rebuild each journey from the
    /// passenger's History only, so a skipped stage can never be narrated with
    /// stale state from another stage.
    /// </summary>
    public static partial class Program
    {
        // -------------------------------------------------------------
        // Service times used across the journey fixture, in seconds.
        // Every stage has a DISTINCT value so a stage that accidentally
        // resolved the map Default (40 s) or another stage's key fails loudly.
        // -------------------------------------------------------------
        private const double ManualEntrySeconds = 15.0;
        private const double DigiYatraEntrySeconds = 7.0;
        private const double CounterSeconds = 30.0;
        private const double KioskSeconds = 20.0;
        private const double BagDropSeconds = 40.0;   // the map Default
        private const double VerificationSeconds = 12.0;   // 300 pax/h throughput
        private const double EmigrationEgateSeconds = 10.0;
        private const double EmigrationCounterSeconds = 25.0;
        private const double SecurityEgateSeconds = 6.0;
        private const double SecurityAtrsSeconds = 18.0;
        private const double SecurityWalkMinutes = 0.5;

        private static readonly double Tolerance = 1e-9;

        // Canonical stage order of the GHX departure chain.
        private static readonly string[] DepartureStageOrder =
        {
            "Terminal Entry",
            "Check-In",
            "Bag Drop",
            "Verification",
            "Emigration",
            "Security"
        };

        // -------------------------------------------------------------
        // The shared Service Time Map, scoped per facility by the solvers,
        // exactly the way one STMAP component feeds every facility in the GHX.
        // -------------------------------------------------------------
        private static ServiceTimeMap BuildJourneyServiceTimeMap()
        {
            ServiceProfile Profile(double seconds)
            {
                return new ServiceProfile
                {
                    MeanServiceTime = seconds / 60.0,
                    StandardDeviation = 0.0
                };
            }

            ServiceTimeMap map = new ServiceTimeMap
            {
                // Bag Drop has no keyed row: it runs on the Default profile.
                Default = Profile(BagDropSeconds)
            };

            map.Entries.Add(new ServiceTimeEntry { Key = "Manual Verification", Facility = "Terminal Entry", Profile = Profile(ManualEntrySeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "DigiYatra", Facility = "Terminal Entry", Profile = Profile(DigiYatraEntrySeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "Counter", Facility = "Check-In", Profile = Profile(CounterSeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "Kiosk", Facility = "Check-In", Profile = Profile(KioskSeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "EGate", Facility = "Emigration", Profile = Profile(EmigrationEgateSeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "Counter", Facility = "Emigration", Profile = Profile(EmigrationCounterSeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "EGate", Facility = "Security", Profile = Profile(SecurityEgateSeconds) });
            map.Entries.Add(new ServiceTimeEntry { Key = "ATRS", Facility = "Security", Profile = Profile(SecurityAtrsSeconds) });

            return map;
        }

        // -------------------------------------------------------------
        // Deterministic mixed cohort. Stable IDs; 60-minute spacing so the
        // uncongested journey has zero waits and exact stage arithmetic.
        // -------------------------------------------------------------
        private static List<Passenger> BuildJourneyCohort()
        {
            List<Passenger> cohort = new List<Passenger>();

            // 1: domestic origin departure, counter check-in, bag accepted at
            // the counter (no separate bag drop).
            cohort.Add(MakeJourneyPassenger(1, DHPType.DomesticDeparture, PassengerType.Domestic,
                PassengerJourney.Origin, "Domestic Economy", "Counter", hasBag: true));

            // 2: domestic origin departure, web check-in, checked bag -> the
            // separate Bag Drop stage, and NO check-in history.
            cohort.Add(MakeJourneyPassenger(2, DHPType.DomesticDeparture, PassengerType.Domestic,
                PassengerJourney.Origin, "Domestic Economy", "Web", hasBag: true));

            // 3: international origin departure, kiosk check-in, checked bag.
            cohort.Add(MakeJourneyPassenger(3, DHPType.InternationalDeparture, PassengerType.International,
                PassengerJourney.Origin, "International Economy", "Kiosk", hasBag: true));

            // 4: international origin departure, counter check-in, no bag.
            cohort.Add(MakeJourneyPassenger(4, DHPType.InternationalDeparture, PassengerType.International,
                PassengerJourney.Origin, "International Economy", "Counter", hasBag: false));

            // 5: domestic transfer (D->D) whose scenario rule requires check-in
            // and security but no bag drop. Transfers bypass the terminal entry.
            Passenger transferDomDom = MakeJourneyPassenger(5, DHPType.DomesticDeparture, PassengerType.Domestic,
                PassengerJourney.Transfer, "Domestic Economy", "Counter", hasBag: false);
            transferDomDom.TransferScenario = TransferScenario.DomesticToDomestic;
            transferDomDom.TransferProcessing = new TransferScenarioRules
            {
                Scenario = TransferScenario.DomesticToDomestic,
                HasProcessingRules = true,
                RequiresCheckIn = true,
                RequiresBagDrop = false,
                RequiresSecurityScreening = true
            };
            cohort.Add(transferDomDom);

            // 6: international transfer (I->I) whose rule requires security but
            // explicitly bypasses emigration (already airside).
            Passenger transferIntInt = MakeJourneyPassenger(6, DHPType.InternationalDeparture, PassengerType.International,
                PassengerJourney.Transfer, "International Economy", "Web", hasBag: false);
            transferIntInt.TransferScenario = TransferScenario.InternationalToInternational;
            transferIntInt.TransferProcessing = new TransferScenarioRules
            {
                Scenario = TransferScenario.InternationalToInternational,
                HasProcessingRules = true,
                RequiresCheckIn = false,
                RequiresBagDrop = false,
                RequiresSecurityScreening = true,
                RequiresEmigration = false
            };
            cohort.Add(transferIntInt);

            // 7 + 8: arriving passengers. They use no departure facility at
            // all; the GHX still feeds them through Verification.
            cohort.Add(MakeJourneyPassenger(7, DHPType.DomesticArrival, PassengerType.Domestic,
                PassengerJourney.Destination, "Domestic Economy", "Counter", hasBag: false));
            cohort.Add(MakeJourneyPassenger(8, DHPType.InternationalArrival, PassengerType.International,
                PassengerJourney.Destination, "International Economy", "Counter", hasBag: false));

            // 9: international transfer WITHOUT explicit rules. Unspecified
            // transfers keep normal behaviour: no check-in (web), no bag drop,
            // emigration and security both apply.
            Passenger transferUnspecified = MakeJourneyPassenger(9, DHPType.InternationalDeparture, PassengerType.International,
                PassengerJourney.Transfer, "International Economy", "Web", hasBag: false);
            transferUnspecified.TransferScenario = TransferScenario.InternationalToInternational;
            cohort.Add(transferUnspecified);

            return cohort;
        }

        private static Passenger MakeJourneyPassenger(
            int id,
            DHPType dhpType,
            PassengerType passengerType,
            PassengerJourney journey,
            string profileName,
            string channel,
            bool hasBag)
        {
            double arrival = 180.0 + 60.0 * (id - 1);

            return new Passenger
            {
                Id = id,
                DHPType = dhpType,
                PassengerType = passengerType,
                Journey = journey,
                ProfileName = profileName,
                Channel = channel,
                HasCheckedBag = hasBag,
                Gender = id % 2 == 0 ? PassengerGender.Female : PassengerGender.Male,
                ArrivalTime = arrival,
                QueueArrivalTime = arrival,
                TerminalEntryArrivalTime = arrival,
                TerminalEntryWalkMinutes = 3.0,
                WalkingSpeed = 1.4
            };
        }

        private sealed class JourneyResult
        {
            public List<Passenger> Cohort;
            public TerminalEntryResults Entry;
            public CheckInResults CheckIn;
            public BagDropResults BagDrop;
            public PassengerVerificationResults Verification;
            public EmigrationResults Emigration;
            public SecurityResults Security;
            public List<Passenger> Final;
        }

        /// <summary>
        /// Runs the full GHX chain headlessly: entry -> check-in -> bag drop ->
        /// verification, then the domestic / international branch with the
        /// security merge. One shared facility-tagged service time map.
        /// </summary>
        private static JourneyResult RunGhxJourney(List<Passenger> cohort)
        {
            ServiceTimeMap map = BuildJourneyServiceTimeMap();

            TerminalEntryResults entry = new TerminalEntrySolver().Solve(new TerminalEntryInputs
            {
                Passengers = cohort.Select(p => p.Clone()).ToList(),
                ServiceTimeProvider = map,
                DigiYatraSharePercentage = 0.0,
                ManualPhysicalPositions = 4,
                DigiYatraPhysicalPositions = 2,
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 3
            });

            CheckInResults checkIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = entry.QueueResults.Passengers,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter", "Kiosk" },
                PhysicalResources = new List<int> { 3, 2 },
                TargetWait95 = new List<double> { 5.0, 5.0 },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 11
            });

            BagDropResults bagDrop = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = checkIn.QueueResults.Passengers,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 5.0 },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 12
            });

            PassengerVerificationResults verification = new PassengerVerificationSolver().Solve(
                new PassengerVerificationInputs
                {
                    Passengers = bagDrop.QueueResults.Passengers,
                    EligiblePercentage = 100.0,
                    ThroughputPerGate = 300.0,
                    DesignUtilization = 0.85
                });

            List<Passenger> afterVerification = verification.Passengers;

            // The Passenger Filter split used on the canvas: domestic flows
            // straight to security, international goes through emigration.
            List<Passenger> domesticBranch = afterVerification
                .Where(p => p.PassengerType == PassengerType.Domestic)
                .ToList();
            List<Passenger> internationalBranch = afterVerification
                .Where(p => p.PassengerType == PassengerType.International)
                .ToList();

            EmigrationResults emigration = new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = internationalBranch,
                ServiceTimeProvider = map,
                EGateSharePercentage = 100.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Facility = "Emigration", Channel = "EGate", PhysicalResources = 2, TargetWait95 = 2.0, Range = new ResourceSearchRange(1, 6) },
                    new PoolPlan { Facility = "Emigration", Channel = "Counter", PhysicalResources = 2, TargetWait95 = 10.0, Range = new ResourceSearchRange(1, 6) }
                },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 13
            });

            List<Passenger> merged = domesticBranch
                .Concat(emigration.QueueResults.Passengers)
                .ToList();

            SecurityResults security = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = merged,
                ServiceTimeProvider = map,
                UseEGateSeries = true,
                EGateToAtrsWalkMinutes = SecurityWalkMinutes,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Facility = "Security", Channel = "EGate", PhysicalResources = 2, TargetWait95 = 2.0, Range = new ResourceSearchRange(1, 6) },
                    new PoolPlan { Facility = "Security", Channel = "ATRS", PhysicalResources = 3, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 6) }
                },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 14
            });

            return new JourneyResult
            {
                Cohort = cohort,
                Entry = entry,
                CheckIn = checkIn,
                BagDrop = bagDrop,
                Verification = verification,
                Emigration = emigration,
                Security = security,
                Final = security.QueueResults.Passengers
            };
        }

        // -------------------------------------------------------------
        // Passenger Filter segregation (new outputs)
        // -------------------------------------------------------------
        private static void RunPassengerFilterSegregationTests()
        {
            Console.WriteLine("== Passenger Filter segregation ==");

            List<Passenger> population = new List<Passenger>();
            int id = 0;

            void Add(DHPType dhp, PassengerType type, PassengerJourney journey)
            {
                population.Add(new Passenger { Id = ++id, DHPType = dhp, PassengerType = type, Journey = journey });
            }

            Add(DHPType.DomesticDeparture, PassengerType.Domestic, PassengerJourney.Origin);          // 1
            Add(DHPType.DomesticDeparture, PassengerType.Domestic, PassengerJourney.Transfer);        // 2
            Add(DHPType.InternationalDeparture, PassengerType.International, PassengerJourney.Origin);// 3
            Add(DHPType.InternationalDeparture, PassengerType.International, PassengerJourney.Transfer);// 4
            Add(DHPType.DomesticArrival, PassengerType.Domestic, PassengerJourney.Origin);            // 5
            Add(DHPType.DomesticArrival, PassengerType.Domestic, PassengerJourney.Destination);       // 6
            Add(DHPType.InternationalArrival, PassengerType.International, PassengerJourney.Destination); // 7
            Add(DHPType.InternationalArrival, PassengerType.International, PassengerJourney.Transfer);// 8

            int[] Ids(List<Passenger> list)
            {
                return list.Select(p => p.Id).OrderBy(i => i).ToArray();
            }

            Check(Ids(PassengerSegregation.DomesticDepartures(population)).SequenceEqual(new[] { 1, 2 }),
                "Domestic Departures = domestic departures only (by DHPType)");
            Check(Ids(PassengerSegregation.InternationalDepartures(population)).SequenceEqual(new[] { 3, 4 }),
                "International Departures = international departures only (by DHPType)");
            Check(Ids(PassengerSegregation.DomesticArrivals(population)).SequenceEqual(new[] { 5, 6 }),
                "Domestic Arrivals = domestic arrivals only (by DHPType)");
            Check(Ids(PassengerSegregation.InternationalArrivals(population)).SequenceEqual(new[] { 7, 8 }),
                "International Arrivals = international arrivals only (by DHPType)");

            Check(Ids(PassengerSegregation.Departures(population)).SequenceEqual(new[] { 1, 2, 3, 4 }),
                "Departures = domestic + international departures");
            Check(Ids(PassengerSegregation.Arrivals(population)).SequenceEqual(new[] { 5, 6, 7, 8 }),
                "Arrivals = domestic + international arrivals");

            Check(Ids(PassengerSegregation.Origin(population)).SequenceEqual(new[] { 1, 3, 5 }),
                "Origin passengers (by Journey)");
            Check(Ids(PassengerSegregation.Transfer(population)).SequenceEqual(new[] { 2, 4, 8 }),
                "Transfer passengers (by Journey)");
            Check(Ids(PassengerSegregation.Destination(population)).SequenceEqual(new[] { 6, 7 }),
                "Destination passengers (by Journey)");

            Check(Ids(PassengerSegregation.Domestic(population)).SequenceEqual(new[] { 1, 2, 5, 6 }),
                "Domestic split unchanged (by PassengerType)");
            Check(Ids(PassengerSegregation.International(population)).SequenceEqual(new[] { 3, 4, 7, 8 }),
                "International split unchanged (by PassengerType)");

            // Partitions: no passenger lost or duplicated inside one dimension.
            List<Passenger> legUnion = PassengerSegregation.Departures(population)
                .Concat(PassengerSegregation.Arrivals(population))
                .ToList();
            Check(legUnion.Count == population.Count &&
                  legUnion.Select(p => p.Id).Distinct().Count() == population.Count,
                "Departure/arrival split partitions the population exactly once");

            List<Passenger> journeyUnion = PassengerSegregation.Origin(population)
                .Concat(PassengerSegregation.Transfer(population))
                .Concat(PassengerSegregation.Destination(population))
                .ToList();
            Check(journeyUnion.Count == population.Count &&
                  journeyUnion.Select(p => p.Id).Distinct().Count() == population.Count,
                "Origin/transfer/destination split partitions the population exactly once");

            // The two dimensions are independent: a transfer departure belongs
            // to BOTH Transfer and Departures.
            List<Passenger> transferDepartures = PassengerSegregation.Transfer(population)
                .Intersect(PassengerSegregation.Departures(population))
                .ToList();
            Check(Ids(transferDepartures).SequenceEqual(new[] { 2, 4 }),
                "A transfer departure appears in both the Transfer and Departures flows");

            List<Passenger> arrivalTransfers = PassengerSegregation.Transfer(population)
                .Intersect(PassengerSegregation.Arrivals(population))
                .ToList();
            Check(Ids(arrivalTransfers).SequenceEqual(new[] { 8 }),
                "A transfer arrival appears in both the Transfer and Arrivals flows");

            // Empty population: every split is empty, nothing throws.
            List<Passenger> empty = new List<Passenger>();
            Check(PassengerSegregation.Departures(empty).Count == 0 &&
                  PassengerSegregation.Transfer(empty).Count == 0,
                "An empty population splits into empty flows");

            // The real journey cohort splits consistently too.
            List<Passenger> cohort = BuildJourneyCohort();
            Check(PassengerSegregation.Departures(cohort).Count == 7 &&
                  PassengerSegregation.Arrivals(cohort).Count == 2 &&
                  PassengerSegregation.Transfer(cohort).Count == 3 &&
                  PassengerSegregation.Origin(cohort).Count == 4 &&
                  PassengerSegregation.Destination(cohort).Count == 2,
                "The journey cohort splits into 7 departures, 2 arrivals, 3 transfers, 4 origins, 2 destinations");
        }

        // -------------------------------------------------------------
        // End-to-end journey across the GHX chain
        // -------------------------------------------------------------
        private static void RunGhxJourneyIntegrationTests()
        {
            Console.WriteLine("== GHX journey integration ==");

            JourneyResult journey = RunGhxJourney(BuildJourneyCohort());

            // ---- stage populations ---------------------------------------
            Check(journey.Entry.TransfersBypassingEntry == 3,
                $"Terminal Entry reports 3 transfers bypassing the pools ({journey.Entry.TransfersBypassingEntry})");

            Check(journey.Entry.PoolNames.Count == 2 &&
                  journey.Entry.PoolNames.Contains("Manual Verification") &&
                  journey.Entry.PoolNames.Contains("DigiYatra"),
                "Terminal Entry resolves one pool per entry route");

            List<Passenger> checkInServed = journey.CheckIn.QueueResults.ProcessedPassengers;
            Check(checkInServed.Count == 4 &&
                  checkInServed.Select(p => p.Id).OrderBy(i => i).SequenceEqual(new[] { 1, 3, 4, 5 }),
                $"Check-In serves exactly the 4 check-in passengers ({DescribeIds(checkInServed)})");

            List<Passenger> bagDropServed = journey.BagDrop.QueueResults.ProcessedPassengers;
            Check(bagDropServed.Count == 2 &&
                  bagDropServed.Select(p => p.Id).OrderBy(i => i).SequenceEqual(new[] { 2, 3 }),
                $"Bag Drop serves exactly the 2 separate-bag-drop passengers ({DescribeIds(bagDropServed)})");

            List<Passenger> emigrationServed = journey.Emigration.QueueResults.ProcessedPassengers;
            Check(emigrationServed.Count == 3 &&
                  emigrationServed.Select(p => p.Id).OrderBy(i => i).SequenceEqual(new[] { 3, 4, 9 }),
                $"Emigration serves the 3 international departures that use it ({DescribeIds(emigrationServed)})");

            List<Passenger> securityServed = journey.Security.QueueResults.ProcessedPassengers;
            Check(journey.Security.IsSeries && securityServed.Count == 7,
                $"Security series screens exactly the 7 departures ({securityServed.Count})");

            Check(journey.Final.Count == 9 &&
                  journey.Final.Select(p => p.Id).OrderBy(i => i).SequenceEqual(Enumerable.Range(1, 9)),
                "The merged final population keeps all 9 passengers exactly once");

            // ---- per-passenger history-backed expectations -----------------
            // Expected stage chains per passenger, in visit order:
            //   1: TE, CHK, VER, SEC          (domestic origin, counter)
            //   2: TE, BD, VER, SEC           (domestic origin, web + bag)
            //   3: TE, CHK, BD, VER, EMG, SEC (intl origin, kiosk + bag)
            //   4: TE, CHK, VER, EMG, SEC     (intl origin, counter)
            //   5: CHK, VER, SEC              (D-D transfer, rules)
            //   6: VER, SEC                   (I-I transfer, emigration bypassed)
            //   7: VER                        (domestic arrival)
            //   8: VER                        (international arrival)
            //   9: VER, EMG, SEC              (I-I transfer, unspecified rules)
            Dictionary<int, List<FacilityType>> expectedVisits = new Dictionary<int, List<FacilityType>>
            {
                { 1, new List<FacilityType> { FacilityType.TerminalEntry, FacilityType.CheckIn, FacilityType.PassengerVerification, FacilityType.Security } },
                { 2, new List<FacilityType> { FacilityType.TerminalEntry, FacilityType.BagDrop, FacilityType.PassengerVerification, FacilityType.Security } },
                { 3, new List<FacilityType> { FacilityType.TerminalEntry, FacilityType.CheckIn, FacilityType.BagDrop, FacilityType.PassengerVerification, FacilityType.Emigration, FacilityType.Security } },
                { 4, new List<FacilityType> { FacilityType.TerminalEntry, FacilityType.CheckIn, FacilityType.PassengerVerification, FacilityType.Emigration, FacilityType.Security } },
                { 5, new List<FacilityType> { FacilityType.CheckIn, FacilityType.PassengerVerification, FacilityType.Security } },
                { 6, new List<FacilityType> { FacilityType.PassengerVerification, FacilityType.Security } },
                { 7, new List<FacilityType> { FacilityType.PassengerVerification } },
                { 8, new List<FacilityType> { FacilityType.PassengerVerification } },
                { 9, new List<FacilityType> { FacilityType.PassengerVerification, FacilityType.Emigration, FacilityType.Security } }
            };

            foreach (Passenger passenger in journey.Final)
            {
                List<FacilityType> expected = expectedVisits[passenger.Id];

                List<FacilityType> actual = passenger.History.Keys
                    .OrderBy(k => (int)k)
                    .ToList();

                Check(actual.Count == expected.Count &&
                      expected.All(actual.Contains),
                    $"Passenger {passenger.Id} visited exactly {DescribeStages(expected)} (got {DescribeStages(actual)})");
            }

            // ---- exact stage arithmetic, passenger by passenger ------------
            foreach (Passenger passenger in journey.Final)
            {
                if (passenger.History.TryGetValue(FacilityType.TerminalEntry, out FacilityRecord entry))
                {
                    double expectedService = ManualEntrySeconds / 60.0;

                    Check(Math.Abs(entry.StartServiceTime - passenger.TerminalEntryArrivalTime) < Tolerance &&
                          Math.Abs(entry.EndServiceTime - (entry.StartServiceTime + expectedService)) < Tolerance &&
                          Math.Abs(entry.WaitTime) < Tolerance &&
                          entry.PoolName == TerminalEntrySolver.ManualRoute,
                        $"Passenger {passenger.Id} Terminal Entry: start on arrival, {ManualEntrySeconds}s manual service, pool '{entry.PoolName}'");
                }
                else
                {
                    Check(!IsDeparturePassengerForTests(passenger) ||
                              passenger.Journey == PassengerJourney.Transfer,
                        $"Passenger {passenger.Id} has no Terminal Entry history only when bypassed (transfer)");
                }

                if (passenger.History.TryGetValue(FacilityType.CheckIn, out FacilityRecord checkIn))
                {
                    double expectedService = DefaultChannelAssigner.IsKioskChannel(passenger.Channel)
                        ? KioskSeconds / 60.0
                        : CounterSeconds / 60.0;

                    string expectedPool = DefaultChannelAssigner.IsKioskChannel(passenger.Channel)
                        ? "Kiosk"
                        : $"{passenger.Channel}|{passenger.DHPType}|{passenger.ProfileName}";

                    Check(Math.Abs(checkIn.ServiceSpan() - expectedService) < Tolerance &&
                          checkIn.PoolName == expectedPool,
                        $"Passenger {passenger.Id} Check-In: {expectedService * 60:0.#}s service from the '{expectedPool}' pool (got {checkIn.ServiceSpan() * 60:0.##}s, '{checkIn.PoolName}')");
                }
                else
                {
                    Check(!passenger.RequiresCheckIn,
                        $"Passenger {passenger.Id} has no Check-In history exactly when check-in is not required");
                }

                if (passenger.History.TryGetValue(FacilityType.BagDrop, out FacilityRecord bagDrop))
                {
                    Check(Math.Abs(bagDrop.ServiceSpan() - BagDropSeconds / 60.0) < Tolerance &&
                          bagDrop.PoolName == "Shared",
                        $"Passenger {passenger.Id} Bag Drop: {BagDropSeconds}s default service in the shared pool");
                }
                else
                {
                    Check(!passenger.RequiresSeparateBagDrop,
                        $"Passenger {passenger.Id} has no Bag Drop history exactly when separate bag drop is not required");
                }

                if (passenger.History.TryGetValue(FacilityType.PassengerVerification, out FacilityRecord verification))
                {
                    Check(Math.Abs(verification.ServiceSpan() - VerificationSeconds / 60.0) < Tolerance &&
                          verification.PoolName == "Verification",
                        $"Passenger {passenger.Id} Verification: {VerificationSeconds}s (300 pax/h) under the Verification pool identity");
                }

                if (passenger.History.TryGetValue(FacilityType.Emigration, out FacilityRecord emigration))
                {
                    Check(Math.Abs(emigration.ServiceSpan() - EmigrationEgateSeconds / 60.0) < Tolerance &&
                          emigration.PoolName == "EGate",
                        $"Passenger {passenger.Id} Emigration: {EmigrationEgateSeconds}s e-gate service from the Emigration 'EGate' key (not Security's)");
                }
                else
                {
                    bool shouldBypass = !IsDeparturePassengerForTests(passenger) ||
                                        passenger.PassengerType != PassengerType.International ||
                                        (passenger.TransferProcessing != null && !passenger.TransferProcessing.RequiresEmigration);

                    Check(shouldBypass,
                        $"Passenger {passenger.Id} has no Emigration history exactly when emigration is bypassed or not applicable");
                }

                if (passenger.History.TryGetValue(FacilityType.Security, out FacilityRecord security))
                {
                    Check(security.HasEntryStage &&
                          Math.Abs(security.EntrySpan() - SecurityEgateSeconds / 60.0) < Tolerance &&
                          Math.Abs(security.ServiceSpan() - SecurityAtrsSeconds / 60.0) < Tolerance &&
                          security.EntryPoolName.StartsWith("EGate", StringComparison.OrdinalIgnoreCase) &&
                          security.PoolName.StartsWith("ATRS", StringComparison.OrdinalIgnoreCase),
                        $"Passenger {passenger.Id} Security: {SecurityEgateSeconds}s e-gate + {SecurityAtrsSeconds}s ATRS with distinct pools ('{security.EntryPoolName}' / '{security.PoolName}')");

                    Check(Math.Abs(security.WaitTime - passenger.SecurityScreeningWait) < Tolerance &&
                          Math.Abs(security.EntryWaitTime - passenger.SecurityEntryWait) < Tolerance,
                        $"Passenger {passenger.Id} Security history separates the screening wait from the e-gate wait");

                    // Screening arrival = e-gate end + the e-gate walk.
                    Check(Math.Abs(security.ArrivalTime - (security.EntryEndServiceTime + SecurityWalkMinutes)) < Tolerance,
                        $"Passenger {passenger.Id} reaches ATRS {SecurityWalkMinutes} min after clearing the e-gate");
                }
                else
                {
                    Check(!IsDeparturePassengerForTests(passenger),
                        $"Passenger {passenger.Id} has no Security history only when not a departure");
                }
            }

            // ---- strict chronology across the connected chain ---------------
            foreach (Passenger passenger in journey.Final)
            {
                List<string> violations = PassengerNarrativeBuilder.ValidateChronology(passenger);

                Check(violations.Count == 0,
                    $"Passenger {passenger.Id} journey is strictly chronological" +
                    (violations.Count == 0 ? "" : ": " + string.Join("; ", violations)));
            }

            // ---- no stale state: skipped stages carry no visit --------------
            // The exact failure mode from the field: a narration that printed
            // Verification's 12 s record under Terminal Entry, Check-In and
            // Emigration for a passenger who never queued there.
            Passenger web = journey.Final.Single(p => p.Id == 2);

            Check(!web.History.ContainsKey(FacilityType.CheckIn) &&
                  !web.History.ContainsKey(FacilityType.Emigration),
                "Passenger 2 (web origin) carries no Check-In and no Emigration history");

            Passenger arrival = journey.Final.Single(p => p.Id == 7);

            Check(arrival.History.Count == 1 &&
                  arrival.History.ContainsKey(FacilityType.PassengerVerification),
                "Arriving passenger 7 carries only the Verification history");

            Check(arrival.StartServiceTime >= 0.0,
                "Mutable latest state is untouched by the history checks");
        }

        private static bool IsDeparturePassengerForTests(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticDeparture ||
                    passenger.DHPType == DHPType.InternationalDeparture);
        }

        private static string DescribeIds(List<Passenger> passengers)
        {
            return string.Join(", ", passengers.Select(p => p.Id).OrderBy(i => i));
        }

        private static string DescribeStages(IEnumerable<FacilityType> stages)
        {
            return string.Join(" -> ", stages);
        }

        // -------------------------------------------------------------
        // History-backed narration
        // -------------------------------------------------------------
        private static void RunJourneyNarrativeTests()
        {
            Console.WriteLine("== Journey narration (history-backed) ==");

            JourneyResult journey = RunGhxJourney(BuildJourneyCohort());

            // ---- skipped stages are rendered as absent, never with times ----
            foreach (Passenger passenger in journey.Final)
            {
                List<string> narration = PassengerNarrativeBuilder.BuildNarration(passenger);

                foreach (string stage in DepartureStageOrder)
                {
                    FacilityType facility = FacilityForStage(stage);

                    bool visited = passenger.History.ContainsKey(facility);
                    string line = narration.FirstOrDefault(l => l.Contains(stage) && l.TrimStart().StartsWith("["));

                    Check(line != null,
                        $"Passenger {passenger.Id} narration has a {stage} line");

                    if (!visited)
                    {
                        Check(line.Contains("not visited"),
                            $"Passenger {passenger.Id} narration marks {stage} as not visited");
                        Check(!line.Contains("service") && !line.Contains("served"),
                            $"Passenger {passenger.Id} narration quotes no times for the skipped {stage}");
                    }
                    else
                    {
                        Check(line.Contains("served"),
                            $"Passenger {passenger.Id} narration quotes the {stage} visit with times");
                    }
                }
            }

            // ---- content checks on specific passengers ----------------------
            List<string> webNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 2));

            Check(webNarrative.Any(l => l.Contains("Check-In") && l.Contains("not visited")),
                "Web passenger narration: Check-In reads as not visited");

            Check(webNarrative.Any(l => l.Contains("Bag Drop") && l.Contains("Shared")),
                "Web passenger narration: Bag Drop shows the shared pool");

            Check(webNarrative.Any(l => l.Contains("walk") && l.Contains("3.00 min")),
                "Web passenger narration: the terminal-entry walk is visible data");

            List<string> counterNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 1));

            Check(counterNarrative.Any(l => l.Contains("Check-In") && l.Contains("Counter|DomesticDeparture|Domestic Economy")),
                "Counter passenger narration names the exact check-in pool identity");

            Check(!counterNarrative.Any(l => l.Contains("Emigration") && l.Contains("served")),
                "Domestic passenger narration never narrates an Emigration visit");

            List<string> arrivalNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 7));

            int arrivalVisits = arrivalNarrative.Count(l => l.Contains("served"));
            Check(arrivalVisits == 1 && arrivalNarrative.Any(l => l.Contains("Verification") && l.Contains("served")),
                "Arrival passenger narration shows exactly one visit: Verification");

            // ---- Security series: both stages appear, in order --------------
            List<string> securityNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 1));

            int eGateLine = securityNarrative.FindIndex(l => l.Contains("Security") && l.Contains("EGate"));
            int atrsLine = securityNarrative.FindIndex(l => l.Contains("Security") && l.Contains("ATRS"));

            Check(eGateLine >= 0 && atrsLine > eGateLine,
                "Security narration shows the e-gate stage before the ATRS screening stage");

            // ---- the stale-state trap ---------------------------------------
            // A passenger who only ever visited Verification must not have any
            // other stage quoting Verification's times.
            Passenger synthetic = new Passenger
            {
                Id = 99,
                ProfileName = "Solo",
                DHPType = DHPType.DomesticDeparture,
                Journey = PassengerJourney.Origin,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            synthetic.History[FacilityType.PassengerVerification] = new FacilityRecord
            {
                ArrivalTime = 183.88,
                StartServiceTime = 183.88,
                EndServiceTime = 184.08,
                WaitTime = 0.0,
                PoolName = "Verification"
            };

            List<string> staleCheck = PassengerNarrativeBuilder.BuildNarration(synthetic);

            int linesQuotingVerificationTime = staleCheck
                .Count(l => l.Contains("183.88") || l.Contains("184.08"));

            Check(linesQuotingVerificationTime == 1,
                $"Only the Verification line quotes Verification's times ({linesQuotingVerificationTime} lines do)");

            Check(staleCheck.Where(l => l.Contains("not visited")).Count() >= 5,
                "Every unvisited stage is explicitly marked in the narration");

            // ---- the narration text the canvas component will output --------
            // Passenger Details emits one multi-line string per passenger, so
            // the text on the canvas is exactly the tested narration.
            string webText = PassengerNarrativeBuilder.ToText(
                journey.Final.Single(p => p.Id == 2));

            List<string> webLines = webText.Split('\n').ToList();

            Check(webLines.SequenceEqual(PassengerNarrativeBuilder.BuildNarration(
                    journey.Final.Single(p => p.Id == 2))),
                "ToText joins the narration lines one to one");

            Check(webText.StartsWith("Passenger 2:", StringComparison.Ordinal),
                "The narration text opens with the passenger header");

            Check(webText.Contains("Check-In") && webText.Contains("not visited"),
                "The narration text marks the skipped check-in stage");

            // ---- richer narration: reasons, expectations and context --------
            List<string> transferNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 9));

            Check(transferNarrative.Any(l => l.Contains("Terminal Entry") && l.Contains("airside")),
                "Transfer narration explains the terminal-entry bypass (already airside)");

            Check(transferNarrative.Any(l => l.Contains("Check-In") && l.Contains("no queue")),
                "Web-channel transfer narration explains the skipped check-in");

            Check(transferNarrative.Any(l => l.Contains("Bag Drop") && l.Contains("baggage")),
                "Bag-free transfer narration explains the skipped bag drop");

            Check(transferNarrative.Any(l => l.Contains("Emigration") && l.Contains("served")),
                "Unspecified international transfer still narrates its emigration visit");

            List<string> ruleBypassNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 6));

            Check(ruleBypassNarrative.Any(l => l.Contains("Emigration") && l.Contains("scenario rule")),
                "A rule-based emigration bypass is named as such, not as a mystery gap");

            List<string> domArrivalNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 7));

            Check(domArrivalNarrative.Any(l => l.Contains("Terminal Entry") && l.Contains("arrival passengers")) &&
                  domArrivalNarrative.Any(l => l.Contains("Security") && l.Contains("arrival passengers")),
                "Arrival narration marks departure facilities as not applicable");

            Check(!domArrivalNarrative.Any(l => l.Contains("Immigration")),
                "A domestic arrival is not offered an immigration stage");

            List<string> intlArrivalNarrative = PassengerNarrativeBuilder.BuildNarration(
                journey.Final.Single(p => p.Id == 8));

            Check(intlArrivalNarrative.Any(l => l.Contains("Immigration") && l.Contains("expected but missing")),
                "An international arrival that skipped immigration is flagged as expected but missing");

            // The field-reported case: a kiosk transfer with a checked bag that
            // only ever carries a Verification record. The narration must say
            // WHICH expected stages are missing, not just "not visited".
            Passenger incomplete = new Passenger
            {
                Id = 1345,
                ProfileName = "Domestic Economy",
                Channel = "Kiosk",
                HasCheckedBag = true,
                DHPType = DHPType.DomesticDeparture,
                Journey = PassengerJourney.Transfer,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            incomplete.History[FacilityType.PassengerVerification] = new FacilityRecord
            {
                ArrivalTime = -48.16,
                StartServiceTime = -48.16,
                EndServiceTime = -47.96,
                PoolName = "Verification"
            };

            List<string> incompleteNarrative = PassengerNarrativeBuilder.BuildNarration(incomplete);

            Check(incompleteNarrative.Any(l => l.Contains("Terminal Entry") && l.Contains("airside")),
                "The incomplete journey narration keeps the legitimate terminal-entry bypass");

            int expectedMissing = incompleteNarrative.Count(l => l.Contains("expected but missing"));

            Check(expectedMissing >= 3,
                $"Check-In, Bag Drop and Security are flagged as expected but missing ({expectedMissing} lines)");

            Check(!incompleteNarrative.Contains("min min"),
                "No narration line doubles the 'min' unit");

            // ---- context: schedule-mode passengers ---------------------------
            Passenger scheduled = new Passenger
            {
                Id = 2001,
                ProfileName = "Domestic Economy",
                Channel = "Counter",
                DHPType = DHPType.DomesticDeparture,
                Journey = PassengerJourney.Origin,
                FlightLegId = "L-77",
                FlightNumber = "XX 123",
                Airline = "TestAir",
                ScheduledDepartureTime = 300.0,
                CheckInOpenTime = 120.0,
                CheckInLastAdmissionTime = 280.0,
                TerminalEntryArrivalTime = 180.0,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            string scheduledText = PassengerNarrativeBuilder.ToText(scheduled);

            Check(scheduledText.Contains("XX 123") && scheduledText.Contains("departs at 300.00 min"),
                "Schedule-mode narration names the flight and its departure time");

            Check(scheduledText.Contains("checkpoint at 180.00 min"),
                "Schedule-mode narration shows the terminal-entry checkpoint arrival");

            Check(scheduledText.Contains("opens 120.00") && scheduledText.Contains("last admission 280.00"),
                "Schedule-mode narration shows the check-in window");

            // ---- context: PHP windowed passengers ----------------------------
            Passenger windowed = new Passenger
            {
                Id = 2002,
                ProfileName = "Domestic Economy",
                Channel = "Kiosk",
                DHPType = DHPType.DomesticDeparture,
                Journey = PassengerJourney.Origin,
                DemandStepIndex = 2,
                IsDesignStep = true,
                WaveId = 4,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            string windowedText = PassengerNarrativeBuilder.ToText(windowed);

            Check(windowedText.Contains("demand window step 2") && windowedText.Contains("design hour"),
                "PHP-mode narration names the demand window step and design-hour tag");

            Check(windowedText.Contains("wave 4"),
                "PHP-mode narration names the wave");

            // ---- context: a late schedule passenger --------------------------
            scheduled.IsLateForCheckIn = true;
            Check(PassengerNarrativeBuilder.ToText(scheduled).Contains("LATE"),
                "A late passenger is flagged in the narration context");

            // ---- chronology validator catches broken records -----------------
            Passenger broken = new Passenger
            {
                Id = 100,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            broken.History[FacilityType.CheckIn] = new FacilityRecord
            {
                ArrivalTime = 10.0,
                StartServiceTime = 9.0,   // starts before arriving
                EndServiceTime = 8.0,     // ends before starting
                WaitTime = -1.0
            };
            broken.History[FacilityType.Security] = new FacilityRecord
            {
                ArrivalTime = 5.0,        // earlier than the check-in end
                StartServiceTime = 5.0,
                EndServiceTime = 6.0
            };

            List<string> violations = PassengerNarrativeBuilder.ValidateChronology(broken);

            Check(violations.Count >= 3,
                $"The validator flags negative-order and overlapping records ({violations.Count} violations)");

            // Clean history stays clean.
            Passenger clean = new Passenger
            {
                Id = 101,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            clean.History[FacilityType.CheckIn] = new FacilityRecord
            {
                ArrivalTime = 10.0,
                StartServiceTime = 10.5,
                EndServiceTime = 11.0,
                WaitTime = 0.5
            };
            clean.History[FacilityType.Security] = new FacilityRecord
            {
                ArrivalTime = 11.0,
                StartServiceTime = 11.0,
                EndServiceTime = 11.4
            };

            Check(PassengerNarrativeBuilder.ValidateChronology(clean).Count == 0,
                "A consistent history validates cleanly");
        }

        /// <summary>Maps a canonical narration stage label to its history key.</summary>
        private static FacilityType FacilityForStage(string stage)
        {
            switch (stage)
            {
                case "Terminal Entry": return FacilityType.TerminalEntry;
                case "Check-In": return FacilityType.CheckIn;
                case "Bag Drop": return FacilityType.BagDrop;
                case "Verification": return FacilityType.PassengerVerification;
                case "Emigration": return FacilityType.Emigration;
                case "Security": return FacilityType.Security;
                default:
                    throw new ArgumentException($"Unknown stage '{stage}'");
            }
        }

        // -------------------------------------------------------------
        // Determinism: the same seed reproduces the same journey exactly.
        // -------------------------------------------------------------
        private static void RunJourneyDeterminismTests()
        {
            Console.WriteLine("== Journey determinism ==");

            JourneyResult first = RunGhxJourney(BuildJourneyCohort());
            JourneyResult second = RunGhxJourney(BuildJourneyCohort());

            bool identical = true;

            for (int i = 0; i < first.Final.Count && identical; i++)
            {
                Passenger a = first.Final.First(p => p.Id == second.Final[i].Id);
                Passenger b = second.Final[i];

                if (!HistoriesMatch(a, b))
                    identical = false;
            }

            Check(identical && first.Final.Count == second.Final.Count,
                "The same seeds reproduce identical per-passenger histories");
        }

        private static bool HistoriesMatch(Passenger a, Passenger b)
        {
            if (a.History.Count != b.History.Count)
                return false;

            foreach (KeyValuePair<FacilityType, FacilityRecord> pair in a.History)
            {
                if (!b.History.TryGetValue(pair.Key, out FacilityRecord other))
                    return false;

                FacilityRecord record = pair.Value;

                if (Math.Abs(record.ArrivalTime - other.ArrivalTime) > Tolerance ||
                    Math.Abs(record.StartServiceTime - other.StartServiceTime) > Tolerance ||
                    Math.Abs(record.EndServiceTime - other.EndServiceTime) > Tolerance ||
                    Math.Abs(record.WaitTime - other.WaitTime) > Tolerance ||
                    record.AssignedResource != other.AssignedResource ||
                    !string.Equals(record.PoolName, other.PoolName, StringComparison.OrdinalIgnoreCase) ||
                    record.HasEntryStage != other.HasEntryStage ||
                    Math.Abs(record.EntryWaitTime - other.EntryWaitTime) > Tolerance ||
                    !string.Equals(record.EntryPoolName, other.EntryPoolName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        // -------------------------------------------------------------
        // Congested security: real waits must reconcile with the history
        // and with the narration built from it.
        // -------------------------------------------------------------
        private static void RunJourneyCongestedSecurityTests()
        {
            Console.WriteLine("== Congested security reconciliation ==");

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 30; i++)
            {
                passengers.Add(new Passenger
                {
                    Id = 2000 + i,
                    Channel = "Counter",
                    DHPType = DHPType.DomesticDeparture,
                    PassengerType = PassengerType.Domestic,
                    ProfileName = "Domestic Economy",
                    ArrivalTime = i * 0.1,
                    QueueArrivalTime = i * 0.1
                });
            }

            SecurityResults congested = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = passengers,
                ServiceTimeProvider = BuildJourneyServiceTimeMap(),
                UseEGateSeries = true,
                EGateToAtrsWalkMinutes = SecurityWalkMinutes,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Facility = "Security", Channel = "EGate", PhysicalResources = 1, TargetWait95 = 2.0, Range = new ResourceSearchRange(1, 6) },
                    new PoolPlan { Facility = "Security", Channel = "ATRS", PhysicalResources = 1, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 6) }
                },
                OptimizeResources = false,
                StaffingPercentage = 100.0,
                Seed = 21
            });

            List<Passenger> screened = congested.QueueResults.ProcessedPassengers;

            Check(screened.Count == 30 && congested.QueueResults.AverageWait > 0.0,
                $"A single gate and lane produce real queues ({screened.Count} screened, avg wait {congested.QueueResults.AverageWait:0.###} min)");

            foreach (Passenger passenger in screened)
            {
                FacilityRecord record = passenger.History[FacilityType.Security];

                Check(Math.Abs(record.EntryWaitTime - passenger.SecurityEntryWait) < Tolerance &&
                      Math.Abs(record.WaitTime - passenger.SecurityScreeningWait) < Tolerance &&
                      Math.Abs(passenger.WaitTime - (passenger.SecurityEntryWait + passenger.SecurityScreeningWait)) < Tolerance,
                    $"Passenger {passenger.Id}: headline wait is the sum of both stage queues");

                Check(record.EntryStartServiceTime - record.EntryArrivalTime >= -Tolerance &&
                      record.StartServiceTime - record.ArrivalTime >= -Tolerance &&
                      Math.Abs(record.ArrivalTime - (record.EntryEndServiceTime + SecurityWalkMinutes)) < Tolerance,
                    $"Passenger {passenger.Id}: e-gate and ATRS waits fit between the recorded times");

                List<string> violations = PassengerNarrativeBuilder.ValidateChronology(passenger);

                Check(violations.Count == 0,
                    $"Passenger {passenger.Id} congested journey validates chronologically" +
                    (violations.Count == 0 ? "" : ": " + string.Join("; ", violations)));

                List<string> narration = PassengerNarrativeBuilder.BuildNarration(passenger);

                string atrsLine = narration.FirstOrDefault(l => l.Contains("ATRS"));

                Check(atrsLine != null && atrsLine.Contains(FormatWait(record.WaitTime)),
                    $"Passenger {passenger.Id} narration quotes the ATRS wait from the history ({FormatWait(record.WaitTime)})");
            }
        }

        private static string FormatWait(double minutes)
        {
            return (minutes * 60.0).ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static double ServiceSpan(this FacilityRecord record)
        {
            return record.EndServiceTime - record.StartServiceTime;
        }

        private static double EntrySpan(this FacilityRecord record)
        {
            return record.EntryEndServiceTime - record.EntryStartServiceTime;
        }
    }
}
