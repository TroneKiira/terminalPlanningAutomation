using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Area;
using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Emigration;
using AirportPlanning.Facilities.Security;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Sizers;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static int _failures;

        public static int Main()
        {
            RunTransferDemandTests();
            RunTransferRoutingTests();
            RunTransferFacilityTests();
            RunTransferServiceConsistencyTests();
            RunCustomsTests();
            RunCountsAndWarmUp();
            RunWaveOffRespectsShowUp();
            RunDepartures();
            RunDesignMetricsAndOptimizer();
            RunProfileValidation();
            RunDeplaningProfileCategories();
            RunShowUpIntegration();
            RunProfileIdentityAndChannels();
            RunServiceTimeMap();
            RunChannelFiltering();
            RunClassMetrics();
            RunOptimizerDeterminismWithProvider();
            RunAnalyticComparison();
            RunNegativeArrivalQueue();
            RunResourceDomainBounds();
            RunPerChannelPools();
            RunCheckInRoutedPoolSegregation();
            RunCheckInSeparateBagDropChannel();
            RunBagDropSegmentPools();
            RunPoolPlanArchitecture();
            RunFacilityScoping();
            RunServiceTimeKeyLevels();
            RunFacilityTagScoping();
            RunSecuritySeries();
            RunProcessorTimeline();
            RunFacilityArea();
            RunFacilityAreaWiring();
            RunGenericFacilitySizing();
            RunGateSizing();
            RunFlightScheduleImport();
            RunLargeDemandTiming();
            RunPoissonSampler();
            RunLargeDemandPassengerGenerator();
            RunOutputPopulationMerge();
            RunArrivalProfileOptional();
            RunLegacyNullArrivalProfile();
            RunShowUpCurves();
            RunShowUpCurveGeneration();
            RunScheduleGeneratorTiming();
            RunScheduleDemandPeaksWithoutDhp();
            RunScheduleGeneratorCheckInAndArrivals();
            RunScheduleCheckInLastAdmission();
            RunScheduleFallbacksAndRules();
            RunScheduleArrivalPeak();
            RunFlightHourDemand();
            RunCheckInOpeningGate();

            RunDHPTypeTagging();
            RunPerTypeDemandCounts();
            RunFourGroupTotal();
            RunDHPValidation();
            RunPassengerGroupValidation();
            RunGroupIndependence();
            RunDesignHourPerType();
            RunWarmUpPerType();
            RunShowUpPerType();
            RunOptionalArrivalGroups();
            RunLegacyDHPTypeTagging();
            RunClonePreservesTags();
            RunDepartureOnlyRouting();
            RunShowUpCacheInvalidation();
            RunWholeRunOptimizationTarget();
            RunInfeasibleTargetWarning();
            RunTerminalEntryQueueAndRouting();
            RunTerminalEntryScheduleTiming();
            RunTerminalEntryTransfers();
            RunTerminalEntryOptimizer();
            RunTerminalEntryPoolPlans();
            RunTerminalEntrySizing();
            RunModelDiagnosticsTests();
            RunPassengerFilterSegregationTests();
            RunGhxJourneyIntegrationTests();
            RunJourneyNarrativeTests();
            RunJourneyDeterminismTests();
            RunJourneyCongestedSecurityTests();
            RunGhxDefinitionContractTests();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "ALL TESTS PASSED"
                : $"{_failures} TEST(S) FAILED");

            return _failures == 0 ? 0 : 1;
        }

        // -------------------------------------------------------------
        // 1. Exact step counts and warm-up tagging
        // -------------------------------------------------------------
        private static void RunCountsAndWarmUp()
        {
            Console.WriteLine("== Counts / warm-up ==");

            List<PassengerGroup> groups = BuildGroups(
                dhd: 4000, factors: new[] { 40.0, 60.0, 100.0, 90.0, 70.0 },
                designIndex: 2, dep: 100.0, dom: 100.0,
                warmUp: true, warmMinutes: 60, warmFactor: 50.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), seed: 123);

            for (int i = 0; i < groups[0].DHP.StepCount; i++)
            {
                int actual = result.Passengers.Count(p => p.DemandStepIndex == i);
                int expected = TotalStepDemand(groups, i);
                Check(actual == expected, $"Step {i} count {actual} == expected {expected}");
            }

            int warmActual = result.Passengers.Count(p => p.IsWarmUp);
            int warmExpected = TotalWarmUpDemand(groups);
            Check(warmActual == warmExpected, $"Warm-up count {warmActual} == expected {warmExpected}");

            int designActual = result.Passengers.Count(p => p.IsDesignStep);
            int designExpected = TotalStepDemand(groups, 2);
            Check(designActual == designExpected, $"Design-step tagged count {designActual} == {designExpected}");

            bool designOnlyStep2 = result.Passengers
                .Where(p => p.IsDesignStep)
                .All(p => p.DemandStepIndex == 2);
            Check(designOnlyStep2, "All design-tagged passengers belong to step 2");
        }

        // -------------------------------------------------------------
        // 2. Wave coverage Off leaves the show-up profile in charge
        // -------------------------------------------------------------
        private static void RunWaveOffRespectsShowUp()
        {
            Console.WriteLine("== Wave coverage Off / show-up respected ==");

            List<PassengerGroup> groups = BuildGroups(
                dhd: 4000, factors: new[] { 50.0, 80.0, 100.0, 90.0, 60.0 },
                designIndex: 2, dep: 100.0, dom: 100.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            WaveSettings off = new WaveSettings
            {
                ScheduleVariabilityMinutes = 1.0,
                WalkDispersionMinutes = 2.0,
                Coverage = WaveCoverage.Off
            };

            List<string> warnings = new List<string>();

            PassengerGenerationResult curved = GenerateCurved(
                groups, off, 7, ShowUpProfile.Leisure(), warnings.Add);

            Check(warnings.Count == 0, $"No warnings on a clean run ({warnings.Count})");

            for (int i = 0; i < groups[0].DHP.StepCount; i++)
            {
                int actual = curved.Passengers.Count(p => p.DemandStepIndex == i);
                int expected = TotalStepDemand(groups, i);
                Check(actual == expected, $"Step {i} total {actual} == expected {expected}");
            }

            Check(curved.Passengers.All(p => p.WaveId < 0),
                "Coverage Off never tags a wave id");

            int step0Demand = TotalStepDemand(groups, 0);
            int firstBand = curved.Passengers.Count(
                p => p.DemandStepIndex == 0 && p.ArrivalTime < 6.0);
            int expectedFirst = (int)Math.Round(step0Demand * 0.26);

            Check(Math.Abs(firstBand - expectedFirst) <= step0Demand * 0.02,
                $"Leisure first band {firstBand} ~= {expectedFirst} (26% of {step0Demand})");

            PassengerGenerationResult again = GenerateCurved(
                groups, off, 7, ShowUpProfile.Leisure(), warnings.Add);

            bool repeatable = curved.Passengers.Count == again.Passengers.Count;

            for (int i = 0; repeatable && i < curved.Passengers.Count; i++)
            {
                if (curved.Passengers[i].ArrivalTime != again.Passengers[i].ArrivalTime)
                    repeatable = false;
            }

            Check(repeatable, "Fixed seed -> identical arrivals");
        }

        // -------------------------------------------------------------
        // 3. Per-passenger departures are after arrival
        // -------------------------------------------------------------
        private static void RunDepartures()
        {
            Console.WriteLine("== Departures ==");

            List<PassengerGroup> groups = BuildGroups(
                dhd: 1000, factors: new[] { 60.0, 100.0, 80.0, 60.0, 40.0 },
                designIndex: 1, dep: 80.0, dom: 60.0,
                warmUp: true, warmMinutes: 60, warmFactor: 50.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), seed: 55);

            bool allAfter = result.Passengers.All(p => p.ScheduledDepartureTime > p.ArrivalTime);
            Check(allAfter, "Every scheduled departure is after arrival");

            double minAdvance = result.Passengers.Min(p => p.ScheduledDepartureTime - p.ArrivalTime);
            Check(minAdvance >= 59.0, $"Minimum advance {Math.Round(minAdvance, 1)} >= 60");
        }

        // -------------------------------------------------------------
        // 4. Design-step metrics, warm-up exclusion and optimizer
        // -------------------------------------------------------------
        private static void RunDesignMetricsAndOptimizer()
        {
            Console.WriteLine("== Design-step metrics / optimizer ==");

            List<PassengerGroup> groups = BuildGroups(
                dhd: 200, factors: new[] { 40.0, 60.0, 100.0, 140.0, 60.0 },
                designIndex: 2, dep: 100.0, dom: 100.0,
                warmUp: true, warmMinutes: 60, warmFactor: 100.0);

            PassengerGenerationResult generation = Generate(groups, new WaveSettings(), seed: 999);

            CheckInInputs input = new CheckInInputs
            {
                Passengers = generation.Passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.5 },
                PhysicalResources = new List<int> { 10 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 1,
                StaffingPercentage = 100.0
            };

            CheckInSolver solver = new CheckInSolver();
            CheckInResults checkIn = solver.Solve(input);
            QueueResults qr = checkIn.QueueResults;

            Check(qr.DesignStepIndex == 2, $"DesignStepIndex {qr.DesignStepIndex} == 2");
            Check(qr.IntervalMetrics.Count == 5, $"IntervalMetrics count {qr.IntervalMetrics.Count} == 5");

            int stepSum = qr.IntervalMetrics.Sum(m => m.ArrivalCount);
            int expectedStepSum = 0;

            for (int i = 0; i < groups[0].DHP.StepCount; i++)
                expectedStepSum += TotalStepDemand(groups, i);

            Check(stepSum == expectedStepSum,
                $"Interval arrival sum {stepSum} == non-warm summary {expectedStepSum}");

            bool warmUpExcluded = qr.IntervalMetrics.All(m => m.StepIndex >= 0);
            Check(warmUpExcluded, "No warm-up steps in IntervalMetrics");

            double designWait = qr.GetEvaluationWait95();
            IntervalQueueMetrics designMetric = qr.IntervalMetrics.FirstOrDefault(m => m.StepIndex == 2);
            Check(designMetric != null && Math.Abs(designWait - designMetric.Wait95) < 1e-6,
                "GetEvaluationWait95 returns the design-step value");

            Console.WriteLine(
                $"   design Wait95 = {Math.Round(designWait, 2)} | whole-run Wait95 = {Math.Round(qr.Wait95, 2)} | " +
                $"peak queue step = {qr.PeakQueueStepIndex} | peak time = {Math.Round(qr.PeakQueueTime, 1)}");
        }

        // -------------------------------------------------------------
        // 5. Profile shape validation
        // -------------------------------------------------------------
        private static void RunProfileValidation()
        {
            Console.WriteLine("== Profile validation ==");

            Check(DeplaningProfile.Profile1().GetValidationErrors().Count == 0, "deplaning Profile 1 valid");
            Check(DeplaningProfile.Profile2().GetValidationErrors().Count == 0, "deplaning Profile 2 valid");
            Check(DeplaningProfile.Profile3().GetValidationErrors().Count == 0, "deplaning Profile 3 valid");

            Check(new DeplaningProfile("bad", 0.60, 0.50).GetValidationErrors().Count > 0,
                "deplaning W15 > W30 is rejected");
            Check(new DeplaningProfile("bad", 0.20, 0.45).GetValidationErrors().Count > 0,
                "deplaning W30 below the 0.50 floor is rejected");
            Check(new DeplaningProfile("bad", 0.10, 0.30).GetValidationErrors().Count > 0,
                "deplaning W30 > 2 x W15 is rejected");
            Check(new DeplaningProfile("bad", 0.30, 0.50, 0.90).GetValidationErrors().Count > 0,
                "deplaning W60 not 100% is rejected");

            Check(ShowUpProfile.Leisure().GetValidationErrors().Count == 0,
                "show-up Leisure valid");
            Check(ShowUpProfile.Business().GetValidationErrors().Count == 0,
                "show-up Business valid");
            Check(ShowUpProfile.Mix().GetValidationErrors().Count == 0,
                "show-up Mix valid");

            Check(new ShowUpProfile("bad", 0.60, 0.50).GetValidationErrors().Count > 0,
                "show-up W15 > W30 is rejected");
            Check(new ShowUpProfile("bad", 0.30, 0.50, 0.90).GetValidationErrors().Count > 0,
                "show-up W60 not 100% is rejected");
            Check(new ShowUpProfile("bad", 1.20, 0.90).GetValidationErrors().Count > 0,
                "show-up W15 above 100% is rejected");
            Check(new ShowUpProfile("bad", 0.30, 1.40).GetValidationErrors().Count > 0,
                "show-up W30 above 100% is rejected");

            ShowUpProfile shortBands = ShowUpProfile.FromBands("short", new List<double> { 0.5, 0.5 });
            Check(shortBands.GetValidationErrors().Count == 0,
                "a two-value band list is resampled rather than rejected");

            ShowUpProfile broken = ShowUpProfile.FromBands("broken", new List<double> { 0.5, 0.5 });
            broken.Bands = new List<double> { 0.5, 0.5 };
            Check(broken.GetValidationErrors().Count > 0,
                "a profile that is not ten bands is rejected");
        }

        // -------------------------------------------------------------
        // 6. Show-up profile drives arrivals within the hour
        // -------------------------------------------------------------
        private static void RunShowUpIntegration()
        {
            Console.WriteLine("== Show-up profile integration ==");

            List<PassengerGroup> hour = BuildSimpleGroups(10000, 1, new[] { 100.0 }, 0);
            int stepCount = TotalStepDemand(hour, 0);

            PassengerGenerationResult leisure = Generate(
                hour, new WaveSettings(), 42, ShowUpProfile.Leisure());

            int firstBand = leisure.Passengers.Count(p => p.ArrivalTime < 6.0);
            int expectedFirst = (int)Math.Round(stepCount * 0.26);

            Check(Math.Abs(firstBand - expectedFirst) <= stepCount * 0.01,
                $"Leisure first band {firstBand} ~= {expectedFirst} (26% of {stepCount})");

            int lastBand = leisure.Passengers.Count(p => p.ArrivalTime >= 54.0);
            int expectedLast = (int)Math.Round(stepCount * 0.02);

            Check(Math.Abs(lastBand - expectedLast) <= stepCount * 0.01,
                $"Leisure last band {lastBand} ~= {expectedLast} (2% of {stepCount})");

            int last15 = leisure.Passengers.Count(p => p.ArrivalTime >= 45.0);
            Check(Math.Abs(last15 / (double)stepCount - 0.125) < 0.015,
                $"Leisure last-15-minute share {last15 / (double)stepCount:P1} ~= 12.5%");

            PassengerGenerationResult business = Generate(
                hour, new WaveSettings(), 42, ShowUpProfile.Business());

            int businessFirst = business.Passengers.Count(p => p.ArrivalTime < 6.0);

            Check(businessFirst < firstBand / 3.0,
                $"Business first band {businessFirst} << Leisure {firstBand}");

            int businessLast15 = business.Passengers.Count(p => p.ArrivalTime >= 45.0);
            Check(Math.Abs(businessLast15 / (double)stepCount - 0.205) < 0.015,
                $"Business last-15-minute share {businessLast15 / (double)stepCount:P1} ~= 20.5%");

            PassengerGenerationResult bare = Generate(hour, new WaveSettings(), 42, null);

            int[] even = new int[10];

            foreach (Passenger p in bare.Passengers)
            {
                int band = (int)Math.Floor(p.ArrivalTime / 6.0);

                if (band >= 0 && band < 10)
                    even[band]++;
            }

            int maxBand = even.Max();
            int minBand = even.Min();

            Check(maxBand - minBand <= stepCount * 0.02,
                $"unwired fallback is even across bands (min {minBand}, max {maxBand})");
        }

        // -------------------------------------------------------------
        // 7. Profile identity and channel assignment
        // -------------------------------------------------------------
        private static void RunProfileIdentityAndChannels()
        {
            Console.WriteLine("== Identity / channels ==");

            List<PassengerGroup> groupsCounter = BuildGroupsWithProfiles(
                1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 0.0, 0.5) },
                new List<PassengerProfile>());

            PassengerGenerationResult rCounter = GenerateWithProfiles(groupsCounter, 5, null);

            Check(rCounter.Passengers.All(p => p.ProfileName == "D"), "ProfileName is propagated");
            Check(rCounter.Passengers.All(p => p.Channel == DefaultChannelAssigner.CounterChannel),
                "Default routing: not online -> Counter");

            PassengerGenerationResult rBag = GenerateWithProfiles(
                BuildGroupsWithProfiles(1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                    new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 1.0, 1.0) },
                    new List<PassengerProfile>()), 5, null);

            Check(rBag.Passengers.All(p => p.Channel == DefaultChannelAssigner.WebChannel),
                "Default routing: online + bag -> Web, with Bag Drop as a separate stage");
            Check(rBag.Passengers.All(p => !p.RequiresCheckIn && p.RequiresSeparateBagDrop),
                "An online bag carrier has no Check-In queue but does use the separate Bag Drop stage");

            PassengerGenerationResult rWeb = GenerateWithProfiles(
                BuildGroupsWithProfiles(1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                    new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 1.0, 0.0) },
                    new List<PassengerProfile>()), 5, null);

            Check(rWeb.Passengers.All(p => p.Channel == DefaultChannelAssigner.WebChannel),
                "Default routing: online, no bag -> Web");

            ProfileChannelMap map = new ProfileChannelMap();
            map.Entries.Add(new ProfileChannelEntry
            {
                ProfileName = "D",
                Shares = new List<ChannelShare>
                {
                    new ChannelShare { Channel = "Counter", Share = 0.6 },
                    new ChannelShare { Channel = "Kiosk", Share = 0.4 }
                }
            });

            PassengerGenerationResult rSplit = GenerateWithProfiles(
                BuildGroupsWithProfiles(1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                    new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 0.0, 0.0) },
                    new List<PassengerProfile>()), 11, map);

            int counter = rSplit.Passengers.Count(p => p.Channel == "Counter");
            int kiosk = rSplit.Passengers.Count(p => p.Channel == "Kiosk");
            int total = rSplit.Passengers.Count;
            double counterShare = total > 0 ? (double)counter / total : 0.0;

            Check(counter + kiosk == total, "Every passenger gets a mapped channel");
            Check(Math.Abs(counterShare - 0.6) < 0.05, $"Counter share {Math.Round(counterShare, 3)} ~ 0.60");

            // A legacy BagDrop share no longer creates a physical primary
            // channel: it is normalised to Web. The separate Bag Drop stage is
            // derived from the checked-bag flag instead.
            ProfileChannelMap bagMap = new ProfileChannelMap();
            bagMap.Entries.Add(new ProfileChannelEntry
            {
                ProfileName = "D",
                Shares = new List<ChannelShare>
                {
                    new ChannelShare { Channel = "Counter", Share = 0.6 },
                    new ChannelShare { Channel = "Kiosk", Share = 0.4 },
                    new ChannelShare { Channel = "Bag-Drop", Share = 0.2 }
                }
            });

            Check(bagMap.BagDropRoutingChannel == "Bag-Drop",
                "The legacy bag-drop name is still discoverable");
            Check(bagMap.GetChannels().Contains("Web") &&
                  !bagMap.GetChannels().Contains("Bag-Drop"),
                "A legacy BagDrop share is exposed as Web to the facilities");

            PassengerGenerationResult rBagEnforced = GenerateWithProfiles(
                BuildGroupsWithProfiles(1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                    new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 1.0, 1.0) },
                    new List<PassengerProfile>()), 13, bagMap);

            Check(rBagEnforced.Passengers.All(p => p.Channel != "Bag-Drop"),
                "No passenger is assigned Bag Drop as a primary channel");
            Check(rBagEnforced.Passengers.All(p =>
                    p.Channel == "Counter" || p.Channel == "Kiosk" || p.Channel == "Web"),
                "Every passenger keeps a Counter, Kiosk or Web primary channel");
            Check(rBagEnforced.Passengers
                    .Where(p => p.Channel == "Web")
                    .All(p => p.RequiresSeparateBagDrop),
                "A Web-routed bag carrier is eligible for the separate Bag Drop stage");

            // A transfer rule requiring bag drop marks the separate stage but
            // must not overwrite the primary channel.
            Passenger transferBag = new Passenger
            {
                ProfileName = "D",
                DHPType = DHPType.DomesticDeparture,
                Journey = PassengerJourney.Transfer,
                UsesOnlineCheckIn = false,
                HasCheckedBag = false,
                TransferProcessing = new TransferScenarioRules
                {
                    Scenario = TransferScenario.DomesticToDomestic,
                    HasProcessingRules = true,
                    RequiresCheckIn = true,
                    RequiresBagDrop = true
                }
            };
            string transferChannel = bagMap.AssignChannel(transferBag, new Random(1));
            Check(transferChannel == "Counter" ||
                  transferChannel == "Kiosk" ||
                  transferChannel == "Web",
                $"A transfer rule requiring bag drop keeps a primary channel ({transferChannel})");
            Check(bagMap.AssignChannel(transferBag, new Random(1)) == transferChannel,
                "Share-driven assignment is stable for the same seed");
            transferBag.Channel = transferChannel;
            Check(transferBag.RequiresSeparateBagDrop,
                "A transfer rule requiring bag drop uses the separate Bag Drop stage");

            // Disabling the legacy enforcement changes nothing now.
            bagMap.EnforceBagDropRouting = false;
            PassengerGenerationResult rBagByShares = GenerateWithProfiles(
                BuildGroupsWithProfiles(1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                    new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 1.0, 1.0) },
                    new List<PassengerProfile>()), 13, bagMap);
            Check(!rBagByShares.Passengers.Any(p => p.Channel == "Bag-Drop"),
                "The legacy enforcement flag never creates a BagDrop primary channel");
            bagMap.EnforceBagDropRouting = true;
        }

        // -------------------------------------------------------------
        // 8. Service-time map precedence
        // -------------------------------------------------------------
        private static void RunServiceTimeMap()
        {
            Console.WriteLine("== Service time map ==");

            ServiceProfile baseProfile = new ServiceProfile { MeanServiceTime = 60.0, StandardDeviation = 0.0 };
            ServiceProfile channelProfile = new ServiceProfile { MeanServiceTime = 30.0, StandardDeviation = 0.0 };
            ServiceProfile profileProfile = new ServiceProfile { MeanServiceTime = 90.0, StandardDeviation = 0.0 };
            ServiceProfile compositeProfile = new ServiceProfile { MeanServiceTime = 15.0, StandardDeviation = 0.0 };
            ServiceProfile defaultProfile = new ServiceProfile { MeanServiceTime = 45.0, StandardDeviation = 0.0 };

            ServiceTimeMap map = new ServiceTimeMap { Default = defaultProfile };
            map.Entries.Add(new ServiceTimeEntry { Key = "Kiosk", Profile = channelProfile });
            map.Entries.Add(new ServiceTimeEntry { Key = "DomEcon", Profile = profileProfile });
            map.Entries.Add(new ServiceTimeEntry { Key = "DomEcon|Kiosk", Profile = compositeProfile });

            Passenger composite = new Passenger { ProfileName = "DomEcon", Channel = "Kiosk" };
            Passenger profileOnly = new Passenger { ProfileName = "DomEcon", Channel = "Web" };
            Passenger channelOnly = new Passenger { ProfileName = "Other", Channel = "Kiosk" };
            Passenger fallback = new Passenger { ProfileName = "Other", Channel = "Web" };

            Check(ReferenceEquals(map.Resolve(composite), compositeProfile), "profile|channel wins");
            Check(ReferenceEquals(map.Resolve(profileOnly), profileProfile), "profile wins over channel");
            Check(ReferenceEquals(map.Resolve(channelOnly), channelProfile), "channel used when no profile match");
            Check(ReferenceEquals(map.Resolve(fallback), defaultProfile), "default used when no key matches");
            Check(ReferenceEquals(map.Resolve(null), defaultProfile), "null passenger -> default");

            ServiceProfile dhpSpecific = new ServiceProfile { MeanServiceTime = 8.0, StandardDeviation = 0.0 };
            map.Entries.Add(new ServiceTimeEntry
            {
                Key = "DomesticDeparture|DomEcon|Kiosk",
                Profile = dhpSpecific
            });
            Passenger domesticComposite = new Passenger
            {
                DHPType = DHPType.DomesticDeparture,
                ProfileName = "DomEcon",
                Channel = "Kiosk"
            };
            Check(ReferenceEquals(map.Resolve(domesticComposite), dhpSpecific),
                "legacy DHPType|profile|channel key resolves when no Pool Plans key exists");

            map.Entries.Add(new ServiceTimeEntry
            {
                Key = "InternationalDeparture|DomEcon",
                Profile = new ServiceProfile { MeanServiceTime = 9.0, StandardDeviation = 0.0 }
            });
            Passenger intlProfile = new Passenger
            {
                DHPType = DHPType.InternationalDeparture,
                ProfileName = "DomEcon",
                Channel = "Web"
            };
            Check(map.Resolve(intlProfile).MeanServiceTime == 9.0,
                "DHPType|profile service key resolves when no channel-specific key exists");

            // Pool Plans identity keys use "Channel|DHPType|ProfileName", the
            // reverse segment order of the legacy key, and must win when both
            // are present.
            ServiceProfile ppExact = new ServiceProfile { MeanServiceTime = 5.0, StandardDeviation = 0.0 };
            ServiceProfile legacyExact = new ServiceProfile { MeanServiceTime = 7.0, StandardDeviation = 0.0 };
            ServiceProfile ppProfileOnly = new ServiceProfile { MeanServiceTime = 6.0, StandardDeviation = 0.0 };

            ServiceTimeMap ppMap = new ServiceTimeMap { Default = defaultProfile };
            ppMap.Entries.Add(new ServiceTimeEntry
            {
                Key = "Counter|DomesticDeparture|Economy",
                Profile = ppExact
            });
            ppMap.Entries.Add(new ServiceTimeEntry
            {
                Key = "DomesticDeparture|Economy|Counter",
                Profile = legacyExact
            });

            Passenger ppPassenger = new Passenger
            {
                DHPType = DHPType.DomesticDeparture,
                ProfileName = "Economy",
                Channel = "Counter"
            };
            Check(ReferenceEquals(ppMap.Resolve(ppPassenger), ppExact),
                "Channel|DHPType|profile (Pool Plans identity) wins over the legacy order");

            ServiceTimeMap ppProfileOnlyMap = new ServiceTimeMap { Default = defaultProfile };
            ppProfileOnlyMap.Entries.Add(new ServiceTimeEntry
            {
                Key = "Counter|Economy",
                Profile = ppProfileOnly
            });
            Check(ReferenceEquals(ppProfileOnlyMap.Resolve(ppPassenger), ppProfileOnly),
                "Channel|profile (Pool Plans profile-only identity) resolves");

            Passenger ppUnmatched = new Passenger
            {
                DHPType = DHPType.InternationalArrival,
                ProfileName = "Other",
                Channel = "Web"
            };
            Check(ReferenceEquals(ppProfileOnlyMap.Resolve(ppUnmatched), defaultProfile),
                "default profile used when no Pool Plans or legacy key matches");

            string before = map.CacheKey;
            map.Entries.Add(new ServiceTimeEntry { Key = "New", Profile = baseProfile });
            Check(before != map.CacheKey, "CacheKey changes when the map changes");
        }

        // -------------------------------------------------------------
        // 9. Channel filtering through a facility
        // -------------------------------------------------------------
        private static void RunChannelFiltering()
        {
            Console.WriteLine("== Channel filtering ==");

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1000, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            ProfileChannelMap map = new ProfileChannelMap();
            map.Entries.Add(new ProfileChannelEntry
            {
                ProfileName = "D",
                Shares = new List<ChannelShare>
                {
                    new ChannelShare { Channel = "Counter", Share = 0.5 },
                    new ChannelShare { Channel = "Kiosk", Share = 0.5 }
                }
            });

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 3, map);

            int counterCount = gen.Passengers.Count(p => p.Channel == "Counter");

            CheckInInputs input = new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 5 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 1,
                StaffingPercentage = 100.0
            };

            QueueResults qr = new CheckInSolver().Solve(input).QueueResults;

            Check(qr.ProcessedPassengers.Count == counterCount,
                $"Check-in served {qr.ProcessedPassengers.Count} == Counter count {counterCount}");
            Check(qr.ProcessedPassengers.All(p => p.Channel == "Counter"),
                "Only Counter passengers are served");
        }

        // -------------------------------------------------------------
        // 10. Per-channel / per-profile metrics (overall and per step)
        // -------------------------------------------------------------
        private static void RunClassMetrics()
        {
            Console.WriteLine("== Class metrics ==");

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                600, 2, new[] { 100.0, 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>(),
                warmUp: true, warmMinutes: 60, warmFactor: 50.0);

            ProfileChannelMap map = new ProfileChannelMap();
            map.Entries.Add(new ProfileChannelEntry
            {
                ProfileName = "D",
                Shares = new List<ChannelShare>
                {
                    new ChannelShare { Channel = "Counter", Share = 0.5 },
                    new ChannelShare { Channel = "Kiosk", Share = 0.5 }
                }
            });

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 21, map);

            CheckInInputs input = new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                PhysicalResources = new List<int> { 10 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 1,
                StaffingPercentage = 100.0
            };

            QueueResults qr = new CheckInSolver().Solve(input).QueueResults;

            int nonWarm = qr.Passengers.Count(p => !p.IsWarmUp);
            int byChannelSum = qr.ByChannel.Values.Sum(m => m.Count);
            int byProfileSum = qr.ByProfile.Values.Sum(m => m.Count);

            Check(byChannelSum == nonWarm, $"ByChannel sum {byChannelSum} == non-warm {nonWarm}");
            Check(byProfileSum == nonWarm, $"ByProfile sum {byProfileSum} == non-warm {nonWarm}");
            Check(qr.ByChannel.ContainsKey("Counter") && qr.ByChannel.ContainsKey("Kiosk"),
                "ByChannel keys present");
            Check(qr.ByProfile.ContainsKey("D"), "ByProfile key present");
            Check(qr.IntervalMetrics.All(m => m.ByChannel.Count > 0 && m.ByProfile.Count > 0),
                "Per-step class metrics present");
        }

        // -------------------------------------------------------------
        // 11. Optimizer determinism with a service-time provider
        // -------------------------------------------------------------
        private static void RunOptimizerDeterminismWithProvider()
        {
            Console.WriteLine("== Optimizer determinism (provider) ==");

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                500, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 77, null);

            CheckInInputs CreateInput(double mean)
            {
                ServiceTimeMap map = new ServiceTimeMap
                {
                    Default = new ServiceProfile { MeanServiceTime = mean, StandardDeviation = 0.5 }
                };

                return new CheckInInputs
                {
                    Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                    ServiceProfile = new ServiceProfile { MeanServiceTime = mean, StandardDeviation = 0.5 },
                    ServiceTimeProvider = map,
                    PhysicalResources = new List<int> { 4 },
                    OptimizeResources = true,
                    TargetWait95 = new List<double> { 10.0 },
                    Seed = 1,
                    StaffingPercentage = 100.0
                };
            }

            CheckInResults a = new CheckInSolver().Solve(CreateInput(2.0));
            CheckInResults b = new CheckInSolver().Solve(CreateInput(2.0));

            Check(a.PhysicalCounters == b.PhysicalCounters &&
                  Math.Abs(a.QueueResults.GetEvaluationWait95() - b.QueueResults.GetEvaluationWait95()) < 1e-9,
                $"Same inputs -> identical optimizer result ({a.PhysicalCounters} counters)");

            CheckInResults c = new CheckInSolver().Solve(CreateInput(4.0));
            Check(c.PhysicalCounters >= a.PhysicalCounters,
                $"Slower service needs at least as many counters ({c.PhysicalCounters} >= {a.PhysicalCounters})");
        }

        // -------------------------------------------------------------
        // 12. Analytic service-time comparison (per passenger type)
        // -------------------------------------------------------------
        private static void RunAnalyticComparison()
        {
            Console.WriteLine("== Analytic service-time comparison ==");

            ServiceProfile econProfile = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.0 };
            ServiceProfile busProfile = new ServiceProfile { MeanServiceTime = 6.0, StandardDeviation = 0.0 };

            ServiceTimeMap map = new ServiceTimeMap { Default = econProfile };
            map.Entries.Add(new ServiceTimeEntry { Key = "Econ", Profile = econProfile });
            map.Entries.Add(new ServiceTimeEntry { Key = "Bus", Profile = busProfile });

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                400, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile>
                {
                    MakeProfile("Econ", 50.0, 60.0, 150.0, 0.0, 0.0),
                    MakeProfile("Bus", 50.0, 60.0, 150.0, 0.0, 0.0)
                },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 15, null);

            CheckInInputs input = new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = econProfile,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 20 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 30.0 },
                Seed = 1,
                StaffingPercentage = 100.0
            };

            QueueResults qr = new CheckInSolver().Solve(input).QueueResults;

            List<Passenger> econGroup = qr.ProcessedPassengers.Where(p => p.ProfileName == "Econ").ToList();
            List<Passenger> busGroup = qr.ProcessedPassengers.Where(p => p.ProfileName == "Bus").ToList();

            Check(econGroup.Count > 0 && busGroup.Count > 0, "Both passenger types were served");

            double econMean = econGroup.Count > 0 ? econGroup.Average(p => p.ServiceTime) : 0.0;
            double busMean = busGroup.Count > 0 ? busGroup.Average(p => p.ServiceTime) : 0.0;

            Check(Math.Abs(econMean - 2.0) < 1e-6, $"Economy mean service {econMean} == 2.0");
            Check(Math.Abs(busMean - 6.0) < 1e-6, $"Business mean service {busMean} == 6.0");

            double servedWork = qr.ProcessedPassengers.Sum(p => p.ServiceTime);
            double analyticWork = econGroup.Count * 2.0 + busGroup.Count * 6.0;

            Check(Math.Abs(servedWork - analyticWork) < 0.5,
                $"DES service work {Math.Round(servedWork, 2)} == analytic {Math.Round(analyticWork, 2)}");
        }

        // -------------------------------------------------------------
        // 13b. Pre-midnight arrivals are served before minute 0
        // -------------------------------------------------------------
        private static void RunNegativeArrivalQueue()
        {
            Console.WriteLine("== Pre-midnight queue timing ==");

            List<Passenger> MakeCohort(params double[] arrivals)
            {
                return arrivals
                    .Select((time, index) => new Passenger
                    {
                        Id = index + 1,
                        ArrivalTime = time,
                        QueueArrivalTime = time,
                        ServiceTime = 1.0
                    })
                    .ToList();
            }

            QueueResults Run(List<Passenger> cohort, int resourceCount)
            {
                List<QueueResource> fleet = QueueResource.Create(resourceCount);
                QueueSimulator simulator = new QueueSimulator();

                simulator.ResetPassengers(cohort);
                simulator.ResetResources(fleet);
                simulator.RunQueue(cohort, fleet);

                return new QueueMetricsCalculator().Calculate(cohort, fleet);
            }

            // Enough servers: every pre-midnight passenger starts on arrival.
            List<Passenger> clear = MakeCohort(-30.0, -20.0, -10.0, -5.0);
            QueueResults clearResults = Run(clear, 4);

            Check(clear.All(p => Math.Abs(p.WaitTime) < 1e-9),
                "Pre-midnight arrivals start service on arrival, not at minute 0");

            Check(clear.All(p => Math.Abs(p.StartServiceTime - p.ArrivalTime) < 1e-9),
                "Pre-midnight start times keep their negative arrival times");

            Check(clearResults.PeakQueueLength == 0 && clearResults.Wait95 == 0.0,
                $"No synthetic midnight queue or wait (peak {clearResults.PeakQueueLength}, W95 {clearResults.Wait95})");

            // One server: the queue is real, but it runs on its own clock and
            // may cross minute 0 rather than collapsing there.
            List<Passenger> congested = MakeCohort(-10.0, -9.0, -8.0);
            congested.ForEach(p => p.ServiceTime = 5.0);
            QueueResults congestedResults = Run(congested, 1);

            Check(Math.Abs(congested[0].StartServiceTime + 10.0) < 1e-9 &&
                  Math.Abs(congested[1].StartServiceTime + 5.0) < 1e-9,
                "A busy server blocks the next pre-midnight arrival until service frees up");

            Check(Math.Abs(congested[2].StartServiceTime) < 1e-9 &&
                  Math.Abs(congested[2].WaitTime - 8.0) < 1e-9,
                $"Congestion crosses minute 0 without a reset (start {congested[2].StartServiceTime}, wait {congested[2].WaitTime})");

            Check(congestedResults.QueueTimeline.Count > 0 &&
                  congestedResults.QueueTimeline[0].Time < 0.0,
                "Queue timeline records pre-midnight waiting");

            Check(congestedResults.PeakQueueLength == 2 &&
                  Math.Abs(congestedResults.PeakQueueTime + 8.0) < 1e-9,
                $"Peak queue {congestedResults.PeakQueueLength} occurs at {congestedResults.PeakQueueTime} min, before 0");

            Check(congestedResults.QueueTimeline.Last().Queue == 0,
                "The pre-midnight queue drains when its last passenger starts service");

            QueueResource probe = QueueResource.Create(1)[0];
            probe.NextAvailableTime = 0.0;
            probe.BusyTime = 12.0;
            probe.Reset();

            Check(double.IsNegativeInfinity(probe.NextAvailableTime) && probe.BusyTime == 0.0,
                "QueueResource.Reset opens servers before any simulated time");

            Check(double.IsNegativeInfinity(QueueResource.Create(1)[0].NextAvailableTime),
                "QueueResource.Create starts servers available before time 0");

            Check(ChartText.DescribeActivity(new QueuePoolResult { PassengerCount = 3 })
                    .Contains("re-run"),
                "Old pool snapshots without an arrival span ask for a solver re-run");

            // End-to-end: the solver's per-pool snapshot must keep the same
            // negative-clock timing, otherwise the fixed simulator would still
            // be reported through a stale pool summary.
            List<Passenger> midnight = Enumerable.Range(0, 6)
                .Select(i => new Passenger
                {
                    Id = i + 1,
                    Channel = "Counter",
                    DHPType = DHPType.DomesticDeparture,
                    ProfileName = "Economy",
                    ArrivalTime = -30.0 + i,
                    QueueArrivalTime = -30.0 + i,
                    ServiceTime = 1.0
                })
                .ToList();

            CheckInResults midnightResult = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = midnight,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 6 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 11,
                StaffingPercentage = 100.0
            });

            QueuePoolResult midnightPool = midnightResult.QueueResults.PoolResults.Single();

            Check(midnightPool.FirstArrivalTime < 0.0 && midnightPool.LastArrivalTime < 0.0,
                $"Pool snapshot keeps pre-midnight arrivals ({midnightPool.FirstArrivalTime} to {midnightPool.LastArrivalTime})");

            Check(midnightPool.WaitingPassengerCount == 0 &&
                  midnightPool.QueueTimeline.Count == 0 &&
                  Math.Abs(midnightPool.Wait95) < 1e-9,
                "Pool snapshot reports no wait and no queue when pre-midnight demand has enough servers");

            Check(ChartText.DescribeActivity(midnightPool).Contains("no passenger waited"),
                "Pool activity line explains an empty queue instead of leaving it ambiguous");

            List<Passenger> midnightQueue = MakeCohort(-10.0, -9.0, -8.0);

            foreach (Passenger passenger in midnightQueue)
            {
                passenger.Channel = "Counter";
                passenger.DHPType = DHPType.DomesticDeparture;
                passenger.ProfileName = "Economy";
            }

            CheckInResults queuedResult = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = midnightQueue,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 5.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 1 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 12,
                StaffingPercentage = 100.0
            });

            QueuePoolResult queuedPool = queuedResult.QueueResults.PoolResults.Single();

            Check(queuedPool.WaitingPassengerCount == 2 &&
                  Math.Abs(queuedPool.Wait95 - 8.0) < 1e-9 &&
                  Math.Abs(queuedPool.FirstQueueEventTime + 9.0) < 1e-9 &&
                  Math.Abs(queuedPool.LastQueueEventTime) < 1e-9,
                $"Pool snapshot reports pre-midnight queue events ({queuedPool.FirstQueueEventTime} to {queuedPool.LastQueueEventTime})");

            Check(ChartText.DescribeActivity(queuedPool).Contains("queue events -9 to 0"),
                "Pool activity line reports the exact queue-event span");
        }

        // -------------------------------------------------------------
        // 13. Resource domain bounds are respected
        // -------------------------------------------------------------
        private static void RunResourceDomainBounds()
        {
            Console.WriteLine("== Resource domain bounds ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.5, StandardDeviation = 0.5 }
            };

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1500, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Solo", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 21, null);

            CheckInInputs Input(int min, int max, double target)
            {
                return new CheckInInputs
                {
                    Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                    ServiceProfile = map.Default,
                    ServiceTimeProvider = map,
                    PhysicalResources = new List<int> { 10 },
                    OptimizeResources = true,
                    TargetWait95 = new List<double> { target },
                    Seed = 3,
                    StaffingPercentage = 100.0,
                    ResourceDomain = new ResourceSearchRange(min, max)
                };
            }

            CheckInResults tight = new CheckInSolver().Solve(Input(1, 3, 10.0));
            Check(tight.PhysicalCounters >= 1 && tight.PhysicalCounters <= 3,
                $"Result {tight.PhysicalCounters} stays inside domain 1-3");

            CheckInResults wide = new CheckInSolver().Solve(Input(1, 900, 10.0));
            Check(wide.PhysicalCounters <= 900,
                $"Result {wide.PhysicalCounters} stays inside domain 1-900");

            CheckInResults floorBound = new CheckInSolver().Solve(Input(40, 500, 10.0));
            Check(floorBound.PhysicalCounters >= 40,
                $"Result {floorBound.PhysicalCounters} honours the 40 floor");

            CheckInResults unreachable = new CheckInSolver().Solve(Input(1, 5, 0.01));
            Check(unreachable.PhysicalCounters == 5,
                $"Unreachable target returns the domain maximum ({unreachable.PhysicalCounters} == 5)");

            CheckInResults second = new CheckInSolver().Solve(Input(1, 3, 10.0));
            Check(second.PhysicalCounters == tight.PhysicalCounters,
                "Same domain and inputs -> same capacity");
        }

        // -------------------------------------------------------------
        // 14. Dedicated pool per channel
        // -------------------------------------------------------------
        private static void RunPerChannelPools()
        {
            Console.WriteLine("== Per-channel dedicated pools ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 3.0, StandardDeviation = 0.5 }
            };

            map.Entries.Add(new ServiceTimeEntry
            {
                Key = "Counter",
                Profile = new ServiceProfile { MeanServiceTime = 4.0, StandardDeviation = 0.5 }
            });

            map.Entries.Add(new ServiceTimeEntry
            {
                Key = "Kiosk",
                Profile = new ServiceProfile { MeanServiceTime = 1.5, StandardDeviation = 0.3 }
            });

            ProfileChannelMap routing = new ProfileChannelMap { DefaultChannel = "Counter" };
            routing.Entries.Add(new ProfileChannelEntry { ProfileName = "Solo" });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Counter", Share = 0.6 });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Kiosk", Share = 0.4 });

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1200, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Solo", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 33, routing);

            int counterCount = gen.Passengers.Count(p => p.Channel == "Counter");
            int kioskCount = gen.Passengers.Count(p => p.Channel == "Kiosk");

            Check(counterCount > 0 && kioskCount > 0,
                $"Both channels were generated (Counter {counterCount}, Kiosk {kioskCount})");

            CheckInResults shared = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 30 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(shared.PhysicalPerChannel.Count == 1,
                $"No channels wired -> exactly one shared pool ({shared.PhysicalPerChannel.Count})");

            Check(shared.QueueResults.ProcessorPools.Count == 1 &&
                  shared.QueueResults.ProcessorPools[0].IsShared,
                "Shared run records one shared processor pool");

            Check(shared.QueueResults.ProcessorPools[0].ResourceCount ==
                  shared.QueueResults.Resources.Count,
                $"Shared pool resource count matches the simulated resources " +
                $"({shared.QueueResults.ProcessorPools[0].ResourceCount} == {shared.QueueResults.Resources.Count})");

            Check(shared.Wait95PerChannel.Count == 0,
                "A shared pool reports no per-channel waits");

            CheckInResults pooled = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter", "Kiosk" },
                PhysicalResources = new List<int> { 5, 5 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 10.0, 10.0 },
                Seed = 5,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 400)
            });

            Check(pooled.PhysicalPerChannel.Count == 2,
                $"Two channel pools sized ({pooled.PhysicalPerChannel.Count})");

            Check(pooled.PhysicalCounters == pooled.PhysicalPerChannel.Sum(),
                $"Aggregate {pooled.PhysicalCounters} == sum of pools");

            Check(pooled.Wait95PerChannel.ContainsKey("Counter") &&
                  pooled.Wait95PerChannel.ContainsKey("Kiosk"),
                "Wait95 reported per channel");

            List<ProcessorPoolInfo> poolInfo = pooled.QueueResults.ProcessorPools;

            Check(poolInfo.Count == 2 &&
                  poolInfo[0].Name == "Counter" &&
                  poolInfo[1].Name == "Kiosk",
                "Each dedicated channel records its own processor pool");

            Check(poolInfo.All(p => !p.IsShared),
                "Dedicated channel pools are not marked shared");

            Check(poolInfo.Sum(p => p.ResourceCount) == pooled.QueueResults.Resources.Count,
                $"Pool resource counts sum to the merged resources " +
                $"({poolInfo.Sum(p => p.ResourceCount)} == {pooled.QueueResults.Resources.Count})");

            Check(poolInfo[0].FirstResourceIndex == 0 &&
                  poolInfo[1].FirstResourceIndex == poolInfo[0].ResourceCount,
                "Pool resource offsets are cumulative and collision-free");

            Check(poolInfo[0].PhysicalResourceCount == pooled.PhysicalPerChannel[0] &&
                  poolInfo[1].PhysicalResourceCount == pooled.PhysicalPerChannel[1],
                $"Each pool carries its installed unit count " +
                $"({poolInfo[0].PhysicalResourceCount}, {poolInfo[1].PhysicalResourceCount})");

            Check(poolInfo.Sum(p => p.PhysicalResourceCount) == pooled.QueueResults.PhysicalProcessorCount,
                $"Installed counts sum to the facility's physical count " +
                $"({poolInfo.Sum(p => p.PhysicalResourceCount)} == {pooled.QueueResults.PhysicalProcessorCount})");

            Check(shared.QueueResults.ProcessorPools[0].PhysicalResourceCount == 30,
                $"The shared pool records its installed count " +
                $"({shared.QueueResults.ProcessorPools[0].PhysicalResourceCount})");

            double counterWait = pooled.Wait95PerChannel["Counter"];
            double kioskWait = pooled.Wait95PerChannel["Kiosk"];

            Check(counterWait <= 10.0 && kioskWait <= 10.0,
                $"Both pools meet the target (Counter {Math.Round(counterWait, 2)}, Kiosk {Math.Round(kioskWait, 2)})");

            HashSet<int> processed = new HashSet<int>(
                pooled.QueueResults.ProcessedPassengers.Select(p => p.Id));

            int expected = counterCount + kioskCount;

            Check(processed.Count == expected,
                $"Dedicated pools processed {processed.Count} == Counter+Kiosk {expected}");

            bool everyProcessedHasHistory = pooled.QueueResults.ProcessedPassengers
                .All(p => p.History.ContainsKey(FacilityType.CheckIn));

            Check(everyProcessedHasHistory, "Every processed passenger has a Check-In record");

            List<Passenger> source = gen.Passengers;

            double[] arrivalsBefore = source.Select(p => p.ArrivalTime).ToArray();
            bool historyBefore = source.Any(p => p.History.ContainsKey(FacilityType.CheckIn));

            new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = source,
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter", "Kiosk" },
                PhysicalResources = new List<int> { 6, 6 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0, 10.0 },
                Seed = 5,
                StaffingPercentage = 100.0
            });

            bool arrivalsUnchanged = source
                .Select(p => p.ArrivalTime)
                .SequenceEqual(arrivalsBefore);

            Check(arrivalsUnchanged, "Solve left the source population's arrival times untouched");

            Check(!source.Any(p => p.History.ContainsKey(FacilityType.CheckIn)) && !historyBefore,
                "Solve left the source population's history untouched");
        }

        // -------------------------------------------------------------
        // Check-In: routed service channels and counter sector/class sub-pools
        // -------------------------------------------------------------
        private static void RunCheckInRoutedPoolSegregation()
        {
            Console.WriteLine("== Check-In routed pools / counter segregation ==");

            List<Passenger> passengers = new List<Passenger>();
            int id = 1;

            void AddPassengers(string channel, DHPType type, string profile, int count, double arrival, bool online = false, bool bag = false)
            {
                for (int i = 0; i < count; i++)
                {
                    passengers.Add(new Passenger
                    {
                        Id = id++,
                        Channel = channel,
                        DHPType = type,
                        ProfileName = profile,
                        ArrivalTime = arrival + i * 0.01,
                        QueueArrivalTime = arrival + i * 0.01,
                        UsesOnlineCheckIn = online,
                        HasCheckedBag = bag
                    });
                }
            }

            AddPassengers("Counter", DHPType.DomesticDeparture, "Economy", 8, 0.0);
            AddPassengers("Counter", DHPType.DomesticDeparture, "Business", 7, 0.0);
            AddPassengers("Counter", DHPType.InternationalDeparture, "Economy", 6, 0.0);
            AddPassengers("Counter", DHPType.InternationalDeparture, "Business", 5, 0.0);
            AddPassengers("Kiosk", DHPType.DomesticDeparture, "Economy", 4, 0.0);
            AddPassengers("Kiosk", DHPType.InternationalDeparture, "Economy", 3, 0.0, online: false, bag: true);
            AddPassengers("Counter", DHPType.DomesticArrival, "Economy", 2, 0.0);
            AddPassengers("Web", DHPType.DomesticDeparture, "Economy", 2, 0.0);

            ServiceTimeMap serviceTimes = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 }
            };

            string[] profiles = { "Economy", "Business" };
            DHPType[] sectors = { DHPType.DomesticDeparture, DHPType.InternationalDeparture };
            double counterServiceTime = 2.0;
            foreach (DHPType sector in sectors)
            {
                foreach (string profile in profiles)
                {
                    serviceTimes.Entries.Add(new ServiceTimeEntry
                    {
                        Key = $"{sector}|{profile}|Counter",
                        Profile = new ServiceProfile
                        {
                            MeanServiceTime = counterServiceTime,
                            StandardDeviation = 0.0
                        }
                    });
                    counterServiceTime += 1.0;
                }
            }

            serviceTimes.Entries.Add(new ServiceTimeEntry
            {
                Key = "Kiosk",
                Profile = new ServiceProfile { MeanServiceTime = 0.5, StandardDeviation = 0.0 }
            });

            CheckInResults result = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceTimeProvider = serviceTimes,
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolPhysicalResources = new List<int> { 2, 3, 4, 5, 6 },
                PoolTargetWait95 = new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 },
                PoolMinimumResources = new List<int> { 1, 2, 3, 4, 5 },
                PoolMaximumResources = new List<int> { 8, 9, 10, 11, 12 },
                OptimizeResources = false,
                Seed = 41,
                StaffingPercentage = 100.0
            });

            List<string> expectedPoolNames = new List<string>
            {
                "Counter|DomesticDeparture|Business",
                "Counter|DomesticDeparture|Economy",
                "Counter|InternationalDeparture|Business",
                "Counter|InternationalDeparture|Economy",
                "Kiosk"
            };

            Check(result.PoolNames.SequenceEqual(expectedPoolNames),
                "Four counter pools plus a separate Kiosk channel pool are reported");
            Check(result.PhysicalPerChannel.SequenceEqual(new[] { 2, 3, 4, 5, 6 }),
                "Nominal resources are independently assigned in expanded pool order");
            Check(result.PhysicalCounters == 20,
                $"Total physical resources equal the sum across all routed pools ({result.PhysicalCounters} == 20)");
            Check(result.ChannelNames.SequenceEqual(new[] { "Counter", "Kiosk" }) &&
                  result.PhysicalPerChannelTotal.SequenceEqual(new[] { 14, 6 }),
                "Channel resource totals aggregate Counter sub-pools and retain other channel totals");

            List<Passenger> processed = result.QueueResults.ProcessedPassengers;
            Check(processed.Count == 33,
                $"All routed departures are served, including Kiosk bag carriers ({processed.Count} == 33)");
            Check(processed.All(p => p.Channel != "Web" && p.DHPType != DHPType.DomesticArrival),
                "Unconfigured Web and arrival passengers are excluded from physical Check-In pools");

            Dictionary<string, double> expectedMeans = new Dictionary<string, double>
            {
                ["Counter|DomesticDeparture|Economy"] = 2.0,
                ["Counter|DomesticDeparture|Business"] = 3.0,
                ["Counter|InternationalDeparture|Economy"] = 4.0,
                ["Counter|InternationalDeparture|Business"] = 5.0,
                ["Kiosk"] = 0.5
            };

            for (int i = 0; i < expectedPoolNames.Count; i++)
            {
                List<Passenger> poolPassengers = result.PoolResults[i].ProcessedPassengers;
                double mean = poolPassengers.Count > 0 ? poolPassengers.Average(p => p.ServiceTime) : 0.0;
                Check(Math.Abs(mean - expectedMeans[expectedPoolNames[i]]) < 1e-9,
                    $"Pool '{expectedPoolNames[i]}' uses its own service-time mapping");
            }

            Check(result.Wait95PerChannel.Keys.Count == 5,
                "Wait95 is reported independently for every expanded pool");

            List<PassengerProfile> classes = new List<PassengerProfile>
            {
                MakeProfile("Economy", 50.0, 60.0, 150.0, 0.0, 0.0),
                MakeProfile("Business", 50.0, 60.0, 150.0, 0.0, 0.0)
            };
            ProfileChannelMap routing = new ProfileChannelMap { DefaultChannel = "Counter" };
            foreach (string profile in profiles)
            {
                routing.Entries.Add(new ProfileChannelEntry
                {
                    ProfileName = profile,
                    Shares = new List<ChannelShare>
                    {
                        new ChannelShare { Channel = "Counter", Share = 0.75 },
                        new ChannelShare { Channel = "Kiosk", Share = 0.25 }
                    }
                });
            }

            PassengerGenerationResult generated = GenerateWithProfiles(
                BuildGroupsWithProfiles(1200, 1, new[] { 100.0 }, 0, 100.0, 50.0, classes, classes),
                301,
                routing);
            int generatedCounter = generated.Passengers.Count(p => p.Channel == "Counter");
            int generatedKiosk = generated.Passengers.Count(p => p.Channel == "Kiosk");

            Check(Math.Abs(generatedCounter / (double)generated.Passengers.Count - 0.75) < 0.04,
                "CHR Counter share is realized across domestic/international Economy and Business profiles");
            Check(Math.Abs(generatedKiosk / (double)generated.Passengers.Count - 0.25) < 0.04,
                "CHR Kiosk share is realized without changing generation or channel labels");

            CheckInResults generatedPools = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = generated.Passengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                ChannelRouting = routing,
                PoolPhysicalResources = new List<int> { 2, 2, 2, 2, 2 },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Check(generatedPools.PoolNames.Count == 5 &&
                  generatedPools.PoolNames.Take(4).Distinct().Count() == 4,
                "Check-In consumes CHR directly and creates four dedicated counter queues");
            Check(generatedPools.PoolResults.Take(4).All(pool => pool.ProcessedPassengers.Count > 0),
                "All four generated domestic/international Economy/Business counter queues receive demand");
            Check(generatedPools.PoolResults.Take(4).Sum(pool => pool.ProcessedPassengers.Count) == generatedCounter &&
                  generatedPools.PoolResults[4].ProcessedPassengers.Count == generatedKiosk,
                "Generated Counter and Kiosk populations are served exactly once in their routed pools");

            CheckInResults optimized = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers.Select(p => p.Clone()).ToList(),
                ServiceTimeProvider = serviceTimes,
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolTargetWait95 = new List<double> { 1, 2, 3, 4, 5 },
                PoolMinimumResources = new List<int> { 2, 3, 4, 5, 6 },
                PoolMaximumResources = new List<int> { 4, 5, 6, 7, 8 },
                OptimizeResources = true,
                Seed = 41,
                StaffingPercentage = 100.0
            });

            Check(optimized.SearchOutcomes.Count == 5,
                "Optimizer searches each routed channel/sub-pool independently");

            bool eachPoolUsesOwnSettings = optimized.SearchOutcomes.Count == 5 &&
                optimized.SearchOutcomes.Select((outcome, index) =>
                    outcome.Plan.TargetWait95 == index + 1 &&
                    outcome.Plan.Range.Min == index + 2 &&
                    outcome.Plan.Range.Max == index + 4 &&
                    outcome.OptimalPhysicalResources >= index + 2 &&
                    outcome.OptimalPhysicalResources <= index + 4).All(x => x);

            Check(eachPoolUsesOwnSettings,
                "Each optimizer pool respects its own target and Min/Max resource bounds");

            // ---------------------------------------------------------
            // Pool-level queue history and chart filtering
            // ---------------------------------------------------------
            List<QueuePoolResult> queuePools = result.QueueResults.PoolResults;

            Check(queuePools.Count == expectedPoolNames.Count,
                $"Merged results keep every pool's own queue history ({queuePools.Count} == {expectedPoolNames.Count})");

            Check(queuePools.Select(p => p.Name).SequenceEqual(expectedPoolNames),
                "Per-pool queue identities match the solver's Pool Names output");

            Check(queuePools.Zip(result.PoolResults, (summary, raw) => summary.Wait95 == raw.Wait95).All(x => x),
                "Each pool keeps its own Wait95 instead of the facility aggregate");

            Check(queuePools.All(p => p.Channel == "Counter" ? p.PoolKey.Length > 0 : p.PoolKey.Length == 0),
                "Channel and PoolKey are split from the full pool identity");

            Check(queuePools.Sum(p => p.PassengerCount) == processed.Count,
                $"Per-pool passenger counts sum to the merged population ({queuePools.Sum(p => p.PassengerCount)} == {processed.Count})");

            Check(queuePools.All(p => p.QueueTimeline != null && p.CumulativeFlow != null),
                "Every pool carries its own queue timeline and cumulative flow");

            Check(queuePools.All(p => p.ActiveResources > 0),
                "Every pool records its staffed processor count");

            Check(queuePools.Select(p => p.TargetWait95).SequenceEqual(new double[] { 1, 2, 3, 4, 5 }),
                "Per-pool targets survive the merge in pool order");

            Check(queuePools.All(p =>
                {
                    QueueCumulativePoint last = p.CumulativeFlow.LastOrDefault();
                    return last != null &&
                           last.CumulativeArrivals == p.PassengerCount &&
                           last.CumulativeDepartures == p.PassengerCount;
                }),
                "Each pool's cumulative flow reconciles with its own served count");

            QueueCumulativePoint facilityLast = result.QueueResults.CumulativeFlow.LastOrDefault();

            Check(facilityLast != null &&
                  queuePools.Sum(p => p.CumulativeFlow.LastOrDefault()?.CumulativeArrivals ?? 0) ==
                  facilityLast.CumulativeArrivals,
                "Pool cumulative arrivals sum to the facility cumulative arrivals");

            Check(ChartText.SelectPools(queuePools, "").Count == 0,
                "An empty pool filter leaves the chart on the facility total");

            Check(ChartText.SelectPools(queuePools, "*").Count == queuePools.Count,
                "The '*' filter selects every recorded pool");

            Check(ChartText.SelectPools(queuePools, "Counter").Count == 4,
                "A channel filter selects all of that channel's sub-pools");

            Check(ChartText.SelectPools(queuePools, "Counter|DomesticDeparture|Economy").Count == 1,
                "A full pool identity selects exactly one pool");

            Check(ChartText.SelectPools(queuePools, "Kiosk,Counter|DomesticDeparture|Economy").Count == 2,
                "A comma-separated filter selects several pools");

            Check(ChartText.SelectPools(queuePools, "Nope").Count == 0,
                "An unmatched filter selects no pool");

            Check(ChartText.Row("a", "1,2", "x\"y") == "a,\"1,2\",\"x\"\"y\"",
                "CSV rows escape commas and quotes");

            List<Passenger> sharedDemand = passengers
                .Where(p => p.DHPType == DHPType.DomesticDeparture)
                .Take(12)
                .Select(p => p.Clone())
                .ToList();

            CheckInResults shared = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = sharedDemand,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string>(),
                PhysicalResources = new List<int> { 3 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(shared.QueueResults.PoolResults.Count == 1 &&
                  shared.QueueResults.PoolResults[0].Name == "Shared",
                "A single facility-wide queue is reported as one Shared pool");
        }

        // -------------------------------------------------------------
        // Check-In: Counter/Kiosk are physical; Bag Drop is a separate stage
        // -------------------------------------------------------------
        private static void RunCheckInSeparateBagDropChannel()
        {
            Console.WriteLine("== Check-In and separate Bag Drop ==");

            ProfileChannelMap routing = new ProfileChannelMap { DefaultChannel = "Counter" };
            routing.Entries.Add(new ProfileChannelEntry
            {
                ProfileName = "Solo",
                Shares = new List<ChannelShare>
                {
                    new ChannelShare { Channel = "Counter", Share = 0.4 },
                    new ChannelShare { Channel = "BagDrop", Share = 0.35 },
                    new ChannelShare { Channel = "Web", Share = 0.25 }
                }
            });

            ServiceTimeMap serviceTimes = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 }
            };

            serviceTimes.Entries.Add(new ServiceTimeEntry
            {
                Key = "Counter",
                Profile = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.0 }
            });

            serviceTimes.Entries.Add(new ServiceTimeEntry
            {
                Key = "Web",
                Profile = new ServiceProfile { MeanServiceTime = 0.5, StandardDeviation = 0.0 }
            });

            List<Passenger> passengers = new List<Passenger>();
            int id = 1;

            Action<string, int, bool, bool> addPassengers = (channel, count, online, bag) =>
            {
                for (int i = 0; i < count; i++)
                {
                    passengers.Add(new Passenger
                    {
                        Id = id,
                        Channel = channel,
                        DHPType = DHPType.DomesticDeparture,
                        ProfileName = "Solo",
                        ArrivalTime = id * 0.01,
                        QueueArrivalTime = id * 0.01,
                        UsesOnlineCheckIn = online,
                        HasCheckedBag = bag,
                        PassengerType = PassengerType.Domestic
                    });

                    id++;
                }
            };

            addPassengers("Counter", 8, false, false);
            addPassengers("Web", 6, true, true);
            addPassengers("Web", 5, true, false);

            CheckInSolver solver = new CheckInSolver();

            CheckInResults checkIn = solver.Solve(new CheckInInputs
            {
                Passengers = passengers,
                ChannelRouting = routing,
                ServiceTimeProvider = serviceTimes,
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 9,
                StaffingPercentage = 100.0
            });

            Check(checkIn.ChannelNames.SequenceEqual(new[] { "Counter" }),
                "CHR-derived Check-In channels are physical only; legacy BagDrop becomes Web and is excluded");
            Check(checkIn.PoolNames.SequenceEqual(new[] { "Counter" }),
                "Check-In exposes only the Counter pool");
            Check(checkIn.PhysicalCounters == 2,
                $"Physical counters count only the Check-In pools ({checkIn.PhysicalCounters} == 2)");
            Check(!checkIn.Wait95PerChannel.ContainsKey("Web") &&
                  !checkIn.Wait95PerChannel.ContainsKey("BagDrop"),
                "Wait95 is never reported for Web or Bag Drop at Check-In");

            List<Passenger> processed = checkIn.QueueResults.ProcessedPassengers;

            Check(processed.Count == 8,
                $"Check-In serves Counter passengers only ({processed.Count} == 8)");
            Check(processed.All(p => p.Channel == "Counter"),
                "Check-In never serves a Web or Bag Drop record");
            Check(processed.All(p => p.History.ContainsKey(FacilityType.CheckIn)),
                "Every Check-In passenger carries Check-In history");

            int counterPool = checkIn.PoolNames.IndexOf("Counter");
            double counterMean = checkIn.PoolResults[counterPool].ProcessedPassengers
                .Average(p => p.ServiceTime);

            Check(Math.Abs(counterMean - 2.0) < 1e-9,
                $"Counter uses its own service-time mapping ({counterMean} == 2.0)");

            BagDropResults bagDrop = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = checkIn.QueueResults.Passengers,
                ServiceTimeProvider = serviceTimes,
                PhysicalResources = new List<int> { 2 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 9,
                StaffingPercentage = 100.0
            });

            List<Passenger> dropped = bagDrop.QueueResults.ProcessedPassengers;

            Check(dropped.Count == 6,
                $"Bag Drop serves only the Web passengers with a checked bag ({dropped.Count} == 6)");
            Check(dropped.All(p => p.Channel == "Web" && p.HasCheckedBag),
                "Bag Drop takes Web bag carriers, not Counter passengers or Web hand-baggage");
            Check(dropped.All(p => p.History.ContainsKey(FacilityType.BagDrop)),
                "Every Bag Drop passenger carries Bag Drop history");
            Check(!dropped.Any(p => p.History.ContainsKey(FacilityType.CheckIn)),
                "Bag Drop does not fabricate a Check-In visit");

            int bagDropPool = bagDrop.PoolNames.IndexOf("Shared");
            double bagDropMean = bagDrop.PoolResults[bagDropPool].ProcessedPassengers
                .Average(p => p.ServiceTime);

            Check(Math.Abs(bagDropMean - 0.5) < 1e-9,
                $"Bag Drop uses the Web service-time mapping through the shared queue ({bagDropMean} == 0.5)");
        }

        // -------------------------------------------------------------
        // Bag Drop: keyed BagDrop| segment pools with a shared fallback
        // -------------------------------------------------------------
        private static void RunBagDropSegmentPools()
        {
            Console.WriteLine("== Bag Drop keyed segment pools ==");

            List<Passenger> passengers = new List<Passenger>();
            int id = 1;

            void AddPassenger(string channel, DHPType dhp, string profile, int count, bool bag)
            {
                for (int i = 0; i < count; i++)
                {
                    passengers.Add(new Passenger
                    {
                        Id = id++,
                        Channel = channel,
                        DHPType = dhp,
                        ProfileName = profile,
                        PassengerType = dhp == DHPType.InternationalDeparture
                            ? PassengerType.International
                            : PassengerType.Domestic,
                        ArrivalTime = id * 0.01,
                        QueueArrivalTime = id * 0.01,
                        UsesOnlineCheckIn = channel == "Web",
                        HasCheckedBag = bag
                    });
                }
            }

            // Web and Kiosk bag carriers are the Bag Drop population.
            AddPassenger("Web", DHPType.DomesticDeparture, "Domestic Economy", 6, true);
            AddPassenger("Web", DHPType.DomesticDeparture, "Domestic Business", 4, true);
            AddPassenger("Kiosk", DHPType.InternationalDeparture, "International Economy", 5, true);
            // Web hand-baggage: not in any bag-drop pool.
            AddPassenger("Web", DHPType.DomesticDeparture, "Domestic Economy", 3, false);
            // Counter bag carriers: bag accepted at the desk, never at Bag Drop.
            AddPassenger("Counter", DHPType.DomesticDeparture, "Domestic Economy", 4, true);
            // An uncovered segment to exercise the shared fallback pool.
            AddPassenger("Web", DHPType.InternationalDeparture, "International Business", 2, true);

            ServiceProfile service = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 };

            // ---------------------------------------------------------
            // Keyed segment pools: BagDrop|DHPType|Profile identities
            // ---------------------------------------------------------
            BagDropSolver keyedSolver = new BagDropSolver();

            BagDropResults keyed = keyedSolver.Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = service,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "BagDrop", PoolKey = "DomesticDeparture|Domestic Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 8) },
                    new PoolPlan { Channel = "BagDrop", PoolKey = "DomesticDeparture|Domestic Business", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 8) },
                    new PoolPlan { Channel = "BagDrop", PoolKey = "InternationalDeparture|International Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 8) }
                },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = false,
                Seed = 21,
                StaffingPercentage = 100.0
            });

            Check(keyedSolver.Warnings.Any(w =>
                    w.Contains("match no keyed Bag Drop pool plan") &&
                    w.Contains("InternationalDeparture|International Business 2")),
                "The uncovered segment is reported and routed to a shared fallback pool");

            Check(keyed.PoolNames.SequenceEqual(new[]
                {
                    "BagDrop|DomesticDeparture|Domestic Economy",
                    "BagDrop|DomesticDeparture|Domestic Business",
                    "BagDrop|InternationalDeparture|International Economy",
                    "Shared"
                }),
                $"Keyed BagDrop pools plus the shared fallback are reported ({string.Join(", ", keyed.PoolNames)})");

            Check(keyed.PhysicalCounters == 9,
                $"Physical counters sum the three segment pools plus the fallback ({keyed.PhysicalCounters} == 9)");

            List<Passenger> dropped = keyed.QueueResults.ProcessedPassengers;

            Check(dropped.Count == 17,
                $"Bag Drop serves 6 + 4 + 5 segment passengers plus 2 fallback ({dropped.Count} == 17)");
            Check(dropped.All(p => p.Channel != "Counter"),
                "Counter bag carriers never enter a keyed or fallback Bag Drop pool");
            Check(dropped.All(p => p.HasCheckedBag),
                "Web hand-baggage passengers are not served at Bag Drop");
            Check(dropped.All(p => p.History.ContainsKey(FacilityType.BagDrop)),
                "Every served passenger carries Bag Drop history");

            int domEconPool = keyed.PoolNames.IndexOf("BagDrop|DomesticDeparture|Domestic Economy");
            Check(keyed.PoolResults[domEconPool].ProcessedPassengers.Count == 6 &&
                  keyed.PoolResults[domEconPool].ProcessedPassengers
                      .All(p => p.ProfileName == "Domestic Economy" &&
                                p.DHPType == DHPType.DomesticDeparture),
                "The Domestic Economy pool takes only its own profile slice");

            int fallbackPool = keyed.PoolNames.IndexOf("Shared");
            Check(keyed.PoolResults[fallbackPool].ProcessedPassengers.Count == 2 &&
                  keyed.PoolResults[fallbackPool].ProcessedPassengers
                      .All(p => p.ProfileName == "International Business"),
                "The shared fallback pool takes only the uncovered segment");

            // ---------------------------------------------------------
            // Facility-wide BagDrop plan: covers everything, segment rows
            // alongside it are rejected
            // ---------------------------------------------------------
            BagDropSolver wideSolver = new BagDropSolver();

            BagDropResults wide = wideSolver.Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = service,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "BagDrop", PhysicalResources = 5, TargetWait95 = 8.0, Range = new ResourceSearchRange(2, 12) },
                    new PoolPlan { Channel = "BagDrop", PoolKey = "DomesticDeparture|Domestic Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 8) }
                },
                OptimizeResources = false,
                Seed = 21,
                StaffingPercentage = 100.0
            });

            Check(wideSolver.Warnings.Any(w =>
                    w.Contains("ignored because the facility-wide") &&
                    w.Contains("BagDrop")),
                "Segment plans alongside a facility-wide BagDrop plan are reported as ignored");
            Check(wide.PoolNames.SequenceEqual(new[] { "BagDrop" }),
                $"A facility-wide BagDrop plan yields one pool ({string.Join(", ", wide.PoolNames)})");
            Check(wide.PhysicalCounters == 5 &&
                  wide.QueueResults.ProcessedPassengers.Count == 17,
                $"The facility-wide pool serves the whole bag-drop population ({wide.PhysicalCounters} counters, {wide.QueueResults.ProcessedPassengers.Count} served)");

            // ---------------------------------------------------------
            // Invalid channel prefix: a legacy Web| key is rejected
            // ---------------------------------------------------------
            BagDropSolver invalidSolver = new BagDropSolver();

            BagDropResults invalid = invalidSolver.Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = service,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "Web", PoolKey = "DomesticDeparture|Domestic Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 8) }
                },
                OptimizeResources = false,
                Seed = 21,
                StaffingPercentage = 100.0
            });

            Check(invalidSolver.Warnings.Any(w =>
                    w.Contains("'Web|DomesticDeparture|Domestic Economy'") &&
                    w.Contains("BagDrop|DHPType|ProfileName")),
                "A Web-keyed Bag Drop plan is rejected with an explicit message");
            Check(invalid.PoolNames.SequenceEqual(new[] { "Shared" }) &&
                  invalid.QueueResults.ProcessedPassengers.Count == 17,
                $"With no valid plans the facility falls back to one shared pool serving everyone ({invalid.QueueResults.ProcessedPassengers.Count} == 17)");

            // ---------------------------------------------------------
            // Optimizer honours each segment plan's own domain
            // ---------------------------------------------------------
            BagDropSolver optimized = new BagDropSolver();

            BagDropResults optimizedResult = optimized.Solve(new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = service,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "BagDrop", PoolKey = "DomesticDeparture|Domestic Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = "BagDrop", PoolKey = "DomesticDeparture|Domestic Business", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = "BagDrop", PoolKey = "InternationalDeparture|International Economy", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 4) }
                },
                PhysicalResources = new List<int> { 3 },
                TargetWait95 = new List<double> { 10.0 },
                OptimizeResources = true,
                Seed = 21,
                StaffingPercentage = 100.0
            });

            Check(optimizedResult.SearchOutcomes.Count == 4,
                $"The optimizer searches every segment pool plus the fallback ({optimizedResult.SearchOutcomes.Count} == 4)");
            Check(optimizedResult.SearchOutcomes.All(o =>
                    o.OptimalPhysicalResources >= o.Plan.Range.Min &&
                    o.OptimalPhysicalResources <= o.Plan.Range.Max),
                "Every segment and fallback search stays inside its own resource domain");
        }

        // -------------------------------------------------------------
        // Pool Plan architecture: keyed plans, PDomain, PNR, and per-facility use
        // -------------------------------------------------------------
        private static void RunPoolPlanArchitecture()
        {
            Console.WriteLine("== Pool Plan architecture ==");

            ResourceSearchRange fallback = new ResourceSearchRange(3, 40);
            List<string> warnings = new List<string>();

            List<PoolPlan> resolverPlans = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Channel = "Counter", PoolKey = "DomesticDeparture|Economy",
                    PhysicalResources = 12, TargetWait95 = 10.0
                },
                new PoolPlan
                {
                    Channel = "Counter", PoolKey = "DomesticDeparture|Business",
                    PhysicalResources = 4, TargetWait95 = 3.0,
                    Range = new ResourceSearchRange(1, 11)
                },
                new PoolPlan
                {
                    Channel = "Counter", PoolKey = "DomesticDeparture|Economy",
                    PhysicalResources = 99, TargetWait95 = 99.0
                }
            };

            List<ChannelResourcePlan> resolved =
                PoolPlanResolver.Resolve(resolverPlans, fallback, warnings.Add);

            Check(resolved.Count == 2,
                $"Keyed plans resolve to one pool per unique identity ({resolved.Count} == 2)");
            Check(resolved[0].Range.Min == 3 && resolved[0].Range.Max == 40,
                "A plan with no PDomain inherits the solver fallback");
            Check(resolved[1].Range.Min == 1 && resolved[1].Range.Max == 11,
                "An explicit PDomain is preserved");
            Check(warnings.Any(w => w.Contains("listed more than once")),
                "A duplicate pool identity is warned on");

            warnings.Clear();

            List<ChannelResourcePlan> shared =
                PoolPlanResolver.Resolve(new List<PoolPlan>
                {
                    new PoolPlan { Channel = "Counter", PhysicalResources = 5, TargetWait95 = 3.0 },
                    new PoolPlan
                    {
                        Channel = null, PhysicalResources = 9, TargetWait95 = 7.0,
                        Range = new ResourceSearchRange(2, 20)
                    }
                }, fallback, warnings.Add);

            Check(shared.Count == 1 && shared[0].IsSharedPool,
                "A shared plan replaces every channel plan");
            Check(shared[0].PhysicalResources == 9 && shared[0].TargetWait95 == 7.0,
                "The shared plan's PNR and PTW are used");
            Check(warnings.Any(w => w.Contains("shared-pool plan")),
                "Channel plans are warned on when a shared plan exists");
            Check(PoolPlanResolver.Resolve(new List<PoolPlan>(), fallback, warnings.Add) == null,
                "No plans returns null so legacy resolution is used");

            // ---------------------------------------------------------
            // Check-In: keyed plans drive the four counter subpools
            // ---------------------------------------------------------
            List<Passenger> checkInPassengers = new List<Passenger>();
            int id = 1;

            void AddCheckIn(string channel, DHPType type, string profile, int count, bool online, bool bag)
            {
                for (int i = 0; i < count; i++)
                {
                    checkInPassengers.Add(new Passenger
                    {
                        Id = id++,
                        Channel = channel,
                        DHPType = type,
                        ProfileName = profile,
                        ArrivalTime = i * 0.01,
                        QueueArrivalTime = i * 0.01,
                        UsesOnlineCheckIn = online,
                        HasCheckedBag = bag,
                        PassengerType = type == DHPType.InternationalDeparture || type == DHPType.InternationalArrival
                            ? PassengerType.International
                            : PassengerType.Domestic
                    });
                }
            }

            AddCheckIn("Counter", DHPType.DomesticDeparture, "Economy", 8, false, false);
            AddCheckIn("Counter", DHPType.DomesticDeparture, "Business", 7, false, false);
            AddCheckIn("Counter", DHPType.InternationalDeparture, "Economy", 6, false, false);
            AddCheckIn("Counter", DHPType.InternationalDeparture, "Business", 5, false, false);
            AddCheckIn("Kiosk", DHPType.DomesticDeparture, "Economy", 4, false, false);
            AddCheckIn("Kiosk", DHPType.InternationalDeparture, "Economy", 3, false, true);

            List<PoolPlan> checkInPlans = new List<PoolPlan>
            {
                new PoolPlan { Channel = "Counter", PoolKey = "DomesticDeparture|Business", PhysicalResources = 2, TargetWait95 = 3.0, Range = new ResourceSearchRange(1, 8) },
                new PoolPlan { Channel = "Counter", PoolKey = "DomesticDeparture|Economy", PhysicalResources = 3, TargetWait95 = 10.0, Range = new ResourceSearchRange(2, 9) },
                new PoolPlan { Channel = "Counter", PoolKey = "InternationalDeparture|Business", PhysicalResources = 4, TargetWait95 = 3.0, Range = new ResourceSearchRange(3, 10) },
                new PoolPlan { Channel = "Counter", PoolKey = "InternationalDeparture|Economy", PhysicalResources = 5, TargetWait95 = 10.0, Range = new ResourceSearchRange(4, 11) },
                new PoolPlan { Channel = "Kiosk", PhysicalResources = 6, TargetWait95 = 2.0, Range = new ResourceSearchRange(5, 12) }
            };

            CheckInSolver checkInSolver = new CheckInSolver();

            CheckInResults planned = checkInSolver.Solve(new CheckInInputs
            {
                Passengers = checkInPassengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolPlans = checkInPlans,
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(planned.PoolNames.SequenceEqual(new[]
                {
                    "Counter|DomesticDeparture|Business",
                    "Counter|DomesticDeparture|Economy",
                    "Counter|InternationalDeparture|Business",
                    "Counter|InternationalDeparture|Economy",
                    "Kiosk"
                }),
                "Keyed Check-In plans define the four counter subpools plus Kiosk");
            Check(planned.PhysicalPerChannel.SequenceEqual(new[] { 2, 3, 4, 5, 6 }),
                "Nominal resources come from each plan's PNR");
            Check(planned.NominalPerChannel.SequenceEqual(new[] { 2, 3, 4, 5, 6 }),
                "Nominal resources are reported separately from the simulated count");
            Check(planned.PhysicalCounters == 20,
                $"Physical total is the sum of the planned pools ({planned.PhysicalCounters} == 20)");
            Check(!checkInSolver.Warnings.Any(w => w.Contains("received no passengers")),
                "Every configured pool receives its routed demand");
            Check(!checkInSolver.Warnings.Any(w => w.Contains("no pool plan")),
                "Every routed channel is covered by a plan");

            Check(planned.QueueResults.ProcessedPassengers.All(p =>
                    planned.PoolNames.Contains(p.AssignedPool, StringComparer.OrdinalIgnoreCase)),
                "The solver tags every processed passenger with its exact pool");

            List<ProcessorTimelinePool> plannedAllocation =
                ProcessorTimeline.Build(planned.QueueResults);

            Check(plannedAllocation.Count == planned.PoolNames.Count,
                $"Processor allocation keeps every planned pool ({plannedAllocation.Count})");

            int plannedServed = planned.QueueResults.ProcessedPassengers.Count(p => !p.IsWarmUp);

            Check(plannedAllocation.Sum(p => p.Blocks.Count) == plannedServed,
                $"Keyed pools allocate all served passengers ({plannedAllocation.Sum(p => p.Blocks.Count)} == {plannedServed})");

            bool plannedCountsMatch = planned.QueueResults.PoolResults.All(pool =>
            {
                ProcessorTimelinePool timeline = plannedAllocation.FirstOrDefault(p =>
                    string.Equals(p.Name, pool.Name, StringComparison.OrdinalIgnoreCase));

                return timeline != null && timeline.Blocks.Count == pool.PassengerCount;
            });

            Check(plannedCountsMatch,
                "Each planned pool's processor blocks match its own served count");

            CheckInResults plannedOptimized = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = checkInPassengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolPlans = checkInPlans,
                OptimizeResources = true,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            bool checkInDomainsRespected = plannedOptimized.SearchOutcomes.Count == 5;

            for (int i = 0; i < plannedOptimized.SearchOutcomes.Count; i++)
            {
                PoolPlan expected = checkInPlans[i];
                ResourceSearchOutcome outcome = plannedOptimized.SearchOutcomes[i];

                bool inside = outcome.OptimalPhysicalResources >= expected.Range.Min &&
                              outcome.OptimalPhysicalResources <= expected.Range.Max;
                bool target = Math.Abs(outcome.Plan.TargetWait95 - expected.TargetWait95) < 1e-9;

                if (!inside || !target)
                    checkInDomainsRespected = false;
            }

            Check(checkInDomainsRespected,
                "Each planned pool honours its own PTW and PDomain during optimization");
            Check(plannedOptimized.NominalPerChannel.SequenceEqual(planned.NominalPerChannel),
                "PNR is retained as a benchmark when optimization is on");

            // Missing plan and empty pool diagnostics.
            CheckInSolver uncovered = new CheckInSolver();
            uncovered.Solve(new CheckInInputs
            {
                Passengers = checkInPassengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolPlans = checkInPlans.Where(p => p.Channel != "Kiosk").ToList(),
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(uncovered.Warnings.Any(w => w.Contains("Kiosk") && w.Contains("no pool plan")),
                "A routed channel without a plan is reported");

            CheckInSolver emptyPool = new CheckInSolver();
            emptyPool.Solve(new CheckInInputs
            {
                Passengers = checkInPassengers.Where(p => p.Channel != "Kiosk").Select(p => p.Clone()).ToList(),
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Counter", "Kiosk" },
                PoolPlans = checkInPlans,
                OptimizeResources = false,
                Seed = 7,
                StaffingPercentage = 100.0
            });

            Check(emptyPool.Warnings.Any(w => w.Contains("Kiosk") && w.Contains("received no passengers")),
                "A plan with no demand is reported");

            // ---------------------------------------------------------
            // Security: keyed plans and per-pool domains
            // ---------------------------------------------------------
            List<Passenger> securityPassengers = new List<Passenger>();

            for (int i = 0; i < 30; i++)
            {
                securityPassengers.Add(new Passenger
                {
                    Id = 1000 + i,
                    Channel = i % 2 == 0 ? "Standard" : "Fast",
                    DHPType = DHPType.DomesticDeparture,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            List<PoolPlan> securityPlans = new List<PoolPlan>
            {
                new PoolPlan { Channel = "Standard", PhysicalResources = 4, TargetWait95 = 20.0, Range = new ResourceSearchRange(2, 10) },
                new PoolPlan { Channel = "Fast", PhysicalResources = 2, TargetWait95 = 5.0, Range = new ResourceSearchRange(1, 6) }
            };

            SecurityResults secured = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = securityPassengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                Channels = new List<string> { "Standard", "Fast" },
                PoolPlans = securityPlans,
                OptimizeResources = false,
                Seed = 11,
                StaffingPercentage = 100.0
            });

            Check(secured.PoolNames.SequenceEqual(new[] { "Standard", "Fast" }),
                "Security consumes keyed plans and reports plan-derived pool names");
            Check(secured.PhysicalPerChannel.SequenceEqual(new[] { 4, 2 }) &&
                  secured.NominalPerChannel.SequenceEqual(new[] { 4, 2 }),
                "Security applies each plan's PNR and reports it as nominal");

            // ---------------------------------------------------------
            // Bag Drop: keyed plans
            // ---------------------------------------------------------
            List<Passenger> bagPassengers = new List<Passenger>();

            for (int i = 0; i < 20; i++)
            {
                bagPassengers.Add(new Passenger
                {
                    Id = 2000 + i,
                    Channel = "Web",
                    DHPType = DHPType.DomesticDeparture,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2,
                    UsesOnlineCheckIn = true,
                    HasCheckedBag = true
                });
            }

            BagDropResults bagged = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = bagPassengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = "BagDrop", PhysicalResources = 3, TargetWait95 = 3.0, Range = new ResourceSearchRange(1, 7) }
                },
                OptimizeResources = false,
                Seed = 13,
                StaffingPercentage = 100.0
            });

            Check(bagged.PoolNames.SequenceEqual(new[] { "BagDrop" }) &&
                  bagged.PhysicalPerChannel.SequenceEqual(new[] { 3 }),
                "Bag Drop applies its keyed plan and nominal count");

            // ---------------------------------------------------------
            // Emigration: keyed plans for e-gate and counter
            // ---------------------------------------------------------
            List<Passenger> emigrationPassengers = new List<Passenger>();

            for (int i = 0; i < 24; i++)
            {
                emigrationPassengers.Add(new Passenger
                {
                    Id = 3000 + i,
                    Channel = "Counter",
                    DHPType = i % 2 == 0 ? DHPType.InternationalDeparture : DHPType.InternationalArrival,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2,
                    PassengerType = PassengerType.International
                });
            }

            EmigrationResults emigrated = new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = emigrationPassengers,
                ServiceProfile = new ServiceProfile { MeanServiceTime = 1.0, StandardDeviation = 0.0 },
                EGateSharePercentage = 50.0,
                Channels = new List<string> { EmigrationSolver.EGatePool, EmigrationSolver.CounterPool },
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan { Channel = EmigrationSolver.EGatePool, PhysicalResources = 2, TargetWait95 = 2.0, Range = new ResourceSearchRange(1, 4) },
                    new PoolPlan { Channel = EmigrationSolver.CounterPool, PhysicalResources = 5, TargetWait95 = 10.0, Range = new ResourceSearchRange(2, 8) }
                },
                OptimizeResources = false,
                Seed = 17,
                StaffingPercentage = 100.0
            });

            Check(emigrated.PoolNames.SequenceEqual(new[]
                {
                    EmigrationSolver.EGatePool,
                    EmigrationSolver.CounterPool
                }),
                "Emigration consumes keyed plans and reports plan-derived pool names");
            Check(emigrated.PhysicalPerChannel.SequenceEqual(new[] { 2, 5 }) &&
                  emigrated.NominalPerChannel.SequenceEqual(new[] { 2, 5 }),
                "Emigration applies each plan's PNR and reports it as nominal");
            Check(emigrated.QueueResults.ProcessedPassengers.Count == 12 &&
                  emigrated.QueueResults.ProcessedPassengers.All(p =>
                      p.DHPType == DHPType.InternationalDeparture),
                "Emigration serves international departures only");
            Check(emigrated.QueueResults.ProcessedPassengers.Count(p =>
                      p.EmigrationRoute == EmigrationSolver.EGatePool) == 6 &&
                  emigrated.QueueResults.ProcessedPassengers.Count(p =>
                      p.EmigrationRoute == EmigrationSolver.CounterPool) == 6,
                "Emigration applies the e-gate share across the departing population");
            Check(emigrated.QueueResults.ProcessedPassengers.All(p => p.Channel == "Counter"),
                "Emigration restores the upstream check-in channel");
            Check(emigrated.PoolResults.Count == 2 &&
                  emigrated.PoolResults.All(p => p.ProcessedPassengers.Count > 0),
                "Both e-gate and counter pools receive passengers without an upstream EGate channel");
        }

        // -------------------------------------------------------------
        // Facility-scoped pool plans and service times
        // -------------------------------------------------------------
        private static void RunFacilityScoping()
        {
            Console.WriteLine("== Facility scoping ==");

            ResourceSearchRange fallback = new ResourceSearchRange(1, 20);
            List<string> warnings = new List<string>();

            // One shared list carrying the same identity at two facilities.
            List<PoolPlan> scoped = new List<PoolPlan>
            {
                new PoolPlan
                {
                    Facility = "Emigration", Channel = "EGate",
                    PhysicalResources = 4, TargetWait95 = 5.0
                },
                new PoolPlan
                {
                    Facility = "Emigration", Channel = "Counter",
                    PhysicalResources = 12, TargetWait95 = 10.0
                },
                new PoolPlan
                {
                    Facility = "Security", Channel = "EGate",
                    PhysicalResources = 2, TargetWait95 = 2.0
                },
                new PoolPlan
                {
                    Facility = "Security", Channel = "ATRS",
                    PhysicalResources = 6, TargetWait95 = 3.0
                }
            };

            List<ChannelResourcePlan> emigration =
                PoolPlanResolver.Resolve(scoped, fallback, warnings.Add, "Emigration");

            Check(emigration.Count == 2 &&
                  emigration[0].PhysicalResources == 4 && emigration[1].PhysicalResources == 12,
                "Facility scoping gives Emigration its own EGate/Counter values");

            warnings.Clear();

            List<ChannelResourcePlan> security =
                PoolPlanResolver.Resolve(scoped, fallback, warnings.Add, "Security");

            Check(security.Count == 2 &&
                  security[0].PhysicalResources == 2 && security[1].PhysicalResources == 6 &&
                  security[1].Channel == "ATRS",
                "Facility scoping gives Security its own EGate/ATRS values");

            Check(warnings.Count == 0,
                "Facility-scoped rows with one identity per facility raise no duplicate warning");

            warnings.Clear();

            // A facility-tagged row wins over an untagged row with the same identity.
            List<ChannelResourcePlan> preferred = PoolPlanResolver.Resolve(
                new List<PoolPlan>
                {
                    new PoolPlan { Channel = "EGate", PhysicalResources = 1, TargetWait95 = 1.0 },
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "EGate",
                        PhysicalResources = 3, TargetWait95 = 3.0
                    }
                },
                fallback, warnings.Add, "Security");

            Check(preferred.Count == 1 && preferred[0].PhysicalResources == 3,
                "A facility-tagged plan wins over an untagged plan with the same identity");

            // Foreign-facility rows are ignored, and an empty match falls back.
            warnings.Clear();

            List<ChannelResourcePlan> foreign = PoolPlanResolver.Resolve(
                new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Emigration", Channel = "EGate",
                        PhysicalResources = 4, TargetWait95 = 5.0
                    }
                },
                fallback, warnings.Add, "Security");

            Check(foreign == null && warnings.Any(w => w.Contains("No pool plan belongs to 'Security'")),
                "Rows tagged for another facility fall back to legacy resolution with a warning");

            // Unscoped resolution still sees everything (legacy behaviour).
            warnings.Clear();

            List<ChannelResourcePlan> unscoped = PoolPlanResolver.Resolve(
                scoped, fallback, warnings.Add);

            Check(unscoped.Count == 4,
                "Unscoped resolution keeps every plan, tagged or not");

            // ---------------------------------------------------------
            // Service-time map scoping
            // ---------------------------------------------------------
            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 1.0 },
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "EGate",
                        Facility = "Emigration",
                        Profile = new ServiceProfile { MeanServiceTime = 25.0 / 60.0 }
                    },
                    new ServiceTimeEntry
                    {
                        Key = "EGate",
                        Facility = "Security",
                        Profile = new ServiceProfile { MeanServiceTime = 7.0 / 60.0 }
                    },
                    new ServiceTimeEntry
                    {
                        Key = "ATRS",
                        Profile = new ServiceProfile { MeanServiceTime = 20.0 / 60.0 }
                    }
                }
            };

            Passenger emi = new Passenger { Channel = "EGate", ProfileName = "Economy" };

            double emigrationSeconds =
                map.ScopedTo("Emigration").Resolve(emi).MeanServiceTime * 60.0;
            double securitySeconds =
                map.ScopedTo("Security").Resolve(emi).MeanServiceTime * 60.0;

            Check(Math.Abs(emigrationSeconds - 25.0) < 1e-9,
                $"The Emigration-scoped map resolves EGate at 25 s ({emigrationSeconds})");
            Check(Math.Abs(securitySeconds - 7.0) < 1e-9,
                $"The Security-scoped map resolves EGate at 7 s ({securitySeconds})");

            Passenger atrs = new Passenger { Channel = "ATRS", ProfileName = "Economy" };

            Check(Math.Abs(map.ScopedTo("Security").Resolve(atrs).MeanServiceTime * 60.0 - 20.0) < 1e-9,
                "An untagged key applies at every facility");

            // A constant provider passes through the scoping untouched.
            IServiceTimeProvider constant = new ConstantServiceProvider(
                new ServiceProfile { MeanServiceTime = 2.0 });

            Check(ReferenceEquals(
                    ((FacilitySolverProbe)Activator.CreateInstance(typeof(FacilitySolverProbe)))
                        .ProbeResolve(constant, "Security"),
                    constant),
                "A non-map provider is returned without scoping");
        }

        private sealed class FacilitySolverProbe : FacilitySolverBase
        {
            public IServiceTimeProvider ProbeResolve(IServiceTimeProvider provider, string facility)
            {
                return ResolveServiceTimeProvider(provider, null, facility);
            }
        }

        // -------------------------------------------------------------
        // Service Time Map key levels: the same segregation grammar as
        // Pool Plans (stage-wide, sector-only, profile-only, full identity)
        // -------------------------------------------------------------
        private static void RunServiceTimeKeyLevels()
        {
            Console.WriteLine("== Service Time Map key levels ==");

            ServiceProfile Profile(double seconds)
            {
                return new ServiceProfile { MeanServiceTime = seconds / 60.0 };
            }

            Passenger Pax(string channel, DHPType dhp, string profile)
            {
                return new Passenger
                {
                    Channel = channel,
                    DHPType = dhp,
                    ProfileName = profile
                };
            }

            // ---- sector-only keys: 'Channel|DHPType' -------------------------
            ServiceTimeMap sector = new ServiceTimeMap
            {
                Default = Profile(99),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "EGate|DomesticDeparture", Profile = Profile(7) },
                    new ServiceTimeEntry { Key = "EGate|InternationalDeparture", Profile = Profile(8) },
                    new ServiceTimeEntry { Key = "ATRS|DomesticDeparture", Profile = Profile(20) },
                    new ServiceTimeEntry { Key = "ATRS|InternationalDeparture", Profile = Profile(22) }
                }
            };

            Passenger domestic = Pax("EGate", DHPType.DomesticDeparture, "Domestic Economy");
            Passenger intl = Pax("EGate", DHPType.InternationalDeparture, "International Economy");

            Check(Math.Abs(sector.Resolve(domestic).MeanServiceTime * 60 - 7) < 1e-9,
                "A sector-only key 'EGate|DomesticDeparture' matches any domestic profile");
            Check(Math.Abs(sector.Resolve(intl).MeanServiceTime * 60 - 8) < 1e-9,
                "A sector-only key 'EGate|InternationalDeparture' matches any international profile");
            Check(Math.Abs(sector.Resolve(Pax("ATRS", DHPType.DomesticDeparture, "Domestic Economy"))
                    .MeanServiceTime * 60 - 20) < 1e-9,
                "The same sector-only grammar resolves the ATRS stage");

            // ---- precedence: the specific key beats the stage-wide one -------
            ServiceTimeMap precedence = new ServiceTimeMap
            {
                Default = Profile(99),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "EGate", Profile = Profile(10) },
                    new ServiceTimeEntry { Key = "EGate|DomesticDeparture", Profile = Profile(7) }
                }
            };

            Check(Math.Abs(precedence.Resolve(domestic).MeanServiceTime * 60 - 7) < 1e-9,
                "A sector key outranks the stage-wide 'EGate' key");
            Check(Math.Abs(precedence.Resolve(Pax("EGate", DHPType.InternationalDeparture, "X"))
                    .MeanServiceTime * 60 - 10) < 1e-9,
                "The stage-wide 'EGate' key covers sectors with no key of their own");

            // ---- bare DHP key applies at every channel ------------------------
            ServiceTimeMap bare = new ServiceTimeMap
            {
                Default = Profile(99),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "DomesticDeparture", Profile = Profile(6) }
                }
            };

            Check(Math.Abs(bare.Resolve(Pax("EGate", DHPType.DomesticDeparture, "Domestic Economy"))
                    .MeanServiceTime * 60 - 6) < 1e-9 &&
                  Math.Abs(bare.Resolve(Pax("ATRS", DHPType.DomesticDeparture, "Domestic Economy"))
                    .MeanServiceTime * 60 - 6) < 1e-9,
                "A bare DHP key applies at every channel");

            // ---- profile-only key still works ---------------------------------
            ServiceTimeMap profileOnly = new ServiceTimeMap
            {
                Default = Profile(99),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "EGate|Domestic Economy", Profile = Profile(5) }
                }
            };

            Check(Math.Abs(profileOnly.Resolve(domestic).MeanServiceTime * 60 - 5) < 1e-9,
                "A 'Channel|Profile' key still resolves");

            // ---- TryResolve reports the silent miss ---------------------------
            ServiceTimeMap miss = new ServiceTimeMap
            {
                Default = Profile(50),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "Counter|DomesticDeparture|Domestic Economy", Profile = Profile(1) }
                }
            };

            Check(!miss.TryResolve(domestic, out ServiceProfile fallback) &&
                  ReferenceEquals(fallback, miss.Default),
                "TryResolve reports when no key matched and hands back the Default");
            Check(miss.TryResolve(domestic, out _) == false ||
                  miss.Resolve(domestic) != null,
                "Resolve still falls back to Default after a miss");

            // ---- full identity still wins over sector-only --------------------
            ServiceTimeMap full = new ServiceTimeMap
            {
                Default = Profile(99),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry { Key = "EGate|DomesticDeparture", Profile = Profile(7) },
                    new ServiceTimeEntry
                    {
                        Key = "EGate|DomesticDeparture|Domestic Business",
                        Profile = Profile(4)
                    }
                }
            };

            Check(Math.Abs(full.Resolve(Pax("EGate", DHPType.DomesticDeparture, "Domestic Business"))
                    .MeanServiceTime * 60 - 4) < 1e-9,
                "The full identity outranks the sector-only key");
            Check(Math.Abs(full.Resolve(domestic).MeanServiceTime * 60 - 7) < 1e-9,
                "The sector-only key covers profiles without a full identity");
        }

        // -------------------------------------------------------------
        // Facility tag scoping: lenient matching, loud failures
        // -------------------------------------------------------------
        private static void RunFacilityTagScoping()
        {
            Console.WriteLine("== Facility tag scoping ==");

            // ---- canonicalization accepts the planning-table spellings -------
            Check(FacilityName.Canonicalize("Security Check") == FacilityName.Security &&
                  FacilityName.Canonicalize("security  check") == FacilityName.Security,
                "'Security Check' canonicalizes to Security");

            Check(FacilityName.Canonicalize("Departure Emigration") == FacilityName.Emigration &&
                  FacilityName.Canonicalize("Arrival Immigration") == FacilityName.Immigration &&
                  FacilityName.Canonicalize("Check-in Area") == FacilityName.CheckIn &&
                  FacilityName.Canonicalize("Terminal Entry Gates") == FacilityName.TerminalEntry,
                "Planning-table spellings canonicalize to their facilities");

            Check(FacilityName.Canonicalize("Emmigration") == null,
                "An unknown tag canonicalizes to nothing");

            Check(FacilityName.Matches("Security Check", "Security") &&
                  FacilityName.Matches("SECURITY", "Security") &&
                  !FacilityName.Matches("Emigration", "Security"),
                "Lenient tag matching separates facilities");

            // ---- Pool Plans: a mistagged row is warned, not silently dropped --
            ResourceSearchRange fallback = new ResourceSearchRange(1, 20);
            List<string> warnings = new List<string>();

            List<ChannelResourcePlan> mistagged = PoolPlanResolver.Resolve(
                new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Departure Emigration", Channel = "EGate",
                        PhysicalResources = 4, TargetWait95 = 5.0
                    }
                },
                fallback, warnings.Add, "Emigration");

            Check(mistagged != null && mistagged.Count == 1 && mistagged[0].PhysicalResources == 4,
                "A planning-table tag 'Departure Emigration' now resolves for Emigration");

            warnings.Clear();

            List<ChannelResourcePlan> unknown = PoolPlanResolver.Resolve(
                new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Emmigreation", Channel = "EGate",
                        PhysicalResources = 4, TargetWait95 = 5.0
                    }
                },
                fallback, warnings.Add, "Emigration");

            Check(unknown == null &&
                  warnings.Any(w => w.Contains("matches no facility") && w.Contains("Emmigreation")) &&
                  warnings.Any(w => w.Contains("Valid facility names")),
                "A mistagged pool plan is reported with the valid names, not silently dropped");

            warnings.Clear();

            List<ChannelResourcePlan> empty = PoolPlanResolver.Resolve(
                new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "EGate",
                        PhysicalResources = 2, TargetWait95 = 2.0
                    }
                },
                fallback, warnings.Add, "Emigration");

            Check(empty == null &&
                  warnings.Any(w => w.Contains("No pool plan belongs to 'Emigration'") &&
                                    w.Contains("Security")),
                "A scope with no rows names the tags that exist elsewhere");

            // ---- Service Time Map: the same leniency and the same warnings ----
            ServiceProfile Profile(double seconds)
            {
                return new ServiceProfile { MeanServiceTime = seconds / 60.0 };
            }

            ServiceTimeMap table = new ServiceTimeMap
            {
                Default = Profile(10),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "EGate", Facility = "Departure Emigration", Profile = Profile(25)
                    },
                    new ServiceTimeEntry
                    {
                        Key = "EGate", Facility = "Security Check", Profile = Profile(7)
                    }
                }
            };

            Passenger emiGate = new Passenger
            {
                Channel = "EGate",
                DHPType = DHPType.InternationalDeparture,
                ProfileName = "Economy"
            };

            List<string> mapWarnings = new List<string>();

            double emigrationSeconds =
                table.ScopedTo("Emigration", mapWarnings.Add).Resolve(emiGate).MeanServiceTime * 60.0;

            Check(Math.Abs(emigrationSeconds - 25.0) < 1e-9,
                $"The planning-table tag gives Emigration its 25 s e-gate row ({emigrationSeconds})");

            Check(mapWarnings.Count == 0,
                "Correct planning-table tags raise no warning");

            double securitySeconds =
                table.ScopedTo("Security", mapWarnings.Add).Resolve(emiGate).MeanServiceTime * 60.0;

            Check(Math.Abs(securitySeconds - 7.0) < 1e-9,
                $"The same map scoped to Security resolves its own 7 s row ({securitySeconds})");

            ServiceTimeMap mistaggedMap = new ServiceTimeMap
            {
                Default = Profile(10),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "EGate", Facility = "Emmigreation", Profile = Profile(25)
                    }
                }
            };

            mapWarnings.Clear();

            double fallbackSeconds =
                mistaggedMap.ScopedTo("Emigration", mapWarnings.Add).Resolve(emiGate).MeanServiceTime * 60.0;

            Check(Math.Abs(fallbackSeconds - 10.0) < 1e-9 &&
                  mapWarnings.Any(w => w.Contains("matches no facility")) &&
                  mapWarnings.Any(w => w.Contains("No Service Time Map row belongs to 'Emigration'")),
                "A mistagged map row warns and the scope falls back to Default");

            // ---- a foreign untagged stage key is flagged ----------------------
            ServiceTimeMap leaked = new ServiceTimeMap
            {
                Default = Profile(10),
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "ATRS|DomesticDeparture", Profile = Profile(20)
                    }
                }
            };

            mapWarnings.Clear();

            leaked.ScopedTo("Emigration", mapWarnings.Add);

            Check(mapWarnings.Any(w =>
                    w.Contains("ATRS|DomesticDeparture") && w.Contains("tag the row 'Security'")),
                "An untagged Security stage key wired to Emigration is flagged before it leaks");
        }

        // -------------------------------------------------------------
        // Security as e-gate -> ATRS queues in series
        // -------------------------------------------------------------
        private static void RunSecuritySeries()
        {
            Console.WriteLine("== Security series ==");

            List<Passenger> passengers = new List<Passenger>();

            // Every fourth passenger is an arrival: arrivals never use security,
            // so the series stage must leave them untouched.
            for (int i = 0; i < 60; i++)
            {
                bool arrival = i % 4 == 3;

                passengers.Add(new Passenger
                {
                    Id = 4000 + i,
                    Channel = "Counter",
                    DHPType = arrival
                        ? DHPType.InternationalArrival
                        : DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            SecurityResults series = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = passengers,
                UseEGateSeries = true,
                EGatePhysicalResources = 2,
                EGateTargetWait95 = 2.0,
                EGateServiceTimeSeconds = 7.0,
                PhysicalResources = new List<int> { 3 },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(series.IsSeries && series.EGateResults != null && series.AtrsResults != null,
                "Series mode reports both stage results");

            Check(series.PoolNames.SequenceEqual(new[] { "EGate", "ATRS" }),
                "Series pools are named EGate and ATRS");

            Check(series.EGatePhysicalCount == 2 && series.AtrsPhysicalCount == 3 &&
                  series.PhysicalLanes == 5,
                $"Physical counts are per stage and sum facility-wide ({series.EGatePhysicalCount}/{series.AtrsPhysicalCount}/{series.PhysicalLanes})");

            List<Passenger> processed = series.QueueResults.ProcessedPassengers;

            int departures = 60 - 60 / 4;

            Check(processed.Count == departures,
                $"Series mode screens departing passengers only ({processed.Count} == {departures})");

            Check(processed.All(p => p.DHPType == DHPType.InternationalDeparture),
                "Series mode never serves arrival passengers");

            Check(processed.All(p => p.SecurityEntryRoute == "EGate") &&
                  processed.All(p => p.SecurityScreeningRoute == "ATRS"),
                "Every passenger carries both stage routes");

            Check(processed.All(p => p.Channel == "Counter"),
                "The upstream channel survives the checkpoint");

            Check(processed.All(p => p.History.ContainsKey(FacilityType.Security)),
                "The Security history is the screening stage's record");

            // The Security record carries BOTH stages: main fields are the
            // screening visit, entry fields are the e-gate visit.
            FacilityRecord securityRecord = processed[0].History[FacilityType.Security];

            Check(securityRecord.HasEntryStage &&
                  !string.IsNullOrWhiteSpace(securityRecord.EntryPoolName) &&
                  securityRecord.EntryEndServiceTime <= securityRecord.StartServiceTime + 1e9,
                "The Security history carries the e-gate stage in its entry fields");

            Check(Math.Abs(securityRecord.WaitTime - processed[0].SecurityScreeningWait) < 1e-9 &&
                  Math.Abs(securityRecord.EntryWaitTime - processed[0].SecurityEntryWait) < 1e-9,
                "History separates the screening wait (main) from the e-gate wait (entry)");

            Check(!string.IsNullOrWhiteSpace(securityRecord.PoolName),
                $"The Security history names the pool that served the visit ({securityRecord.PoolName})");

            // The screening stage's arrivals must start after the e-gate stage
            // has served: no passenger can enter ATRS before finishing a gate.
            double firstAtrsArrival = processed.Min(p => p.ArrivalTime);
            double firstGateFinish = series.EGateResults.ProcessedPassengers
                .Where(p => p.ServiceTime > 0)
                .Min(p => p.StartServiceTime + p.ServiceTime);

            Check(firstAtrsArrival >= firstGateFinish - 1e-9,
                $"No passenger reaches ATRS before clearing an e-gate ({firstAtrsArrival} >= {firstGateFinish})");

            // The service chain: screening arrival = gate end (zero walk).
            Passenger slowest = processed
                .OrderByDescending(p => p.StartServiceTime - p.ArrivalTime)
                .First();

            Check(processed.All(p => p.WaitTime >= 0 && p.SecurityEntryWait >= 0),
                "Both stage waits are recorded and non-negative");

            // Bottleneck warning: 3 ATRS lanes at 20 s serve 9 pax/3min-window;
            // the e-gate stage with 2 gates at 7 s is faster per pax, so ATRS
            // should hold the larger queue.
            Check(series.AtrsResults.PeakQueueLength >= series.EGateResults.PeakQueueLength,
                $"The slower ATRS stage holds the larger queue ({series.AtrsResults.PeakQueueLength} >= {series.EGateResults.PeakQueueLength})");

            // ---- single-stage behaviour is unchanged -------------------------
            List<Passenger> legacyPassengers = new List<Passenger>();

            for (int i = 0; i < 40; i++)
            {
                legacyPassengers.Add(new Passenger
                {
                    Id = 5000 + i,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            SecurityResults single = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = legacyPassengers,
                PhysicalResources = new List<int> { 4 },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(!single.IsSeries && single.EGateResults == null,
                "Series off keeps the single-stage behaviour");

            Check(single.QueueResults.ProcessedPassengers.Count == 40 &&
                  single.PoolNames.SequenceEqual(new[] { "Shared" }),
                "Single-stage security serves everyone through one shared pool");

            // ---- series with an ATRS target and optimization -----------------
            List<Passenger> optimized = new List<Passenger>();

            for (int i = 0; i < 80; i++)
            {
                optimized.Add(new Passenger
                {
                    Id = 6000 + i,
                    Channel = "Kiosk",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.1,
                    QueueArrivalTime = i * 0.1
                });
            }

            SecurityResults tuned = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = optimized,
                UseEGateSeries = true,
                EGatePhysicalResources = 2,
                EGateTargetWait95 = 2.0,
                EGateServiceTimeSeconds = 7.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "ATRS",
                        PhysicalResources = 4, TargetWait95 = 3.0,
                        Range = new ResourceSearchRange(1, 12)
                    }
                },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = true,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(tuned.IsSeries && tuned.AtrsPhysicalCount >= 1,
                "Series optimization sizes the ATRS stage from its scoped plan");

            Check(tuned.SearchOutcomes.Count >= 2,
                $"Both stages report optimizer outcomes ({tuned.SearchOutcomes.Count} >= 2)");

            // ---- four pools: sector split by PoolKey at both stages ----------
            // Domestic and international passengers queue separately at the
            // e-gate stage and at the ATRS stage: four pool identities, and
            // Physical per Pool / Active per Pool carry four entries.
            List<Passenger> sectorSplit = new List<Passenger>();

            for (int i = 0; i < 45; i++)
            {
                bool domestic = i % 2 == 0;

                sectorSplit.Add(new Passenger
                {
                    Id = 7000 + i,
                    Channel = "Counter",
                    DHPType = domestic
                        ? DHPType.DomesticDeparture
                        : DHPType.InternationalDeparture,
                    PassengerType = domestic
                        ? PassengerType.Domestic
                        : PassengerType.International,
                    ProfileName = domestic ? "Domestic Economy" : "International Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            List<PoolPlan> splitPlans = new List<PoolPlan>();

            foreach (string stage in new[] { "EGate", "ATRS" })
            {
                splitPlans.Add(new PoolPlan
                {
                    Facility = "Security",
                    Channel = stage,
                    PoolKey = "DomesticDeparture|Domestic Economy",
                    PhysicalResources = 1,
                    TargetWait95 = 3.0,
                    Range = new ResourceSearchRange(1, 6)
                });
                splitPlans.Add(new PoolPlan
                {
                    Facility = "Security",
                    Channel = stage,
                    PoolKey = "InternationalDeparture|International Economy",
                    PhysicalResources = 1,
                    TargetWait95 = 3.0,
                    Range = new ResourceSearchRange(1, 6)
                });
            }

            SecurityResults split = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = sectorSplit,
                UseEGateSeries = true,
                EGateServiceTimeSeconds = 7.0,
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                PoolPlans = splitPlans,
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(split.PoolNames.Count == 4,
                $"Four sector-split pool plans produce four pools ({split.PoolNames.Count} == 4)");

            Check(split.PoolNames.Count(n => n.StartsWith("EGate|", StringComparison.OrdinalIgnoreCase)) == 2 &&
                  split.PoolNames.Count(n => n.StartsWith("ATRS|", StringComparison.OrdinalIgnoreCase)) == 2,
                "Two pools per stage, one per sector");

            Check(split.PhysicalPerChannel.Count == 4 && split.ActivePerChannel.Count == 4 &&
                  split.Wait95PerChannel.Count == 4 &&
                  split.NominalPerChannel.Count == 4,
                "Physical, active, Wait95 and nominal outputs carry the four pools");

            int[] expectedPoolCounts = { 23, 22, 23, 22 };

            Check(split.QueueResults.ProcessedPassengers.Count == 45 &&
                  split.QueueResults.PoolResults.Select(p => p.PassengerCount)
                      .SequenceEqual(expectedPoolCounts),
                $"Every sector pool serves only its own sector ({string.Join(", ", split.QueueResults.PoolResults.Select(p => p.PassengerCount))})");

            Check(split.QueueResults.ProcessedPassengers.All(p =>
                    Math.Abs(p.WaitTime - (p.SecurityEntryWait + p.SecurityScreeningWait)) < 1e-9),
                "Checkpoint wait is the sum of both stage queues");

            Check(split.QueueResults.AverageWait > 0.0,
                $"The checkpoint queues produce waits ({split.QueueResults.AverageWait:0.###} min average)");

            // ---- sizing against the RAW demand, not the throttled stream -------
            // Arrivals every 0.05 min (1200/h) outrun a single 7 s e-gate
            // (~514/h), so stage 1 throttles. Raw demand needs at least
            // ceil(1200 h x 20 s / 3600) = 7 ATRS lanes just to stay stable;
            // a smoothed (released) stream would suggest far fewer. The
            // optimizer must therefore pick a clearly larger count than the
            // smoothed stream would.
            List<Passenger> rawDemand = new List<Passenger>();

            for (int i = 0; i < 100; i++)
            {
                rawDemand.Add(new Passenger
                {
                    Id = 9000 + i,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "International Economy",
                    ArrivalTime = i * 0.05,
                    QueueArrivalTime = i * 0.05
                });
            }

            SecuritySolver rawSolver = new SecuritySolver();

            SecurityResults rawSized = rawSolver.Solve(new SecurityInputs
            {
                Passengers = rawDemand,
                UseEGateSeries = true,
                EGatePhysicalResources = 1,
                EGateTargetWait95 = 0.0,
                EGateServiceTimeSeconds = 7.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "ATRS",
                        PhysicalResources = 1, TargetWait95 = 1.0,
                        Range = new ResourceSearchRange(1, 20)
                    }
                },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = true,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            int atrsChosen = rawSized.AtrsPhysicalCount;

            // The discriminator: sizing runs on the raw arrivals, so the chosen
            // ATRS count must not depend on how hard the e-gate stage throttles
            // the realized stream into the screening stage. Run the same model
            // with an e-gate bank that never throttles (10 gates at 7 s give
            // ~5140/h against the 1200/h demand): the ATRS search sees
            // identical raw demand either way, so both runs size it the same.
            SecuritySolver unthrottledSolver = new SecuritySolver();

            SecurityResults unthrottled = unthrottledSolver.Solve(new SecurityInputs
            {
                Passengers = rawDemand,
                UseEGateSeries = true,
                EGatePhysicalResources = 10,
                EGateTargetWait95 = 0.0,
                EGateServiceTimeSeconds = 7.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "ATRS",
                        PhysicalResources = 1, TargetWait95 = 1.0,
                        Range = new ResourceSearchRange(1, 20)
                    }
                },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = true,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            int atrsChosenUnthrottled = unthrottled.AtrsPhysicalCount;

            Check(atrsChosen == atrsChosenUnthrottled,
                $"ATRS is sized on the raw demand: {atrsChosen} lanes with a throttling e-gate vs " +
                $"{atrsChosenUnthrottled} with an unthrottled one");

            Check(rawSized.SearchOutcomes.Count >= 2,
                $"Both stages report optimizer outcomes ({rawSized.SearchOutcomes.Count} >= 2)");

            // The realized simulation still runs on the staged arrivals: no
            // passenger reaches ATRS before clearing an e-gate.
            List<Passenger> rawProcessed = rawSized.QueueResults.ProcessedPassengers;

            double firstRawAtrsArrival = rawProcessed.Min(p => p.ArrivalTime);
            double firstRawGateFinish = rawSized.EGateResults.ProcessedPassengers
                .Where(p => p.ServiceTime > 0)
                .Min(p => p.StartServiceTime + p.ServiceTime);

            Check(firstRawAtrsArrival >= firstRawGateFinish - 1e-9,
                $"The realized screening sim still follows the e-gate releases ({firstRawAtrsArrival} >= {firstRawGateFinish})");

            // The wired MQT survives the sizing/realized split.
            Check(rawSized.PoolNames.Contains("ATRS"),
                $"The ATRS pool result keeps its identity ({string.Join(", ", rawSized.PoolNames)})");

            Check(rawSized.NominalPerChannel.Contains(1),
                "Nominal reports the wired PNR, not the optimized count");

            // ---- the Service Time Map is scoped to Security in series --------
            // Emigration also owns an 'EGate' row and it is listed first; the
            // series stages must ignore it and use Security's own rows.
            ServiceTimeMap stageMap = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 20.0 / 60.0, StandardDeviation = 0.0 },
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "EGate", Facility = "Emigration",
                        Profile = new ServiceProfile { MeanServiceTime = 25.0 / 60.0, StandardDeviation = 0.0 }
                    },
                    new ServiceTimeEntry
                    {
                        Key = "EGate", Facility = "Security",
                        Profile = new ServiceProfile { MeanServiceTime = 7.0 / 60.0, StandardDeviation = 0.0 }
                    },
                    new ServiceTimeEntry
                    {
                        Key = "ATRS", Facility = "Security",
                        Profile = new ServiceProfile { MeanServiceTime = 20.0 / 60.0, StandardDeviation = 0.0 }
                    }
                }
            };

            List<Passenger> mapPassengers = new List<Passenger>();

            for (int i = 0; i < 40; i++)
            {
                mapPassengers.Add(new Passenger
                {
                    Id = 7500 + i,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            SecurityResults scoped = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = mapPassengers,
                UseEGateSeries = true,
                ServiceTimeProvider = stageMap,
                PhysicalResources = new List<int> { 4 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            double gateSeconds = scoped.EGateResults.ProcessedPassengers
                .Average(p => p.ServiceTime) * 60.0;
            double screeningSeconds = scoped.AtrsResults.ProcessedPassengers
                .Average(p => p.ServiceTime) * 60.0;

            Check(Math.Abs(gateSeconds - 7.0) < 0.01,
                $"The series e-gate ignores another facility's 'EGate' map row ({gateSeconds:0.##} s == 7)");

            Check(Math.Abs(screeningSeconds - 20.0) < 0.01,
                $"The series ATRS uses its own scoped map row ({screeningSeconds:0.##} s == 20)");

            // ---- sector-only map keys give the stages different times --------
            // Keys 'EGate|DomesticDeparture' (7 s) and 'ATRS|DomesticDeparture'
            // (20 s) resolve per sector; the miss case warns instead of
            // silently running both stages on the Default profile.
            ServiceTimeMap sectorMap = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 12.0 / 60.0, StandardDeviation = 0.0 },
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "EGate|DomesticDeparture",
                        Profile = new ServiceProfile { MeanServiceTime = 7.0 / 60.0, StandardDeviation = 0.0 }
                    },
                    new ServiceTimeEntry
                    {
                        Key = "ATRS|DomesticDeparture",
                        Profile = new ServiceProfile { MeanServiceTime = 20.0 / 60.0, StandardDeviation = 0.0 }
                    }
                }
            };

            List<Passenger> sectorMapPassengers = new List<Passenger>();

            for (int i = 0; i < 40; i++)
            {
                sectorMapPassengers.Add(new Passenger
                {
                    Id = 8500 + i,
                    Channel = "Counter",
                    DHPType = DHPType.DomesticDeparture,
                    PassengerType = PassengerType.Domestic,
                    ProfileName = "Domestic Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            SecuritySolver sectorSolver = new SecuritySolver();

            SecurityResults sectorTimed = sectorSolver.Solve(new SecurityInputs
            {
                Passengers = sectorMapPassengers,
                UseEGateSeries = true,
                ServiceTimeProvider = sectorMap,
                PhysicalResources = new List<int> { 4 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            double sectorGateSeconds = sectorTimed.EGateResults.ProcessedPassengers
                .Average(p => p.ServiceTime) * 60.0;
            double sectorScreenSeconds = sectorTimed.AtrsResults.ProcessedPassengers
                .Average(p => p.ServiceTime) * 60.0;

            Check(Math.Abs(sectorGateSeconds - 7.0) < 0.01 &&
                  Math.Abs(sectorScreenSeconds - 20.0) < 0.01,
                $"Sector-only map keys give each stage its own time ({sectorGateSeconds:0.##} s / {sectorScreenSeconds:0.##} s)");

            Check(!sectorSolver.Warnings.Any(w =>
                    w.Contains("No Service Time Map key matched")),
                "Matching sector keys raise no unmatched-key warning");

            ServiceTimeMap wrongMap = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 12.0 / 60.0, StandardDeviation = 0.0 },
                Entries = new List<ServiceTimeEntry>
                {
                    new ServiceTimeEntry
                    {
                        Key = "SecurityGate|DomesticDeparture",
                        Profile = new ServiceProfile { MeanServiceTime = 7.0 / 60.0, StandardDeviation = 0.0 }
                    }
                }
            };

            SecuritySolver missSolver = new SecuritySolver();

            missSolver.Solve(new SecurityInputs
            {
                Passengers = sectorMapPassengers,
                UseEGateSeries = true,
                ServiceTimeProvider = wrongMap,
                PhysicalResources = new List<int> { 4 },
                OptimizeResources = false,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(missSolver.Warnings.Count(w =>
                    w.Contains("No Service Time Map key matched")) == 2,
                "A map whose keys match nothing warns per stage instead of failing silently");

            // ---- a zero target keeps the plan's nominal capacity -------------
            List<Passenger> nominalPassengers = new List<Passenger>();

            for (int i = 0; i < 40; i++)
            {
                nominalPassengers.Add(new Passenger
                {
                    Id = 8000 + i,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.2,
                    QueueArrivalTime = i * 0.2
                });
            }

            SecurityResults nominalKept = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = nominalPassengers,
                UseEGateSeries = true,
                EGateServiceTimeSeconds = 7.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "EGate",
                        PhysicalResources = 2, TargetWait95 = 2.0,
                        Range = new ResourceSearchRange(1, 10)
                    },
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "ATRS",
                        PhysicalResources = 5, TargetWait95 = 0.0,
                        Range = new ResourceSearchRange(1, 60)
                    }
                },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = true,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(nominalKept.AtrsPhysicalCount == 5,
                $"A zero target keeps the ATRS nominal capacity ({nominalKept.AtrsPhysicalCount} == 5)");

            ResourceSearchOutcome atrsNominal = nominalKept.SearchOutcomes
                .FirstOrDefault(o => o.Plan != null && o.Plan.Channel == "ATRS");

            Check(atrsNominal != null && atrsNominal.Success,
                "A zero target is not reported as an unreachable search");

            // ---- the bottleneck warning works both ways ----------------------
            // One forced e-gate against 30 pax/min makes the e-gate the slow
            // stage; ATRS is sized on the released stream and looks quiet.
            List<Passenger> gateBound = new List<Passenger>();

            for (int i = 0; i < 600; i++)
            {
                gateBound.Add(new Passenger
                {
                    Id = 8500 + i,
                    Channel = "Counter",
                    DHPType = DHPType.InternationalDeparture,
                    PassengerType = PassengerType.International,
                    ProfileName = "Economy",
                    ArrivalTime = i * (20.0 / 600.0),
                    QueueArrivalTime = i * (20.0 / 600.0)
                });
            }

            SecuritySolver gateBoundSolver = new SecuritySolver();

            SecurityResults gateBoundResult = gateBoundSolver.Solve(new SecurityInputs
            {
                Passengers = gateBound,
                UseEGateSeries = true,
                EGateServiceTimeSeconds = 7.0,
                PoolPlans = new List<PoolPlan>
                {
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "EGate",
                        PhysicalResources = 1, TargetWait95 = 2.0,
                        Range = new ResourceSearchRange(1, 1)
                    },
                    new PoolPlan
                    {
                        Facility = "Security", Channel = "ATRS",
                        PhysicalResources = 6, TargetWait95 = 5.0,
                        Range = new ResourceSearchRange(1, 60)
                    }
                },
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 20.0 / 60.0,
                    StandardDeviation = 0.0
                },
                OptimizeResources = true,
                Seed = 5,
                StaffingPercentage = 100.0
            });

            Check(gateBoundResult.EGateResults.Wait95 >
                  gateBoundResult.AtrsResults.Wait95 * 2.0,
                "A forced single e-gate outwaits the ATRS stage it feeds");

            Check(gateBoundSolver.Warnings.Any(w =>
                    w.IndexOf("e-gate stage", StringComparison.OrdinalIgnoreCase) >= 0),
                "An e-gate bottleneck raises its own warning");

            // ---- schedule-mode gates: sector sum and concurrent floor ---------
            FlightScheduleResult gateSchedule = new FlightScheduleResult
            {
                ScheduleName = "Sector-sharing schedule",
                Legs = new List<FlightLeg>
                {
                    GateTestLeg("S1-C", LegDirection.Arrival, 8 * 60, LegSector.Domestic),
                    GateTestLeg("S1-C", LegDirection.Departure, 9 * 60, LegSector.Domestic),
                    GateTestLeg("S2-C", LegDirection.Arrival, 8 * 60, LegSector.International),
                    GateTestLeg("S2-C", LegDirection.Departure, 9 * 60, LegSector.International)
                }
            };

            foreach (FlightLeg leg in gateSchedule.Legs)
                leg.Terminal = "T2";

            GateScheduleDemand sharing = GateScheduleDemandBuilder.Build(
                gateSchedule, "T2", 5.0, 5.0, 120.0);

            GateScheduleCodeDemand sharingC = sharing.Codes.First(c => c.Code == "C");

            Check(sharingC.DomesticPeak == 1 && sharingC.InternationalPeak == 1 &&
                  sharingC.CombinedPeak == 2 && sharingC.SectorSum == 2,
                $"Sector sum is reported alongside the concurrent peak ({sharingC.SectorSum} == {sharingC.CombinedPeak})");

            Check(sharingC.Swing == 0 && sharingC.ConcurrentFloorMet &&
                  sharingC.DedicatedDomestic == 1 && sharingC.DedicatedInternational == 1,
                "Coincident sector peaks need no swing and meet the concurrent floor");

            GateSizingResult sizedSharing = GateCalculator.Calculate(
                new List<GateCodeInput>(), new GateStandards(), sharing);

            Check(sizedSharing.ScheduleMode &&
                  sizedSharing.ConcurrentPeakTotal == 2 &&
                  sizedSharing.SectorSumTotal == 2 &&
                  sizedSharing.ActiveTotal >= sizedSharing.ConcurrentPeakTotal,
                "The sizing result reports the concurrent peak and sector sum totals");
        }

        // -------------------------------------------------------------
        // Processor allocation timeline (chart data preparation)
        // -------------------------------------------------------------
        private static void RunProcessorTimeline()
        {
            Console.WriteLine("== Processor allocation timeline ==");

            QueueResults results = new QueueResults
            {
                ProcessedPassengers = new List<Passenger>
                {
                    new Passenger
                    {
                        Id = 1, Channel = "Counter", ProfileName = "Domestic Economy",
                        AssignedCounter = 0, StartServiceTime = 10.0, EndServiceTime = 14.0
                    },
                    new Passenger
                    {
                        Id = 2, Channel = "Counter", ProfileName = "Domestic Economy",
                        AssignedCounter = 1, StartServiceTime = 11.0, EndServiceTime = 12.5
                    },
                    new Passenger
                    {
                        Id = 3, Channel = "Counter", ProfileName = "Domestic Business",
                        AssignedCounter = 0, StartServiceTime = 15.0, EndServiceTime = 16.0
                    },
                    new Passenger
                    {
                        Id = 4, Channel = "Kiosk", ProfileName = "Domestic Economy",
                        AssignedCounter = 0, StartServiceTime = 10.5, EndServiceTime = 11.5
                    },
                    new Passenger
                    {
                        Id = 5, Channel = "Counter", ProfileName = "Domestic Economy",
                        AssignedCounter = 0, StartServiceTime = 9.0, EndServiceTime = 9.0
                    },
                    new Passenger
                    {
                        Id = 6, Channel = "Counter", ProfileName = "Domestic Economy",
                        AssignedCounter = 0, StartServiceTime = 8.0, EndServiceTime = 9.0,
                        IsWarmUp = true
                    },
                    new Passenger
                    {
                        Id = 7, Channel = "Counter", ProfileName = "Domestic Economy",
                        AssignedCounter = -1, StartServiceTime = 9.0, EndServiceTime = 10.0
                    }
                },
                Resources = new List<QueueResource>
                {
                    new QueueResource { Id = 0 },
                    new QueueResource { Id = 1 },
                    new QueueResource { Id = 0 }
                },
                ResourcesUsed = 3,
                ProcessorPools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo
                    {
                        Name = "Counter", ResourceCount = 2, FirstResourceIndex = 0
                    },
                    new ProcessorPoolInfo
                    {
                        Name = "Kiosk", ResourceCount = 1, FirstResourceIndex = 2
                    }
                },
                QueueTimeline = new List<QueueTimelinePoint>
                {
                    new QueueTimelinePoint(10.0, 1),
                    new QueueTimelinePoint(12.0, 0)
                }
            };

            List<ProcessorTimelinePool> pools = ProcessorTimeline.Build(results);

            Check(pools.Count == 2,
                $"Two processor pools built ({pools.Count})");

            Check(pools[0].Name == "Counter" && pools[0].ProcessorCount == 2,
                $"Counter pool keeps its own processor count ({pools[0].ProcessorCount})");

            Check(pools[1].Name == "Kiosk" && pools[1].ProcessorCount == 1,
                $"Kiosk pool keeps its own processor count ({pools[1].ProcessorCount})");

            Check(pools[0].Blocks.Count == 3,
                $"Counter blocks = 3 served passengers ({pools[0].Blocks.Count})");

            Check(pools[1].Blocks.Count == 1,
                $"Kiosk blocks = 1 served passenger ({pools[1].Blocks.Count})");

            Check(pools[0].Blocks.All(b => b.ProcessorId >= 0 && b.ProcessorId < pools[0].ProcessorCount),
                "Counter processor IDs stay inside the pool");

            Check(pools.All(p => p.Blocks.All(b => b.EndTime > b.StartTime)),
                "Zero-length service intervals are excluded");

            Check(pools.All(p => p.Blocks.All(b => b.PassengerId != 6)),
                "Warm-up passengers are excluded by default");

            Check(pools.All(p => p.Blocks.All(b => b.PassengerId != 7)),
                "Passengers without a processor assignment are excluded");

            Check(pools[0].Blocks.Select(b => b.StartTime).SequenceEqual(new[] { 10.0, 11.0, 15.0 }),
                "Blocks are ordered by service start time");

            List<ProcessorTimelinePool> withWarmUp = ProcessorTimeline.Build(results, true);

            Check(withWarmUp[0].Blocks.Any(b => b.PassengerId == 6),
                "Warm-up passengers can be included explicitly");

            QueueResults fallback = new QueueResults
            {
                ResourcesUsed = 3,
                ProcessedPassengers = results.ProcessedPassengers
            };

            List<ProcessorTimelinePool> single = ProcessorTimeline.Build(fallback);

            Check(single.Count == 1 && single[0].IsShared && single[0].ProcessorCount == 3,
                $"Results without pool metadata fall back to one shared pool ({single.Count})");

            Check(ProcessorTimeline.Build(null).Count == 0,
                "Null results produce no pools");

            // Keyed sub-pools share their parent channel, so the bare channel
            // alone cannot identify the pool that served a passenger.
            QueueResults keyed = new QueueResults
            {
                ProcessedPassengers = new List<Passenger>
                {
                    new Passenger
                    {
                        Id = 8, Channel = "Counter", ProfileName = "Economy",
                        AssignedPool = "Counter|DomesticDeparture|Economy",
                        AssignedCounter = 0, StartServiceTime = 10.0, EndServiceTime = 11.0
                    },
                    new Passenger
                    {
                        Id = 9, Channel = "Counter", ProfileName = "Business",
                        AssignedPool = "Counter|DomesticDeparture|Business",
                        AssignedCounter = 0, StartServiceTime = 11.0, EndServiceTime = 12.0
                    },
                    new Passenger
                    {
                        Id = 10, Channel = "Kiosk", ProfileName = "Economy",
                        AssignedPool = "Kiosk",
                        AssignedCounter = 0, StartServiceTime = 12.0, EndServiceTime = 13.0
                    }
                },
                Resources = new List<QueueResource>
                {
                    new QueueResource { Id = 0 },
                    new QueueResource { Id = 0 },
                    new QueueResource { Id = 0 }
                },
                ResourcesUsed = 3,
                ProcessorPools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo
                    {
                        Name = "Counter|DomesticDeparture|Economy",
                        ResourceCount = 1, FirstResourceIndex = 0
                    },
                    new ProcessorPoolInfo
                    {
                        Name = "Counter|DomesticDeparture|Business",
                        ResourceCount = 1, FirstResourceIndex = 1
                    },
                    new ProcessorPoolInfo
                    {
                        Name = "Kiosk", ResourceCount = 1, FirstResourceIndex = 2
                    }
                }
            };

            List<ProcessorTimelinePool> keyedPools = ProcessorTimeline.Build(keyed);

            Check(keyedPools.Count == 3,
                $"Keyed results keep every pool ({keyedPools.Count})");

            Check(keyedPools.Sum(p => p.Blocks.Count) == 3,
                $"Every keyed-pool passenger is assigned to a processor band ({keyedPools.Sum(p => p.Blocks.Count)})");

            Check(keyedPools[0].Blocks.Count == 1 && keyedPools[0].Blocks[0].PassengerId == 8,
                "Economy service stays in Counter|DomesticDeparture|Economy");

            Check(keyedPools[1].Blocks.Count == 1 && keyedPools[1].Blocks[0].PassengerId == 9,
                "Business service stays in Counter|DomesticDeparture|Business");

            Check(keyedPools[2].Blocks.Count == 1 && keyedPools[2].Blocks[0].PassengerId == 10,
                "A bare channel pool still receives its passengers");

            List<Passenger> passengers = new List<Passenger>();

            for (int i = 0; i < 60; i++)
            {
                passengers.Add(new Passenger
                {
                    Id = i,
                    ProfileName = "Economy",
                    ArrivalTime = i * 0.5,
                    QueueArrivalTime = i * 0.5
                });
            }

            CheckInResults staffed = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = passengers,
                ServiceProfile = new ServiceProfile
                {
                    MeanServiceTime = 2.0,
                    StandardDeviation = 0.2
                },
                PhysicalResources = new List<int> { 10 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 3,
                StaffingPercentage = 70.0
            });

            Check(staffed.QueueResults.Resources.Count == 7,
                $"70% staffing of 10 counters opens 7 ({staffed.QueueResults.Resources.Count})");

            Check(staffed.QueueResults.ProcessorPools.Count == 1 &&
                  staffed.QueueResults.ProcessorPools[0].ResourceCount == 7,
                "Processor pool reports the staffed count, not the physical count");
        }

        // -------------------------------------------------------------
        // 14c. Facility area calculation (routed pools, net to gross)
        // -------------------------------------------------------------
        private static void RunFacilityArea()
        {
            Console.WriteLine("== Facility area ==");

            FacilityAreaStandardSet standards = new FacilityAreaStandardSet
            {
                DefaultQueueSpacePerPax = 1.2,
                CirculationPercent = 15.0,
                MepPercent = 10.0,
                StructurePercent = 5.0
            };

            standards.Units.Add(new FacilityUnitStandard
            {
                Key = "Counter",
                AreaPerUnit = 15.0,
                QueueSpacePerPax = 1.8
            });

            standards.Units.Add(new FacilityUnitStandard
            {
                Key = "Kiosk",
                AreaPerUnit = 2.0
            });

            FacilityAreaInput routed = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter", "Kiosk" },
                PhysicalPerPool = new List<int> { 10, 4 },
                ActivePerPool = new List<int> { 8, 4 },
                GlobalPeakQueue = 30,
                PeakQueueByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 25 },
                    { "Kiosk", 9 }
                },
                Standards = standards,
                FallbackAreaPerUnit = 3.0,
                FallbackQueueSpacePerPax = 1.2
            };

            FacilityAreaResult area = FacilityAreaCalculator.Calculate(routed);

            Check(area.Lines.Count == 2,
                $"Both routed pools are sized separately ({area.Lines.Count})");

            Check(Math.Abs(area.Lines[0].EquipmentArea - 150.0) < 1e-6,
                $"Counter equipment = 10 x 15 m2 = 150 m2 ({area.Lines[0].EquipmentArea})");

            Check(Math.Abs(area.Lines[1].EquipmentArea - 8.0) < 1e-6,
                $"Kiosk equipment = 4 x 2 m2 = 8 m2 ({area.Lines[1].EquipmentArea})");

            Check(Math.Abs(area.Lines[0].QueueArea - 45.0) < 1e-6,
                $"Counter queue = 25 waiting x 1.8 m2 = 45 m2 ({area.Lines[0].QueueArea})");

            Check(Math.Abs(area.Lines[1].QueueArea - 10.8) < 1e-6,
                $"Kiosk queue = 9 waiting x the default 1.2 m2 = 10.8 m2 ({area.Lines[1].QueueArea})");

            Check(Math.Abs(area.NetArea - 213.8) < 1e-6,
                $"Net area = 158 equipment + 55.8 queue = 213.8 m2 ({area.NetArea})");

            Check(area.UsedStandards,
                "Routed area reports that the unit standards were used");

            // A shared pool waits in one queue, so it is sized on the whole
            // facility's peak.
            FacilityAreaInput shared = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Shared" },
                PhysicalPerPool = new List<int> { 20 },
                GlobalPeakQueue = 30,
                Standards = standards,
                FallbackAreaPerUnit = 3.0
            };

            FacilityAreaResult sharedArea = FacilityAreaCalculator.Calculate(shared);

            Check(Math.Abs(sharedArea.Lines[0].QueueArea - 36.0) < 1e-6,
                $"Shared pool queue = the facility peak 30 x 1.2 m2 = 36 m2 ({sharedArea.Lines[0].QueueArea})");

            Check(Math.Abs(sharedArea.Lines[0].EquipmentArea - 60.0) < 1e-6,
                $"Shared pool has no standard, so the fallback 3 m2 was used ({sharedArea.Lines[0].EquipmentArea})");

            Check(sharedArea.Warnings.Any(w => w.Contains("no area standard")),
                "An unmatched pool is reported, not silently dropped");

            // No queue metric for the channel: the facility peak stands in.
            FacilityAreaInput missingQueue = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter" },
                PhysicalPerPool = new List<int> { 10 },
                GlobalPeakQueue = 30,
                Standards = standards
            };

            FacilityAreaResult missingArea = FacilityAreaCalculator.Calculate(missingQueue);

            Check(missingArea.Lines[0].QueuePax == 30,
                $"A channel with no queue metric falls back to the facility peak ({missingArea.Lines[0].QueuePax})");

            Check(missingArea.Warnings.Any(w => w.Contains("No queue metric")),
                "The missing queue metric is reported");

            // ---- the net to gross build-up, through a real sizer ----
            CheckInStandards checkInStandards = new CheckInStandards
            {
                Width = 1.5,
                Depth = 2.0,
                Pitch = 1.8,
                QueueAreaPerPassenger = 1.2,
                SupportPercent = 10.0,
                CirculationPercent = 15.0
            };

            FacilitySizingContext context = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 10,
                    ActiveProcessorCount = 8,
                    PeakQueue = 25,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = checkInStandards,
                AreaStandards = standards,
                UseRoutedPoolAreas = true,
                PoolNames = new List<string> { "Counter" },
                PhysicalPerPool = new List<int> { 10 },
                ActivePerPool = new List<int> { 8 },
                GlobalPeakQueue = 25,
                PeakQueueByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 25 }
                }
            };

            FacilitySizingResult routedResult = new CheckInSizer().SizeFacility(context);

            Check(Math.Abs(routedResult.QueueArea - 45.0) < 1e-6,
                $"Routed queue area = 25 x 1.8 m2 = 45 m2 ({routedResult.QueueArea})");

            Check(Math.Abs(routedResult.ProcessingArea - 150.0) < 1e-6,
                $"Routed processing area = 10 x 15 m2 = 150 m2 ({routedResult.ProcessingArea})");

            Check(Math.Abs(routedResult.SupportArea - 45.0) < 1e-6,
                $"Check-in support = the rear working space, 10 x 1.8 x 2.5 = 45 m2 ({routedResult.SupportArea})");

            Check(Math.Abs(routedResult.NetArea - 240.0) < 1e-6,
                $"Net = 45 + 150 + 45 = 240 m2 ({routedResult.NetArea})");

            Check(Math.Abs(routedResult.CirculationArea - 36.0) < 1e-6,
                $"Circulation = 15% of 240 = 36 m2 ({routedResult.CirculationArea})");

            Check(Math.Abs(routedResult.MepArea - 27.6) < 1e-6,
                $"MEP = 10% of 276 = 27.6 m2 ({routedResult.MepArea})");

            Check(Math.Abs(routedResult.StructureArea - 15.18) < 1e-6,
                $"Structure = 5% of 303.6 = 15.18 m2 ({routedResult.StructureArea})");

            Check(Math.Abs(routedResult.GrossArea - 318.78) < 1e-6,
                $"Gross = 303.6 + 15.18 = 318.78 m2 ({routedResult.GrossArea})");

            Check(Math.Abs(routedResult.TotalArea - routedResult.GrossArea) < 1e-9,
                "Total area is the gross area");

            Check(routedResult.PoolAreas.Count > 0,
                "The routed breakdown is reported");

            // Without area standards the facility's own rules are used, and
            // there is no MEP or structure allowance.
            FacilitySizingContext legacy = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 10,
                    ActiveProcessorCount = 8,
                    PeakQueue = 25,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = checkInStandards
            };

            FacilitySizingResult legacyResult = new CheckInSizer().SizeFacility(legacy);

            Check(Math.Abs(legacyResult.QueueArea - 30.0) < 1e-6,
                $"Legacy queue area = 25 x 1.2 m2 = 30 m2 ({legacyResult.QueueArea})");

            Check(Math.Abs(legacyResult.ProcessingArea - 36.0) < 1e-6,
                $"Legacy processing area = 10 x 1.8 x 2 = 36 m2 ({legacyResult.ProcessingArea})");

            Check(Math.Abs(legacyResult.TotalArea - 127.65) < 1e-6,
                $"Legacy total = 30 + 36 + 45 + 16.65 = 127.65 m2 ({legacyResult.TotalArea})");

            Check(Math.Abs(legacyResult.MepArea) < 1e-9 &&
                  Math.Abs(legacyResult.StructureArea) < 1e-9,
                "No MEP or structure allowance without area standards");

            // A fixed room is added as a lump, and is not the MEP allowance.
            FacilityAreaStandardSet withFixed = new FacilityAreaStandardSet
            {
                DefaultQueueSpacePerPax = 1.2,
                CirculationPercent = 15.0,
                MepPercent = 10.0,
                StructurePercent = 5.0
            };

            withFixed.Units.Add(new FacilityUnitStandard { Key = "Counter", AreaPerUnit = 15.0 });
            withFixed.FixedAreas.Add(new NamedArea { Name = "Reconciliation room", Area = 50.0 });

            FacilitySizingContext fixedContext = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 10,
                    PeakQueue = 25,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = checkInStandards,
                AreaStandards = withFixed,
                UseRoutedPoolAreas = true,
                PoolNames = new List<string> { "Counter" },
                PhysicalPerPool = new List<int> { 10 },
                GlobalPeakQueue = 25
            };

            FacilitySizingResult fixedResult = new CheckInSizer().SizeFacility(fixedContext);

            Check(Math.Abs(fixedResult.NetArea - 275.0) < 1e-6,
                $"A fixed room joins the net: 30 + 150 + 45 + 50 = 275 m2 ({fixedResult.NetArea})");

            Check(Math.Abs(fixedResult.MepArea - 31.625) < 1e-6,
                $"MEP rides on the net including the fixed room: 10% of 316.25 = 31.63 m2 ({fixedResult.MepArea})");
        }

        // -------------------------------------------------------------
        // 14d. Facility area wiring (pool fallbacks, mapping, routes)
        // -------------------------------------------------------------
        private static void RunFacilityAreaWiring()
        {
            Console.WriteLine("== Facility area wiring ==");

            FacilityAreaStandardSet standards = new FacilityAreaStandardSet
            {
                DefaultQueueSpacePerPax = 1.2,
                CirculationPercent = 15.0,
                MepPercent = 10.0,
                StructurePercent = 5.0
            };

            standards.Units.Add(new FacilityUnitStandard { Key = "Counter", AreaPerUnit = 15.0 });
            standards.Units.Add(new FacilityUnitStandard { Key = "Kiosk", AreaPerUnit = 2.0 });

            List<ProcessorPoolInfo> pools = new List<ProcessorPoolInfo>
            {
                new ProcessorPoolInfo
                {
                    Name = "Counter",
                    ResourceCount = 8,
                    PhysicalResourceCount = 10,
                    FirstResourceIndex = 0,
                    IsShared = false
                },
                new ProcessorPoolInfo
                {
                    Name = "Kiosk",
                    ResourceCount = 4,
                    PhysicalResourceCount = 4,
                    FirstResourceIndex = 8,
                    IsShared = false
                }
            };

            Dictionary<string, int> byChannel =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 25 },
                    { "Kiosk", 9 }
                };

            // Pool names only: the counts come from the queue results.
            FacilityAreaInput namesOnly = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter", "Kiosk" },
                Pools = pools,
                GlobalPeakQueue = 30,
                PeakQueueByChannel = byChannel,
                Standards = standards,
                FallbackAreaPerUnit = 3.0
            };

            FacilityAreaResult namesArea = FacilityAreaCalculator.Calculate(namesOnly);

            Check(Math.Abs(namesArea.Lines[0].EquipmentArea - 150.0) < 1e-6,
                $"A pool with no count takes its installed count from the results " +
                $"({namesArea.Lines[0].EquipmentArea})");

            Check(Math.Abs(namesArea.Lines[0].ActiveUnits - 8) < 1e-6,
                $"A pool with no staffed count takes it from the results too ({namesArea.Lines[0].ActiveUnits})");

            Check(Math.Abs(namesArea.Lines[1].EquipmentArea - 8.0) < 1e-6,
                $"The second pool falls back the same way ({namesArea.Lines[1].EquipmentArea})");

            // Explicit counts win over the results.
            FacilityAreaInput explicitCounts = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter", "Kiosk" },
                PhysicalPerPool = new List<int> { 12, 6 },
                ActivePerPool = new List<int> { 12, 6 },
                Pools = pools,
                GlobalPeakQueue = 30,
                PeakQueueByChannel = byChannel,
                Standards = standards
            };

            FacilityAreaResult explicitArea = FacilityAreaCalculator.Calculate(explicitCounts);

            Check(Math.Abs(explicitArea.Lines[0].EquipmentArea - 180.0) < 1e-6,
                $"An explicit count overrides the results ({explicitArea.Lines[0].EquipmentArea})");

            // A pool name that is not a unit standard can be mapped to one.
            FacilityAreaInput aliased = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Kiosk East" },
                PhysicalPerPool = new List<int> { 5 },
                FacilityKeyPerPool = new List<string> { "Kiosk" },
                Pools = pools,
                GlobalPeakQueue = 30,
                Standards = standards
            };

            FacilityAreaResult aliasArea = FacilityAreaCalculator.Calculate(aliased);

            Check(Math.Abs(aliasArea.Lines[0].EquipmentArea - 10.0) < 1e-6,
                $"A pool mapped to a unit standard is sized with it ({aliasArea.Lines[0].EquipmentArea})");

            Check(aliasArea.Lines[0].HasStandard,
                "A mapped pool reports that a standard was used");

            // A route with no physical counterpart contributes no equipment.
            FacilityAreaInput routeOnly = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter", "Web" },
                PhysicalPerPool = new List<int> { 10, 0 },
                NoPhysicalAreaPerPool = new List<bool> { false, true },
                Pools = pools,
                GlobalPeakQueue = 30,
                PeakQueueByChannel = byChannel,
                Standards = standards
            };

            FacilityAreaResult routeArea = FacilityAreaCalculator.Calculate(routeOnly);

            Check(Math.Abs(routeArea.Lines[1].EquipmentArea) < 1e-9,
                $"A route with no physical area contributes no equipment area ({routeArea.Lines[1].EquipmentArea})");

            Check(routeArea.Warnings.Any(w => w.Contains("no physical area")),
                "A route with no physical area is reported");

            // A pool with nothing to size is reported rather than ignored.
            FacilityAreaInput emptyPool = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Nothing" },
                Standards = standards
            };

            FacilityAreaResult emptyArea = FacilityAreaCalculator.Calculate(emptyPool);

            Check(emptyArea.Warnings.Any(w => w.Contains("no units and no queue metric")),
                "A pool with no units and no queue metric is reported");

            // A short list is reported, and the pools it misses still work.
            FacilityAreaInput shortList = new FacilityAreaInput
            {
                PoolNames = new List<string> { "Counter", "Kiosk" },
                PhysicalPerPool = new List<int> { 10 },
                Pools = pools,
                GlobalPeakQueue = 30,
                PeakQueueByChannel = byChannel,
                Standards = standards
            };

            FacilityAreaResult shortArea = FacilityAreaCalculator.Calculate(shortList);

            Check(shortArea.Warnings.Any(w => w.Contains("Physical per Pool")),
                "A count list shorter than the pool list is reported");

            Check(Math.Abs(shortArea.Lines[1].EquipmentArea - 8.0) < 1e-6,
                $"The pool the list misses still takes its count from the results ({shortArea.Lines[1].EquipmentArea})");

            // ---- a facility that cannot be sized from routed pools ----
            FacilitySizingContext securityContext = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 4,
                    PeakQueue = 10,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                Standards = new SecurityStandards
                {
                    Width = 1.5,
                    Depth = 2.0,
                    Pitch = 1.8,
                    QueueAreaPerPassenger = 1.2,
                    SupportPercent = 10.0,
                    CirculationPercent = 15.0,
                    IsGenderSegregated = false
                },
                AreaStandards = standards,
                UseRoutedPoolAreas = true,
                PoolNames = new List<string> { "Counter" },
                PhysicalPerPool = new List<int> { 10 },
                GlobalPeakQueue = 10
            };

            FacilitySizingResult securityResult = new SecuritySizer().SizeFacility(securityContext);

            Check(securityResult.Warnings.Any(w => w.Contains("not available for this facility type")),
                "Routed areas are refused by a facility that cannot use them");

            Check(Math.Abs(securityResult.ProcessingArea - 4.0 * 1.8 * (2.0 + 10.0)) < 1e-6,
                $"The facility's own rule - pitch x depth plus the divest allowance - is kept " +
                $"({securityResult.ProcessingArea})");

            // ---- end to end: a real check-in solve, then the areas ----
            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 3.0, StandardDeviation = 0.5 }
            };

            ProfileChannelMap routing = new ProfileChannelMap { DefaultChannel = "Counter" };
            routing.Entries.Add(new ProfileChannelEntry { ProfileName = "Solo" });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Counter", Share = 0.6 });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Kiosk", Share = 0.4 });

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1200, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Solo", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 33, routing);

            CheckInResults solved = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter", "Kiosk" },
                PhysicalResources = new List<int> { 20, 10 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0, 10.0 },
                Seed = 5,
                StaffingPercentage = 100.0
            });

            QueueResults qr = solved.QueueResults;

            Check(qr.ProcessorPools.Count == 2,
                $"The solved run carries two pools ({qr.ProcessorPools.Count})");

            FacilitySizingContext solvedContext = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = qr.PhysicalProcessorCount,
                    ActiveProcessorCount = qr.Resources.Count,
                    PeakQueue = qr.PeakQueueLength,
                    Wait95 = qr.Wait95,
                    AnalysisMethod = AnalysisMethod.Simulation
                },
                PassengerSummary = new PassengerSummaryData
                {
                    TotalPassengers = qr.ProcessedPassengers.Count
                },
                Standards = new CheckInStandards
                {
                    Width = 1.5,
                    Depth = 2.0,
                    Pitch = 1.8,
                    QueueAreaPerPassenger = 1.2,
                    SupportPercent = 10.0,
                    CirculationPercent = 15.0
                },
                AreaStandards = standards,
                UseRoutedPoolAreas = true,
                ProcessorPools = qr.ProcessorPools,
                PeakQueueByChannel = qr.ByChannel.ToDictionary(
                    k => k.Key, v => v.Value.PeakQueueLength),
                GlobalPeakQueue = qr.PeakQueueLength
            };

            FacilitySizingResult solvedResult = new CheckInSizer().SizeFacility(solvedContext);

            Check(solvedResult.PoolAreas.Count == 3,
                $"One line per pool plus the net line ({solvedResult.PoolAreas.Count})");

            Check(Math.Abs(solvedResult.ProcessingArea - (20 * 15.0 + 10 * 2.0)) < 1e-6,
                $"The solved run's pools are sized from their own standards ({solvedResult.ProcessingArea})");

            int counterPeak = qr.ByChannel["Counter"].PeakQueueLength;
            int kioskPeak = qr.ByChannel["Kiosk"].PeakQueueLength;

            Check(Math.Abs(solvedResult.QueueArea - (counterPeak * 1.2 + kioskPeak * 1.2)) < 1e-6,
                $"Each pool's waiting area uses its own channel peak ({solvedResult.QueueArea})");

            Check(solvedResult.Warnings.Count == 0,
                $"A correctly wired run reports no warnings ({string.Join(" | ", solvedResult.Warnings)})");
        }

        // -------------------------------------------------------------
        // 14e. Generic solver-count facility sizing
        // -------------------------------------------------------------
        private static void RunGenericFacilitySizing()
        {
            Console.WriteLine("== Generic facility sizing ==");

            FacilityAreaStandardSet standards = new FacilityAreaStandardSet
            {
                SupportPercent = 10.0,
                CirculationPercent = 15.0,
                MepPercent = 10.0,
                StructurePercent = 5.0
            };
            standards.Units.Add(new FacilityUnitStandard
            {
                Key = "Counter",
                AreaPerUnit = 15.0,
                QueueSpacePerPax = 1.2
            });
            standards.Units.Add(new FacilityUnitStandard
            {
                Key = "Kiosk",
                AreaPerUnit = 2.0,
                QueueSpacePerPax = 0.8
            });
            standards.FixedAreas.Add(new NamedArea { Name = "Store", Area = 4.0 });

            FacilitySizingContext context = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData
                {
                    ProcessorCount = 12,
                    ActiveProcessorCount = 9,
                    BaselineProcessorCount = 11
                },
                PassengerSummary = new PassengerSummaryData { TotalPassengers = 100 },
                AreaStandards = standards,
                PoolNames = new List<string> { "Counter", "Kiosk" },
                PhysicalPerPool = new List<int> { 8, 4 },
                ActivePerPool = new List<int> { 6, 3 },
                ProcessorPools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo
                    {
                        Name = "Counter",
                        PhysicalResourceCount = 99,
                        ResourceCount = 90,
                        IsShared = false
                    },
                    new ProcessorPoolInfo
                    {
                        Name = "Kiosk",
                        PhysicalResourceCount = 99,
                        ResourceCount = 90,
                        IsShared = false
                    }
                },
                GlobalPeakQueue = 15,
                PeakQueueByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 10 },
                    { "Kiosk", 5 }
                },
                PeakQueueByPool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 10 },
                    { "Kiosk", 5 }
                }
            };

            FacilitySizingResult result = new GenericFacilitySizer().SizeFacility(context);

            Check(Math.Abs(result.ProcessingArea - 128.0) < 1e-9,
                $"Installed counts come from solver outputs, not QR counts ({result.ProcessingArea})");
            Check(Math.Abs(result.QueueArea - 16.0) < 1e-9,
                $"Each pool uses its matching A-STD queue space ({result.QueueArea})");
            Check(Math.Abs(result.SupportArea - 12.8) < 1e-9,
                $"Support uses the A-STD percentage ({result.SupportArea})");
            Check(Math.Abs(result.NetArea - 160.8) < 1e-9,
                $"Net area includes equipment, queue, support and fixed rooms ({result.NetArea})");
            Check(Math.Abs(result.GrossArea - 213.5826) < 1e-8,
                $"A-STD allowances build the gross area ({result.GrossArea})");
            Check(result.ProcessorCount == 12,
                $"Result processor total comes from the solver counts ({result.ProcessorCount})");
            Check(result.EquipmentCounts["Counter units (installed)"] == 8 &&
                  result.EquipmentCounts["Kiosk units (installed)"] == 4,
                "Per-pool solver counts are reported against automatically matched names");
            Check(result.Warnings.Count == 0,
                $"Matching pool names and standards need no warnings ({string.Join(" | ", result.Warnings)})");

            string taggedPool = "Counter|DomesticDeparture|Economy";
            FacilityAreaResult taggedArea = FacilityAreaCalculator.Calculate(new FacilityAreaInput
            {
                PoolNames = new List<string> { taggedPool },
                PhysicalPerPool = new List<int> { 2 },
                Pools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo { Name = taggedPool, IsShared = false }
                },
                PeakQueueByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Counter", 0 }
                },
                Standards = standards,
                RequireExplicitCounts = true
            });

            Check(Math.Abs(taggedArea.EquipmentArea - 30.0) < 1e-9 &&
                  taggedArea.Lines[0].FacilityKey == "Counter",
                "Tagged solver pool names automatically match their channel's A-STD key");

            FacilityAreaStandardSet oneUnitStandard = new FacilityAreaStandardSet();
            oneUnitStandard.Units.Add(new FacilityUnitStandard
            {
                Key = "Counter",
                AreaPerUnit = 15.0
            });
            FacilityAreaResult sharedPoolArea = FacilityAreaCalculator.Calculate(new FacilityAreaInput
            {
                PoolNames = new List<string> { "Shared" },
                PhysicalPerPool = new List<int> { 2 },
                Pools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo { Name = "Shared", IsShared = true }
                },
                Standards = oneUnitStandard,
                RequireExplicitCounts = true
            });

            Check(Math.Abs(sharedPoolArea.EquipmentArea - 30.0) < 1e-9 &&
                  sharedPoolArea.Lines[0].FacilityKey == "Counter",
                "A single A-STD unit key automatically applies to a shared single pool");

            FacilitySizingContext unmatched = new FacilitySizingContext
            {
                OperationalData = new FacilityOperationalData { ProcessorCount = 3 },
                PassengerSummary = new PassengerSummaryData(),
                AreaStandards = standards,
                PoolNames = new List<string> { "Kiosk East" },
                PhysicalPerPool = new List<int> { 3 },
                ProcessorPools = new List<ProcessorPoolInfo>
                {
                    new ProcessorPoolInfo { Name = "Kiosk East", IsShared = true }
                }
            };

            FacilitySizingResult unmatchedResult = new GenericFacilitySizer().SizeFacility(unmatched);

            Check(Math.Abs(unmatchedResult.ProcessingArea) < 1e-9 &&
                  unmatchedResult.Warnings.Any(w => w.Contains("no matching area standard")),
                "An unmatched pool warns instead of silently borrowing another unit standard");
        }

        // -------------------------------------------------------------
        // 14f. Gate stands and gate lounges
        // -------------------------------------------------------------
        private static void RunGateSizing()
        {
            Console.WriteLine("== Gate stands and lounges ==");

            GateStandards blankStandards = new GateStandards();
            Check(blankStandards.ContactPctDomestic == 0.0 &&
                  blankStandards.ContactPctSwing == 0.0 &&
                  blankStandards.ContactPctInternational == 0.0 &&
                  blankStandards.RemoteStandsPerBusGate == 1.0 &&
                  blankStandards.MaxStandsRatio == 1.0 &&
                  new GateLoungeProfile().PodiumsPerLounge == 0,
                "Blank gate inputs use the TypeScript seed identities (zero shares/podiums, ratios of one)");

            Dictionary<string, GateLoungeProfile> parsedProfiles = GateLoungeProfileTableParser.Parse(
                new[]
                {
                    "Input,Code C,Code E,Code F",
                    "Average seats,180,300,500",
                    "Load factor (%),85,85,85",
                    "Seated (%),60,60,60",
                    "Standing (%),20,20,20",
                    "Next-seat allowance (%),10,10,10",
                    "Open-lounge reduction (%),0,0,0",
                    "Boarding podiums (positions per lounge),1,1,1",
                    "In boarding queue (%),20,20,20"
                });
            Check(parsedProfiles["C"].Seats == 180 &&
                  Math.Abs(parsedProfiles["C"].LoadFactor - 0.85) < 1e-9 &&
                  Math.Abs(parsedProfiles["C"].NextSeatAllowancePct - 0.10) < 1e-9 &&
                  parsedProfiles["F"].Seats == 500,
                "The saved C/E/F lounge-profile table parses seats, percentages, podiums and queues by code");

            List<GateCodeInput> codes = new List<GateCodeInput>
            {
                new GateCodeInput
                {
                    Code = "C",
                    DomesticAtms = 4,
                    InternationalAtms = 3,
                    DomesticOccupancyMinutes = 45.0,
                    InternationalOccupancyMinutes = 45.0
                },
                new GateCodeInput
                {
                    Code = "E",
                    DomesticAtms = 2,
                    InternationalAtms = 3,
                    DomesticOccupancyMinutes = 75.0,
                    InternationalOccupancyMinutes = 75.0
                }
            };

            List<GateLoungeProfile> loungeProfiles = new List<GateLoungeProfile>
            {
                new GateLoungeProfile
                {
                    Code = "C",
                    Seats = 180,
                    LoadFactor = 0.85,
                    SeatedPct = 0.70,
                    StandingPct = 0.10,
                    BoardingQueuePct = 0.15,
                    PodiumsPerLounge = 1
                },
                new GateLoungeProfile
                {
                    Code = "E",
                    Seats = 300,
                    LoadFactor = 0.85,
                    SeatedPct = 0.60,
                    StandingPct = 0.15,
                    BoardingQueuePct = 0.20,
                    PodiumsPerLounge = 1
                }
            };

            GateStandards standards = new GateStandards
            {
                BufferMinutes = 10.0,
                SparesPct = 0.0,
                ContactPctDomestic = 0.8,
                ContactPctSwing = 0.8,
                ContactPctInternational = 0.8,
                RemoteStandsPerBusGate = 2.0,
                MaxStandsRatio = 1.35,
                SeatedSpacePerPax = 1.7,
                StandingSpacePerPax = 1.2,
                BoardingQueueSpacePerPax = 1.2,
                PodiumAreaEach = 0.0,
            };

            standards.SwingStands["C"] = 1;

            GateSizingResult gates = GateCalculator.Calculate(codes, standards, null, loungeProfiles);

            // Domestic: C 4 x 55/60 = 3.67 -> 4 stands; E 2 x 85/60 = 2.83 -> 3.
            // International: C 3 x 55/60 = 2.75 -> 3; E 3 x 85/60 = 4.25 -> 5.
            // One stand swings for C.
            Check(gates.ActiveTotal == 14,
                $"Active stands = 14 (domestic 6 + swing 1 + international 7) ({gates.ActiveTotal})");

            Check(gates.ContactTotal == 13,
                $"Contact stands = 13 ({gates.ContactTotal})");

            Check(gates.BusTotal == 1,
                $"Bus gates = 1 ({gates.BusTotal})");

            Check(gates.RemoteActive == 2,
                $"Remote stands = 1 bus gate x 2 = 2 ({gates.RemoteActive})");

            Check(gates.MaxStands == 19,
                $"Max stands = 14 x 1.35 = 19 ({gates.MaxStands})");

            Check(gates.NetRemote == 6,
                $"Net remote stands = 19 - 13 = 6 ({gates.NetRemote})");

            GateBucketStands international = gates.Buckets
                .First(b => b.Bucket == "International");

            Check(international.SplitExtra == 1,
                $"The per-code split is one stand over the international sector total ({international.SplitExtra})");

            // A code C lounge: 180 seats x 0.85 = 153 passengers.
            //   seated 107.1 x 1.7 = 182.07; standing 15.3 x 1.2 = 18.36
            //   hold 200.43; queue 22.95 x 1.2 = 27.54; area 227.97
            GateLounge codeC = gates.Lounges.First(l => l.Code == "C" && l.Bucket == "Domestic");

            Check(Math.Abs(codeC.AreaEach - 227.97) < 1e-6,
                $"A code C lounge is 227.97 m2 ({codeC.AreaEach})");

            GateSizingResult perCodeArea = GateCalculator.Calculate(
                new List<GateCodeInput>
                {
                    new GateCodeInput
                    {
                        Code = "C"
                    }
                },
                new GateStandards
                {
                    ContactPctDomestic = 1.0,
                    ContactPctSwing = 1.0,
                    ContactPctInternational = 1.0,
                    SeatedSpacePerPax = 2.0,
                    StandingSpacePerPax = 1.0,
                    BoardingQueueSpacePerPax = 1.0
                },
                new GateScheduleDemand
                {
                    Codes = new List<GateScheduleCodeDemand>
                    {
                        new GateScheduleCodeDemand
                        {
                            Code = "C",
                            CombinedPeak = 1,
                            DedicatedDomestic = 1
                        }
                    }
                },
                new List<GateLoungeProfile>
                {
                    new GateLoungeProfile
                    {
                        Code = "C",
                        Seats = 100,
                        LoadFactor = 1.0,
                        SeatedPct = 0.5,
                        StandingPct = 0.5,
                        NextSeatAllowancePct = 0.2,
                        OpenLoungeReductionPct = 0.1,
                        PodiumsPerLounge = 0
                    }
                });
            Check(Math.Abs(perCodeArea.Lounges.Single().HoldArea - 153.0) < 1e-9,
                $"Next-seat allowance applies to seated area only before the open-lounge reduction ({perCodeArea.Lounges.Single().HoldArea})");

            Check(codeC.Count == 3,
                $"Three domestic code C contact gates need three lounges ({codeC.Count})");

            Check(gates.LoungeCount == 14,
                $"One lounge per contact gate and per bus gate ({gates.LoungeCount})");

            double loungeSum = gates.Lounges.Sum(l => l.Area);

            Check(Math.Abs(gates.NetArea - loungeSum) < 1e-6,
                $"The net lounge area is the sum of the lounges ({gates.NetArea})");

            Check(Math.Abs(gates.NetArea - 4166.19) < 1e-6,
                $"Net lounge area = 4166.19 m2 ({gates.NetArea})");

            // A bus gate's lounge is sized as the bus lounge code, not as the
            // aircraft it happens to serve.
            GateLounge busLounge = gates.Lounges.FirstOrDefault(l =>
                l.Bucket == "International" && l.Code == "C" && l.Count == 1);

            Check(busLounge != null,
                "The international bus gate has a code C lounge");

            // Swing is capped at the smaller sector.
            standards.SwingStands["E"] = 9;

            GateSizingResult capped = GateCalculator.Calculate(codes, standards);

            Check(capped.Warnings.Any(w => w.Contains("capped")),
                "Swing beyond the smaller sector is reported");

            // A swing that is impossible is capped, not honoured.
            Check(capped.ActiveTotal == 11,
                $"More swing means fewer stands in total: 15 less the 4 swung = 11 ({capped.ActiveTotal})");

            standards.SwingStands.Remove("E");

            // The gross build-up is the same one every facility uses.
            GrossArea gross = AreaGrossUp.Calculate(gates.NetArea, 15.0, 10.0, 5.0);

            Check(Math.Abs(gross.Total - gates.NetArea * 1.15 * 1.10 * 1.05) < 1e-6,
                $"The lounge area grosses up like any other ({gross.Total})");

            Check(gates.NetArea > 0 && gross.Total > gates.NetArea,
                "The gross lounge area is larger than the net");

            GateSizingResult splitOccupancy = GateCalculator.Calculate(
                new List<GateCodeInput>
                {
                    new GateCodeInput
                    {
                        Code = "C",
                        DomesticAtms = 1,
                        InternationalAtms = 1,
                        DomesticOccupancyMinutes = 20,
                        InternationalOccupancyMinutes = 80
                    }
                },
                new GateStandards
                {
                    BufferMinutes = 10,
                    ContactPctDomestic = 1,
                    ContactPctSwing = 1,
                    ContactPctInternational = 1
                });
            double domesticHours = splitOccupancy.Buckets.First(b => b.Bucket == "Domestic").Codes.First().StandHours;
            double internationalHours = splitOccupancy.Buckets.First(b => b.Bucket == "International").Codes.First().StandHours;
            Check(Math.Abs(domesticHours - 0.5) < 1e-9 && Math.Abs(internationalHours - 1.5) < 1e-9,
                $"Domestic and international stand hours use their own occupancy inputs ({domesticHours}/{internationalHours})");

            // Nothing to size is reported rather than silently zero.
            GateSizingResult empty = GateCalculator.Calculate(new List<GateCodeInput>(), standards);

            Check(empty.Warnings.Count > 0 && empty.ActiveTotal == 0,
                "No aircraft types reports a warning and sizes nothing");

            RunGateScheduleSizing();
        }

        private static void RunGateScheduleSizing()
        {
            Console.WriteLine("== Schedule-based gate demand ==");

            FlightScheduleResult schedule = new FlightScheduleResult
            {
                ScheduleName = "Gate test schedule",
                Legs = new List<FlightLeg>
                {
                    GateTestLeg("D-C", LegDirection.Arrival, 8 * 60, LegSector.Domestic),
                    GateTestLeg("D-C", LegDirection.Departure, 9 * 60, LegSector.Domestic),
                    GateTestLeg("I-C", LegDirection.Arrival, 8 * 60, LegSector.International),
                    GateTestLeg("I-C", LegDirection.Departure, 9 * 60, LegSector.International),
                    GateTestLeg("M-C", LegDirection.Arrival, 8 * 60, LegSector.Domestic),
                    GateTestLeg("M-C", LegDirection.Departure, 9 * 60, LegSector.International)
                }
            };

            foreach (FlightLeg leg in schedule.Legs)
                leg.Terminal = "T1";

            GateScheduleDemand demand = GateScheduleDemandBuilder.Build(
                schedule, "T1", 5.0, 5.0, 120.0);
            GateScheduleCodeDemand codeC = demand.Codes.First(c => c.Code == "C");

            Check(demand.RotationCount == 3 && codeC.CombinedPeak == 3,
                $"Schedule mode pairs rotations and measures a combined C peak of 3 ({demand.RotationCount}, {codeC.CombinedPeak})");
            Check(codeC.DomesticPeak == 2 && codeC.InternationalPeak == 2 &&
                  codeC.DedicatedDomestic == 1 && codeC.DedicatedInternational == 1 && codeC.Swing == 1,
                "Mixed-sector aircraft are counted in both sector peaks and assigned to swing stands");

            GateLoungeProfile loungeC = new GateLoungeProfile
            {
                Code = "C",
                Seats = 180,
                LoadFactor = 0.85,
                SeatedPct = 0.60,
                StandingPct = 0.20,
                BoardingQueuePct = 0.20,
                NextSeatAllowancePct = 0.10,
                OpenLoungeReductionPct = 0.0,
                PodiumsPerLounge = 1
            };
            GateStandards standards = new GateStandards
            {
                SparesPct = 0.10,
                ContactPctDomestic = 0.0,
                ContactPctSwing = 0.0,
                ContactPctInternational = 0.0,
                RemoteStandsPerBusGate = 2.0,
                MaxStandsRatio = 9.0
            };

            GateSizingResult sized = GateCalculator.Calculate(
                new List<GateCodeInput>(), standards, demand,
                new List<GateLoungeProfile> { loungeC });

            Check(sized.ScheduleMode && sized.ActiveTotal == 4 &&
                  sized.Buckets.First(b => b.Bucket == "Swing").Active == 2,
                $"Schedule spares are added to swing and the PHP max/active ratio is ignored ({sized.ActiveTotal})");
            Check(sized.RemoteActive == 4 && sized.BusTotal == 2 && sized.MaxStands == 4,
                $"Schedule mode sizes remote stands, bus gates, and max stands from AOG counts ({sized.RemoteActive}/{sized.BusTotal}/{sized.MaxStands})");
            Check(sized.LoungeCount == 2 && sized.NetArea > 0,
                "Schedule demand reuses the per-code lounge profile and Code C bus lounge");

            // The group buckets split the non-contact stands into remote stands
            // (raw) and the facility derives bus gates from the ratio.
            GateBucketStands domBucket = sized.Buckets.First(b => b.Bucket == "Domestic");
            GateBucketStands swingBucket2 = sized.Buckets.First(b => b.Bucket == "Swing");
            Check(sized.Buckets.Sum(b => b.Remote) == sized.RemoteActive,
                $"Schedule mode: the group remote stands reconcile with the facility total ({sized.Buckets.Sum(b => b.Remote)} = {sized.RemoteActive})");
            Check(domBucket.Remote == domBucket.Bus,
                "Schedule mode: a group's non-contact stands are its remote stands");

            // PHP mode keeps the non-contact stands as bus gates; the remote
            // stands follow from the ratio at facility level.
            GateSizingResult php = GateCalculator.Calculate(
                new List<GateCodeInput>
                {
                    new GateCodeInput
                    {
                        Code = "C",
                        DomesticAtms = 4,
                        DomesticOccupancyMinutes = 60,
                        InternationalAtms = 4,
                        InternationalOccupancyMinutes = 60
                    }
                },
                new GateStandards
                {
                    BufferMinutes = 10.0,
                    ContactPctDomestic = 0.5,
                    ContactPctSwing = 0.5,
                    ContactPctInternational = 0.5,
                    RemoteStandsPerBusGate = 2.0
                });
            Check(php.Buckets.Sum(b => b.Bus) == php.BusTotal,
                $"PHP mode: bus gates reconcile across the groups ({php.BusTotal})");
            Check(php.RemoteActive == php.BusTotal * 2,
                $"PHP mode: remote stands follow the ratio ({php.RemoteActive} = {php.BusTotal} x 2)");

            GateScheduleDemand noProfile = GateScheduleDemandBuilder.Build(schedule, "T1");
            GateSizingResult standsOnly = GateCalculator.Calculate(
                new List<GateCodeInput>(), standards, noProfile);
            Check(standsOnly.ActiveTotal > 0 && standsOnly.LoungeCount == standsOnly.BusTotal &&
                  standsOnly.Warnings.Any(w => w.Contains("lounge profile")),
                "A schedule sizes stands without PHP inputs, keeps lounge counts, and warns when profiles are absent");

            FlightScheduleResult midnight = new FlightScheduleResult
            {
                ScheduleName = "Midnight turn",
                Legs = new List<FlightLeg>
                {
                    GateTestLeg("NIGHT", LegDirection.Arrival, 23 * 60 + 30, LegSector.Domestic),
                    GateTestLeg("NIGHT", LegDirection.Departure, 30, LegSector.Domestic)
                }
            };
            GateScheduleCodeDemand midnightC = GateScheduleDemandBuilder.Build(midnight, null, 0, 0, 120)
                .Codes.First(c => c.Code == "C");
            Check(midnightC.CombinedPeak == 1,
                "A paired turn departing after midnight wraps to the next design-day day");
        }

        private static FlightLeg GateTestLeg(
            string link,
            LegDirection direction,
            int minute,
            LegSector sector)
        {
            return new FlightLeg
            {
                Id = link + direction,
                LinkId = link,
                Direction = direction,
                HasTime = true,
                MinutesFromMidnight = minute % 1440,
                DayOffset = minute / 1440,
                IcaoCodeRaw = "C",
                SectorRaw = sector,
                ServiceType = LegServiceType.Passenger
            };
        }

        // -------------------------------------------------------------
        // 14f. Flight schedule import
        // -------------------------------------------------------------
        private static void RunFlightScheduleImport()
        {
            Console.WriteLine("== Flight schedule import ==");

            RunFlightScheduleTimeFormats();
            RunFlightScheduleFixture();
            RunFlightScheduleEdgeCases();
            RunFlightScheduleRotationIdLink();
            RunRealScheduleIntegration();
        }

        private static void RunFlightScheduleTimeFormats()
        {
            var fraction = ScheduleTimeDecoder.Decode(TimeCell("0.5"), string.Empty);
            Check(fraction.IsValid && fraction.Minutes == 720,
                "Schedule time: an Excel fraction decodes to 12:00");

            var decimalHours = ScheduleTimeDecoder.Decode(TimeCell("9.5"), string.Empty);
            Check(decimalHours.IsValid && decimalHours.Minutes == 570,
                "Schedule time: decimal hours decode to 09:30");

            var compact = ScheduleTimeDecoder.Decode(TimeCell("0930"), string.Empty);
            Check(compact.IsValid && compact.Minutes == 570,
                "Schedule time: four-digit HHmm decodes to 09:30");

            var compactThree = ScheduleTimeDecoder.Decode(TimeCell("930"), string.Empty);
            Check(compactThree.IsValid && compactThree.Minutes == 570,
                "Schedule time: three-digit Hmm decodes to 09:30");

            var afterMidnight = ScheduleTimeDecoder.Decode(TimeCell("24:10"), string.Empty);
            Check(afterMidnight.IsValid &&
                  afterMidnight.DayOffset == 1 &&
                  afterMidnight.Minutes == 10,
                "Schedule time: 24:10 rolls to the next day");

            var blank = ScheduleTimeDecoder.Decode(TimeCell(string.Empty), string.Empty);
            Check(!blank.IsValid,
                "Schedule time: a blank cell is invalid, not midnight");

            var invalid = ScheduleTimeDecoder.Decode(TimeCell("2360"), string.Empty);
            Check(!invalid.IsValid,
                "Schedule time: an invalid HHmm value is rejected");

            var notATime = ScheduleTimeDecoder.Decode(TimeCell("not a time"), string.Empty);
            Check(!notATime.IsValid,
                "Schedule time: unreadable text is rejected");

            var serial = new DateTime(2026, 10, 19, 14, 30, 0)
                .ToOADate()
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            var serialTime = ScheduleTimeDecoder.Decode(
                TimeCell(serial),
                "2026-10-19");

            Check(serialTime.IsValid &&
                  serialTime.Minutes == 870 &&
                  serialTime.DayOffset == 0 &&
                  serialTime.Date == "2026-10-19",
                "Schedule time: an Excel date serial decodes with its date");
        }

        private static XlsxCell TimeCell(string value)
        {
            return new XlsxCell("J", value, value, false, true, false);
        }

        private static void RunFlightScheduleFixture()
        {
            var builder = new ScheduleFixtureBuilder("26.5mppa Schedule", headerRow: 7);

            builder.AddRow(8, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("6E"),
                ["C"] = FixtureCell.TextCell("6E"),
                ["D"] = FixtureCell.NumberCell(180),
                ["E"] = FixtureCell.TextCell("320"),
                ["F"] = FixtureCell.TextCell("C"),
                ["G"] = FixtureCell.TextCell("COK"),
                ["H"] = FixtureCell.TextCell("COK"),
                ["I"] = FixtureCell.TextCell("D"),
                ["J"] = FixtureCell.NumberCell(610.0 / 1440.0),
                ["K"] = FixtureCell.NumberCell(0.87),
                ["L"] = FixtureCell.NumberCell(157),
                ["M"] = FixtureCell.TextCell("HYD"),
                ["N"] = FixtureCell.TextCell("HYD"),
                ["O"] = FixtureCell.TextCell("D"),
                ["P"] = FixtureCell.NumberCell(730.0 / 1440.0),
                ["Q"] = FixtureCell.NumberCell(0.74),
                ["R"] = FixtureCell.NumberCell(134),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J"),
                ["U"] = FixtureCell.FormulaCell("IFERROR(INDEX(#REF!),\"\")", null, true),
                ["V"] = FixtureCell.FormulaCell("IFERROR(INDEX(#REF!),\"\")")
            });

            builder.AddRow(9, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("AI"),
                ["C"] = FixtureCell.TextCell("AI"),
                ["D"] = FixtureCell.NumberCell(250),
                ["E"] = FixtureCell.TextCell("B77W"),
                ["F"] = FixtureCell.TextCell("E"),
                ["G"] = FixtureCell.TextCell("SIN"),
                ["I"] = FixtureCell.TextCell("I"),
                ["J"] = FixtureCell.TextCell("23:50"),
                ["K"] = FixtureCell.NumberCell(0.90),
                ["L"] = FixtureCell.NumberCell(220),
                ["N"] = FixtureCell.TextCell("SIN"),
                ["O"] = FixtureCell.TextCell("I"),
                ["P"] = FixtureCell.TextCell("06:10"),
                ["Q"] = FixtureCell.NumberCell(0.85),
                ["R"] = FixtureCell.NumberCell(200),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J"),
                ["U"] = FixtureCell.TextCell("T1"),
                ["V"] = FixtureCell.TextCell("T1")
            });

            builder.AddRow(10, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("FD"),
                ["C"] = FixtureCell.TextCell("FD"),
                ["D"] = FixtureCell.NumberCell(0),
                ["E"] = FixtureCell.TextCell("77X"),
                ["F"] = FixtureCell.TextCell("E"),
                ["G"] = FixtureCell.TextCell("SIN"),
                ["H"] = FixtureCell.TextCell("SIN"),
                ["I"] = FixtureCell.TextCell("I"),
                ["J"] = FixtureCell.NumberCell(360.0 / 1440.0),
                ["K"] = FixtureCell.NumberCell(0.80),
                ["L"] = FixtureCell.NumberCell(0),
                ["M"] = FixtureCell.TextCell("LEJ"),
                ["N"] = FixtureCell.TextCell("LEJ"),
                ["O"] = FixtureCell.TextCell("I"),
                ["P"] = FixtureCell.NumberCell(400.0 / 1440.0),
                ["Q"] = FixtureCell.NumberCell(0.70),
                ["R"] = FixtureCell.NumberCell(0),
                ["S"] = FixtureCell.TextCell("F"),
                ["T"] = FixtureCell.TextCell("F"),
                ["U"] = FixtureCell.FormulaCell("IF(S8=\"F\",\"Cargo\",\"\")", "Cargo"),
                ["V"] = FixtureCell.FormulaCell("IF(T8=\"F\",\"Cargo\",\"\")", "Cargo")
            });

            var result = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "fixture.xlsx",
                    ScheduleName = "Fixture",
                    SheetName = string.Empty,
                    BaseDate = "2026-10-19",
                    LinkedRows = true
                });

            Check(result.SheetName == "26.5mppa Schedule",
                $"The actual worksheet is reported ({result.SheetName})");

            Check(result.HeaderRow == 7,
                $"The header row is detected ({result.HeaderRow})");

            Check(result.SourceRowCount == 3,
                $"Three source turns were read ({result.SourceRowCount})");

            Check(result.TotalLegCount == 6 && result.ArrivalCount == 3 && result.DepartureCount == 3,
                $"Each turn produced two legs ({result.ArrivalCount} / {result.DepartureCount})");

            Check(result.RotationCount == 3 && result.Rotations().All(r => r.IsComplete),
                $"Three complete rotations were reconstructed ({result.RotationCount})");

            Check(result.Columns.TryGetValue("ArrivalPax", out var arrivalPaxColumn) && arrivalPaxColumn == "L",
                "The arrival Pax header binds to physical column L");

            Check(result.Columns.TryGetValue("DeparturePax", out var departurePaxColumn) && departurePaxColumn == "R",
                "The departure Pax header binds to physical column R");

            Check(result.Columns.TryGetValue("AircraftType", out var aircraftColumn) && aircraftColumn == "E" &&
                  result.Columns.TryGetValue("IcaoCode", out var icaoColumn) && icaoColumn == "F",
                "Aircraft type and ICAO code are separate columns");

            var columnMap = result.ColumnMapLines();
            Check(columnMap.Contains("ArrivalPax = L") &&
                  columnMap.Contains("DeparturePax = R") &&
                  columnMap.IndexOf("ArrivalPax = L") < columnMap.IndexOf("DeparturePax = R"),
                "The column map exposes the resolved bindings in sheet order");

            var arrival8 = result.Legs.Single(l =>
                l.SourceRow == 8 && l.Direction == LegDirection.Arrival);

            Check(arrival8.Airline == "6E" &&
                  arrival8.Station == "COK" &&
                  arrival8.AircraftTypeRaw == "320" &&
                  arrival8.IcaoCodeRaw == "C",
                "Arrival identity fields are mapped from the correct columns");

            Check(arrival8.HasTime &&
                  arrival8.MinutesFromMidnight == 610 &&
                  arrival8.DayOffset == 0,
                $"Arrival time is 10:10 ({arrival8.MinutesFromMidnight})");

            Check(arrival8.SectorRaw == LegSector.Domestic &&
                  arrival8.ServiceType == LegServiceType.Passenger &&
                  arrival8.PaxRaw == 157 &&
                  Math.Abs((arrival8.LoadFactor ?? 0) - 0.87) < 1e-9,
                "Arrival sector, service, passengers and load factor are mapped");

            Check(arrival8.TerminalUnresolved &&
                  arrival8.Issues.Any(i => i.Field == "Terminal" && i.Severity == "Warning"),
                "An empty cached terminal formula is flagged as unresolved");

            var departure9 = result.Legs.Single(l =>
                l.SourceRow == 9 && l.Direction == LegDirection.Departure);

            Check(departure9.DayOffset == 1 &&
                  departure9.MinutesFromMidnight == 370,
                $"An overnight departure rolls to the next day ({departure9.DayOffset}d {departure9.MinutesFromMidnight}m)");

            var rotation9 = result.Rotations().Single(r => r.Arrival?.SourceRow == 9);
            Check(rotation9.TurnMinutes == 380,
                $"The overnight turn is 380 minutes ({rotation9.TurnMinutes})");

            var arrival10 = result.Legs.Single(l =>
                l.SourceRow == 10 && l.Direction == LegDirection.Arrival);

            Check(arrival10.ServiceType == LegServiceType.Freight &&
                  arrival10.Terminal == "Cargo",
                "Freight legs are retained and a cached cargo terminal is preserved");

            var summary = result.CreateSummary();

            Check(summary.SourceRowCount == 3 &&
                  summary.TotalLegs == 6 &&
                  summary.ArrivalPax == 377 &&
                  summary.DeparturePax == 334 &&
                  summary.TotalPax == 711,
                $"Summary totals are correct ({summary.ArrivalPax} / {summary.DeparturePax})");

            Check(summary.FreightLegs == 2 &&
                  summary.PassengerLegs == 4 &&
                  summary.UnresolvedTerminals == 2 &&
                  summary.FallbackTerminalLegs == 0 &&
                  summary.ErrorCount == 0 &&
                  summary.WarningCount == 2,
                $"Summary service and quality counts are correct " +
                $"({summary.FreightLegs} freight, {summary.WarningCount} warnings)");

            Check(result.Legs.All(l => l.Date == "2026-10-19"),
                "The Base Date is applied to time-only legs");

            var fallbackResult = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "fixture.xlsx",
                    ScheduleName = "Fixture",
                    BaseDate = "2026-10-19",
                    LinkedRows = true,
                    FallbackTerminal = "T1"
                });

            var fallbackArrival8 = fallbackResult.Legs.Single(l =>
                l.SourceRow == 8 && l.Direction == LegDirection.Arrival);
            var fallbackDeparture8 = fallbackResult.Legs.Single(l =>
                l.SourceRow == 8 && l.Direction == LegDirection.Departure);

            Check(fallbackArrival8.Terminal == "T1" &&
                  fallbackArrival8.TerminalSource == TerminalSource.Fallback &&
                  fallbackArrival8.TerminalIsFallback &&
                  !fallbackArrival8.TerminalUnresolved &&
                  !fallbackArrival8.Issues.Any(i => i.Field == "Terminal"),
                "An unresolved passenger terminal uses the configured fallback");

            Check(fallbackDeparture8.Terminal == "T1" &&
                  fallbackDeparture8.TerminalSource == TerminalSource.Fallback &&
                  !fallbackDeparture8.TerminalUnresolved,
                "The fallback applies independently to both directions");

            var fallbackSummary = fallbackResult.CreateSummary();

            Check(fallbackSummary.FallbackTerminalLegs == 2 &&
                  fallbackSummary.UnresolvedTerminals == 0 &&
                  fallbackSummary.WarningCount == 1 &&
                  fallbackResult.GlobalIssues.Count(i =>
                      i.Field == "Terminal" &&
                      i.Severity == "Warning" &&
                      i.Message.IndexOf("fallback terminal 'T1'", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                "A fallback assumption is reported once at workbook level, not per leg");

            var cargoArrival = fallbackResult.Legs.Single(l =>
                l.SourceRow == 10 && l.Direction == LegDirection.Arrival);

            Check(cargoArrival.Terminal == "Cargo" &&
                  cargoArrival.TerminalSource == TerminalSource.Workbook &&
                  cargoArrival.TerminalIsFallback == false,
                "A cached Cargo terminal remains a workbook value");

            var literalArrival9 = fallbackResult.Legs.Single(l =>
                l.SourceRow == 9 && l.Direction == LegDirection.Arrival);

            Check(literalArrival9.Terminal == "T1" &&
                  literalArrival9.TerminalSource == TerminalSource.Workbook &&
                  !literalArrival9.TerminalIsFallback,
                "A literal workbook terminal is not marked as fallback");

            var unlinked = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "fixture.xlsx",
                    ScheduleName = "Fixture",
                    BaseDate = "2026-10-19",
                    LinkedRows = false
                });

            Check(unlinked.RotationCount == unlinked.TotalLegCount,
                $"Unlinked rows produce one rotation per leg ({unlinked.RotationCount})");
        }

        private static void RunFlightScheduleEdgeCases()
        {
            var builder = new ScheduleFixtureBuilder("Odd Header Sheet", headerRow: 4);

            // Arrival only. The departure side is deliberately absent.
            builder.AddRow(5, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("ZZ"),
                ["D"] = FixtureCell.NumberCell(100),
                ["E"] = FixtureCell.TextCell("320"),
                ["F"] = FixtureCell.TextCell("C"),
                ["G"] = FixtureCell.TextCell("DEL"),
                ["I"] = FixtureCell.TextCell("D"),
                ["J"] = FixtureCell.TextCell("15:00"),
                ["L"] = FixtureCell.NumberCell(50),
                ["S"] = FixtureCell.TextCell("J")
            });

            // Arrival time is blank, although the row carries arrival data.
            builder.AddRow(6, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("QQ"),
                ["C"] = FixtureCell.TextCell("QQ"),
                ["D"] = FixtureCell.NumberCell(150),
                ["E"] = FixtureCell.TextCell("320"),
                ["F"] = FixtureCell.TextCell("C"),
                ["G"] = FixtureCell.TextCell("BOM"),
                ["I"] = FixtureCell.TextCell("I"),
                ["L"] = FixtureCell.NumberCell(80),
                ["M"] = FixtureCell.TextCell("BLR"),
                ["N"] = FixtureCell.TextCell("BLR"),
                ["O"] = FixtureCell.TextCell("I"),
                ["P"] = FixtureCell.TextCell("16:00"),
                ["Q"] = FixtureCell.NumberCell(0.80),
                ["R"] = FixtureCell.NumberCell(70),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J")
            });

            // Invalid arrival time and an uncached departure-time formula.
            builder.AddRow(7, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("XX"),
                ["C"] = FixtureCell.TextCell("XX"),
                ["G"] = FixtureCell.TextCell("AAA"),
                ["I"] = FixtureCell.TextCell("X"),
                ["J"] = FixtureCell.TextCell("not a time"),
                ["L"] = FixtureCell.NumberCell(10),
                ["M"] = FixtureCell.TextCell("BBB"),
                ["N"] = FixtureCell.TextCell("BBB"),
                ["O"] = FixtureCell.TextCell("X"),
                ["P"] = FixtureCell.FormulaCell("A1+1"),
                ["R"] = FixtureCell.NumberCell(5),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J")
            });

            // Valid times, but terminal and airline formulas have no usable
            // cached result.
            builder.AddRow(8, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.FormulaCell("VLOOKUP(A1,B:C,2,FALSE)"),
                ["C"] = FixtureCell.TextCell("YY"),
                ["G"] = FixtureCell.TextCell("CCC"),
                ["I"] = FixtureCell.TextCell("D"),
                ["J"] = FixtureCell.TextCell("08:00"),
                ["L"] = FixtureCell.NumberCell(20),
                ["N"] = FixtureCell.TextCell("DDD"),
                ["O"] = FixtureCell.TextCell("D"),
                ["P"] = FixtureCell.TextCell("09:00"),
                ["R"] = FixtureCell.NumberCell(18),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J"),
                ["U"] = FixtureCell.FormulaCell("A1", null, true),
                ["V"] = FixtureCell.FormulaCell("A1")
            });

            var result = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "edge.xlsx",
                    ScheduleName = "Edge",
                    BaseDate = "2026-10-19",
                    LinkedRows = true
                });

            Check(result.HeaderRow == 4,
                $"A header below the title rows is detected ({result.HeaderRow})");

            Check(result.SourceRowCount == 4,
                $"Four populated source rows were read ({result.SourceRowCount})");

            Check(result.TotalLegCount == 7,
                $"A missing side is not manufactured into a leg ({result.TotalLegCount})");

            var loneArrival = result.Legs.Single(l => l.SourceRow == 5);
            Check(loneArrival.Direction == LegDirection.Arrival &&
                  result.Rotations().Count(r => r.IsLoneArrival) == 1,
                "The arrival-only row is reported as a lone arrival");

            var missingTime = result.Legs.Single(l =>
                l.SourceRow == 6 && l.Direction == LegDirection.Arrival);

            Check(!missingTime.HasTime &&
                  missingTime.Issues.Any(i => i.Field == "Time" && i.Severity == "Error"),
                "A blank time is an error, not midnight");

            var invalidTime = result.Legs.Single(l =>
                l.SourceRow == 7 && l.Direction == LegDirection.Arrival);

            Check(!invalidTime.HasTime &&
                  invalidTime.Issues.Any(i => i.Field == "Time" && i.Severity == "Error"),
                "An unreadable time is an error, not midnight");

            var uncachedTime = result.Legs.Single(l =>
                l.SourceRow == 7 && l.Direction == LegDirection.Departure);

            Check(!uncachedTime.HasTime &&
                  uncachedTime.Issues.Any(i => i.Field == "Time" && i.Severity == "Error"),
                "An uncached time formula is an error, not midnight");

            Check(result.CreateSummary().LegsWithoutTime == 3,
                "Every unusable time is counted without guessing a clock time");

            var terminalLeg = result.Legs.Single(l =>
                l.SourceRow == 8 && l.Direction == LegDirection.Arrival);

            Check(terminalLeg.TerminalUnresolved &&
                  terminalLeg.Issues.Any(i => i.Field == "Terminal"),
                "Both empty and missing cached terminal formula results are flagged");

            Check(terminalLeg.Issues.Any(i =>
                    i.Field == "Airline" &&
                    i.Message.IndexOf("cached", StringComparison.OrdinalIgnoreCase) >= 0),
                "An uncached text formula is reported instead of becoming a silent blank");

            var fallbackEdge = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "edge.xlsx",
                    ScheduleName = "Edge",
                    BaseDate = "2026-10-19",
                    LinkedRows = true,
                    FallbackTerminal = "T1"
                });

            var fallbackLoneArrival = fallbackEdge.Legs.Single(l => l.SourceRow == 5);

            Check(fallbackLoneArrival.Terminal == "T1" &&
                  fallbackLoneArrival.TerminalSource == TerminalSource.Fallback,
                "A passenger leg with no terminal cell at all receives the fallback");

            var fallbackTerminalLeg = fallbackEdge.Legs.Single(l =>
                l.SourceRow == 8 && l.Direction == LegDirection.Arrival);

            Check(fallbackTerminalLeg.Terminal == "T1" &&
                  fallbackTerminalLeg.TerminalSource == TerminalSource.Fallback &&
                  !fallbackTerminalLeg.TerminalUnresolved &&
                  !fallbackTerminalLeg.Issues.Any(i => i.Field == "Terminal"),
                "An uncached terminal formula is resolved by the fallback without a per-leg warning");

            var unlinked = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "edge.xlsx",
                    BaseDate = "2026-10-19",
                    LinkedRows = false
                });

            Check(unlinked.RotationCount == unlinked.TotalLegCount,
                "Unlinked mode creates one rotation per leg");

            var overrideBuilder = new ScheduleFixtureBuilder("Overrides", headerRow: 4);

            overrideBuilder.AddRow(5, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("OV"),
                ["C"] = FixtureCell.TextCell("OV"),
                ["G"] = FixtureCell.TextCell("AAA"),
                ["I"] = FixtureCell.TextCell("D"),
                ["J"] = FixtureCell.TextCell("10:00"),
                ["L"] = FixtureCell.NumberCell(40),
                ["N"] = FixtureCell.TextCell("BBB"),
                ["O"] = FixtureCell.TextCell("D"),
                ["P"] = FixtureCell.TextCell("11:00"),
                ["R"] = FixtureCell.NumberCell(30),
                ["S"] = FixtureCell.TextCell("J"),
                ["T"] = FixtureCell.TextCell("J")
            });

            overrideBuilder.AddCell(5, "X", FixtureCell.NumberCell(99));

            var overridden = FlightScheduleParser.Parse(
                overrideBuilder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "overrides.xlsx",
                    BaseDate = "2026-10-19",
                    ColumnOverrides = new Dictionary<string, string>
                    {
                        ["ArrivalPax"] = "X"
                    }
                });

            var overriddenArrival = overridden.Legs.Single(l =>
                l.SourceRow == 5 && l.Direction == LegDirection.Arrival);

            Check(overridden.Columns["ArrivalPax"] == "X" &&
                  overriddenArrival.PaxRaw == 99,
                "A column override can bind a physical column without a heading");

            Check(overridden.Legs.Where(l => l.Direction == LegDirection.Arrival)
                      .All(l => l.PaxRaw == 99),
                "The override is applied only to the requested field");
        }

        private static void RunFlightScheduleRotationIdLink()
        {
            var builder = new ScheduleFixtureBuilder("Rotation Links", headerRow: 4);

            builder.AddRow(5, new Dictionary<string, FixtureCell>
            {
                ["B"] = FixtureCell.TextCell("RL"),
                ["D"] = FixtureCell.NumberCell(180),
                ["E"] = FixtureCell.TextCell("320"),
                ["F"] = FixtureCell.TextCell("C"),
                ["G"] = FixtureCell.TextCell("AAA"),
                ["I"] = FixtureCell.TextCell("D"),
                ["J"] = FixtureCell.TextCell("23:30"),
                ["L"] = FixtureCell.NumberCell(60),
                ["S"] = FixtureCell.TextCell("J"),
                ["X"] = FixtureCell.TextCell("T1")
            });

            builder.AddRow(6, new Dictionary<string, FixtureCell>
            {
                ["C"] = FixtureCell.TextCell("RL"),
                ["D"] = FixtureCell.NumberCell(180),
                ["E"] = FixtureCell.TextCell("320"),
                ["F"] = FixtureCell.TextCell("C"),
                ["N"] = FixtureCell.TextCell("BBB"),
                ["O"] = FixtureCell.TextCell("D"),
                ["P"] = FixtureCell.TextCell("06:15"),
                ["Q"] = FixtureCell.NumberCell(0.80),
                ["R"] = FixtureCell.NumberCell(55),
                ["T"] = FixtureCell.TextCell("J"),
                ["X"] = FixtureCell.TextCell("T1")
            });

            var result = FlightScheduleParser.Parse(
                builder.Build(),
                new ScheduleImportOptions
                {
                    SourceFile = "rotation-links.xlsx",
                    BaseDate = "2026-10-19",
                    LinkedRows = true,
                    ColumnOverrides = new Dictionary<string, string>
                    {
                        ["RotationId"] = "X"
                    }
                });

            var rotation = result.Rotations().Single();

            Check(result.SourceRowCount == 2 &&
                  result.TotalLegCount == 2 &&
                  result.RotationCount == 1 &&
                  rotation.IsComplete,
                "A shared rotation ID links split arrival and departure rows");

            Check(result.Legs.All(l => l.RotationId == "T1") &&
                  result.Columns["RotationId"] == "X",
                "The rotation ID is preserved and its physical column is reported");

            Check(rotation.TurnMinutes == 405,
                $"A cross-row overnight turn uses the next day ({rotation.TurnMinutes})");
        }

        private static void RunRealScheduleIntegration()
        {
            const string schedulePath = @"N:\ZZ_TRANSFER\261019_Archive\25mppa Schedule.xlsx";

            if (!File.Exists(schedulePath))
            {
                Console.WriteLine(
                    $"  SKIP: optional real-schedule integration test; not found at {schedulePath}");
                return;
            }

            var result = FlightScheduleParser.Parse(
                File.ReadAllBytes(schedulePath),
                new ScheduleImportOptions
                {
                    SourceFile = schedulePath,
                    ScheduleName = "25mppa Schedule",
                    SheetName = string.Empty,
                    BaseDate = string.Empty,
                    LinkedRows = true
                });

            Check(result.SheetName == "26mppa Schedule",
                $"Real schedule: actual worksheet is reported ({result.SheetName})");

            Check(result.SourceRowCount == 292 &&
                  result.TotalLegCount == 582 &&
                  result.ArrivalCount == 291 &&
                  result.DepartureCount == 291 &&
                  result.RotationCount == 292,
                $"Real schedule: {result.SourceRowCount} rows / {result.TotalLegCount} legs / {result.RotationCount} rotations");

            var summary = result.CreateSummary();

            Console.WriteLine(
                $"  Real schedule summary: {summary.SourceRowCount} rows, " +
                $"{summary.TotalLegs} legs ({summary.CompleteRotationCount} complete, " +
                $"{summary.LoneArrivalCount} lone arrivals, {summary.LoneDepartureCount} lone departures), " +
                $"{summary.ArrivalPax} arrival pax, {summary.DeparturePax} departure pax, " +
                $"{summary.PassengerLegs} passenger, {summary.FreightLegs} freight, " +
                $"{summary.UnresolvedTerminals} unresolved terminals, {summary.WarningCount} warnings");

            Check(summary.ArrivalPax == 41653 &&
                  summary.DeparturePax == 40917 &&
                  summary.TotalPax == 82570,
                $"Real schedule: passenger totals are correct ({summary.ArrivalPax} / {summary.DeparturePax})");

            /* This workbook carries no via-qualified arrivals, so the legacy
               Via-qualified filter finds zero transfer connections. The default
               all-passenger-legs eligibility matches direct legs instead and
               the schedule yields a real transfer ceiling. */
            TransferDemandProfile realProfile = new TransferDemandProfile();
            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                realProfile.ScenarioRules[scenario].MinimumConnectionMinutes = 45.0;
                realProfile.ScenarioRules[scenario].MaximumConnectionMinutes = 300.0;
            }

            int allLegsTotal = TransferDemandCalculator
                .CalculateScheduleCapacity(result, realProfile)
                .Total;

            realProfile.ScheduleEligibility = TransferScheduleEligibility.ViaQualifiedLegs;
            int viaOnlyTotal = TransferDemandCalculator
                .CalculateScheduleCapacity(result, realProfile)
                .Total;

            Check(allLegsTotal > 0,
                $"Real schedule: all-legs eligibility finds transfer connections ({allLegsTotal} pax within 45–300 min)");
            Check(viaOnlyTotal == 0,
                $"Real schedule: via-qualified eligibility still finds none on this workbook ({viaOnlyTotal})");

            Check(summary.FreightLegs > 0 &&
                  summary.PassengerLegs > 0 &&
                  summary.ArrivalDomestic > 0 &&
                  summary.ArrivalInternational > 0,
                "Real schedule: passenger, freight, domestic and international legs are present");

            Check(result.Legs.All(l => !string.IsNullOrWhiteSpace(l.IcaoCodeRaw)) &&
                  result.Legs.All(l => !string.IsNullOrWhiteSpace(l.AircraftTypeRaw)),
                "Real schedule: aircraft type and ICAO code are populated separately");

            Check(summary.UnresolvedTerminals > 0 &&
                  summary.FallbackTerminalLegs == 0,
                $"Real schedule: uncached terminal formulas are flagged when no fallback is set " +
                $"({summary.UnresolvedTerminals} legs)");

            var fallbackResult = FlightScheduleParser.Parse(
                File.ReadAllBytes(schedulePath),
                new ScheduleImportOptions
                {
                    SourceFile = schedulePath,
                    ScheduleName = "25mppa Schedule",
                    SheetName = string.Empty,
                    BaseDate = string.Empty,
                    LinkedRows = true,
                    FallbackTerminal = "T1"
                });

            var fallbackSummary = fallbackResult.CreateSummary();

            Console.WriteLine(
                $"  Real schedule fallback: {fallbackSummary.FallbackTerminalLegs} fallback terminals, " +
                $"{fallbackSummary.CargoTerminalLegs} cargo terminals, " +
                $"{fallbackSummary.UnresolvedTerminals} unresolved, {fallbackSummary.WarningCount} warnings");

            Check(fallbackSummary.FallbackTerminalLegs == 562 &&
                  fallbackSummary.UnresolvedTerminals == 0 &&
                  fallbackSummary.CargoTerminalLegs == 20,
                $"Real schedule: the T1 fallback resolves every passenger terminal " +
                $"({fallbackSummary.FallbackTerminalLegs} fallback, {fallbackSummary.CargoTerminalLegs} cargo)");

            Check(fallbackResult.Legs
                    .Where(l => l.ServiceType == LegServiceType.Passenger)
                    .All(l => l.Terminal == "T1" && l.TerminalSource == TerminalSource.Fallback),
                "Real schedule: every passenger leg carries the explicit fallback terminal");

            Check(fallbackResult.Legs
                    .Where(l => l.ServiceType == LegServiceType.Freight)
                    .All(l => l.Terminal == "Cargo" && !l.TerminalIsFallback),
                "Real schedule: every freight leg keeps Cargo rather than the passenger fallback");

            Check(fallbackSummary.WarningCount == 1 &&
                  fallbackSummary.ErrorCount == 0 &&
                  fallbackResult.GlobalIssues.Count(i =>
                      i.Field == "Terminal" &&
                      i.Message.IndexOf("fallback terminal 'T1'", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                $"Real schedule: the fallback is reported once, not as 562 warnings " +
                $"({fallbackSummary.WarningCount} warning)");

            var errors = result.GlobalIssues
                .Where(i => i.Severity == "Error")
                .Select(i => $"Workbook · {i.Field}: {i.Message}")
                .Concat(result.Legs.SelectMany(l => l.Issues
                    .Where(i => i.Severity == "Error")
                    .Select(i => $"Row {l.SourceRow} {l.Direction} · {i.Field}: {i.Message}")))
                .ToList();

            foreach (var error in errors)
                Console.WriteLine($"  Real schedule error: {error}");

            Check(errors.Count == 0,
                $"Real schedule: no blocking parse errors ({errors.Count})");
        }

        // -------------------------------------------------------------
        // 15. Large demand timing (the reported hang)
        // -------------------------------------------------------------
        private static void RunLargeDemandTiming()
        {
            Console.WriteLine("== Large demand timing ==");

            List<PassengerGroup> groups = BuildGroups(
                5700, new[] { 100.0, 100.0, 100.0, 100.0, 100.0 }, 2,
                100.0, 100.0, warmUp: true, warmMinutes: 60, warmFactor: 50.0);

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.5, StandardDeviation = 0.6 }
            };

            System.Diagnostics.Stopwatch genWatch = System.Diagnostics.Stopwatch.StartNew();

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 99, null);

            genWatch.Stop();

            Console.WriteLine($"  generated {gen.Passengers.Count} passengers in {genWatch.ElapsedMilliseconds} ms");

            Check(gen.Passengers.Count >= 25000,
                $"Generated a multi-hour population ({gen.Passengers.Count} passengers)");

            CheckInInputs input = new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 1,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            };

            System.Diagnostics.Stopwatch optWatch = System.Diagnostics.Stopwatch.StartNew();

            CheckInResults result = new CheckInSolver().Solve(input);

            optWatch.Stop();

            Console.WriteLine(
                $"  optimized {result.PhysicalCounters} counters in {optWatch.ElapsedMilliseconds} ms " +
                $"(design-step wait {Math.Round(result.QueueResults.GetEvaluationWait95(), 2)} min)");

            Check(optWatch.ElapsedMilliseconds < 15000,
                $"30k-passenger optimize finishes quickly ({optWatch.ElapsedMilliseconds} ms < 15000 ms)");

            Check(result.PhysicalCounters >= 1 && result.PhysicalCounters <= 1200,
                $"Large-demand result respects the domain ({result.PhysicalCounters})");

            Check(result.QueueResults.ProcessedPassengers.Count > 0,
                "Large-demand solve processed passengers");

            CheckInSolver cachedSolver = new CheckInSolver();

            System.Diagnostics.Stopwatch coldWatch = System.Diagnostics.Stopwatch.StartNew();
            CheckInResults cold = cachedSolver.Solve(FreshInput(gen, map));
            coldWatch.Stop();

            System.Diagnostics.Stopwatch warmWatch = System.Diagnostics.Stopwatch.StartNew();
            CheckInResults warm = cachedSolver.Solve(FreshInput(gen, map));
            warmWatch.Stop();

            Check(warm.PhysicalCounters == cold.PhysicalCounters,
                $"Cached second solve returns the same capacity ({warm.PhysicalCounters} == {cold.PhysicalCounters})");

            Console.WriteLine(
                $"  cache check: cold {coldWatch.ElapsedMilliseconds} ms, warm {warmWatch.ElapsedMilliseconds} ms");

            Check(warmWatch.ElapsedMilliseconds <= coldWatch.ElapsedMilliseconds,
                $"Cached solve is not slower ({warmWatch.ElapsedMilliseconds} ms <= {coldWatch.ElapsedMilliseconds} ms)");
        }

        private static CheckInInputs FreshInput(
            PassengerGenerationResult gen,
            ServiceTimeMap map)
        {
            return new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 1,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            };
        }

        // -------------------------------------------------------------
        // 16. Poisson sampler is bounded and unbiased
        // -------------------------------------------------------------
        private static void RunPoissonSampler()
        {
            Console.WriteLine("== Poisson sampler ==");

            Check(PoissonDistribution.Sample(0.0, new Random(1)) == 0, "lambda 0 -> 0");
            Check(PoissonDistribution.Sample(-5.0, new Random(1)) == 0, "lambda -5 -> 0");
            Check(PoissonDistribution.Sample(double.NaN, new Random(1)) == 0, "lambda NaN -> 0");

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int big = PoissonDistribution.Sample(5000.0, new Random(7));
            watch.Stop();

            Check(watch.ElapsedMilliseconds < 50,
                $"Sample(5000) is instant ({watch.ElapsedMilliseconds} ms) -> {big}");

            Check(Math.Abs(big - 5000.0) < 150.0,
                $"Sample(5000) lands near its mean ({big})");

            const int draws = 200_000;
            double mean = 0.0;

            Random rnd = new Random(12345);

            for (int i = 0; i < draws; i++)
                mean += PoissonDistribution.Sample(4.0, rnd);

            mean /= draws;

            Check(Math.Abs(mean - 4.0) < 0.05,
                $"Sample(4) mean {Math.Round(mean, 4)} ~= 4.0 over {draws:N0} draws");

            Random a = new Random(99);
            Random b = new Random(99);

            bool identical = true;

            for (int i = 0; i < 1000; i++)
            {
                if (PoissonDistribution.Sample(6.5, a) != PoissonDistribution.Sample(6.5, b))
                {
                    identical = false;
                    break;
                }
            }

            Check(identical, "Fixed seed -> identical Poisson sequence");
        }

        // -------------------------------------------------------------
        // 17. Legacy Passenger Generator survives design-hour demand in the thousands
        // -------------------------------------------------------------
        private static void RunLargeDemandPassengerGenerator()
        {
            Console.WriteLine("== Passenger Generator at large DHD ==");

            PassengerDemand demand = new PassengerDemand
            {
                DesignHourDemand = 30000,
                DeparturePercentage = 70.0,
                DomesticPercentage = 70.0
            };

            ArrivalProfile narrow = new ArrivalProfile { Peak = 90.0, Sigma = 20.0 };

            List<PassengerProfile> domestic = new List<PassengerProfile>
            {
                new PassengerProfile
                {
                    Name = "Economy",
                    Percentage = 100.0,
                    Journey = PassengerJourney.Origin,
                    OnlineCheckInProbability = 0.2,
                    CheckedBagProbability = 0.5,
                    ServiceTimeMultiplier = 1.0,
                    MaleProbability = 0.5,
                    ArrivalProfile = narrow
                }
            };

            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            PassengerGenerator generator = new PassengerGenerator();

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            PassengerGenerationResult result = generator.Generate(
                demand, domestic, new List<PassengerProfile>(), walking,
                800, 300, 42, null);

            watch.Stop();

            int expected = demand.DomesticPassengers;

            Console.WriteLine(
                $"  generated {result.Passengers.Count} of {expected} expected in {watch.ElapsedMilliseconds} ms");

            Check(watch.ElapsedMilliseconds < 15000,
                $"Narrow-kernel 30k generation completes ({watch.ElapsedMilliseconds} ms < 15000 ms)");

            Check(result.Passengers.Count > 0,
                $"Generated passengers ({result.Passengers.Count})");

            System.Diagnostics.Stopwatch guardWatch = System.Diagnostics.Stopwatch.StartNew();

            List<PassengerProfile> zeroSigma = new List<PassengerProfile>
            {
                new PassengerProfile
                {
                    Name = "Zero",
                    Percentage = 100.0,
                    Journey = PassengerJourney.Origin,
                    MaleProbability = 0.5,
                    ArrivalProfile = new ArrivalProfile { Peak = 90.0, Sigma = 0.0 }
                }
            };

            PassengerGenerationResult guarded = new PassengerGenerator().Generate(
                demand, zeroSigma, new List<PassengerProfile>(), walking,
                800, 300, 42, null);

            guardWatch.Stop();

            Check(guardWatch.ElapsedMilliseconds < 15000,
                $"Sigma 0 is guarded ({guardWatch.ElapsedMilliseconds} ms)");

            Check(guarded.Passengers.Count > 0,
                $"Sigma 0 still produces passengers ({guarded.Passengers.Count})");
        }

        // -------------------------------------------------------------
        // 18. Outgoing population carries service history and is a new list
        // -------------------------------------------------------------
        private static void RunOutputPopulationMerge()
        {
            Console.WriteLine("== Output population merge ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.4 }
            };

            ProfileChannelMap routing = new ProfileChannelMap { DefaultChannel = "Counter" };
            routing.Entries.Add(new ProfileChannelEntry { ProfileName = "Solo" });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Counter", Share = 0.5 });
            routing.Entries[0].Shares.Add(new ChannelShare { Channel = "Web", Share = 0.5 });

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1200, 1, new[] { 100.0, 0.0, 0.0, 0.0, 0.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Solo", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 8, routing);

            List<Passenger> source = gen.Passengers;

            CheckInResults result = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = source,
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 6 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0
            });

            List<Passenger> output = result.QueueResults.Passengers;

            Check(ReferenceEquals(output, source) == false,
                "Outgoing list is a new instance, not the input list");

            Check(output.Count == source.Count,
                $"Population preserved ({output.Count} == {source.Count})");

            bool allCarriedHistory = output
                .Where(p => p.Channel == "Counter")
                .All(p => p.History.ContainsKey(FacilityType.CheckIn));

            Check(allCarriedHistory, "Served Counter passengers carry Check-In history downstream");

            Check(output
                    .Where(p => p.Channel == "Web")
                    .All(p => !p.History.ContainsKey(FacilityType.CheckIn)),
                "Web passengers have no physical Check-In history");

            bool unservedUntouched = output
                .Where(p => p.Channel != "Counter")
                .All(p => !p.History.ContainsKey(FacilityType.CheckIn));

            Check(unservedUntouched, "Unserved passengers are passed through untouched");

            bool sourceUnmutated = !source.Any(p => p.History.ContainsKey(FacilityType.CheckIn));

            Check(sourceUnmutated, "Upstream population is never mutated");

            Passenger served = output.FirstOrDefault(p => p.History.ContainsKey(FacilityType.CheckIn));

            if (served != null)
            {
                Check(served.ArrivalTime >= served.EndServiceTime - 1e-9,
                    $"Served passenger clock advanced ({Math.Round(served.ArrivalTime, 1)} >= {Math.Round(served.EndServiceTime, 1)})");
            }
            else
            {
                Check(false, "Expected at least one served passenger");
            }
        }

        // -------------------------------------------------------------
        // 19. Legacy Peak/Sigma Arrival Profile is ignored by windowed generation
        // -------------------------------------------------------------
        private static void RunArrivalProfileOptional()
        {
            Console.WriteLine("== Legacy Arrival Profile ignored by windowed generation ==");

            PassengerProfile legacyCarrier = MakeProfile("Domestic", 100.0, 60.0, 150.0);

            legacyCarrier.ArrivalProfile = new ArrivalProfile { Peak = 45.0, Sigma = 12.0 };

            List<PassengerGroup> withApGroups = BuildGroupsWithProfiles(
                1200, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { legacyCarrier },
                new List<PassengerProfile> { StrippedProfile("International", 100.0, 120.0, 240.0) });

            List<PassengerGroup> withoutApGroups = BuildGroupsWithProfiles(
                1200, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { StrippedProfile("Domestic", 100.0, 60.0, 150.0) },
                new List<PassengerProfile> { StrippedProfile("International", 100.0, 120.0, 240.0) });

            PassengerGenerationResult withAp = Generate(withApGroups, new WaveSettings(), 7);
            PassengerGenerationResult withoutAp = Generate(withoutApGroups, new WaveSettings(), 7);

            Check(withoutAp.Passengers.Count == withAp.Passengers.Count &&
                  withAp.Passengers.Count > 0,
                $"AP-less run produces the full population ({withoutAp.Passengers.Count} == {withAp.Passengers.Count})");

            bool identical = withAp.Passengers.Count == withoutAp.Passengers.Count;

            for (int i = 0; identical && i < withAp.Passengers.Count; i++)
            {
                identical =
                    withAp.Passengers[i].ArrivalTime == withoutAp.Passengers[i].ArrivalTime &&
                    withAp.Passengers[i].QueueArrivalTime == withoutAp.Passengers[i].QueueArrivalTime;
            }

            Check(identical, "windowed arrivals ignore the legacy Peak/Sigma Arrival Profile");

            PassengerGenerationResult withShowUp = Generate(
                withoutApGroups, new WaveSettings(), 7, ShowUpProfile.Leisure());

            bool differs = withShowUp.Passengers.Count != withoutAp.Passengers.Count;

            for (int i = 0; !differs && i < withShowUp.Passengers.Count; i++)
            {
                if (withShowUp.Passengers[i].ArrivalTime != withoutAp.Passengers[i].ArrivalTime)
                    differs = true;
            }

            Check(differs, "a Show-Up Profile does change windowed arrivals");
        }

        // -------------------------------------------------------------
        // 20. Legacy generator survives a null Arrival Profile
        // -------------------------------------------------------------
        private static void RunLegacyNullArrivalProfile()
        {
            Console.WriteLine("== Legacy generator with null Arrival Profile ==");

            PassengerDemand demand = new PassengerDemand
            {
                DesignHourDemand = 300,
                DeparturePercentage = 100.0,
                DomesticPercentage = 60.0
            };

            int expected = demand.DomesticPassengers + demand.InternationalPassengers;

            List<PassengerProfile> domestic = new List<PassengerProfile>
            {
                StrippedProfile("Economy", 100.0, 60.0, 150.0)
            };

            List<PassengerProfile> international = new List<PassengerProfile>
            {
                StrippedProfile("Business", 100.0, 60.0, 150.0)
            };

            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            PassengerGenerationResult result = new PassengerGenerator().Generate(
                demand, domestic, international, walking,
                240, 180, 11, null);

            watch.Stop();

            int tolerance = Math.Max(5, expected / 20);

            Check(Math.Abs(result.Passengers.Count - expected) <= tolerance,
                $"Null-AP legacy run produces ~the full demand ({result.Passengers.Count} ~= {expected})");

            bool inWindow = result.Passengers.All(p =>
                p.ArrivalTime >= 240.0 - 180.0 && p.ArrivalTime <= 240.0);

            Check(inWindow, "Null-AP fallback arrivals land inside the horizon window");

            Check(watch.ElapsedMilliseconds < 15000,
                $"Null-AP legacy run completes ({watch.ElapsedMilliseconds} ms < 15000 ms)");
        }

        // -------------------------------------------------------------
        // 21. Show-up curves: presets, window shares and the Gaussian fit
        // -------------------------------------------------------------
        private static void RunShowUpCurves()
        {
            Console.WriteLine("== Show-up curves ==");

            ShowUpProfile leisure = ShowUpProfile.Leisure();
            ShowUpProfile business = ShowUpProfile.Business();
            ShowUpProfile mix = ShowUpProfile.Mix();

            foreach (ShowUpProfile preset in new[] { leisure, business, mix })
            {
                Check(preset.Bands.Count == 10,
                    $"{preset.Name}: 10 bands ({preset.Bands.Count})");

                double total = preset.Bands.Sum();

                Check(Math.Abs(total - 1.0) < 1e-9,
                    $"{preset.Name}: bands sum to 100% ({total:0.######})");

                Check(preset.GetValidationErrors().Count == 0,
                    $"{preset.Name}: passes validation");

                Check(preset.W60 == 1.0, $"{preset.Name}: W60 is 100%");
            }

            double[] expectedLeisure = { 0.26, 0.12, 0.03, 0.08, 0.15, 0.10, 0.07, 0.13, 0.04, 0.02 };
            bool leisureExact = true;

            for (int i = 0; i < 10; i++)
            {
                if (Math.Abs(leisure.Bands[i] - expectedLeisure[i]) > 1e-9)
                    leisureExact = false;
            }

            Check(leisureExact, "Leisure bands match the published table");

            Check(Math.Abs(leisure.GetWindowShare(30) - 0.36) < 0.005,
                $"Leisure W30 == 0.36 ({leisure.GetWindowShare(30):0.###})");

            Check(Math.Abs(leisure.GetWindowShare(15) - 0.125) < 0.005,
                $"Leisure W15 == 0.125 ({leisure.GetWindowShare(15):0.###})");

            Check(Math.Abs(business.GetWindowShare(30) - 0.56) < 0.005,
                $"Business W30 == 0.56 ({business.GetWindowShare(30):0.###})");

            Check(Math.Abs(mix.GetWindowShare(30) - 0.43) < 0.005,
                $"Mix W30 == 0.43 ({mix.GetWindowShare(30):0.###})");

            double blendedFirstBand = (leisure.Bands[0] + business.Bands[0]) / 2.0;

            Check(Math.Abs(mix.Bands[0] - blendedFirstBand) > 0.01,
                $"Mix first band {mix.Bands[0]:0.###} is not the blend {blendedFirstBand:0.###}");

            Check(leisure.GetCumulativeAt(6.0) > business.GetCumulativeAt(6.0),
                $"Leisure arrives earlier ({leisure.GetCumulativeAt(6.0):0.###} vs {business.GetCumulativeAt(6.0):0.###} by 54 min out)");

            ShowUpProfile fitted = ShowUpProfile.FromWindowShares("Fitted", 0.125, 0.36);

            Check(fitted.Bands.Count == 10, $"Fitted profile has 10 bands ({fitted.Bands.Count})");
            Check(Math.Abs(fitted.Bands.Sum() - 1.0) < 1e-9,
                $"Fitted bands sum to 100% ({fitted.Bands.Sum():0.######})");

            Check(Math.Abs(fitted.GetWindowShare(15) - 0.125) < 0.02,
                $"Fitted W15 ~= 0.125 ({fitted.GetWindowShare(15):0.###})");

            Check(Math.Abs(fitted.GetWindowShare(30) - 0.36) < 0.02,
                $"Fitted W30 ~= 0.36 ({fitted.GetWindowShare(30):0.###})");

            Check(fitted.GetValidationErrors().Count == 0, "Fitted profile passes validation");

            Check(fitted.W15 == 0.125 && fitted.W30 == 0.36,
                "Fitted profile reports the requested window shares");

            ShowUpProfile flat = ShowUpProfile.FromWindowShares("Flat", 0.25, 0.50);
            Check(Math.Abs(flat.GetWindowShare(30) - 0.50) < 0.02,
                $"Flat profile W30 ~= 0.50 ({flat.GetWindowShare(30):0.###})");

            ShowUpProfile three = ShowUpProfile.FromBands("Three", new List<double> { 0.6, 0.3, 0.1 });
            Check(three.Bands.Count == 10, $"3-value input resampled to 10 ({three.Bands.Count})");
            Check(Math.Abs(three.Bands.Sum() - 1.0) < 1e-9, "Resampled bands sum to 100%");
            Check(Math.Abs(three.GetCumulativeAt(20.0) - 0.6) < 0.02,
                $"Resampled curve passes through the 60% point ({three.GetCumulativeAt(20.0):0.###})");

            ShowUpProfile twenty = ShowUpProfile.FromBands(
                "Twenty", Enumerable.Range(1, 20).Select(i => (double)i).ToList());

            Check(twenty.Bands.Count == 10, $"20-value input resampled to 10 ({twenty.Bands.Count})");
            Check(Math.Abs(twenty.Bands.Sum() - 1.0) < 1e-9, "Resampled 20 bands sum to 100%");

            bool decreasing = true;

            for (int i = 1; i < three.Bands.Count; i++)
            {
                if (three.Bands[i] > three.Bands[i - 1] + 1e-9)
                    decreasing = false;
            }

            Check(decreasing,
                "Resampled bands stay non-increasing for a decreasing source (" +
                string.Join(",", three.Bands.Select(v => v.ToString("0.000"))) + ")");

            ShowUpProfile empty = ShowUpProfile.FromBands("Empty", new List<double>());
            Check(empty.Bands.Count == 10 && empty.Bands.Sum() > 0.0,
                "Empty input falls back to a uniform curve");

            ShowUpProfile negative = ShowUpProfile.FromBands("Negative", new List<double> { -1.0, 2.0, 3.0 });
            Check(negative.Bands.Count == 10 && negative.Bands.All(v => v >= 0.0),
                "Negative input is clamped rather than rejected");

            Check(Math.Abs(leisure.GetBandStartMinutesBeforeDeparture(0) - 54.0) < 1e-9,
                "First band starts 54 minutes before departure");

            Check(Math.Abs(leisure.GetBandStartMinutesBeforeDeparture(9) - 0.0) < 1e-9,
                "Last band starts at departure");
        }

        // -------------------------------------------------------------
        // 22. A show-up profile shapes arrivals for the whole run
        // -------------------------------------------------------------
        private static void RunShowUpCurveGeneration()
        {
            Console.WriteLine("== Show-up curve generation ==");

            List<PassengerGroup> groups = BuildGroups(
                4000, new[] { 100.0, 100.0 }, 1, 100.0, 50.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            List<string> warnings = new List<string>();

            PassengerGenerationResult mixed = GenerateCurved(
                groups, new WaveSettings(), 31337, ShowUpProfile.Mix(), warnings.Add);

            Check(warnings.Count == 0, $"No warnings on a clean curved run ({warnings.Count})");
            Check(mixed.Passengers.Count > 0, $"Population generated ({mixed.Passengers.Count})");

            for (int step = 0; step < groups[0].DHP.StepCount; step++)
            {
                int actual = mixed.Passengers.Count(p => p.DemandStepIndex == step);
                int expected = TotalStepDemand(groups, step);

                Check(actual == expected,
                    $"Hour {step}: {actual} generated == {expected} demanded");
            }

            List<Passenger> dom = mixed.Passengers
                .Where(p => p.ProfileName == "Domestic")
                .ToList();

            List<Passenger> intl = mixed.Passengers
                .Where(p => p.ProfileName == "International")
                .ToList();

            Check(dom.Count > 0 && intl.Count > 0,
                $"Both populations generated (domestic {dom.Count}, international {intl.Count})");

            PassengerGenerationResult leisure = GenerateCurved(
                groups, new WaveSettings(), 31337, ShowUpProfile.Leisure(), warnings.Add);

            List<Passenger> domHour = leisure.Passengers
                .Where(p => p.ProfileName == "Domestic" && p.DemandStepIndex == 0)
                .ToList();

            List<Passenger> intlHour = leisure.Passengers
                .Where(p => p.ProfileName == "International" && p.DemandStepIndex == 0)
                .ToList();

            double domesticEarly = (double)domHour.Count(p => p.ArrivalTime < 20.0) / domHour.Count;
            double internationalEarly = (double)intlHour.Count(p => p.ArrivalTime < 20.0) / intlHour.Count;

            Check(Math.Abs(domesticEarly - internationalEarly) < 0.03,
                $"One profile shapes both groups alike ({domesticEarly:P1} vs {internationalEarly:P1} early)");

            Check(domesticEarly > 0.38,
                $"Leisure front-loads ({domesticEarly:P1} arrive in the first 20 min, expected ~44%)");

            ShowUpProfile curve = ShowUpProfile.Leisure();

            List<Passenger> stepZero = leisure.Passengers
                .Where(p => p.DemandStepIndex == 0)
                .ToList();

            int[] bandCounts = new int[10];

            foreach (Passenger p in stepZero)
            {
                int band = (int)Math.Floor(p.ArrivalTime / 6.0);

                if (band >= 0 && band < 10)
                    bandCounts[band]++;
            }

            double bandTotal = bandCounts.Sum();
            bool bandsRoughlyMatch = bandTotal > 0;

            for (int b = 0; b < 10 && bandsRoughlyMatch; b++)
            {
                double expectedShare = curve.Bands[b];
                double actualShare = bandCounts[b] / bandTotal;

                bandsRoughlyMatch = Math.Abs(actualShare - expectedShare) < 0.05;
            }

            Check(bandsRoughlyMatch,
                "Band counts track the published curve " +
                $"({string.Join(",", bandCounts.Select(c => Math.Round(100.0 * c / Math.Max(1, bandTotal)).ToString("0")))})");

            PassengerGenerationResult again = GenerateCurved(
                groups, new WaveSettings(), 31337, ShowUpProfile.Mix(), warnings.Add);

            bool repeatable = mixed.Passengers.Count == again.Passengers.Count;

            for (int i = 0; repeatable && i < mixed.Passengers.Count; i++)
            {
                if (mixed.Passengers[i].ArrivalTime != again.Passengers[i].ArrivalTime)
                    repeatable = false;
            }

            Check(repeatable, "Fixed seed -> identical curved arrivals");

            PassengerGenerationResult noCurve = GenerateCurved(
                groups, new WaveSettings(), 31337, null, warnings.Add);

            Check(noCurve.Passengers.Count == mixed.Passengers.Count,
                $"Curve does not change population size ({mixed.Passengers.Count})");

            Check(mixed.Passengers.All(p => p.WaveId < 0),
                "Coverage Off leaves every passenger unbatched");
        }

        // -------------------------------------------------------------
        // 23. Each passenger carries the DHP type it was generated from
        // -------------------------------------------------------------
        private static void RunDHPTypeTagging()
        {
            Console.WriteLine("== DHP type tagging ==");

            List<PassengerGroup> groups = BuildGroups(
                4000, new[] { 100.0, 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), 17);

            foreach (PassengerGroup group in groups)
            {
                int expected = 0;

                for (int step = 0; step < group.DHP.StepCount; step++)
                    expected += (int)Math.Round(group.DHP.GetStepDemand(step));

                int actual = result.Passengers.Count(p => p.DHPType == group.DHP.Type);

                Check(actual == expected,
                    $"{group.DHP.Type}: {actual} passengers tagged == {expected} demanded");
            }

            bool domesticMaps = result.Passengers
                .Where(p => p.DHPType == DHPType.DomesticDeparture ||
                            p.DHPType == DHPType.DomesticArrival)
                .All(p => p.PassengerType == PassengerType.Domestic);

            Check(domesticMaps, "Domestic DHP types map to Domestic passenger type");

            bool internationalMaps = result.Passengers
                .Where(p => p.DHPType == DHPType.InternationalDeparture ||
                            p.DHPType == DHPType.InternationalArrival)
                .All(p => p.PassengerType == PassengerType.International);

            Check(internationalMaps, "International DHP types map to International passenger type");
        }

        // -------------------------------------------------------------
        // 24. Each type generates exactly its own per-step demand
        // -------------------------------------------------------------
        private static void RunPerTypeDemandCounts()
        {
            Console.WriteLine("== Per-type demand counts ==");

            List<PassengerGroup> groups = BuildGroups(
                4000, new[] { 80.0, 100.0, 60.0 }, 1, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), 808);

            int expectedTotal = 0;
            bool everyTypeExact = true;

            foreach (PassengerGroup group in groups)
            {
                for (int step = 0; step < groups[0].DHP.StepCount; step++)
                {
                    int expected = (int)Math.Round(group.DHP.GetStepDemand(step));
                    int actual = result.Passengers.Count(
                        p => p.DHPType == group.DHP.Type && p.DemandStepIndex == step);

                    if (actual != expected)
                    {
                        everyTypeExact = false;
                        Check(false, $"{group.DHP.Type} step {step}: {actual} != {expected}");
                    }
                }

                int groupTotal = 0;
                for (int step = 0; step < groups[0].DHP.StepCount; step++)
                    groupTotal += (int)Math.Round(group.DHP.GetStepDemand(step));

                expectedTotal += groupTotal;
            }

            Check(everyTypeExact, "Every type generates exactly its own per-step demand");
            Check(result.Passengers.Count == expectedTotal,
                $"Two groups total {result.Passengers.Count} == {expectedTotal}");
        }

        // -------------------------------------------------------------
        // 25. Two departure groups plus warm-up reconcile to the generated total
        // -------------------------------------------------------------
        private static void RunFourGroupTotal()
        {
            Console.WriteLine("== Two-group total ==");

            List<PassengerGroup> groups = BuildGroups(
                3000, new[] { 40.0, 100.0, 70.0, 50.0, 30.0 }, 1, 90.0, 40.0,
                warmUp: true, warmMinutes: 45, warmFactor: 60.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), 4242);

            int stepTotal = 0;
            for (int step = 0; step < groups[0].DHP.StepCount; step++)
                stepTotal += TotalStepDemand(groups, step);

            int warmTotal = TotalWarmUpDemand(groups);

            Check(result.Passengers.Count == stepTotal + warmTotal,
                $"Population {result.Passengers.Count} == steps {stepTotal} + warm-up {warmTotal}");

            Check(result.Passengers.Count(p => p.IsWarmUp) == warmTotal,
                $"Warm-up passengers {result.Passengers.Count(p => p.IsWarmUp)} == {warmTotal}");

            Check(result.Passengers.Count(p => !p.IsWarmUp) == stepTotal,
                $"Analysis-window passengers {result.Passengers.Count(p => !p.IsWarmUp)} == {stepTotal}");
        }

        // -------------------------------------------------------------
        // 26. DHP validation and arithmetic
        // -------------------------------------------------------------
        private static void RunDHPValidation()
        {
            Console.WriteLine("== DHP validation ==");

            DHP valid = new DHP
            {
                Type = DHPType.DomesticDeparture,
                DesignHourDemand = 1000,
                DemandFactors = new List<double> { 50.0, 100.0, 80.0 },
                AnalysisPeriodHours = 3,
                DesignHourIndex = 1
            };

            Check(valid.GetValidationErrors().Count == 0, "Valid DHP has no errors");

            DHP wrongCount = new DHP
            {
                DemandFactors = new List<double> { 50.0, 100.0 },
                AnalysisPeriodHours = 3,
                DesignHourIndex = 1
            };
            Check(wrongCount.GetValidationErrors().Count > 0, "Factor count mismatch rejected");

            DHP negative = new DHP
            {
                DemandFactors = new List<double> { 50.0, -10.0, 80.0 },
                AnalysisPeriodHours = 3,
                DesignHourIndex = 1
            };
            Check(negative.GetValidationErrors().Count > 0, "Negative factor rejected");

            DHP badIndex = new DHP
            {
                DemandFactors = new List<double> { 50.0, 100.0, 80.0 },
                AnalysisPeriodHours = 3,
                DesignHourIndex = 5
            };
            Check(badIndex.GetValidationErrors().Count > 0, "Design Hour Index out of range rejected");

            DHP badWarm = new DHP
            {
                DemandFactors = new List<double> { 50.0, 100.0, 80.0 },
                AnalysisPeriodHours = 3,
                DesignHourIndex = 1,
                WarmUpEnabled = true,
                WarmUpMinutes = 60,
                WarmUpFactor = -5.0
            };
            Check(badWarm.GetValidationErrors().Count > 0, "Negative warm-up factor rejected");

            Check(Math.Abs(valid.GetStepDemand(1) - 1000.0) < 1e-9,
                "GetStepDemand scales DHD by the 100% factor");
            Check(Math.Abs(valid.GetStepDemand(0) - 500.0) < 1e-9,
                "GetStepDemand scales DHD by the 50% factor");
            Check(Math.Abs(valid.GetStepDemand(99)) < 1e-9,
                "GetStepDemand returns 0 outside the window");

            DHP warm = new DHP
            {
                DesignHourDemand = 1000,
                DemandFactors = new List<double> { 100.0 },
                AnalysisPeriodHours = 1,
                DesignHourIndex = 0,
                WarmUpEnabled = true,
                WarmUpMinutes = 60,
                WarmUpFactor = 50.0
            };
            Check(Math.Abs(warm.GetWarmUpDemand() - 500.0) < 1e-9,
                "GetWarmUpDemand = DHD x factor x duration");
        }

        // -------------------------------------------------------------
        // 27. Passenger group validation
        // -------------------------------------------------------------
        private static void RunPassengerGroupValidation()
        {
            Console.WriteLine("== Passenger group validation ==");

            DHP dhp = new DHP
            {
                Type = DHPType.DomesticDeparture,
                DesignHourDemand = 1000,
                DemandFactors = new List<double> { 100.0 },
                AnalysisPeriodHours = 1,
                DesignHourIndex = 0
            };

            PassengerGroup nullDhp = new PassengerGroup
            {
                DHP = null,
                Profiles = new List<PassengerProfile> { MakeProfile("D", 100.0, 60.0, 150.0) }
            };
            Check(nullDhp.GetValidationErrors().Count > 0, "Missing DHP rejected");

            PassengerGroup noProfiles = new PassengerGroup
            {
                DHP = dhp,
                Profiles = new List<PassengerProfile>()
            };
            Check(noProfiles.GetValidationErrors().Count > 0, "Empty profiles rejected");

            PassengerGroup badSum = new PassengerGroup
            {
                DHP = dhp,
                Profiles = new List<PassengerProfile>
                {
                    MakeProfile("A", 40.0, 60.0, 150.0),
                    MakeProfile("B", 40.0, 60.0, 150.0)
                }
            };
            Check(badSum.GetValidationErrors().Count > 0, "Profiles not summing to 100% rejected");

            PassengerGroup valid = new PassengerGroup
            {
                DHP = dhp,
                Profiles = new List<PassengerProfile>
                {
                    MakeProfile("A", 60.0, 60.0, 150.0),
                    MakeProfile("B", 40.0, 60.0, 150.0)
                }
            };
            Check(valid.GetValidationErrors().Count == 0, "Valid group has no errors");
        }

        // -------------------------------------------------------------
        // 28. Changing one group leaves the others' counts untouched
        // -------------------------------------------------------------
        private static void RunGroupIndependence()
        {
            Console.WriteLine("== Group independence ==");

            List<PassengerGroup> baseline = BuildGroups(
                2000, new[] { 100.0 }, 0, 100.0, 100.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            baseline[1].DHP.DesignHourDemand = 500;

            PassengerGenerationResult baseResult = Generate(baseline, new WaveSettings(), 900);

            int baseDomDep = baseResult.Passengers.Count(p => p.DHPType == DHPType.DomesticDeparture);
            int baseIntlDep = baseResult.Passengers.Count(p => p.DHPType == DHPType.InternationalDeparture);

            List<PassengerGroup> changed = BuildGroups(
                2000, new[] { 100.0 }, 0, 100.0, 100.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            changed[1].DHP.DesignHourDemand = 5000;

            PassengerGenerationResult changedResult = Generate(changed, new WaveSettings(), 900);

            int changedDomDep = changedResult.Passengers.Count(p => p.DHPType == DHPType.DomesticDeparture);
            int changedIntlDep = changedResult.Passengers.Count(p => p.DHPType == DHPType.InternationalDeparture);

            Check(baseDomDep == changedDomDep,
                $"Changing international departure leaves domestic departure untouched ({baseDomDep} == {changedDomDep})");
            Check(baseIntlDep == 500, $"Baseline international departure demand honoured ({baseIntlDep} == 500)");
            Check(changedIntlDep == 5000, $"Changed international departure demand honoured ({changedIntlDep} == 5000)");
        }

        // -------------------------------------------------------------
        // 29. Design hour is tagged for every populated type
        // -------------------------------------------------------------
        private static void RunDesignHourPerType()
        {
            Console.WriteLine("== Design hour per type ==");

            List<PassengerGroup> groups = BuildGroups(
                4000, new[] { 50.0, 100.0, 60.0, 40.0, 30.0 }, 1, 80.0, 50.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), 300);

            bool designOnlyStep1 = result.Passengers
                .Where(p => p.IsDesignStep)
                .All(p => p.DemandStepIndex == 1);
            Check(designOnlyStep1, "All design-tagged passengers are in step 1");

            int designExpected = TotalStepDemand(groups, 1);
            Check(result.Passengers.Count(p => p.IsDesignStep) == designExpected,
                $"Design-tagged count {result.Passengers.Count(p => p.IsDesignStep)} == {designExpected}");

            DHPType[] types =
            {
                DHPType.DomesticDeparture, DHPType.InternationalDeparture
            };

            bool everyType = true;

            foreach (DHPType type in types)
            {
                if (!result.Passengers.Any(p => p.DHPType == type && p.IsDesignStep))
                    everyType = false;
            }

            Check(everyType, "Every populated departure type has design-step passengers");
        }

        // -------------------------------------------------------------
        // 30. Warm-up is tagged per type
        // -------------------------------------------------------------
        private static void RunWarmUpPerType()
        {
            Console.WriteLine("== Warm-up per type ==");

            List<PassengerGroup> groups = BuildGroups(
                2000, new[] { 100.0 }, 0, 80.0, 50.0,
                warmUp: true, warmMinutes: 60, warmFactor: 40.0);

            PassengerGenerationResult result = Generate(groups, new WaveSettings(), 606);

            bool everyTypeExact = true;

            foreach (PassengerGroup group in groups)
            {
                int expected = (int)Math.Round(group.DHP.GetWarmUpDemand());
                int actual = result.Passengers.Count(
                    p => p.DHPType == group.DHP.Type && p.IsWarmUp);

                if (actual != expected)
                {
                    everyTypeExact = false;
                    Check(false, $"{group.DHP.Type} warm-up {actual} != {expected}");
                }
            }

            Check(everyTypeExact, "Every type emits exactly its own warm-up demand");

            bool warmNoStep = result.Passengers
                .Where(p => p.IsWarmUp)
                .All(p => p.DemandStepIndex == -1 && p.DemandHourIndex == -1);
            Check(warmNoStep, "Warm-up passengers carry no analysis step index");
        }

        // -------------------------------------------------------------
        // 31. Show-up profile shapes every populated type alike
        // -------------------------------------------------------------
        private static void RunShowUpPerType()
        {
            Console.WriteLine("== Show-up per type ==");

            List<PassengerGroup> groups = BuildGroups(
                8000, new[] { 100.0 }, 0, 100.0, 50.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult result = Generate(
                groups, new WaveSettings(), 500, ShowUpProfile.Business());

            int stepDemand = TotalStepDemand(groups, 0);
            int last15 = result.Passengers.Count(p => p.ArrivalTime >= 45.0);

            Check(Math.Abs(last15 / (double)stepDemand - 0.205) < 0.02,
                $"Business last-15-minute share {last15 / (double)stepDemand:P1} ~= 20.5%");

            foreach (DHPType type in new[] { DHPType.DomesticDeparture, DHPType.InternationalDeparture })
            {
                List<Passenger> group = result.Passengers.Where(p => p.DHPType == type).ToList();
                int groupLast15 = group.Count(p => p.ArrivalTime >= 45.0);
                double share = groupLast15 / (double)group.Count;

                Check(Math.Abs(share - 0.205) < 0.03,
                    $"{type} last-15 share {share:P1} ~= 20.5%");
            }
        }

        // -------------------------------------------------------------
        // 32. Generators are departures-only and reject arrival groups
        // -------------------------------------------------------------
        private static void RunOptionalArrivalGroups()
        {
            Console.WriteLine("== Departures-only generation ==");

            List<PassengerGroup> full = BuildGroups(
                2000, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            // The helper now builds departure groups only: DD (0) and ID (1).
            List<PassengerGroup> departuresOnly = new List<PassengerGroup> { full[0], full[1] };

            PassengerGenerationResult result = Generate(departuresOnly, new WaveSettings(), 42);

            int expected = 0;
            foreach (PassengerGroup group in departuresOnly)
                for (int step = 0; step < group.DHP.StepCount; step++)
                    expected += (int)Math.Round(group.DHP.GetStepDemand(step));

            Check(result.Passengers.Count == expected,
                $"Departures-only run produces exactly its own demand ({result.Passengers.Count} == {expected})");

            Check(result.Passengers.All(p =>
                    p.DHPType == DHPType.DomesticDeparture ||
                    p.DHPType == DHPType.InternationalDeparture),
                "Departures-only run contains no arrival passengers");

            Check(result.Passengers.All(p => p.ShowUpSignature == "none"),
                "Run without a show-up profile tags every passenger 'none'");

            string businessSig = ShowUpProfile.Business().GetSignature();

            PassengerGenerationResult curved = Generate(
                departuresOnly, new WaveSettings(), 42, ShowUpProfile.Business());

            Check(curved.Passengers.Count == expected,
                $"Show-up curve does not change departures-only size ({curved.Passengers.Count} == {expected})");

            Check(curved.Passengers.All(p => p.ShowUpSignature == businessSig),
                "Every passenger carries the show-up fingerprint");

            // Null slots are dropped, not fatal.
            List<PassengerGroup> withNulls = new List<PassengerGroup>
                { full[0], null, full[1], null };

            PassengerGenerationResult nulled = Generate(withNulls, new WaveSettings(), 42);

            Check(nulled.Passengers.Count == expected,
                $"Null group slots are skipped ({nulled.Passengers.Count} == {expected})");

            // Arrival groups are rejected loudly.
            List<PassengerGroup> arrivalGroups = new List<PassengerGroup>
            {
                MakeGroup(
                    DHPType.DomesticArrival,
                    2000, 1, new[] { 100.0 }, 0,
                    false, 0, 0.0,
                    new List<PassengerProfile>
                    {
                        MakeProfile("Domestic", 100.0, 60.0, 150.0)
                    })
            };

            bool arrivalRejected = false;
            try
            {
                Generate(arrivalGroups, new WaveSettings(), 42);
            }
            catch (ArgumentException ex)
            {
                arrivalRejected =
                    ex.Message.IndexOf("arrival", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            Check(arrivalRejected, "Arrival groups are rejected by the passenger generator");

            // Mismatched windows are rejected loudly.
            List<PassengerGroup> mismatched = BuildGroups(
                2000, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);
            mismatched[0].DHP.DesignHourIndex = 0;
            mismatched[1].DHP.DesignHourIndex = 0;
            mismatched[1].DHP.AnalysisPeriodHours = 2;

            bool threw = false;
            try
            {
                Generate(mismatched, new WaveSettings(), 42);
            }
            catch (ArgumentException ex)
            {
                threw = ex.Message.Contains("analysis window") || ex.Message.Contains("analysis steps");
            }

            Check(threw, "Mismatched analysis windows are rejected with a clear message");
        }

        // -------------------------------------------------------------
        // 33. Legacy generator tags departures explicitly
        // -------------------------------------------------------------
        private static void RunLegacyDHPTypeTagging()
        {
            Console.WriteLine("== Legacy DHP type tagging ==");

            PassengerDemand demand = new PassengerDemand
            {
                DesignHourDemand = 400,
                DeparturePercentage = 100.0,
                DomesticPercentage = 60.0
            };

            List<PassengerProfile> domestic = new List<PassengerProfile>
            {
                StrippedProfile("Economy", 100.0, 60.0, 150.0)
            };

            List<PassengerProfile> international = new List<PassengerProfile>
            {
                StrippedProfile("Business", 100.0, 60.0, 150.0)
            };

            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            PassengerGenerationResult result = new PassengerGenerator().Generate(
                demand, domestic, international, walking,
                240, 180, 11, null);

            Check(result.Passengers.Count > 0, "Legacy run produces passengers");

            bool domesticTagged = result.Passengers
                .Where(p => p.PassengerType == PassengerType.Domestic)
                .All(p => p.DHPType == DHPType.DomesticDeparture);

            Check(domesticTagged, "Every legacy domestic passenger is tagged DomesticDeparture");

            bool internationalTagged = result.Passengers
                .Where(p => p.PassengerType == PassengerType.International)
                .All(p => p.DHPType == DHPType.InternationalDeparture);

            Check(internationalTagged, "Every legacy international passenger is tagged InternationalDeparture");

            Check(result.Passengers.All(p => p.ShowUpSignature == "none"),
                "Legacy passengers carry the 'none' show-up signature");
        }

        // -------------------------------------------------------------
        // 34. Clone preserves demand tags
        // -------------------------------------------------------------
        private static void RunClonePreservesTags()
        {
            Console.WriteLine("== Clone preserves tags ==");

            List<PassengerGroup> groups = BuildGroups(
                1200, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult result = Generate(
                groups, new WaveSettings(), 77, ShowUpProfile.Leisure());

            Passenger source = result.Passengers.FirstOrDefault(p => p.DHPType == DHPType.DomesticDeparture);

            Check(source != null, "Windowed run contains a domestic departure to clone");

            if (source != null)
            {
                Passenger clone = source.Clone();

                Check(clone.DHPType == source.DHPType,
                    $"Clone keeps DHPType ({clone.DHPType} == {source.DHPType})");
                Check(clone.ShowUpSignature == source.ShowUpSignature,
                    "Clone keeps the show-up signature");
                Check(clone.DemandStepIndex == source.DemandStepIndex &&
                      clone.IsWarmUp == source.IsWarmUp &&
                      clone.IsDesignStep == source.IsDesignStep,
                    "Clone keeps demand tags");
            }
        }

        // -------------------------------------------------------------
        // 35. Only departures use Check-In, Bag Drop and Security
        // -------------------------------------------------------------
        private static void RunDepartureOnlyRouting()
        {
            Console.WriteLine("== Departure-only routing ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.4 }
            };

            // Everyone requires check-in (online 0) so the only filter that can
            // exclude anyone is the departures-only gate.
            List<PassengerProfile> checkInDomestic = new List<PassengerProfile>
                { MakeProfile("Dom", 100.0, 60.0, 150.0, 0.0, 0.0) };
            List<PassengerProfile> checkInInternational = new List<PassengerProfile>
                { MakeProfile("Intl", 100.0, 120.0, 240.0, 0.0, 0.0) };

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1600, 1, new[] { 100.0 }, 0, 80.0, 60.0,
                checkInDomestic, checkInInternational,
                false, 0, 0.0);

            PassengerGenerationResult gen = Generate(groups, new WaveSettings(), 21);

            int departures = gen.Passengers.Count(FacilitySolverBase.IsDeparturePassenger);
            int arrivals = gen.Passengers.Count - departures;

            Check(departures > 0 && arrivals == 0,
                $"Generated population is departures-only ({departures} dep / {arrivals} arr)");

            CheckInResults checkIn = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            });

            Check(checkIn.QueueResults.ProcessedPassengers.Count == departures,
                $"Check-In serves exactly the departures ({checkIn.QueueResults.ProcessedPassengers.Count} == {departures})");

            Check(checkIn.QueueResults.ProcessedPassengers.All(FacilitySolverBase.IsDeparturePassenger),
                "Every Check-In passenger is a departure");

            Check(checkIn.QueueResults.Passengers.All(FacilitySolverBase.IsDeparturePassenger),
                "Check-In sees departure passengers only");

            // Dedicated channel pools must not let arrivals through either.
            CheckInResults channelled = new CheckInSolver().Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                Channels = new List<string> { "Counter" },
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            });

            Check(channelled.QueueResults.ProcessedPassengers.All(FacilitySolverBase.IsDeparturePassenger),
                "Dedicated Counter pool still serves departures only");

            // Everyone requires bag drop (online + bag) for the bag-drop check.
            List<PassengerProfile> bagDomestic = new List<PassengerProfile>
                { MakeProfile("Dom", 100.0, 60.0, 150.0, 1.0, 1.0) };
            List<PassengerProfile> bagInternational = new List<PassengerProfile>
                { MakeProfile("Intl", 100.0, 120.0, 240.0, 1.0, 1.0) };

            List<PassengerGroup> bagGroups = BuildGroupsWithProfiles(
                1600, 1, new[] { 100.0 }, 0, 80.0, 60.0,
                bagDomestic, bagInternational,
                false, 0, 0.0);

            PassengerGenerationResult bagGen = Generate(bagGroups, new WaveSettings(), 22);
            int bagDepartures = bagGen.Passengers.Count(FacilitySolverBase.IsDeparturePassenger);

            BagDropResults bagDrop = new BagDropSolver().Solve(new BagDropInputs
            {
                Passengers = bagGen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            });

            Check(bagDrop.QueueResults.ProcessedPassengers.Count == bagDepartures,
                $"Bag Drop serves exactly the departures ({bagDrop.QueueResults.ProcessedPassengers.Count} == {bagDepartures})");

            SecurityResults security = new SecuritySolver().Solve(new SecurityInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            });

            Check(security.QueueResults.ProcessedPassengers.Count == departures,
                $"Security serves exactly the departures ({security.QueueResults.ProcessedPassengers.Count} == {departures})");

            // Emigration is departure-only: international arrivals are not served.
            EmigrationResults emigration = new EmigrationSolver().Solve(new EmigrationInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            });

            int expectedEmigration = gen.Passengers.Count(p =>
                p.PassengerType == PassengerType.International &&
                p.DHPType == DHPType.InternationalDeparture);

            Check(emigration.QueueResults.ProcessedPassengers.Count == expectedEmigration,
                $"Emigration serves exactly the international departures ({emigration.QueueResults.ProcessedPassengers.Count} == {expectedEmigration})");

            Check(emigration.QueueResults.ProcessedPassengers.All(p => p.DHPType == DHPType.InternationalDeparture),
                "Emigration excludes international arrivals");
        }

        // -------------------------------------------------------------
        // 36. A new show-up curve forces a fresh optimizer search
        // -------------------------------------------------------------
        private static void RunShowUpCacheInvalidation()
        {
            Console.WriteLine("== Show-up cache invalidation ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.4 }
            };

            List<PassengerGroup> groups = BuildGroups(
                1600, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            PassengerGenerationResult leisure = Generate(
                groups, new WaveSettings(), 31337, ShowUpProfile.Leisure());
            PassengerGenerationResult business = Generate(
                groups, new WaveSettings(), 31337, ShowUpProfile.Business());

            Check(leisure.Passengers.Count == business.Passengers.Count &&
                  leisure.Passengers.Count > 0,
                $"Both curves produce the full population ({leisure.Passengers.Count} == {business.Passengers.Count})");

            Check(leisure.Passengers[0].ShowUpSignature != business.Passengers[0].ShowUpSignature,
                "Leisure and Business carry different show-up fingerprints");

            CheckInSolver solver = new CheckInSolver();

            CheckInResults leisureCold = solver.Solve(FreshCheckInInput(leisure, map));
            CheckInResults leisureWarm = solver.Solve(FreshCheckInInput(leisure, map));

            Check(leisureWarm.PhysicalCounters == leisureCold.PhysicalCounters,
                $"Identical show-up input reuses the cache ({leisureWarm.PhysicalCounters} == {leisureCold.PhysicalCounters})");

            CheckInResults businessWarm = solver.Solve(FreshCheckInInput(business, map));
            CheckInResults businessFresh = new CheckInSolver().Solve(FreshCheckInInput(business, map));

            Check(businessWarm.PhysicalCounters == businessFresh.PhysicalCounters,
                $"New show-up curve re-optimizes from scratch ({businessWarm.PhysicalCounters} == {businessFresh.PhysicalCounters})");

            Check(Math.Abs(businessWarm.QueueResults.GetEvaluationWait95() -
                           businessFresh.QueueResults.GetEvaluationWait95()) < 1e-9,
                "Re-optimized wait matches a fresh solve exactly");
        }

        private static CheckInInputs FreshCheckInInput(
            PassengerGenerationResult gen,
            ServiceTimeMap map)
        {
            return new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 1200)
            };
        }

        // -------------------------------------------------------------
        // 37. The target applies to the whole run, not the design hour
        // -------------------------------------------------------------
        private static void RunWholeRunOptimizationTarget()
        {
            Console.WriteLine("== Whole-run optimization target ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.0, StandardDeviation = 0.4 }
            };

            // The design hour (step 0) is lightly loaded; the other steps carry
            // full demand. A design-hour-only search would stop early while the
            // whole-run W95 still breaches the target.
            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1500, 3, new[] { 20.0, 100.0, 100.0 }, 0, 80.0, 60.0,
                new List<PassengerProfile> { MakeProfile("Dom", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile> { MakeProfile("Intl", 100.0, 120.0, 240.0, 0.0, 0.0) },
                false, 0, 0.0);

            PassengerGenerationResult gen = Generate(groups, new WaveSettings(), 5);

            CheckInSolver solver = new CheckInSolver();

            CheckInResults result = solver.Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 8 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 20.0 },
                Seed = 4,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 200)
            });

            QueueResults qr = result.QueueResults;

            Check(result.SearchOutcomes.Count == 1 && result.SearchOutcomes[0].Success,
                "Whole-run target is reachable inside the domain");

            Check(qr.Wait95 <= 20.0 + 1e-9,
                $"Whole-run Wait95 meets the 20 min target ({Math.Round(qr.Wait95, 2)} <= 20)");

            double designWait = qr.GetEvaluationWait95();

            Check(designWait < qr.Wait95,
                $"Design-hour Wait95 ({Math.Round(designWait, 2)}) sits below the whole-run value " +
                $"({Math.Round(qr.Wait95, 2)}): a design-hour search would have stopped early");

            Check(result.PhysicalCounters >= 1 && result.PhysicalCounters <= 200,
                $"Result respects the domain ({result.PhysicalCounters})");
        }

        // -------------------------------------------------------------
        // 38. An unreachable target warns instead of failing silently
        // -------------------------------------------------------------
        private static void RunInfeasibleTargetWarning()
        {
            Console.WriteLine("== Infeasible target warning ==");

            ServiceTimeMap map = new ServiceTimeMap
            {
                Default = new ServiceProfile { MeanServiceTime = 2.5, StandardDeviation = 0.5 }
            };

            List<PassengerGroup> groups = BuildGroupsWithProfiles(
                1500, 1, new[] { 100.0 }, 0, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Solo", 100.0, 60.0, 150.0, 0.0, 0.0) },
                new List<PassengerProfile>());

            PassengerGenerationResult gen = GenerateWithProfiles(groups, 21, null);

            CheckInSolver solver = new CheckInSolver();

            CheckInResults result = solver.Solve(new CheckInInputs
            {
                Passengers = gen.Passengers.Select(p => p.Clone()).ToList(),
                ServiceProfile = map.Default,
                ServiceTimeProvider = map,
                PhysicalResources = new List<int> { 10 },
                OptimizeResources = true,
                TargetWait95 = new List<double> { 0.01 },
                Seed = 3,
                StaffingPercentage = 100.0,
                ResourceDomain = new ResourceSearchRange(1, 5)
            });

            Check(result.PhysicalCounters == 5,
                $"Unreachable target still returns the domain maximum ({result.PhysicalCounters} == 5)");

            Check(result.SearchOutcomes.Count == 1 && !result.SearchOutcomes[0].Success,
                "Unreachable target is flagged as a failed search");

            Check(solver.Warnings.Any(w => w.Contains("not met")),
                "Unreachable target raises a 'target not met' warning");

            Check(result.QueueResults.Wait95 > 0.01,
                $"Reported whole-run Wait95 ({Math.Round(result.QueueResults.Wait95, 2)}) " +
                "honestly exceeds the unreachable target");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private static List<PassengerGroup> BuildGroups(
            int dhd, double[] factors, int designIndex,
            double dep, double dom,
            bool warmUp, int warmMinutes, double warmFactor)
        {
            return BuildGroupsWithProfiles(
                dhd, 5, factors, designIndex, dep, dom,
                new List<PassengerProfile> { MakeProfile("Domestic", 100.0, 60.0, 150.0) },
                new List<PassengerProfile> { MakeProfile("International", 100.0, 120.0, 240.0) },
                warmUp, warmMinutes, warmFactor);
        }

        private static List<PassengerGroup> BuildSimpleGroups(
            int dhd, int hours, double[] factors, int designIndex)
        {
            return BuildGroupsWithProfiles(
                dhd, hours, factors, designIndex, 100.0, 100.0,
                new List<PassengerProfile> { MakeProfile("Domestic", 100.0, 60.0, 150.0) },
                new List<PassengerProfile> { MakeProfile("International", 100.0, 120.0, 240.0) });
        }

        private static List<PassengerGroup> BuildGroupsWithProfiles(
            int dhd, int hours, double[] factors, int designIndex,
            double dep, double dom,
            List<PassengerProfile> domestic, List<PassengerProfile> international,
            bool warmUp = false, int warmMinutes = 0, double warmFactor = 0.0)
        {
            // Passenger generators are departures-only. The dep split still
            // reduces the departure demand, but no arrival group is built.
            int departureTotal = (int)Math.Round(dhd * dep / 100.0);

            int domDep = (int)Math.Round(departureTotal * dom / 100.0);
            int intlDep = departureTotal - domDep;

            return new List<PassengerGroup>
            {
                MakeGroup(DHPType.DomesticDeparture, domDep, hours, factors, designIndex,
                    warmUp, warmMinutes, warmFactor, domestic),
                MakeGroup(DHPType.InternationalDeparture, intlDep, hours, factors, designIndex,
                    warmUp, warmMinutes, warmFactor, international)
            };
        }

        private static PassengerGroup MakeGroup(
            DHPType type, int dhd, int hours, double[] factors, int designIndex,
            bool warmUp, int warmMinutes, double warmFactor,
            List<PassengerProfile> profiles)
        {
            return new PassengerGroup
            {
                DHP = new DHP
                {
                    Type = type,
                    DesignHourDemand = dhd,
                    DemandFactors = factors != null ? new List<double>(factors) : new List<double>(),
                    AnalysisPeriodHours = hours,
                    DesignHourIndex = designIndex,
                    WarmUpEnabled = warmUp,
                    WarmUpMinutes = warmMinutes,
                    WarmUpFactor = warmFactor
                },
                Profiles = profiles != null
                    ? new List<PassengerProfile>(profiles)
                    : new List<PassengerProfile>()
            };
        }

        private static int TotalStepDemand(List<PassengerGroup> groups, int step)
        {
            return groups.Sum(g => (int)Math.Round(g.DHP.GetStepDemand(step)));
        }

        private static int TotalWarmUpDemand(List<PassengerGroup> groups)
        {
            return groups.Sum(g => (int)Math.Round(g.DHP.GetWarmUpDemand()));
        }

        private static PassengerGenerationResult Generate(
            List<PassengerGroup> groups,
            WaveSettings waves,
            int seed)
        {
            return Generate(groups, waves, seed, null);
        }

        private static PassengerGenerationResult Generate(
            List<PassengerGroup> groups,
            WaveSettings waves,
            int seed,
            ShowUpProfile showUpProfile)
        {
            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            return new MultiIntervalPassengerGenerator().Generate(
                groups, walking, waves, seed, showUpProfile);
        }

        private static PassengerGenerationResult GenerateCurved(
            List<PassengerGroup> groups,
            WaveSettings waves,
            int seed,
            ShowUpProfile showUp,
            Action<string> warn)
        {
            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            return new MultiIntervalPassengerGenerator().Generate(
                groups, walking, waves, seed, showUp, null, warn);
        }

        private static PassengerGenerationResult GenerateWithProfiles(
            List<PassengerGroup> groups,
            int seed,
            IChannelAssigner channelAssigner)
        {
            WalkingProfile walking = new WalkingProfile
            {
                AverageSpeed = 1.4,
                StandardDeviation = 0.2
            };

            return new MultiIntervalPassengerGenerator().Generate(
                groups, walking, new WaveSettings(), seed, null, channelAssigner);
        }

        private static PassengerProfile MakeProfile(
            string name,
            double percentage,
            double minAdvance,
            double maxAdvance)
        {
            return new PassengerProfile
            {
                Name = name,
                Percentage = percentage,
                Journey = PassengerJourney.Origin,
                OnlineCheckInProbability = 0.0,
                CheckedBagProbability = 0.3,
                ServiceTimeMultiplier = 1.0,
                MaleProbability = 0.5,
                MinAdvanceMinutes = minAdvance,
                MaxAdvanceMinutes = maxAdvance,
                ArrivalProfile = new ArrivalProfile { Peak = 90.0, Sigma = 30.0 }
            };
        }

        private static PassengerProfile MakeProfile(
            string name,
            double percentage,
            double minAdvance,
            double maxAdvance,
            double online,
            double checkedBag)
        {
            return new PassengerProfile
            {
                Name = name,
                Percentage = percentage,
                Journey = PassengerJourney.Origin,
                OnlineCheckInProbability = online,
                CheckedBagProbability = checkedBag,
                ServiceTimeMultiplier = 1.0,
                MaleProbability = 0.5,
                MinAdvanceMinutes = minAdvance,
                MaxAdvanceMinutes = maxAdvance,
                ArrivalProfile = new ArrivalProfile { Peak = 90.0, Sigma = 30.0 }
            };
        }

        private static PassengerProfile StrippedProfile(
            string name,
            double percentage,
            double minAdvance,
            double maxAdvance)
        {
            return new PassengerProfile
            {
                Name = name,
                Percentage = percentage,
                Journey = PassengerJourney.Origin,
                OnlineCheckInProbability = 0.0,
                CheckedBagProbability = 0.3,
                ServiceTimeMultiplier = 1.0,
                MaleProbability = 0.5,
                MinAdvanceMinutes = minAdvance,
                MaxAdvanceMinutes = maxAdvance,
                ArrivalProfile = null
            };
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine($"  PASS: {message}");
            }
            else
            {
                Console.WriteLine($"  FAIL: {message}");
                _failures++;
            }
        }
    }
}
