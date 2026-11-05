using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Facilities.TerminalEntry
{
    /// <summary>
    /// Simulates the departure-terminal admission queue. Originating
    /// departures use the entry pools; transfer passengers are already airside
    /// and pass through the stage without being queued. The channel field is
    /// temporarily mapped to the entry route on private working copies, then
    /// restored before passengers leave this stage; check-in routing is kept
    /// independent in TerminalEntryRoute.
    /// </summary>
    public class TerminalEntrySolver : FacilitySolverBase
    {
        public const string ManualRoute = "Manual Verification";
        public const string DigiYatraRoute = "DigiYatra";

        /// <summary>Built-in manual-verification mean service time, in seconds.</summary>
        public const double DefaultManualServiceTimeSeconds = 15.0;

        /// <summary>Built-in DigiYatra/e-gate mean service time, in seconds.</summary>
        public const double DefaultDigiYatraServiceTimeSeconds = 7.0;

        /// <summary>Default manual-verification MQT used when no Pool Plan is wired.</summary>
        public const double DefaultManualTargetWait95 = 3.0;

        /// <summary>Default DigiYatra MQT used when no Pool Plan is wired.</summary>
        public const double DefaultDigiYatraTargetWait95 = 2.0;

        private static readonly List<string> EntryRoutes =
            new List<string> { ManualRoute, DigiYatraRoute };

        private string _lastCacheKey = string.Empty;
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public TerminalEntryResults Solve(TerminalEntryInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            int transferPassengers = workingPassengers.Count(p =>
                IsDeparturePassenger(p) &&
                p.Journey == PassengerJourney.Transfer);

            Dictionary<Passenger, string> originalChannels = new Dictionary<Passenger, string>();
            PrepareTerminalEntryArrivals(workingPassengers);
            AssignRoutes(workingPassengers, input.DigiYatraSharePercentage, input.Seed, originalChannels);

            IServiceTimeProvider serviceTimes = ResolveEntryServiceTimeProvider(input);

            List<ChannelResourcePlan> plans = ResolvePlans(input);
            bool cacheHit = BuildSolverCacheKey(
                    workingPassengers,
                    serviceTimes,
                    EntryRoutes,
                    plans,
                    input.StaffingPercentage,
                    input.Seed,
                    input.OptimizeResources) == _lastCacheKey &&
                _cachedOutcomes != null &&
                _cachedBaselinePhysical > 0;

            List<ResourceSearchOutcome> outcomes;
            FacilitySolveResult solve = SolvePools(
                workingPassengers,
                plans,
                EntryRoutes,
                p => true,
                serviceTimes,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                RequiresTerminalEntry);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = BuildSolverCacheKey(
                    workingPassengers,
                    serviceTimes,
                    EntryRoutes,
                    plans,
                    input.StaffingPercentage,
                    input.Seed,
                    input.OptimizeResources);
            }

            foreach (KeyValuePair<Passenger, Passenger> pair in solve.Clones)
            {
                if (!originalChannels.TryGetValue(pair.Key, out string originalChannel))
                    continue;

                pair.Key.Channel = originalChannel;
                pair.Value.Channel = originalChannel;
            }

            QueueResults results = solve.Results;
            results.Passengers = BuildOutputPopulation(workingPassengers, solve.Clones);
            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.TerminalEntry);
            AdvancePassengers(solve.Processed);
            AdvancePastEntryWalk(solve.Processed);

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, workingPassengers, Warnings.Add);
            if (!plans.Any(p => p.IsSharedPool))
                PoolPlanResolver.WarnOnUncoveredChannels(plans, EntryRoutes, Warnings.Add);

            List<QueuePoolResult> poolSnapshots = results.PoolResults ??
                new List<QueuePoolResult>();

            return new TerminalEntryResults
            {
                QueueResults = results,
                PhysicalPositions = solve.TotalPhysical,
                ActivePositions = solve.TotalActive,
                PoolNames = solve.PoolNames,
                PhysicalPerPool = solve.PhysicalPerChannel,
                ActivePerPool = solve.ActivePerChannel,
                NominalPerPool = plans.Select(p => p.PhysicalResources).ToList(),
                Wait95PerPool = solve.Wait95PerChannel,
                AverageWaitPerPool = poolSnapshots.Select(p => p.AverageWait).ToList(),
                MaxWaitPerPool = poolSnapshots.Select(p => p.MaxWait).ToList(),
                Wait95ListPerPool = poolSnapshots.Select(p => p.Wait95).ToList(),
                PeakQueuePerPool = poolSnapshots.Select(p => p.PeakQueueLength).ToList(),
                SearchOutcomes = solve.SearchOutcomes ?? new List<ResourceSearchOutcome>(),
                TransfersBypassingEntry = transferPassengers
            };
        }

        private List<ChannelResourcePlan> ResolvePlans(TerminalEntryInputs input)
        {
            ResourceSearchRange domain = input.ResourceDomain ??
                new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

            List<ChannelResourcePlan> plans = PoolPlanResolver.Resolve(
                input.PoolPlans, domain, Warnings.Add, FacilityScopes.TerminalEntry);

            if (plans != null)
                return plans;

            return ChannelResourcePlanResolver.Resolve(
                EntryRoutes,
                new[]
                {
                    Math.Max(1, input.ManualPhysicalPositions),
                    Math.Max(1, input.DigiYatraPhysicalPositions)
                },
                new[]
                {
                    DefaultManualTargetWait95,
                    DefaultDigiYatraTargetWait95
                },
                domain,
                Warnings.Add);
        }

        /// <summary>
        /// Resolves the service-time source. A wired Service Time Map wins, then
        /// a wired fallback Service Profile. With neither, the built-in route
        /// defaults keep the stage usable and a warning makes that assumption
        /// explicit.
        /// </summary>
        private IServiceTimeProvider ResolveEntryServiceTimeProvider(TerminalEntryInputs input)
        {
            IServiceTimeProvider provider = ResolveServiceTimeProvider(
                input.ServiceTimeProvider,
                input.ServiceProfile,
                FacilityScopes.TerminalEntry,
                Warnings.Add);

            if (provider != null)
                return provider;

            Warnings.Add(
                "No Service Profile or Service Time Map is wired; using built-in route " +
                $"defaults ({DefaultManualServiceTimeSeconds:0.##} s manual, " +
                $"{DefaultDigiYatraServiceTimeSeconds:0.##} s DigiYatra).");

            return new TerminalEntryDefaultServiceTimeProvider(
                DefaultManualServiceTimeSeconds,
                DefaultDigiYatraServiceTimeSeconds);
        }

        private static bool HasTerminalEntryTime(Passenger passenger)
        {
            return passenger != null &&
                !double.IsNaN(passenger.TerminalEntryArrivalTime) &&
                !double.IsInfinity(passenger.TerminalEntryArrivalTime);
        }

        private static bool IsSchedulePassenger(Passenger passenger)
        {
            return passenger != null && !string.IsNullOrWhiteSpace(passenger.FlightLegId);
        }

        /// <summary>
        /// True when the passenger enters through the public terminal hall.
        /// Transfer passengers are already airside and therefore bypass the
        /// terminal-entry pools; they still pass through to downstream
        /// facilities on the component's Passengers output.
        /// </summary>
        public static bool RequiresTerminalEntry(Passenger passenger)
        {
            return IsDeparturePassenger(passenger) &&
                   passenger.Journey != PassengerJourney.Transfer;
        }

        private void PrepareTerminalEntryArrivals(List<Passenger> passengers)
        {
            int incompatibleSchedulePassengers = 0;

            foreach (Passenger passenger in passengers)
            {
                if (!RequiresTerminalEntry(passenger))
                    continue;

                if (HasTerminalEntryTime(passenger))
                {
                    // The generator's current ArrivalTime may already include
                    // the entry-to-check-in walk. Queue at the checkpoint first.
                    passenger.ArrivalTime = passenger.TerminalEntryArrivalTime;
                    passenger.QueueArrivalTime = passenger.TerminalEntryArrivalTime;
                }
                else if (IsSchedulePassenger(passenger))
                {
                    incompatibleSchedulePassengers++;
                }
                else
                {
                    passenger.QueueArrivalTime = passenger.ArrivalTime;
                }
            }

            if (incompatibleSchedulePassengers > 0)
            {
                Warnings.Add(
                    $"{incompatibleSchedulePassengers} schedule passenger(s) have no terminal-entry timestamp and were passed through. " +
                    "Set Arrival Basis to Terminal Entry before using the Terminal Entry queue.");
            }
        }

        private void AssignRoutes(
            List<Passenger> passengers,
            double digiYatraSharePercentage,
            int seed,
            Dictionary<Passenger, string> originalChannels)
        {
            double share = digiYatraSharePercentage;
            if (double.IsNaN(share) || double.IsInfinity(share))
                share = 37.5;

            double normalizedShare = Math.Max(0.0, Math.Min(100.0, share));
            if (normalizedShare != share)
            {
                Warnings.Add("DigiYatra Share must be between 0 and 100%; it was clamped to the valid range.");
            }

            List<Passenger> unassigned = new List<Passenger>();

            foreach (Passenger passenger in passengers)
            {
                if (!RequiresTerminalEntry(passenger))
                    continue;

                if (!HasTerminalEntryTime(passenger) && IsSchedulePassenger(passenger))
                    continue;

                originalChannels[passenger] = passenger.Channel;
                unassigned.Add(passenger);
            }

            // Exact share for the realized population, with seeded allocation
            // so the same passenger population produces the same route split.
            int digiYatraCount = (int)Math.Round(
                unassigned.Count * normalizedShare / 100.0,
                MidpointRounding.AwayFromZero);

            Random random = new Random(seed);
            for (int i = unassigned.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Passenger swap = unassigned[i];
                unassigned[i] = unassigned[j];
                unassigned[j] = swap;
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                Passenger passenger = unassigned[i];
                string route = i < digiYatraCount ? DigiYatraRoute : ManualRoute;
                passenger.TerminalEntryRoute = route;
                passenger.Channel = route;
            }
        }

        private static void AdvancePastEntryWalk(IEnumerable<Passenger> passengers)
        {
            // The walk is DATA, not hidden time: the chain stays strictly
            // sequential (a stage starts when the previous one ends), and the
            // walk duration stays on the passenger (WalkingTime /
            // TerminalEntryWalkMinutes) so Passenger Details can show it.
            foreach (Passenger passenger in passengers)
            {
                if (passenger == null)
                    continue;

                double walkMinutes =
                    !double.IsNaN(passenger.TerminalEntryWalkMinutes) &&
                    !double.IsInfinity(passenger.TerminalEntryWalkMinutes)
                        ? Math.Max(0.0, passenger.TerminalEntryWalkMinutes)
                        : 0.0;

                passenger.WalkingTime = walkMinutes;

                passenger.ArrivalTime = passenger.EndServiceTime;
                passenger.QueueArrivalTime = passenger.ArrivalTime;

                if (!double.IsNaN(passenger.CheckInOpenTime))
                    passenger.ArrivedBeforeCheckInOpen = passenger.ArrivalTime < passenger.CheckInOpenTime;

                if (!double.IsNaN(passenger.CheckInLastAdmissionTime))
                    passenger.IsLateForCheckIn = passenger.ArrivalTime > passenger.CheckInLastAdmissionTime;
            }
        }

        /// <summary>
        /// Built-in route defaults, used only when the component has neither a
        /// Service Time Map nor a fallback Service Profile.
        /// </summary>
        private sealed class TerminalEntryDefaultServiceTimeProvider : IServiceTimeProvider
        {
            private readonly ServiceProfile _manual;
            private readonly ServiceProfile _digiYatra;

            public TerminalEntryDefaultServiceTimeProvider(double manualSeconds, double digiYatraSeconds)
            {
                _manual = new ServiceProfile { MeanServiceTime = manualSeconds / 60.0 };
                _digiYatra = new ServiceProfile { MeanServiceTime = digiYatraSeconds / 60.0 };
                CacheKey = string.Format(
                    CultureInfo.InvariantCulture,
                    "terminal-entry-default|manual={0:R}|digiyatra={1:R}",
                    manualSeconds,
                    digiYatraSeconds);
            }

            public string CacheKey { get; }

            public ServiceProfile Resolve(Passenger passenger)
            {
                return string.Equals(
                    passenger?.TerminalEntryRoute,
                    DigiYatraRoute,
                    StringComparison.OrdinalIgnoreCase)
                    ? _digiYatra
                    : _manual;
            }
        }
    }
}
