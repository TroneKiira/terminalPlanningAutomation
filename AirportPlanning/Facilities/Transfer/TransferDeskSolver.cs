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
    /// Arrival-side transfer desk. Serves transfer passengers whose scenario
    /// rules require the desk (Domestic→Domestic, Domestic→International and
    /// International→International in the TypeScript mapping); everyone else
    /// passes through untouched.
    ///
    /// Following the TypeScript model, baggage recheck is the desk facility for
    /// the International→Domestic flow: passengers who require recheck are
    /// served by the recheck queue and are not queued here as well, so a flow
    /// never receives two desk-type services.
    ///
    /// The full population is always returned: served passengers are replaced
    /// by their processed clones (service history and advanced clock travel
    /// downstream) and the rest pass through in their original order. Per-flow
    /// pool routing is temporary and is restored before output, so the
    /// passenger's persistent channel keeps its downstream meaning for the
    /// integrated and separate security/immigration/emigration flows.
    /// </summary>
    public class TransferDeskSolver : FacilitySolverBase
    {
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public TransferDeskSolver() : base() { }

        /// <summary>
        /// True when the passenger must use the transfer desk at this terminal:
        /// only transfer arrivals whose scenario rules require the desk.
        /// Passengers who also require baggage recheck are excluded here, because
        /// recheck substitutes for the desk on that flow (the TypeScript model).
        /// </summary>
        public static bool RequiresDesk(Passenger passenger)
        {
            return TransferServiceUsageCalculator.UsesService(
                       passenger,
                       TransferService.TransferDesk) &&
                   !TransferServiceUsageCalculator.UsesService(
                       passenger,
                       TransferService.BaggageRecheck);
        }

        /// <summary>
        /// True when the passenger's rules bypass the desk while it applies to
        /// their leg (transfer arrivals whose rules explicitly say no desk).
        /// </summary>
        public static bool BypassesDesk(Passenger passenger)
        {
            return TransferServiceUsageCalculator.IsBypassed(
                passenger,
                TransferService.TransferDesk);
        }

        /// <summary>True when the passenger already carries a transfer-desk
        /// record, for example from an earlier solve of the same facility.</summary>
        public static bool HasDeskHistory(Passenger passenger)
        {
            return passenger?.History != null &&
                   passenger.History.ContainsKey(FacilityType.TransferDesk);
        }

        public TransferDeskResults Solve(TransferDeskInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            TransferDeskResults finalResult = new TransferDeskResults();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            // Re-running the same population through the desk must not produce
            // a second desk record: already-served transfers pass through,
            // mirroring the BagDrop repeated-facility rule.
            int alreadyServed = workingPassengers.Count(HasDeskHistory);

            if (alreadyServed > 0)
            {
                Warnings.Add(
                    $"{alreadyServed} transfer passenger(s) already carry a transfer-desk record " +
                    "and are not served again.");
            }

            // TS mapping: recheck substitutes for the desk, so surface any
            // overlap before the solve and let the user fix the rules.
            TransferFlowPools.WarnOnDeskAndRecheckOverlap(workingPassengers, Warnings.Add);

            List<ChannelResourcePlan> plans = ResolvePlans(input, workingPassengers);

            Dictionary<Passenger, string> routedChannels = TransferFlowPools.ApplyFlowRouting(
                workingPassengers,
                RequiresDesk,
                TransferFlowPools.DeskPoolPrefix,
                input.SplitPoolsPerFlow);

            if (routedChannels.Count > 0)
            {
                Warnings.Add(
                    $"{routedChannels.Count} transfer passenger(s) are queued in per-flow desk " +
                    $"pools named '{TransferFlowPools.DeskPoolPrefix} (flow)'. Pool Plans can be " +
                    "keyed by those names; the passengers' own channels are restored after the solve.");
            }

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.TransferDesk, Warnings.Add);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Transfer desk has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            Func<Passenger, bool> deskPassenger = RequiresDesk;

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
                deskPassenger,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                p => RequiresDesk(p) && !HasDeskHistory(p));

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
            finalResult.BypassedCount = workingPassengers.Count(BypassesDesk);

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.TransferDesk);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Resolves the desk pools. With per-flow splitting on, each flow that
        /// actually requires the desk gets its own pool named
        /// "Transfer Desk (flow)"; the legacy physical/target values broadcast.
        /// Keyed Pool Plans take precedence and can override any flow pool by
        /// name, or replace everything with one shared plan.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(
            TransferDeskInputs input,
            List<Passenger> workingPassengers)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.TransferDesk)
                    ?? ResolveLegacyPlans(input, workingPassengers, fallbackDomain);
            }

            return ResolveLegacyPlans(input, workingPassengers, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            TransferDeskInputs input,
            List<Passenger> workingPassengers,
            ResourceSearchRange fallbackDomain)
        {
            List<string> channels = TransferFlowPools.PresentFlowChannels(
                workingPassengers,
                RequiresDesk,
                TransferFlowPools.DeskPoolPrefix,
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
