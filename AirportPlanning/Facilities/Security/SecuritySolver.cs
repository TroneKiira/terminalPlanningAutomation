using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;
namespace AirportPlanning.Facilities.Security
{
    public class SecuritySolver : FacilitySolverBase
    {
        /// <summary>Canonical Pool Plan identity for the security entry e-gate stage.</summary>
        public const string EGatePool = "EGate";

        /// <summary>Canonical Pool Plan identity for the ATRS screening stage.</summary>
        public const string AtrsPool = "ATRS";

        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public SecuritySolver() : base() { }

        /// <summary>
        /// Routes transfer passengers whose scenario rules request a dedicated
        /// transfer-security pool to <see cref="SecurityInputs.TransferChannelName"/>
        /// for pool selection, then restores their original check-in channel so
        /// downstream facilities are unaffected.
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
                int unconfigured = passengers.Count(UsesSeparateSecurity);

                if (unconfigured > 0)
                {
                    Warnings.Add(
                        $"Separate security routing had no matching pool for '{channel}'. " +
                        $"{unconfigured} transfer passenger(s) remain in their original channel " +
                        "so they can share the main security facility. Add a matching channel " +
                        "(and pool plan) to size them independently.");
                }

                return originalChannels;
            }

            foreach (Passenger passenger in passengers)
            {
                if (!UsesSeparateSecurity(passenger))
                    continue;

                originalChannels[passenger] = passenger.Channel;
                passenger.Channel = channel;
            }

            if (originalChannels.Count > 0)
            {
                Warnings.Add(
                    $"{originalChannels.Count} transfer passenger(s) are routed to the separate " +
                    $"'{channel}' security pool.");
            }

