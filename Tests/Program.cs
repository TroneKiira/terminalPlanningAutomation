using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Immigration;
using AirportPlanning.Facilities.Security;

namespace AirportPlanning.Tests
{
    public static class Program
    {
        private static int _failures;

        public static int Main()
        {
            RunCountsAndWarmUp();
            RunWaveOffRespectsShowUp();
            RunDepartures();
            RunDesignMetricsAndOptimizer();
            RunProfileValidation();
            RunShowUpIntegration();
            RunProfileIdentityAndChannels();
            RunServiceTimeMap();
            RunChannelFiltering();
            RunClassMetrics();
            RunOptimizerDeterminismWithProvider();
            RunAnalyticComparison();
            RunResourceDomainBounds();
            RunPerChannelPools();
            RunLargeDemandTiming();
            RunPoissonSampler();
            RunLargeDemandPassengerGenerator();
            RunOutputPopulationMerge();
            RunArrivalProfileOptional();
            RunLegacyNullArrivalProfile();
            RunShowUpCurves();
            RunShowUpCurveGeneration();

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

            Check(rBag.Passengers.All(p => p.Channel == DefaultChannelAssigner.BagDropChannel),
                "Default routing: online + bag -> BagDrop");

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
                Channels = new List<string> { "Counter", "Web" },
                PhysicalResources = new List<int> { 6, 6 },
                OptimizeResources = false,
                TargetWait95 = new List<double> { 10.0, 10.0 },
                Seed = 4,
                StaffingPercentage = 100.0
            });

            List<Passenger> output = result.QueueResults.Passengers;

            Check(ReferenceEquals(output, source) == false,
                "Outgoing list is a new instance, not the input list");

            Check(output.Count == source.Count,
                $"Population preserved ({output.Count} == {source.Count})");

            bool allCarriedHistory = output
                .Where(p => p.Channel == "Counter" || p.Channel == "Web")
                .All(p => p.History.ContainsKey(FacilityType.CheckIn));

            Check(allCarriedHistory, "Served passengers carry Check-In history downstream");

