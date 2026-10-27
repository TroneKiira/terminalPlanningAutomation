using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInSolver : FacilitySolverBase
    {
        // CACHE VARIABLES
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public CheckInSolver() : base()
        {
        }

        public CheckInResults Solve(CheckInInputs input)
        {
            Warnings.Clear();

            CheckInResults finalResult = new CheckInResults();

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile);

            List<string> channels = ResolveChannels(input);
            IList<IList<string>> poolKeysPerChannel = BuildCounterPoolKeys(input, channels);

            List<ChannelResourcePlan> plans = ResolvePlans(input, channels, poolKeysPerChannel);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Check-In has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            int lateForCheckIn = input.Passengers == null
                ? 0
                : input.Passengers.Count(p =>
                    p != null &&
                    p.RequiresCheckIn &&
                    p.IsLateForCheckIn);

            if (lateForCheckIn > 0)
            {
                Warnings.Add(
                    $"{lateForCheckIn} passenger(s) reach check-in after the flight's " +
                    "last-admission time and are not served there.");
            }

            string currentCacheKey = BuildSolverCacheKey(
                input.Passengers,
                serviceTimeProvider,
                channels,
                plans,
                input.StaffingPercentage,
                input.Seed,
                input.OptimizeResources);

            bool cacheHit = currentCacheKey == _lastCacheKey &&
                            _cachedOutcomes != null &&
                            _cachedBaselinePhysical > 0;

            List<ResourceSearchOutcome> outcomes;

            FacilitySolveResult solve = SolvePools(
                input.Passengers,
                plans,
                channels,
                p => p.RequiresCheckIn && !p.IsLateForCheckIn,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                p => IsDeparturePassenger(p) && !p.IsLateForCheckIn);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
            }

            QueueResults results = solve.Results;

            results.Passengers = BuildOutputPopulation(input.Passengers, solve.Clones);

            finalResult.QueueResults = results;
            finalResult.PhysicalCounters = solve.TotalPhysical;
            finalResult.ActiveCounters = solve.TotalActive;
            finalResult.PhysicalPerChannel = solve.PhysicalPerChannel;
            finalResult.ActivePerChannel = solve.ActivePerChannel;
            finalResult.Wait95PerChannel = solve.Wait95PerChannel;
            finalResult.PoolNames = solve.PoolNames;
            finalResult.NominalPerChannel = plans
                .Select(p => p.PhysicalResources)
                .ToList();
            PopulateChannelTotals(finalResult, plans, solve.PhysicalPerChannel, solve.ActivePerChannel);
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, Warnings.Add);
            PoolPlanResolver.WarnOnUncoveredChannels(plans, channels, Warnings.Add);

            // Baseline travels down the wire so the reporter can compare against it.
            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.CheckIn);
            WriteBagDropHistory(solve.Processed);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Derives Counter sub-pools from the data already carried by each
        /// CHR-routed passenger. No profile/sector pool keys need to be entered
        /// again in Grasshopper.
        /// </summary>
        private List<string> ResolveChannels(CheckInInputs input)
        {
            bool explicitChannels =
                input.Channels != null && input.Channels.Any(c => !string.IsNullOrWhiteSpace(c));

            IList<string> source = explicitChannels
                ? input.Channels
                : (IList<string>)input.ChannelRouting?.GetChannels();

            List<string> channels = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> skippedVirtual = new List<string>();

            if (source == null)
                return channels;

            foreach (string rawChannel in source)
            {
                if (string.IsNullOrWhiteSpace(rawChannel))
                    continue;

                string channel = rawChannel.Trim();

                // A virtual route such as Web has no physical Check-In
                // processors. Only CHR-derived routes are filtered here: an
                // explicit Channels list is still taken literally.
                if (!explicitChannels && IsVirtualChannel(channel))
                {
                    if (!skippedVirtual.Contains(channel, StringComparer.OrdinalIgnoreCase))
                        skippedVirtual.Add(channel);

                    continue;
                }

                if (!seen.Add(channel))
                {
                    Warnings.Add($"Channel '{channel}' is listed more than once; duplicates are ignored.");
                    continue;
                }

                channels.Add(channel);
            }

            if (skippedVirtual.Count > 0)
            {
                Warnings.Add(
                    $"Virtual channel(s) {string.Join(", ", skippedVirtual)} are not physical " +
                    "Check-In channels and were excluded. Wire an explicit Channels list only " +
                    "if they should receive processors.");
            }

            return channels;
        }

        private static bool IsVirtualChannel(string channel)
        {
            return string.Equals(
                channel?.Trim(),
                DefaultChannelAssigner.WebChannel,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Records Bag Drop visits for passengers served through a BagDrop
        /// channel. The Check-In record is kept as well, because the processor
        /// they used belongs to the Check-In facility.
        /// </summary>
        private void WriteBagDropHistory(IEnumerable<Passenger> processed)
        {
            if (processed == null)
                return;

            List<Passenger> bagDropPassengers = processed
                .Where(p => p != null && IsBagDropChannel(p.Channel))
                .ToList();

            if (bagDropPassengers.Count > 0)
                WriteFacilityHistory(bagDropPassengers, FacilityType.BagDrop);
        }

        private static bool IsBagDropChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return false;

            // Accept "BagDrop", "Bag Drop", "bag-drop" and similar labels.
            string normalized = new string(channel.Where(char.IsLetterOrDigit).ToArray());

            return string.Equals(
                normalized,
                DefaultChannelAssigner.BagDropChannel,
                StringComparison.OrdinalIgnoreCase);
        }

        private IList<IList<string>> BuildCounterPoolKeys(
            CheckInInputs input,
            IList<string> channels)
        {
            if (input == null || channels == null || channels.Count == 0)
                return null;

            List<IList<string>> result = new List<IList<string>>();
            string counterChannel = string.IsNullOrWhiteSpace(input.CounterChannel)
                ? DefaultChannelAssigner.CounterChannel
                : input.CounterChannel.Trim();
            foreach (string rawChannel in channels)
            {
                string channel = rawChannel == null ? string.Empty : rawChannel.Trim();
                if (channel.Length == 0)
                    continue;

                if (!string.Equals(channel, counterChannel, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(null);
                    continue;
                }

                List<string> keys = (input.Passengers ?? new List<Passenger>())
                    .Where(p => p != null &&
                                string.Equals(p.Channel, channel, StringComparison.OrdinalIgnoreCase) &&
                                IsDeparturePassenger(p) &&
                                !p.IsLateForCheckIn &&
                                !string.IsNullOrWhiteSpace(p.ProfileName))
                    .Select(p => new { p.DHPType, ProfileName = p.ProfileName.Trim() })
                    .Distinct()
                    .OrderBy(x => (int)x.DHPType)
                    .ThenBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.DHPType + "|" + x.ProfileName)
                    .ToList();

                // A single observed DHP/profile combination is equivalent to
                // the legacy whole-channel pool; keep its simple channel name.
                result.Add(keys.Count > 1 ? keys : null);
            }

            return result;
        }

        /// <summary>
        /// Resolves channel resource plans. When <see cref="CheckInInputs.PoolPlans"/>
        /// is supplied, those keyed plans take precedence and are matched to
        /// resolved pools by Channel|PoolKey identity. Otherwise the legacy
        /// parallel-list inputs are used.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(
            CheckInInputs input,
            List<string> channels,
            IList<IList<string>> poolKeysPerChannel)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            // Keyed pool plans take precedence over legacy parallel lists.
            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add)
                    ?? ResolveLegacyPlans(input, channels, poolKeysPerChannel, fallbackDomain);
            }

            return ResolveLegacyPlans(input, channels, poolKeysPerChannel, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            CheckInInputs input,
            List<string> channels,
            IList<IList<string>> poolKeysPerChannel,
            ResourceSearchRange fallbackDomain)
        {
            return ChannelResourcePlanResolver.Resolve(
                channels,
                input.PhysicalResources,
                input.TargetWait95,
                poolKeysPerChannel,
                input.PoolPhysicalResources,
                input.PoolTargetWait95,
                input.PoolMinimumResources,
                input.PoolMaximumResources,
                fallbackDomain,
                Warnings.Add);
        }

        private static void PopulateChannelTotals(
            CheckInResults result,
            IList<ChannelResourcePlan> plans,
            IList<int> physicalPerPool,
            IList<int> activePerPool)
        {
            Dictionary<string, int> physical =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> active =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();

            if (plans != null)
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    string channel = plans[i] == null || plans[i].IsSharedPool
                        ? "Shared"
                        : plans[i].Channel;

                    if (!physical.ContainsKey(channel))
                    {
                        names.Add(channel);
                        physical[channel] = 0;
                        active[channel] = 0;
                    }

                    if (physicalPerPool != null && i < physicalPerPool.Count)
                        physical[channel] += physicalPerPool[i];
                    if (activePerPool != null && i < activePerPool.Count)
                        active[channel] += activePerPool[i];
                }
            }

            result.ChannelNames = names;
            result.PhysicalPerChannelTotal = names.Select(name => physical[name]).ToList();
            result.ActivePerChannelTotal = names.Select(name => active[name]).ToList();
        }
    }
}