            return originalChannels;
        }

        /// <summary>True when security is required and routed to a dedicated
        /// transfer pool.</summary>
        private static bool UsesSeparateSecurity(Passenger passenger)
        {
            return passenger != null &&
                   passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   passenger.TransferProcessing.RequiresSecurityScreening &&
                   passenger.TransferProcessing.SecurityRouting == TransferFacilityRouting.Separate;
        }

        /// <summary>Restores the original channels saved by
        /// <see cref="ApplySeparateTransferRouting"/> on the upstream passengers
        /// and on the clones the simulation served.</summary>
        private static void RestoreSeparateTransferChannels(
            Dictionary<Passenger, string> originalChannels,
            Dictionary<Passenger, Passenger> clones)
        {
            foreach (KeyValuePair<Passenger, string> pair in originalChannels)
            {
                pair.Key.Channel = pair.Value;

                if (clones != null && clones.TryGetValue(pair.Key, out Passenger clone))
                    clone.Channel = pair.Value;
            }
        }

        public SecurityResults Solve(SecurityInputs input)
        {
            Warnings.Clear();

            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (input.UseEGateSeries)
                return SolveSeries(input);

            SecurityResults finalResult = new SecurityResults();

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.Security, Warnings.Add);

            List<ChannelResourcePlan> plans = ResolvePlans(input);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Security has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            string currentCacheKey = BuildSolverCacheKey(
                input.Passengers,
                serviceTimeProvider,
                input.Channels,
                plans,
                input.StaffingPercentage,
                input.Seed,
                input.OptimizeResources);

            bool cacheHit = currentCacheKey == _lastCacheKey &&
                            _cachedOutcomes != null &&
                            _cachedBaselinePhysical > 0;

            Dictionary<Passenger, string> separatedChannels = ApplySeparateTransferRouting(
                input.Passengers,
                input.TransferChannelName,
                plans);

            List<ResourceSearchOutcome> outcomes;

            FacilitySolveResult solve = SolvePools(
                input.Passengers,
                plans,
                input.Channels,
                RequiresSecurityScreening,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                p => IsDeparturePassenger(p) &&
                     TransferServiceUsageCalculator.UsesService(p, TransferService.Security));

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
                        "Separate transfer-security routing had no matching pool; those passengers " +
                        $"were served by: {string.Join(", ", misplaced)}. Add a Pool Plan (or channel " +
                        $"'{input.TransferChannelName.Trim()}') to size them independently.");
                }

                RestoreSeparateTransferChannels(separatedChannels, solve.Clones);
            }

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
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, input.Passengers, Warnings.Add);
            PoolPlanResolver.WarnOnUncoveredChannels(plans, input.Channels, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.Security);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        private static bool RequiresSecurityScreening(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.Journey != PassengerJourney.Transfer ||
                    passenger.TransferProcessing == null ||
                    passenger.TransferProcessing.RequiresSecurityScreening);
        }

        // ------------------------------------------------------------------
        // Series mode: entry e-gates feeding ATRS screening lanes
        // ------------------------------------------------------------------

        /// <summary>
        /// Simulates the checkpoint as two real queues in series.
        ///
        /// Stage 1 is the entry e-gate bank (identity "EGate"). Stage 2 is the
        /// ATRS screening hall (identity "ATRS"), where passengers routed to a
        /// separate transfer-security pool keep that identity instead. Every
        /// stage-1 departure becomes a stage-2 arrival, so each passenger's
        /// checkpoint time is gate queue + gate service + walk + lane queue +
        /// lane service, and a slow screening stage shows up as a queue at the
        /// second stage rather than as throughput the e-gates cannot deliver.
        /// </summary>
        private SecurityResults SolveSeries(SecurityInputs input)
        {
            SecurityResults finalResult = new SecurityResults { IsSeries = true };

            List<Passenger> originals = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .ToList();

            // Clone once so the input population is never mutated, and keep the
            // original -> working mapping so the output restores identity.
            Dictionary<Passenger, Passenger> workingMap =
                new Dictionary<Passenger, Passenger>();

            List<Passenger> working = new List<Passenger>(originals.Count);

            foreach (Passenger original in originals)
            {
                Passenger clone = original.Clone();
                workingMap[original] = clone;
                working.Add(clone);
            }

            // Eligibility mirrors the legacy path: departures that require
            // security (transfer passengers only when their scenario rule asks
            // for it). Without the departure filter the stage would catch
            // arrivals as well, because RequiresSecurityScreening is true for
            // every non-transfer passenger.
            Func<Passenger, bool> required =
                p => p != null &&
                     IsDeparturePassenger(p) &&
                     TransferServiceUsageCalculator.UsesService(p, TransferService.Security);

            Func<Passenger, bool> eligible =
                p => p != null && RequiresSecurityScreening(p) && required(p);

            List<Passenger> securityPassengers = working.Where(eligible).ToList();

            if (securityPassengers.Count == 0)
            {
                Warnings.Add("Security series mode found no passengers requiring screening.");

                finalResult.QueueResults = CreateEmptyResult(new List<Passenger>());
                finalResult.QueueResults.Passengers =
                    BuildOutputPopulation(originals, new Dictionary<Passenger, Passenger>());

                return finalResult;
            }

            // Transfer passengers who requested a dedicated screening pool keep
            // that identity at stage 2; everyone else uses ATRS.
            string transferChannel = (input.TransferChannelName ?? string.Empty).Trim();
            HashSet<Passenger> separateTransfer = new HashSet<Passenger>();

            foreach (Passenger passenger in securityPassengers)
            {
                if (UsesSeparateSecurity(passenger))
                    separateTransfer.Add(passenger);
            }

            // ---- stage plans ------------------------------------------------
            List<ChannelResourcePlan> plans = ResolvePlans(input) ?? new List<ChannelResourcePlan>();

            // A stage keeps every plan keyed to its channel: a plan can be
            // further split by a PoolKey, so a checkpoint that separates
            // domestic and international passengers carries
            // EGate|DHP|Profile and ATRS|DHP|Profile rows - four pools, two
            // queues each. Collapsing stage plans to their first row would
            // size one undifferentiated pool and drop the rest.
            List<ChannelResourcePlan> eGatePlans = StagePlans(plans, EGatePool);
            List<ChannelResourcePlan> atrsPlans = StagePlans(plans, AtrsPool);

            if (eGatePlans.Count == 0)
                eGatePlans.Add(FallbackStagePlan(
                    EGatePool, input.EGatePhysicalResources, input.EGateTargetWait95));

            if (atrsPlans.Count == 0)
            {
                atrsPlans.Add(FallbackStagePlan(
                    AtrsPool,
                    input.PhysicalResources != null && input.PhysicalResources.Count > 0
                        ? input.PhysicalResources[0]
                        : 1,
                    input.TargetWait95 != null && input.TargetWait95.Count > 0
                        ? Math.Max(0.0, input.TargetWait95[0])
                        : 0.0));
            }

            WarnOnSharedStagePlans(plans);

            // Transfer passengers who requested a dedicated screening pool keep
            // that identity at stage 2; everyone else uses ATRS.
            ChannelResourcePlan transferPlan =
                HasConfiguredChannel(plans, transferChannel)
                    ? plans.First(p => !p.IsSharedPool && string.Equals(
                        p.Channel, transferChannel, StringComparison.OrdinalIgnoreCase))
                    : null;

            if (separateTransfer.Count > 0 && transferPlan == null)
            {
                Warnings.Add(
                    $"{separateTransfer.Count} transfer passenger(s) requested a separate " +
                    $"security pool, but no plan for '{transferChannel}' is wired; they join " +
                    "the shared ATRS stage.");
            }

            // Every stage-2 pool, in plan order: ATRS screening first, then any
            // dedicated transfer pool.
            List<ChannelResourcePlan> stage2PlanSet = new List<ChannelResourcePlan>(atrsPlans);

            if (transferPlan != null)
                stage2PlanSet.Add(transferPlan);

            // ---- service times ---------------------------------------------
            // The wired map is scoped to Security by ResolveServiceTimeProvider,
            // and each stage resolves by its own channel identity, so map keys
            // "EGate" and "ATRS" give the stages independent times. Without a
            // map the input's e-gate seconds serves stage 1 and the legacy
            // Service Profile serves stage 2.
            IServiceTimeProvider mapProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, null, FacilityScopes.Security, Warnings.Add);

            IServiceTimeProvider stage1Provider = mapProvider ?? new ConstantServiceProvider(
                new ServiceProfile
                {
                    MeanServiceTime = Math.Max(0.0, input.EGateServiceTimeSeconds) / 60.0,
                    StandardDeviation = Math.Max(0.0, input.EGateServiceTimeSeconds) / 60.0 * 0.3
                });

            IServiceTimeProvider stage2Provider = mapProvider ??
                (input.ServiceProfile != null
                    ? new ConstantServiceProvider(input.ServiceProfile)
                    : null);

            if (stage2Provider == null)
            {
                Warnings.Add(
                    "Security series mode has no service-time source for the ATRS stage: " +
                    "wire a Service Time Map with an 'ATRS' key or a Service Profile. " +
                    "Without one every screening service time is 0 and the stage is meaningless.");
            }

            // A wired map whose keys match no passenger silently degrades both
            // stages to the Default profile - the usual cause of "both stages
            // came out identical". Name the miss and the key grammar instead.
            WarnOnUnmatchedStageKeys(mapProvider, EGatePool, securityPassengers);
            WarnOnUnmatchedStageKeys(mapProvider, AtrsPool, securityPassengers);

            // ---- stage 2 sizing: capacity search on the RAW demand ----------
            // The screening stage is sized against the arrivals the checkpoint
            // is MEANT to handle - the raw security stream - not the stream the
            // e-gates release: a throttled e-gate stage spreads demand and
            // would understate the screening lanes the raw demand needs. This
            // pass searches capacities only; the realized simulation below
            // still runs on the true staged arrivals, so the two stages stay
            // sequential and the sizing stays conservative.
            Dictionary<Passenger, string> originalChannels = new Dictionary<Passenger, string>();

            foreach (Passenger passenger in securityPassengers)
            {
                originalChannels[passenger] = passenger.Channel;
                passenger.Channel =
                    separateTransfer.Contains(passenger) && transferPlan != null
                        ? transferChannel
                        : AtrsPool;
            }

            List<ChannelResourcePlan> stage2SizingPlans = ClonePlans(stage2PlanSet);
            List<ResourceSearchOutcome> stage2SizingOutcomes = null;

            if (input.OptimizeResources)
            {
                SolvePools(
                    securityPassengers,
                    stage2SizingPlans,
                    null,
                    eligible,
                    stage2Provider,
                    input.Seed,
                    true,
                    input.StaffingPercentage,
                    null,
                    out List<ResourceSearchOutcome> sizingOutcomes,
                    Warnings.Add,
                    eligible);

                stage2SizingOutcomes = sizingOutcomes;
                AppendOutcomes(finalResult.SearchOutcomes, sizingOutcomes);
            }

            // The realized stage-2 plans: the chosen capacities when the
            // optimizer ran, otherwise the wired PNR. Each chosen plan's range
            // collapses to the chosen count so the realized pass simulates
            // exactly that capacity.
            List<ChannelResourcePlan> stage2Plans = input.OptimizeResources
                ? RealizedPlans(stage2SizingPlans, stage2SizingOutcomes)
                : stage2PlanSet;

            // ---- stage 1: entry e-gates --------------------------------------
            foreach (Passenger passenger in securityPassengers)
            {
                passenger.Channel = EGatePool;
                passenger.SecurityEntryRoute = EGatePool;
            }

            FacilitySolveResult stage1 = SolvePools(
                securityPassengers,
                eGatePlans,
                new List<string> { EGatePool },
                eligible,
                stage1Provider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                null,
                out List<ResourceSearchOutcome> stage1Outcomes,
                Warnings.Add,
                eligible);

            AppendOutcomes(finalResult.SearchOutcomes, stage1Outcomes);

            List<Passenger> stage1Served = stage1.Processed;

            foreach (Passenger passenger in stage1Served)
            {
                passenger.SecurityEntryWait = passenger.WaitTime;
                passenger.SecurityEntryRoute = EGatePool;
            }

            WriteFacilityHistory(stage1Served, FacilityType.Security);
            AdvancePassengers(stage1Served);

            // The walk sits between the two queues, so it delays every arrival
            // at stage 2 without being a service time.
            double walkMinutes = Math.Max(0.0, input.EGateToAtrsWalkMinutes);

            if (walkMinutes > 0.0)
            {
                foreach (Passenger passenger in stage1Served)
                {
                    passenger.ArrivalTime += walkMinutes;
                    passenger.QueueArrivalTime = passenger.ArrivalTime;
                }
            }

            // ---- stage 2: ATRS screening (realized, on staged arrivals) ------
            foreach (Passenger passenger in stage1Served)
            {
                passenger.Channel = separateTransfer.Contains(passenger) && transferPlan != null
                    ? transferChannel
                    : AtrsPool;

                passenger.SecurityScreeningRoute = passenger.Channel;
            }

            FacilitySolveResult stage2 = SolvePools(
                stage1Served,
                stage2Plans,
                null,
                eligible,
                stage2Provider,
                input.Seed,
                false,
                input.StaffingPercentage,
                null,
                out List<ResourceSearchOutcome> stage2Outcomes,
                Warnings.Add,
                eligible);

            AppendOutcomes(finalResult.SearchOutcomes, stage2Outcomes);

            List<Passenger> stage2Served = stage2.Processed;

            // A passenger missed a stage when its channel matched a stage pool
            // but no plan key matched its demand class. Name it, because the
            // four-pool wiring (sector splits by PoolKey) is exactly where a
            // typo halves the population silently.
            if (stage1Served.Count < securityPassengers.Count)
            {
                Warnings.Add(
                    $"{securityPassengers.Count - stage1Served.Count} security passenger(s) matched no " +
                    $"'{EGatePool}' pool. A PoolKey such as 'DomesticDeparture|Domestic Economy' must " +
                    "match the passenger's demand type and profile; a plain 'DomesticDeparture' key " +
                    "is read as a profile name.");
            }

            if (stage2Served.Count < stage1Served.Count)
            {
                Warnings.Add(
                    $"{stage1Served.Count - stage2Served.Count} security passenger(s) cleared an e-gate " +
                    $"but matched no '{AtrsPool}' (or '{transferChannel}') pool. Check the PoolKey " +
                    "against the passenger's demand type and profile.");
            }

            foreach (Passenger passenger in stage2Served)
                passenger.SecurityScreeningWait = passenger.WaitTime;

            WriteFacilityHistory(stage2Served, FacilityType.Security);

            // The stage-1 record was written on the stage-1 clones; stage 2's
            // write replaced the screening clones' History[Security] with the
            // screening visit. Copy the e-gate stage into the screening
            // record's entry fields so one Security history entry tells the
            // whole checkpoint story: e-gate queue, walk, then screening.
            foreach (KeyValuePair<Passenger, Passenger> pair in stage2.Clones)
            {
                Passenger stage1Clone = pair.Key;

                if (stage1Clone?.History == null ||
                    !stage1Clone.History.TryGetValue(FacilityType.Security, out FacilityRecord entryRecord))
                    continue;

                if (pair.Value == null ||
                    !pair.Value.History.TryGetValue(FacilityType.Security, out FacilityRecord screeningRecord))
                    continue;

                screeningRecord.HasEntryStage = true;
                screeningRecord.EntryArrivalTime = entryRecord.ArrivalTime;
                screeningRecord.EntryStartServiceTime = entryRecord.StartServiceTime;
                screeningRecord.EntryEndServiceTime = entryRecord.EndServiceTime;
                screeningRecord.EntryWaitTime = entryRecord.WaitTime;
                screeningRecord.EntryAssignedResource = entryRecord.AssignedResource;
                screeningRecord.EntryPoolName = entryRecord.PoolName;
            }

            // The checkpoint experience is the sum of both stage queues. The
            // stage-2 clone carries its own wait; adding the stage-1 wait turns
            // the headline metrics (and every per-passenger wait downstream)
            // into checkpoint totals. Stage pool results keep their own waits.
            foreach (KeyValuePair<Passenger, Passenger> pair in stage2.Clones)
            {
                Passenger stage1Clone = pair.Key;
                Passenger stage2Clone = pair.Value;

                if (stage2Clone != null && stage1Clone != null && stage1Clone.WaitTime > 0.0)
                    stage2Clone.WaitTime += stage1Clone.WaitTime;
            }

            AdvancePassengers(stage2Served);

            // ---- restore channels and rebuild the population -----------------
            foreach (KeyValuePair<Passenger, string> pair in originalChannels)
            {
                pair.Key.Channel = pair.Value;

                if (stage1.Clones.TryGetValue(pair.Key, out Passenger clone1))
                {
                    clone1.Channel = pair.Value;

                    // Stage 2 cloned the stage-1 clones, so the restore has to
                    // walk both indirections to reach the output passengers.
                    if (stage2.Clones.TryGetValue(clone1, out Passenger clone2))
                        clone2.Channel = pair.Value;
                }
            }

            // The served population is the stage-2 clones: they carry both
            // stages' times. Map every original passenger to its final clone.
            Dictionary<Passenger, Passenger> finalClones =
                new Dictionary<Passenger, Passenger>();

            foreach (KeyValuePair<Passenger, Passenger> pair in workingMap)
            {
                Passenger finalClone = pair.Value;

                if (stage1.Clones.TryGetValue(pair.Value, out Passenger clone1))
                {
                    finalClone = stage2.Clones.TryGetValue(clone1, out Passenger clone2)
                        ? clone2
                        : clone1;
                }

                finalClones[pair.Key] = finalClone;
            }

            // ---- combined facility result ------------------------------------
            // Reuse the base merge so every stage pool (e-gates, screening,
            // any transfer pool) keeps its own ProcessorPoolInfo, resource
            // offsets and QueuePoolResult. The headline cohort is the stage-2
            // population carrying both stages' waits, so the facility metrics
            // read as checkpoint experience.
            FacilitySolveResult combined = new FacilitySolveResult();

            combined.PoolResults.AddRange(stage1.PoolResults);
            combined.PoolResults.AddRange(stage2.PoolResults);
            combined.PoolNames.AddRange(stage1.PoolNames);
            combined.PoolNames.AddRange(stage2.PoolNames);
            combined.PoolIsShared.AddRange(stage1.PoolIsShared);
            combined.PoolIsShared.AddRange(stage2.PoolIsShared);
            combined.TargetPerPool.AddRange(stage1.TargetPerPool);
            combined.TargetPerPool.AddRange(stage2.TargetPerPool);
            combined.PhysicalPerChannel.AddRange(stage1.PhysicalPerChannel);
            combined.PhysicalPerChannel.AddRange(stage2.PhysicalPerChannel);
            combined.ActivePerChannel.AddRange(stage1.ActivePerChannel);
            combined.ActivePerChannel.AddRange(stage2.ActivePerChannel);
            combined.AllResources.AddRange(stage1.AllResources);
            combined.AllResources.AddRange(stage2.AllResources);
            combined.TotalPhysical = stage1.TotalPhysical + stage2.TotalPhysical;
            combined.TotalActive = stage1.TotalActive + stage2.TotalActive;

            QueueResults aggregate = MergePoolResults(combined);

            QueueResults merged = _metricsCalculator.Calculate(
                stage2Served, new List<QueueResource>());

            merged.Resources = combined.AllResources;
            merged.ResourcesUsed = combined.AllResources.Count;
            merged.ProcessorPools = aggregate.ProcessorPools;
            merged.PoolResults = aggregate.PoolResults;

            // The merged pool list holds one cohort per queue stage: the same
            // passengers pass the e-gate stage and then the ATRS stage (stage 2
            // may split across ATRS and any dedicated transfer pool), so the
            // pool counts multiply by the stage count instead of the visit
            // count. Diagnostics use this to reconcile pools against the
            // processed list.
            merged.SeriesStageCount = 2;

            merged.Passengers = BuildOutputPopulation(originals, finalClones);
            merged.ProcessedPassengers = stage2Served;
            merged.BaselineProcessorCount = combined.TotalPhysical;
            merged.PhysicalProcessorCount = combined.TotalPhysical;

            finalResult.QueueResults = merged;
            finalResult.EGateResults = stage1.Results;
            finalResult.AtrsResults = stage2.Results;
            finalResult.EGatePhysicalCount = stage1.TotalPhysical;
            finalResult.AtrsPhysicalCount = stage2.TotalPhysical;
            finalResult.PhysicalLanes = combined.TotalPhysical;
            finalResult.ActiveLanes = combined.TotalActive;
            finalResult.PhysicalCounters = finalResult.PhysicalLanes;
            finalResult.ActiveCounters = finalResult.ActiveLanes;
            finalResult.PoolNames = combined.PoolNames;
            finalResult.PhysicalPerChannel = combined.PhysicalPerChannel;
            finalResult.ActivePerChannel = combined.ActivePerChannel;
            finalResult.PoolResults = combined.PoolResults;

            finalResult.Wait95PerChannel =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < combined.PoolResults.Count && i < combined.PoolNames.Count; i++)
            {
                string name = combined.PoolNames[i];

                if (!string.IsNullOrWhiteSpace(name) &&
                    !finalResult.Wait95PerChannel.ContainsKey(name))
                {
                    finalResult.Wait95PerChannel[name] =
                        combined.PoolResults[i]?.Wait95 ?? 0.0;
                }
            }

            finalResult.NominalPerChannel = eGatePlans
                .Concat(stage2SizingPlans)
                .Select(p => p.PhysicalResources)
                .ToList();

            // Per-stage empty-pool warnings carry that stage's own population,
            // so a key mismatch names the identities the stage actually saw.
            PoolPlanResolver.WarnOnEmptyPools(
                eGatePlans,
                stage1.PoolResults,
                securityPassengers,
                Warnings.Add);

            PoolPlanResolver.WarnOnEmptyPools(
                stage2Plans,
                stage2.PoolResults,
                stage1Served,
                Warnings.Add);

            WarnOnSeriesBottleneck(stage1, stage2);

            return finalResult;
        }

        private static void AppendOutcomes(
            List<ResourceSearchOutcome> target,
            List<ResourceSearchOutcome> outcomes)
        {
            if (target == null || outcomes == null)
                return;

            foreach (ResourceSearchOutcome outcome in outcomes)
            {
                if (outcome != null)
                    target.Add(outcome);
            }
        }

        /// <summary>
        /// Warns when a wired Service Time Map has no key that matches any
        /// passenger at one stage. Every lookup then falls through to the
        /// map's Default profile, which makes both stages run on the same
        /// service time and collapses the optimizer to identical counts -
        /// the silent failure behind "why are both stages sized the same".
        /// </summary>
        private void WarnOnUnmatchedStageKeys(
            IServiceTimeProvider provider,
            string stageChannel,
            List<Passenger> passengers)
        {
            if (provider == null || passengers == null || passengers.Count == 0)
                return;

            ServiceTimeMap map = provider as ServiceTimeMap;

            if (map == null || map.Entries == null || map.Entries.Count == 0)
                return;

            // One probe per distinct demand class, so the check is cheap even
            // for large populations.
            List<Passenger> probes = passengers
                .GroupBy(p => new Tuple<int, string>(
                    (int)p.DHPType,
                    p.ProfileName ?? string.Empty))
                .Select(g => g.First())
                .ToList();

            foreach (Passenger probe in probes)
            {
                Passenger synthetic = new Passenger
                {
                    Channel = stageChannel,
                    DHPType = probe.DHPType,
                    ProfileName = probe.ProfileName
                };

                if (map.TryResolve(synthetic, out _))
                    return;
            }

            Warnings.Add(
                $"No Service Time Map key matched any passenger at stage '{stageChannel}'. " +
                "Sector-only keys read 'Channel|DHPType' (for example 'EGate|DomesticDeparture'); " +
                "full identities read 'Channel|DHPType|ProfileName'. The map's Default profile " +
                $"was used for all {passengers.Count} passenger(s) at this stage.");
        }

        /// <summary>
        /// Every resolved plan keyed to one stage's channel, in plan order.
        /// A stage can carry several pools: a plan may be split by a PoolKey
        /// ("EGate|DomesticDeparture|Domestic Economy"), so domestic and
        /// international passengers queue separately at the same stage.
        /// </summary>
        private static List<ChannelResourcePlan> StagePlans(
            List<ChannelResourcePlan> plans,
            string stageChannel)
        {
            if (plans == null)
                return new List<ChannelResourcePlan>();

            return plans
                .Where(p => p != null && !p.IsSharedPool && string.Equals(
                    p.Channel, stageChannel, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// The fallback plan a stage runs on when no key plan supplies it.
        /// </summary>
        private ChannelResourcePlan FallbackStagePlan(
            string stageChannel,
            int fallbackPhysical,
            double fallbackTarget)
        {
            return new ChannelResourcePlan
            {
                Channel = stageChannel,
                PhysicalResources = Math.Max(1, fallbackPhysical),
                TargetWait95 = Math.Max(0.0, fallbackTarget),
                Range = new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources)
            };
        }

        /// <summary>
        /// Deep-copies a plan list so the sizing pass and the realized pass can
        /// diverge: the sizing pass keeps the wired PNR, and the realized pass
        /// carries the optimizer's chosen count.
        /// </summary>
        private static List<ChannelResourcePlan> ClonePlans(List<ChannelResourcePlan> plans)
        {
            List<ChannelResourcePlan> clones = new List<ChannelResourcePlan>();

            if (plans == null)
                return clones;

            foreach (ChannelResourcePlan plan in plans)
            {
                if (plan == null)
                    continue;

                clones.Add(new ChannelResourcePlan
                {
                    Channel = plan.Channel,
                    PoolKey = plan.PoolKey,
                    PhysicalResources = plan.PhysicalResources,
                    TargetWait95 = plan.TargetWait95,
                    Range = plan.Range != null
                        ? new ResourceSearchRange(plan.Range.Min, plan.Range.Max)
                        : null
                });
            }

            return clones;
        }

        /// <summary>
        /// The plans the realized stage-2 simulation runs on: each pool at the
        /// capacity the sizing pass chose, with its range collapsed to that
        /// single count. The wired MQT is kept so the pool result still
        /// reports the target it was sized against.
        /// </summary>
        private static List<ChannelResourcePlan> RealizedPlans(
            List<ChannelResourcePlan> sizingPlans,
            List<ResourceSearchOutcome> sizingOutcomes)
        {
            List<ChannelResourcePlan> realized = ClonePlans(sizingPlans);

            if (sizingOutcomes == null)
                return realized;

            int count = Math.Min(realized.Count, sizingOutcomes.Count);

            for (int i = 0; i < count; i++)
            {
                ChannelResourcePlan plan = realized[i];
                ResourceSearchOutcome outcome = sizingOutcomes[i];

                if (plan == null || outcome == null)
                    continue;

                int chosen = Math.Max(1, outcome.OptimalPhysicalResources);

                plan.PhysicalResources = chosen;
                plan.Range = new ResourceSearchRange(chosen, chosen);
            }

            return realized;
        }

        /// <summary>
        /// A shared plan serves the whole facility in one queue and cannot
        /// express the per-stage split, so it is ignored in series mode with a
        /// warning.
        /// </summary>
        private void WarnOnSharedStagePlans(List<ChannelResourcePlan> plans)
        {
            if (plans != null && plans.Any(p => p != null && p.IsSharedPool))
            {
                Warnings.Add(
                    $"A shared pool plan was wired, but series mode needs separate plans for " +
                    $"'{EGatePool}' and '{AtrsPool}'. The shared plan is ignored and both stages " +
                    "use their component fallbacks.");
            }
        }

        /// <summary>
        /// Names the bottleneck when one stage's Wait95 is more than twice the
        /// other's: the checkpoint is only as fast as its slower stage, so an
        /// imbalance means the faster stage is over-provisioned or the slower
        /// one is starved.
        /// </summary>
        private void WarnOnSeriesBottleneck(
            FacilitySolveResult stage1,
            FacilitySolveResult stage2)
        {
            double wait1 = stage1.Results?.Wait95 ?? 0.0;
            double wait2 = stage2.Results?.Wait95 ?? 0.0;

            if (wait2 > wait1 * 2.0 + 1e-9)
            {
                Warnings.Add(
                    $"The ATRS stage Wait95 ({wait2:0.##} min) is more than twice the e-gate stage's " +
                    $"({wait1:0.##} min): screening is the checkpoint bottleneck. " +
                    "Widen the ATRS resource domain or add lanes.");
            }
            else if (wait1 > wait2 * 2.0 + 1e-9)
            {
                Warnings.Add(
                    $"The e-gate stage Wait95 ({wait1:0.##} min) is more than twice the ATRS " +
                    $"stage's ({wait2:0.##} min): the entry e-gates are the checkpoint bottleneck. " +
                    "Note that ATRS is sized on the arrivals the e-gates release, so a throttled " +
                    "gate spreads demand and understates the screening lanes the raw demand needs. " +
                    "Add e-gates, or check the 'EGate' plan and its target wait.");
            }
        }

        /// <summary>
        /// Resolves channel resource plans from keyed PoolPlans when supplied,
        /// otherwise falls back to the legacy PhysicalResources/TargetWait95
        /// lists with a shared ResourceDomain.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(SecurityInputs input)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.Security)
                    ?? ResolveLegacyPlans(input, fallbackDomain);
            }

            return ResolveLegacyPlans(input, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            SecurityInputs input,
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