            bool unservedUntouched = output
                .Where(p => !(p.Channel == "Counter" || p.Channel == "Web"))
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
                $"Four groups total {result.Passengers.Count} == {expectedTotal}");
        }

        // -------------------------------------------------------------
        // 25. Four groups plus warm-up reconcile to the generated total
        // -------------------------------------------------------------
        private static void RunFourGroupTotal()
        {
            Console.WriteLine("== Four-group total ==");

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
            baseline[3].DHP.DesignHourDemand = 500;

            PassengerGenerationResult baseResult = Generate(baseline, new WaveSettings(), 900);

            int baseDomDep = baseResult.Passengers.Count(p => p.DHPType == DHPType.DomesticDeparture);
            int baseArr = baseResult.Passengers.Count(p => p.DHPType == DHPType.DomesticArrival);

            List<PassengerGroup> changed = BuildGroups(
                2000, new[] { 100.0 }, 0, 100.0, 100.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            changed[1].DHP.DesignHourDemand = 5000;
            changed[3].DHP.DesignHourDemand = 500;

            PassengerGenerationResult changedResult = Generate(changed, new WaveSettings(), 900);

            int changedDomDep = changedResult.Passengers.Count(p => p.DHPType == DHPType.DomesticDeparture);
            int changedArr = changedResult.Passengers.Count(p => p.DHPType == DHPType.DomesticArrival);

            Check(baseDomDep == changedDomDep,
                $"Changing domestic arrival leaves domestic departure untouched ({baseDomDep} == {changedDomDep})");
            Check(baseArr == 500, $"Baseline domestic arrival demand honoured ({baseArr} == 500)");
            Check(changedArr == 5000, $"Changed domestic arrival demand honoured ({changedArr} == 5000)");
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
                DHPType.DomesticDeparture, DHPType.DomesticArrival,
                DHPType.InternationalDeparture, DHPType.InternationalArrival
            };

            bool everyType = true;

            foreach (DHPType type in types)
            {
                if (!result.Passengers.Any(p => p.DHPType == type && p.IsDesignStep))
                    everyType = false;
            }

            Check(everyType, "Every populated type has design-step passengers");
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
        // 32. DA / IA are optional: departures-only generation works
        // -------------------------------------------------------------
        private static void RunOptionalArrivalGroups()
        {
            Console.WriteLine("== Optional arrival groups ==");

            List<PassengerGroup> full = BuildGroups(
                2000, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);

            // Departures only: DD (index 0) and ID (index 2).
            List<PassengerGroup> departuresOnly = new List<PassengerGroup> { full[0], full[2] };

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
                { full[0], null, full[2], null };

            PassengerGenerationResult nulled = Generate(withNulls, new WaveSettings(), 42);

            Check(nulled.Passengers.Count == expected,
                $"Null arrival slots are skipped ({nulled.Passengers.Count} == {expected})");

            // Mismatched windows are rejected loudly.
            List<PassengerGroup> mismatched = BuildGroups(
                2000, new[] { 100.0 }, 0, 80.0, 60.0,
                warmUp: false, warmMinutes: 0, warmFactor: 0.0);
            mismatched[2].DHP.DesignHourIndex = 0;
            mismatched[0].DHP.DesignHourIndex = 0;
            mismatched[1].DHP.DesignHourIndex = 0;
            mismatched[3].DHP.DesignHourIndex = 0;
            mismatched[2].DHP.AnalysisPeriodHours = 2;

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

            Passenger source = result.Passengers.FirstOrDefault(p => p.DHPType == DHPType.DomesticArrival);

            Check(source != null, "Windowed run contains a domestic arrival to clone");

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

            Check(departures > 0 && arrivals > 0,
                $"Mixed population has both departures and arrivals ({departures} dep / {arrivals} arr)");

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

            bool arrivalsBypassCheckIn = checkIn.QueueResults.Passengers
                .Where(p => !FacilitySolverBase.IsDeparturePassenger(p))
                .All(p => !p.History.ContainsKey(FacilityType.CheckIn));

            Check(arrivalsBypassCheckIn, "Arrivals pass through Check-In untouched");

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

            // Immigration stays bidirectional: international arrivals are served.
            ImmigrationResults immigration = new ImmigrationSolver().Solve(new ImmigrationInputs
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

            int expectedImmigration = gen.Passengers.Count(p => p.PassengerType == PassengerType.International);

            Check(immigration.QueueResults.ProcessedPassengers.Count == expectedImmigration,
                $"Immigration still serves all international passengers ({immigration.QueueResults.ProcessedPassengers.Count} == {expectedImmigration})");

            Check(immigration.QueueResults.ProcessedPassengers.Any(p => p.DHPType == DHPType.InternationalArrival),
                "Immigration still processes international arrivals");
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
            int departureTotal = (int)Math.Round(dhd * dep / 100.0);
            int arrivalTotal = dhd - departureTotal;

            int domDep = (int)Math.Round(departureTotal * dom / 100.0);
            int intlDep = departureTotal - domDep;
            int domArr = (int)Math.Round(arrivalTotal * dom / 100.0);
            int intlArr = arrivalTotal - domArr;

            return new List<PassengerGroup>
            {
                MakeGroup(DHPType.DomesticDeparture, domDep, hours, factors, designIndex,
                    warmUp, warmMinutes, warmFactor, domestic),
                MakeGroup(DHPType.DomesticArrival, domArr, hours, factors, designIndex,
                    warmUp, warmMinutes, warmFactor, domestic),
                MakeGroup(DHPType.InternationalDeparture, intlDep, hours, factors, designIndex,
                    warmUp, warmMinutes, warmFactor, international),
                MakeGroup(DHPType.InternationalArrival, intlArr, hours, factors, designIndex,
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
