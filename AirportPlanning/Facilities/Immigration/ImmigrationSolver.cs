using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Immigration
{
    /// <summary>
    /// Inbound passport control. Serves originating international arrivals and
    /// transfer flows whose scenario rules require immigration (the
    /// International→Domestic case). Domestic arrivals never clear immigration,
    /// and transfer passengers without an immigration requirement pass through
    /// untouched. Built on the same queue-and-pool machinery as the departure
    /// facilities, so arrivals generation can wire into it unchanged later.
    /// </summary>
    public class ImmigrationSolver : FacilitySolverBase
    {
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public ImmigrationSolver() : base() { }

        /// <summary>
        /// True when the passenger must clear immigration at this terminal:
        /// originating international arrivals always do; transfers only when
        /// their scenario rules require it. Unspecified transfers follow the
        /// normal international-arrival behaviour.
        /// </summary>
        public static bool RequiresImmigration(Passenger passenger)
        {
            return TransferServiceUsageCalculator.UsesService(
                passenger,
                TransferService.Immigration);
        }

        /// <summary>True when the passenger's scenario rules request a dedicated
        /// transfer-immigration pool instead of the shared hall.</summary>
        private static bool UsesSeparateImmigration(Passenger passenger)
        {
            return passenger != null &&
                   passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   passenger.TransferProcessing.RequiresImmigration &&
                   passenger.TransferProcessing.ImmigrationRouting == TransferFacilityRouting.Separate;
        }

        /// <summary>
        /// Routes transfer passengers whose scenario rules request a dedicated
        /// transfer-immigration pool to the transfer channel before pool
        /// selection. Returns the saved original channels so they can be
        /// restored after the solve.
        /// </summary>
        private Dictionary<Passenger, string> ApplySeparateTransferRouting(
            List<Passenger> passengers,
            string transferChannelName,
            IList<ChannelResourcePlan> plans)
        {
            Dictionary<Passenger, string> originalChannels =
                new Dictionary<Passenger, string>();

            if (passengers == null || string.IsNullOrWhiteSpace(transferChannelName))
                return originalChannels;

            string channel = transferChannelName.Trim();

            if (!HasConfiguredChannel(plans, channel))
            {
                int unconfigured = passengers.Count(UsesSeparateImmigration);

                if (unconfigured > 0)
                {
                    Warnings.Add(
                        $"Separate immigration routing had no matching pool for '{channel}'. " +
                        $"{unconfigured} transfer passenger(s) remain in their original channel " +
                        "so they can share the main immigration hall. Add a matching channel " +
                        "(and pool plan) to size them independently.");
                }

                return originalChannels;
            }

            foreach (Passenger passenger in passengers)
            {
                if (!UsesSeparateImmigration(passenger))
                    continue;

                originalChannels[passenger] = passenger.Channel;
                passenger.Channel = channel;
            }

            if (originalChannels.Count > 0)
            {
                Warnings.Add(
                    $"{originalChannels.Count} transfer passenger(s) are routed to the separate " +
                    $"'{channel}' immigration pool.");
            }

            return originalChannels;
        }

        /// <summary>Restores the original channels saved by
        /// <see cref="ApplySeparateTransferRouting"/>.</summary>
        private static void RestoreSeparateTransferChannels(
            Dictionary<Passenger, string> separatedChannels,
            Dictionary<Passenger, Passenger> clones)
        {
            foreach (KeyValuePair<Passenger, string> pair in separatedChannels)
            {
                pair.Key.Channel = pair.Value;

                if (clones != null && clones.TryGetValue(pair.Key, out Passenger clone))
                    clone.Channel = pair.Value;
            }
        }

        public ImmigrationResults Solve(ImmigrationInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            ImmigrationResults finalResult = new ImmigrationResults();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            List<ChannelResourcePlan> plans = ResolvePlans(input);

            Dictionary<Passenger, string> separatedChannels = ApplySeparateTransferRouting(
                workingPassengers,
                input.TransferChannelName,
                plans);

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.Immigration, Warnings.Add);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Immigration has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            Func<Passenger, bool> immigrationPassenger = RequiresImmigration;

            string currentCacheKey = BuildSolverCacheKey(
                workingPassengers,
                serviceTimeProvider,
                input.Channels,
                plans,
                input.StaffingPercentage,
                input.Seed,
                input.OptimizeResources);

            bool cacheHit = currentCacheKey == _lastCacheKey &&
                            _cachedOutcomes != null &&
                            _cachedBaselinePhysical > 0;

            List<ResourceSearchOutcome> outcomes;

            FacilitySolveResult solve = SolvePools(
                workingPassengers,
                plans,
                input.Channels,
                immigrationPassenger,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                immigrationPassenger);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
            }

            if (separatedChannels.Count > 0)
            {
                List<string> misplaced = separatedChannels.Keys
                    .Where(p => solve.Clones.TryGetValue(p, out Passenger clone) &&
                                clone != null &&
                                !string.Equals(
                                    clone.AssignedPool,
                                    input.TransferChannelName.Trim(),
                                    StringComparison.OrdinalIgnoreCase))
                    .Select(p => solve.Clones[p].AssignedPool)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (misplaced.Count > 0)
                {
                    Warnings.Add(
                        "Separate transfer-immigration routing had no matching pool; those passengers " +
                        $"were served by: {string.Join(", ", misplaced)}. Add a Pool Plan (or channel " +
                        $"'{input.TransferChannelName.Trim()}') to size them independently.");
                }

                RestoreSeparateTransferChannels(separatedChannels, solve.Clones);
            }

            QueueResults results = solve.Results;

            results.Passengers = BuildOutputPopulation(workingPassengers, solve.Clones);

            finalResult.QueueResults = results;
            finalResult.PhysicalPositions = solve.TotalPhysical;
            finalResult.ActivePositions = solve.TotalActive;
            finalResult.PhysicalPerChannel = solve.PhysicalPerChannel;
            finalResult.ActivePerChannel = solve.ActivePerChannel;
            finalResult.Wait95PerChannel = solve.Wait95PerChannel;
            finalResult.PoolNames = solve.PoolNames;
            finalResult.NominalPerChannel = plans
                .Select(p => p.PhysicalResources)
                .ToList();
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, workingPassengers, Warnings.Add);
            PoolPlanResolver.WarnOnUncoveredChannels(plans, input.Channels, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.Immigration);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Resolves channel resource plans from keyed PoolPlans when supplied,
        /// otherwise falls back to the legacy PhysicalResources/TargetWait95
        /// lists with a shared ResourceDomain.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(ImmigrationInputs input)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.Immigration)
                    ?? ResolveLegacyPlans(input, fallbackDomain);
            }

            return ResolveLegacyPlans(input, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            ImmigrationInputs input,
            ResourceSearchRange fallbackDomain)
        {
            return ChannelResourcePlanResolver.Resolve(
                input.Channels,
                input.PhysicalResources,
                input.TargetWait95,
                fallbackDomain,
                Warnings.Add);
        }
    }
}
