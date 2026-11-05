using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Transfer
{
    /// <summary>
    /// Arrival-side baggage recheck. Serves transfer passengers whose scenario
    /// rules require recheck — in the TypeScript mapping this is the desk
    /// facility for the International→Domestic flow, so its passengers arrive
    /// with checked bags that must be re-screened and re-checked to the
    /// connecting flight. Everyone else passes through untouched.
    ///
    /// The full population is always returned: served passengers are replaced
    /// by their processed clones (service history and advanced clock travel
    /// downstream) and the rest pass through in their original order. Per-flow
    /// pool routing is temporary and is restored before output, so the
    /// passenger's persistent channel keeps its downstream meaning for the
    /// integrated and separate security/immigration/emigration flows.
    /// </summary>
    public class BaggageRecheckSolver : FacilitySolverBase
    {
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public BaggageRecheckSolver() : base() { }

        /// <summary>
        /// True when the passenger must use baggage recheck at this terminal:
        /// only transfer arrivals whose scenario rules require it.
        /// </summary>
        public static bool RequiresRecheck(Passenger passenger)
        {
            return TransferServiceUsageCalculator.UsesService(
                passenger,
                TransferService.BaggageRecheck);
        }

        /// <summary>
        /// True when the passenger's rules bypass recheck while it applies to
        /// their leg (transfer arrivals whose rules explicitly say no recheck).
        /// </summary>
        public static bool BypassesRecheck(Passenger passenger)
        {
            return TransferServiceUsageCalculator.IsBypassed(
                passenger,
                TransferService.BaggageRecheck);
        }

        /// <summary>True when the passenger already carries a baggage-recheck
        /// record, for example from an earlier solve of the same facility.</summary>
        public static bool HasRecheckHistory(Passenger passenger)
        {
            return passenger?.History != null &&
                   passenger.History.ContainsKey(FacilityType.BaggageRecheck);
        }

        public BaggageRecheckResults Solve(BaggageRecheckInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            BaggageRecheckResults finalResult = new BaggageRecheckResults();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            // Re-running the same population through the recheck must not
            // produce a second recheck record: already-served transfers pass
            // through, mirroring the BagDrop repeated-facility rule.
            int alreadyServedRecheck = workingPassengers.Count(HasRecheckHistory);

            if (alreadyServedRecheck > 0)
            {
                Warnings.Add(
                    $"{alreadyServedRecheck} transfer passenger(s) already carry a baggage-recheck " +
                    "record and are not served again.");
            }

            List<ChannelResourcePlan> plans = ResolvePlans(input, workingPassengers);

            Dictionary<Passenger, string> routedChannels = TransferFlowPools.ApplyFlowRouting(
                workingPassengers,
                RequiresRecheck,
                TransferFlowPools.RecheckPoolPrefix,
                input.SplitPoolsPerFlow);

            if (routedChannels.Count > 0)
            {
                Warnings.Add(
                    $"{routedChannels.Count} transfer passenger(s) are queued in per-flow recheck " +
                    $"pools named '{TransferFlowPools.RecheckPoolPrefix} (flow)'. Pool Plans can be " +
                    "keyed by those names; the passengers' own channels are restored after the solve.");
            }

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.BaggageRecheck, Warnings.Add);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Baggage recheck has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            Func<Passenger, bool> recheckPassenger = RequiresRecheck;

            string currentCacheKey = BuildSolverCacheKey(
                workingPassengers,
                serviceTimeProvider,
                null,
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
                null,
                recheckPassenger,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                p => RequiresRecheck(p) && !HasRecheckHistory(p));

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
            }

            // Per-flow pool identities are internal to this facility: restore
            // the original channels on the originals and their clones before
            // the population is merged back and sent downstream.
            TransferFlowPools.RestoreFlowChannels(routedChannels, solve.Clones);

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

            finalResult.ServedCount = solve.Processed.Count;
            finalResult.BypassedCount = workingPassengers.Count(BypassesRecheck);

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.BaggageRecheck);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Resolves the recheck pools. With per-flow splitting on, each flow
        /// that actually requires recheck gets its own pool named
        /// "Baggage Recheck (flow)"; the legacy physical/target values
        /// broadcast. Keyed Pool Plans take precedence and can override any
        /// flow pool by name, or replace everything with one shared plan.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(
            BaggageRecheckInputs input,
            List<Passenger> workingPassengers)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.BaggageRecheck)
                    ?? ResolveLegacyPlans(input, workingPassengers, fallbackDomain);
            }

            return ResolveLegacyPlans(input, workingPassengers, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            BaggageRecheckInputs input,
            List<Passenger> workingPassengers,
            ResourceSearchRange fallbackDomain)
        {
            List<string> channels = TransferFlowPools.PresentFlowChannels(
                workingPassengers,
                RequiresRecheck,
                TransferFlowPools.RecheckPoolPrefix,
                input.SplitPoolsPerFlow);

            return ChannelResourcePlanResolver.Resolve(
                channels,
                input.PhysicalResources,
                input.TargetWait95,
                fallbackDomain,
                Warnings.Add);
        }
    }
}
