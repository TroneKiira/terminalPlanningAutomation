using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Generation
{
    /// <summary>
    /// Calculates transfer counts for PHP/aggregate demand and Design Day
    /// demand. Design Day shares apply to the full-day departure market; the
    /// schedule-derived capacity is the feasibility limit, so the planner's
    /// entered shares remain the demand assumption whenever the schedule can
    /// support them.
    /// </summary>
    public static class TransferDemandCalculator
    {
        private const int BucketMinutes = 5;
        private const int PeakWindowBuckets = 12;

        public static TransferDemandResult CalculatePhp(
            int domesticDepartureTotal,
            int internationalDepartureTotal,
            TransferDemandProfile profile)
        {
            ValidateArguments(domesticDepartureTotal, internationalDepartureTotal, profile);

            int domestic = Math.Max(0, domesticDepartureTotal);
            int international = Math.Max(0, internationalDepartureTotal);

            TransferDemandResult result = BuildResult(
                domestic,
                international,
                domestic,
                international,
                profile,
                null);

            // The aggregate generator is driven by design-hour demand, so its
            // daily cohort and its peak cohort are the same population.
            result.DomesticDepartureTransferPeak =
                result.EffectiveDomesticTransferPassengers;
            result.InternationalDepartureTransferPeak =
                result.EffectiveInternationalTransferPassengers;
            return result;
        }

        public static TransferDemandResult CalculateSchedule(
            FlightScheduleResult schedule,
            TransferDemandProfile profile)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            List<string> errors = profile.GetValidationErrors();
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(profile));

            SchedulePeaks peaks = GetSchedulePeaks(schedule);
            TransferScheduleCapacity capacity = CalculateScheduleCapacity(schedule, profile);

            // Design Day transfer shares apply to the full operating day.
            // The rolling peak values are retained for reporting and for
            // consumers that still want to inspect the busiest hour.
            TransferDemandResult result = BuildResult(
                peaks.DomesticDepartureTotal,
                peaks.InternationalDepartureTotal,
                peaks.DomesticDeparture,
                peaks.InternationalDeparture,
                profile,
                capacity);
            result.DomesticDeparturePeakAt = peaks.DomesticDepartureAt;
            result.InternationalDeparturePeakAt = peaks.InternationalDepartureAt;
            ApplyScheduleConnectionAllocation(schedule, profile, result, capacity);
            return result;
        }

        public static TransferScheduleCapacity CalculateScheduleCapacity(
            FlightScheduleResult schedule,
            TransferDemandProfile profile)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            List<string> errors = profile.GetValidationErrors();
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(profile));

            // Capacity is an all-day feasibility limit: every passenger
            // arrival/departure pair inside its scenario connection window is
            // available, not only pairs departing during the busiest hour.
            List<ConnectionCandidate> candidates = BuildCandidates(schedule, profile);
            TransferScheduleCapacity result = new TransferScheduleCapacity();

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                result.ByScenario[scenario] = MaximumFlow(
                    candidates,
                    scenarioFilter: scenario,
                    arrivalSectorFilter: null,
                    departureSectorFilter: null);
            }

            result.DomesticOutbound = MaximumFlow(
                candidates, null, null, LegSector.Domestic);
            result.InternationalOutbound = MaximumFlow(
                candidates, null, null, LegSector.International);
            result.DomesticInbound = MaximumFlow(
                candidates, null, LegSector.Domestic, null);
            result.InternationalInbound = MaximumFlow(
                candidates, null, LegSector.International, null);
            result.Total = MaximumFlow(candidates, null, null, null);

            result.EligibleArrivalPax = candidates
                .Select(c => c.Arrival)
                .Distinct()
                .Sum(ResolvePassengerCount);
            result.EligibleDeparturePax = candidates
                .Select(c => c.Departure)
                .Distinct()
                .Sum(ResolvePassengerCount);

            return result;
        }

        private static void ApplyScheduleConnectionAllocation(
            FlightScheduleResult schedule,
            TransferDemandProfile profile,
            TransferDemandResult result,
            TransferScheduleCapacity capacity)
        {
            List<ConnectionCandidate> candidates = BuildCandidates(schedule, profile);

            Dictionary<FlightLeg, int> remainingArrival = candidates
                .Select(c => c.Arrival)
                .Distinct()
                .ToDictionary(leg => leg, ResolvePassengerCount);
            Dictionary<FlightLeg, int> remainingDeparture = candidates
                .Select(c => c.Departure)
                .Distinct()
                .ToDictionary(leg => leg, ResolvePassengerCount);

            // Allocate the most constrained scenario first, then use enum
            // order as a stable tie-break. The flow network enforces passenger
            // capacities on both ends and the selected MCT window per edge.
            IEnumerable<TransferScenario> scenarios = Enum.GetValues(typeof(TransferScenario))
                .Cast<TransferScenario>()
                .OrderBy(s => capacity.ByScenario.TryGetValue(s, out int max) ? max : 0)
                .ThenBy(s => (int)s);

            foreach (TransferScenario scenario in scenarios)
            {
                int requested = result.ByScenario[scenario].EffectivePassengers;
                List<ConnectionCandidate> scenarioEdges = candidates
                    .Where(c => c.Scenario == scenario)
                    .ToList();

                List<TransferScheduleAssignment> assignments = AllocateScenarioFlow(
                    scenarioEdges,
                    remainingArrival,
                    remainingDeparture,
                    requested);

                int allocated = assignments.Sum(a => a.PassengerCount);
                result.ScheduleAssignments.AddRange(assignments);

                foreach (TransferScheduleAssignment assignment in assignments)
                {
                    remainingArrival[assignment.Arrival] -= assignment.PassengerCount;
                    remainingDeparture[assignment.Departure] -= assignment.PassengerCount;
                }

                TransferScenarioDemand scenarioDemand = result.ByScenario[scenario];
                if (allocated < scenarioDemand.EffectivePassengers)
                {
                    scenarioDemand.EffectivePassengers = allocated;
                    result.Warnings.Add(
                        $"{scenario.ToFlowLabel()}: schedule connections shared between flows; " +
                        $"effective demand was reduced to {allocated} passengers.");
                }
            }

            result.EffectiveDomesticTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToDomestic].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToDomestic].EffectivePassengers;
            result.EffectiveInternationalTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToInternational].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToInternational].EffectivePassengers;

            CalculateTransferPeaks(result);
        }

        /// <summary>
        /// Reports the busiest rolling 60-minute transfer-departure window in
        /// each outbound market. The main transfer shares are daily; these
        /// peaks let the planner see how the daily transfer cohort concentrates
        /// without changing the demand calculation.
        /// </summary>
        private static void CalculateTransferPeaks(TransferDemandResult result)
        {
            Dictionary<int, int> domesticDeparture = new Dictionary<int, int>();
            Dictionary<int, int> internationalDeparture = new Dictionary<int, int>();
            Dictionary<int, int> domesticArrival = new Dictionary<int, int>();
            Dictionary<int, int> internationalArrival = new Dictionary<int, int>();

            foreach (TransferScheduleAssignment assignment in result.ScheduleAssignments)
            {
                if (assignment == null || assignment.PassengerCount <= 0)
                    continue;

                AddPeakBucket(
                    assignment.Departure,
                    assignment.PassengerCount,
                    domesticDeparture,
                    internationalDeparture);
                AddPeakBucket(
                    assignment.Arrival,
                    assignment.PassengerCount,
                    domesticArrival,
                    internationalArrival);
            }

            // Find the window start, not just the maximum count, so the result
            // can be labelled and downstream reports can use the same window.
            result.DomesticDepartureTransferPeak =
                PeakWindow(domesticDeparture, out int domesticDepartureAt);
            result.DomesticDepartureTransferPeakAt = domesticDepartureAt;
            result.InternationalDepartureTransferPeak =
                PeakWindow(internationalDeparture, out int internationalDepartureAt);
            result.InternationalDepartureTransferPeakAt = internationalDepartureAt;
            result.DomesticArrivalTransferPeak =
                PeakWindow(domesticArrival, out int domesticArrivalAt);
            result.DomesticArrivalTransferPeakAt = domesticArrivalAt;
            result.InternationalArrivalTransferPeak =
                PeakWindow(internationalArrival, out int internationalArrivalAt);
            result.InternationalArrivalTransferPeakAt = internationalArrivalAt;
        }

        private static void AddPeakBucket(
            FlightLeg leg,
            int passengerCount,
            Dictionary<int, int> domestic,
            Dictionary<int, int> international)
        {
            if (leg == null || passengerCount <= 0)
                return;

            Dictionary<int, int> target = leg.SectorRaw == LegSector.Domestic
                ? domestic
                : leg.SectorRaw == LegSector.International ? international : null;

            if (target == null)
                return;

            int bucket = (int)Math.Floor(
                leg.TotalMinutesFromMidnight / (double)BucketMinutes);
            target[bucket] = (target.TryGetValue(bucket, out int current) ? current : 0) +
                             passengerCount;
        }

        private static int PeakWindow(Dictionary<int, int> buckets, out int at)
        {
            at = 0;
            if (buckets == null || buckets.Count == 0)
                return 0;

            int max = 0;
            int maxStart = 0;
            int minBucket = buckets.Keys.Min();
            int maxBucket = buckets.Keys.Max();

            for (int start = minBucket - PeakWindowBuckets + 1; start <= maxBucket; start++)
            {
                int count = SumWindow(buckets, start);
                if (count > max)
                {
                    max = count;
                    maxStart = start;
                }
            }

            at = maxStart * BucketMinutes;
            return max;
        }

        private static List<TransferScheduleAssignment> AllocateScenarioFlow(
            List<ConnectionCandidate> candidates,
            Dictionary<FlightLeg, int> remainingArrival,
            Dictionary<FlightLeg, int> remainingDeparture,
            int requested)
        {
            List<TransferScheduleAssignment> result = new List<TransferScheduleAssignment>();
            if (requested <= 0 || candidates == null || candidates.Count == 0)
                return result;

            List<ConnectionCandidate> available = candidates
                .Where(c => remainingArrival.TryGetValue(c.Arrival, out int a) && a > 0 &&
                            remainingDeparture.TryGetValue(c.Departure, out int d) && d > 0)
                .ToList();
            if (available.Count == 0)
                return result;

            List<FlightLeg> arrivals = available.Select(c => c.Arrival).Distinct().ToList();
            List<FlightLeg> departures = available.Select(c => c.Departure).Distinct().ToList();
            int source = 0;
            int firstArrival = 1;
            int firstDeparture = firstArrival + arrivals.Count;
            int sink = firstDeparture + departures.Count;
            DinicFlow flow = new DinicFlow(sink + 1);

            Dictionary<FlightLeg, int> arrivalNode = arrivals
                .Select((leg, index) => new { leg, node = firstArrival + index })
                .ToDictionary(x => x.leg, x => x.node);
            Dictionary<FlightLeg, int> departureNode = departures
                .Select((leg, index) => new { leg, node = firstDeparture + index })
                .ToDictionary(x => x.leg, x => x.node);

            foreach (FlightLeg arrival in arrivals)
                flow.AddEdge(source, arrivalNode[arrival], remainingArrival[arrival]);

            Dictionary<ConnectionCandidate, FlowEdge> assignmentEdges =
                new Dictionary<ConnectionCandidate, FlowEdge>();
            foreach (ConnectionCandidate candidate in available)
            {
                int residual = Math.Min(
                    candidate.Capacity,
                    Math.Min(remainingArrival[candidate.Arrival], remainingDeparture[candidate.Departure]));
                assignmentEdges[candidate] = flow.AddEdge(
                    arrivalNode[candidate.Arrival],
                    departureNode[candidate.Departure],
                    residual);
            }

            foreach (FlightLeg departure in departures)
                flow.AddEdge(departureNode[departure], sink, remainingDeparture[departure]);

            flow.MaxFlow(source, sink, requested);

            foreach (KeyValuePair<ConnectionCandidate, FlowEdge> pair in assignmentEdges)
            {
                int passengerCount = pair.Value.InitialCapacity - pair.Value.Capacity;
                if (passengerCount <= 0)
                    continue;

                result.Add(new TransferScheduleAssignment
                {
                    Scenario = pair.Key.Scenario,
                    Arrival = pair.Key.Arrival,
                    Departure = pair.Key.Departure,
                    PassengerCount = passengerCount
                });
            }

            return result;
        }

        private static TransferDemandResult BuildResult(
            int domesticDepartureTotal,
            int internationalDepartureTotal,
            int domesticDeparturePeak,
            int internationalDeparturePeak,
            TransferDemandProfile profile,
            TransferScheduleCapacity capacity)
        {
            int domesticRequested = PercentOf(domesticDepartureTotal, profile.DomesticTransferPercent);
            int internationalRequested = PercentOf(internationalDepartureTotal, profile.InternationalTransferPercent);

            Dictionary<TransferScenario, int> requested = new Dictionary<TransferScenario, int>
            {
                [TransferScenario.DomesticToDomestic] =
                    PercentOf(domesticRequested, profile.DomesticToDomesticPercent),
                [TransferScenario.InternationalToDomestic] =
                    PercentOf(domesticRequested, profile.InternationalToDomesticPercent),
                [TransferScenario.DomesticToInternational] =
                    PercentOf(internationalRequested, profile.DomesticToInternationalPercent),
                [TransferScenario.InternationalToInternational] =
                    PercentOf(internationalRequested, profile.InternationalToInternationalPercent)
            };

            Dictionary<TransferScenario, int> effective =
                new Dictionary<TransferScenario, int>(requested);

            if (capacity != null)
            {
                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    effective[scenario] = Math.Min(
                        effective[scenario],
                        capacity.ByScenario.TryGetValue(scenario, out int scenarioMax)
                            ? scenarioMax
                            : 0);
                }

                // Respect shared inbound and outbound passenger capacities.
                // Each step only reduces demand, so previously satisfied
                // scenario and market caps remain satisfied.
                ScaleToCap(
                    effective,
                    new[]
                    {
                        TransferScenario.DomesticToDomestic,
                        TransferScenario.InternationalToDomestic
                    },
                    Math.Min(domesticRequested, capacity.DomesticOutbound));

                ScaleToCap(
                    effective,
                    new[]
                    {
                        TransferScenario.DomesticToInternational,
                        TransferScenario.InternationalToInternational
                    },
                    Math.Min(internationalRequested, capacity.InternationalOutbound));

                ScaleToCap(
                    effective,
                    new[]
                    {
                        TransferScenario.DomesticToDomestic,
                        TransferScenario.DomesticToInternational
                    },
                    capacity.DomesticInbound);

                ScaleToCap(
                    effective,
                    new[]
                    {
                        TransferScenario.InternationalToDomestic,
                        TransferScenario.InternationalToInternational
                    },
                    capacity.InternationalInbound);

                ScaleToCap(
                    effective,
                    Enum.GetValues(typeof(TransferScenario)).Cast<TransferScenario>().ToArray(),
                    capacity.Total);
            }

            TransferDemandResult result = new TransferDemandResult
            {
                DomesticDepartureTotal = domesticDepartureTotal,
                InternationalDepartureTotal = internationalDepartureTotal,
                DomesticDeparturePeak = domesticDeparturePeak,
                InternationalDeparturePeak = internationalDeparturePeak,
                RequestedDomesticTransferPassengers = domesticRequested,
                RequestedInternationalTransferPassengers = internationalRequested,
                Capacity = capacity
            };

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                int requestedCount = requested[scenario];
                int effectiveCount = effective[scenario];

                result.ByScenario[scenario] = new TransferScenarioDemand
                {
                    Scenario = scenario,
                    RequestedPassengers = requestedCount,
                    FeasibleMaximum = capacity == null
                        ? requestedCount
                        : capacity.ByScenario.TryGetValue(scenario, out int max) ? max : 0,
                    EffectivePassengers = effectiveCount
                };

                if (effectiveCount < requestedCount)
                {
                    result.Warnings.Add(
                        $"{scenario.ToFlowLabel()}: requested {requestedCount} transfer passengers, " +
                        $"but the schedule supports {effectiveCount}; demand was capped.");
                }
            }

            result.EffectiveDomesticTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToDomestic].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToDomestic].EffectivePassengers;

            result.EffectiveInternationalTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToInternational].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToInternational].EffectivePassengers;

            return result;
        }

        private static int PercentOf(int count, double percent)
        {
            if (count <= 0 || percent <= 0.0)
                return 0;

            return Math.Max(0, (int)Math.Floor(count * percent / 100.0 + 1e-9));
        }

        private static void ScaleToCap(
            Dictionary<TransferScenario, int> counts,
            IEnumerable<TransferScenario> scenarios,
            int cap)
        {
            List<TransferScenario> keys = scenarios.Distinct().ToList();
            int total = keys.Sum(s => counts.TryGetValue(s, out int value) ? value : 0);

            if (total <= cap || total <= 0)
                return;

            cap = Math.Max(0, cap);
            Dictionary<TransferScenario, double> exact = keys.ToDictionary(
                scenario => scenario,
                scenario => counts[scenario] * (double)cap / total);

            foreach (TransferScenario scenario in keys)
                counts[scenario] = (int)Math.Floor(exact[scenario]);

            int remaining = cap - keys.Sum(s => counts[s]);
            foreach (TransferScenario scenario in keys
                .OrderByDescending(s => exact[s] - Math.Floor(exact[s]))
                .ThenBy(s => (int)s))
            {
                if (remaining <= 0)
                    break;

                if (counts[scenario] < exact[scenario] + 1e-9)
                {
                    counts[scenario]++;
                    remaining--;
                }
            }
        }

        private static List<ConnectionCandidate> BuildCandidates(
            FlightScheduleResult schedule,
            TransferDemandProfile profile)
        {
            List<FlightLeg> arrivals = schedule.Legs == null
                ? new List<FlightLeg>()
                : schedule.Legs.Where(leg => IsEligibleArrival(leg, profile)).ToList();
            List<FlightLeg> departures = schedule.Legs == null
                ? new List<FlightLeg>()
                : schedule.Legs.Where(leg => IsEligibleDeparture(leg, profile)).ToList();

            List<ConnectionCandidate> candidates = new List<ConnectionCandidate>();

            foreach (FlightLeg arrival in arrivals)
            {
                foreach (FlightLeg departure in departures)
                {
                    if (!TryGetScenario(arrival.SectorRaw, departure.SectorRaw, out TransferScenario scenario))
                        continue;

                    if (!HasFeasibleConnectionTime(arrival, departure, profile.RulesFor(scenario)))
                        continue;

                    candidates.Add(new ConnectionCandidate
                    {
                        Arrival = arrival,
                        Departure = departure,
                        Scenario = scenario,
                        Capacity = Math.Min(ResolvePassengerCount(arrival), ResolvePassengerCount(departure))
                    });
                }
            }

            return candidates.Where(c => c.Capacity > 0).ToList();
        }

        private static bool IsEligibleArrival(FlightLeg leg, TransferDemandProfile profile)
        {
            return leg != null &&
                   leg.Direction == LegDirection.Arrival &&
                   leg.ServiceType == LegServiceType.Passenger &&
                   leg.HasTime &&
                   leg.SectorRaw != LegSector.Unknown &&
                   ResolvePassengerCount(leg) > 0 &&
                   IsLegEligibleForSchedule(leg, profile);
        }

        private static bool IsEligibleDeparture(FlightLeg leg, TransferDemandProfile profile)
        {
            return leg != null &&
                   leg.Direction == LegDirection.Departure &&
                   leg.ServiceType == LegServiceType.Passenger &&
                   leg.HasTime &&
                   leg.SectorRaw != LegSector.Unknown &&
                   ResolvePassengerCount(leg) > 0 &&
                   IsLegEligibleForSchedule(leg, profile);
        }

        /// <summary>
        /// True when the leg may carry transfer demand under the profile's
        /// schedule eligibility. AllPassengerLegs accepts every passenger leg;
        /// ViaQualifiedLegs keeps the stricter via-differs-from-station rule.
        /// </summary>
        public static bool IsLegEligibleForSchedule(FlightLeg leg, TransferDemandProfile profile)
        {
            if (leg == null)
                return false;

            return profile == null ||
                   profile.ScheduleEligibility == TransferScheduleEligibility.AllPassengerLegs ||
                   IsTransferEligibleLeg(leg);
        }

        /// <summary>
        /// For arrivals Station is the overall origin; for departures it is
        /// the final destination. A Via value equal to Station is a direct leg.
        /// </summary>
        public static bool IsTransferEligibleLeg(FlightLeg leg)
        {
            string via = (leg.Via ?? string.Empty).Trim();
            string station = (leg.Station ?? string.Empty).Trim();

            return via.Length > 0 && station.Length > 0 &&
                   !string.Equals(via, station, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetScenario(
            LegSector arrival,
            LegSector departure,
            out TransferScenario scenario)
        {
            if (arrival == LegSector.Domestic && departure == LegSector.Domestic)
                scenario = TransferScenario.DomesticToDomestic;
            else if (arrival == LegSector.Domestic && departure == LegSector.International)
                scenario = TransferScenario.DomesticToInternational;
            else if (arrival == LegSector.International && departure == LegSector.Domestic)
                scenario = TransferScenario.InternationalToDomestic;
            else if (arrival == LegSector.International && departure == LegSector.International)
                scenario = TransferScenario.InternationalToInternational;
            else
            {
                scenario = default(TransferScenario);
                return false;
            }

            return true;
        }

        private static bool HasFeasibleConnectionTime(
            FlightLeg arrival,
            FlightLeg departure,
            TransferScenarioRules rules)
        {
            if (arrival == null || departure == null || rules == null)
                return false;

            double arrivalTime = arrival.TotalMinutesFromMidnight;
            double departureTime = departure.TotalMinutesFromMidnight;

            // Schedules are often authored as a single operating day. Try the
            // original departure time and two following days to support
            // overnight connections even when no Base Date was provided.
            for (int dayOffset = 0; dayOffset <= 2; dayOffset++)
            {
                double candidate = departureTime + dayOffset * 1440.0;
                double connectionMinutes = candidate - arrivalTime;

                if (connectionMinutes < 0.0)
                    continue;

                if (connectionMinutes >= rules.MinimumConnectionMinutes &&
                    connectionMinutes <= rules.MaximumConnectionMinutes)
                    return true;

                if (connectionMinutes > rules.MaximumConnectionMinutes)
                    return false;
            }

            return false;
        }

        private static int MaximumFlow(
            List<ConnectionCandidate> candidates,
            TransferScenario? scenarioFilter,
            LegSector? arrivalSectorFilter,
            LegSector? departureSectorFilter)
        {
            IEnumerable<ConnectionCandidate> filtered = candidates;

            if (scenarioFilter.HasValue)
                filtered = filtered.Where(c => c.Scenario == scenarioFilter.Value);
            if (arrivalSectorFilter.HasValue)
                filtered = filtered.Where(c => c.Arrival.SectorRaw == arrivalSectorFilter.Value);
            if (departureSectorFilter.HasValue)
                filtered = filtered.Where(c => c.Departure.SectorRaw == departureSectorFilter.Value);

            List<ConnectionCandidate> edges = filtered.ToList();
            if (edges.Count == 0)
                return 0;

            List<FlightLeg> arrivals = edges.Select(e => e.Arrival).Distinct().ToList();
            List<FlightLeg> departures = edges.Select(e => e.Departure).Distinct().ToList();

            int source = 0;
            int firstArrival = 1;
            int firstDeparture = firstArrival + arrivals.Count;
            int sink = firstDeparture + departures.Count;
            DinicFlow flow = new DinicFlow(sink + 1);

            Dictionary<FlightLeg, int> arrivalNode = arrivals
                .Select((leg, index) => new { leg, node = firstArrival + index })
                .ToDictionary(x => x.leg, x => x.node);
            Dictionary<FlightLeg, int> departureNode = departures
                .Select((leg, index) => new { leg, node = firstDeparture + index })
                .ToDictionary(x => x.leg, x => x.node);

            foreach (FlightLeg arrival in arrivals)
                flow.AddEdge(source, arrivalNode[arrival], ResolvePassengerCount(arrival));

            foreach (ConnectionCandidate edge in edges)
            {
                flow.AddEdge(
                    arrivalNode[edge.Arrival],
                    departureNode[edge.Departure],
                    edge.Capacity);
            }

            foreach (FlightLeg departure in departures)
                flow.AddEdge(departureNode[departure], sink, ResolvePassengerCount(departure));

            return flow.MaxFlow(source, sink);
        }

        private static int ResolvePassengerCount(FlightLeg leg)
        {
            return leg?.PaxRaw.HasValue == true ? Math.Max(0, leg.PaxRaw.Value) : 0;
        }

        private static SchedulePeaks GetSchedulePeaks(FlightScheduleResult schedule)
        {
            Dictionary<int, int> domestic = new Dictionary<int, int>();
            Dictionary<int, int> international = new Dictionary<int, int>();
            int minBucket = int.MaxValue;
            int maxBucket = int.MinValue;
            int domesticTotal = 0;
            int internationalTotal = 0;

            foreach (FlightLeg leg in schedule.Legs ?? new List<FlightLeg>())
            {
                if (leg == null || leg.Direction != LegDirection.Departure ||
                    leg.ServiceType != LegServiceType.Passenger || !leg.HasTime)
                    continue;

                int count = ResolvePassengerCount(leg);
                if (count <= 0)
                    continue;

                int bucket = (int)Math.Floor(leg.TotalMinutesFromMidnight / (double)BucketMinutes);
                Dictionary<int, int> target = leg.SectorRaw == LegSector.Domestic
                    ? domestic
                    : leg.SectorRaw == LegSector.International ? international : null;

                if (target == null)
                    continue;

                if (leg.SectorRaw == LegSector.Domestic)
                    domesticTotal += count;
                else
                    internationalTotal += count;

                target[bucket] =
                    (target.TryGetValue(bucket, out int current) ? current : 0) + count;
                minBucket = Math.Min(minBucket, bucket);
                maxBucket = Math.Max(maxBucket, bucket);
            }

            SchedulePeaks peaks = new SchedulePeaks
            {
                DomesticDepartureTotal = domesticTotal,
                InternationalDepartureTotal = internationalTotal
            };
            if (minBucket > maxBucket)
                return peaks;

            for (int start = minBucket - PeakWindowBuckets + 1; start <= maxBucket; start++)
            {
                int domesticWindow = SumWindow(domestic, start);
                if (domesticWindow > peaks.DomesticDeparture)
                {
                    peaks.DomesticDeparture = domesticWindow;
                    peaks.DomesticDepartureAt = start * BucketMinutes;
                }

                int internationalWindow = SumWindow(international, start);
                if (internationalWindow > peaks.InternationalDeparture)
                {
                    peaks.InternationalDeparture = internationalWindow;
                    peaks.InternationalDepartureAt = start * BucketMinutes;
                }
            }

            return peaks;
        }

        private static int SumWindow(Dictionary<int, int> buckets, int start)
        {
            int total = 0;
            for (int i = 0; i < PeakWindowBuckets; i++)
            {
                if (buckets.TryGetValue(start + i, out int count))
                    total += count;
            }

            return total;
        }

        private static void ValidateArguments(
            int domesticPeak,
            int internationalPeak,
            TransferDemandProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            List<string> errors = profile.GetValidationErrors();
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(profile));

            if (domesticPeak < 0)
                throw new ArgumentOutOfRangeException(nameof(domesticPeak));
            if (internationalPeak < 0)
                throw new ArgumentOutOfRangeException(nameof(internationalPeak));
        }

        private sealed class ConnectionCandidate
        {
            public FlightLeg Arrival { get; set; }
            public FlightLeg Departure { get; set; }
            public TransferScenario Scenario { get; set; }
            public int Capacity { get; set; }
        }

        private sealed class SchedulePeaks
        {
            public int DomesticDeparture { get; set; }
            public int DomesticDepartureAt { get; set; }
            public int InternationalDeparture { get; set; }
            public int InternationalDepartureAt { get; set; }
            public int DomesticDepartureTotal { get; set; }
            public int InternationalDepartureTotal { get; set; }
        }

        private sealed class DinicFlow
        {
            private readonly List<FlowEdge>[] _graph;
            private readonly int[] _level;
            private readonly int[] _next;

            public DinicFlow(int nodeCount)
            {
                _graph = Enumerable.Range(0, nodeCount)
                    .Select(_ => new List<FlowEdge>())
                    .ToArray();
                _level = new int[nodeCount];
                _next = new int[nodeCount];
            }

            public FlowEdge AddEdge(int from, int to, int capacity)
            {
                FlowEdge forward = new FlowEdge(to, capacity, _graph[to].Count);
                FlowEdge reverse = new FlowEdge(from, 0, _graph[from].Count);
                _graph[from].Add(forward);
                _graph[to].Add(reverse);
                return forward;
            }

            public int MaxFlow(int source, int sink, int limit = int.MaxValue)
            {
                int result = 0;
                while (result < limit && BuildLevels(source, sink))
                {
                    Array.Clear(_next, 0, _next.Length);
                    int pushed;
                    while (result < limit &&
                           (pushed = Push(source, sink, Math.Min(int.MaxValue, limit - result))) > 0)
                        result += pushed;
                }

                return result;
            }

            private bool BuildLevels(int source, int sink)
            {
                for (int i = 0; i < _level.Length; i++)
                    _level[i] = -1;
                Queue<int> queue = new Queue<int>();
                _level[source] = 0;
                queue.Enqueue(source);

                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    foreach (FlowEdge edge in _graph[node])
                    {
                        if (edge.Capacity <= 0 || _level[edge.To] >= 0)
                            continue;

                        _level[edge.To] = _level[node] + 1;
                        queue.Enqueue(edge.To);
                    }
                }

                return _level[sink] >= 0;
            }

            private int Push(int node, int sink, int pushed)
            {
                if (pushed == 0)
                    return 0;
                if (node == sink)
                    return pushed;

                for (; _next[node] < _graph[node].Count; _next[node]++)
                {
                    FlowEdge edge = _graph[node][_next[node]];
                    if (edge.Capacity <= 0 || _level[edge.To] != _level[node] + 1)
                        continue;

                    int amount = Push(edge.To, sink, Math.Min(pushed, edge.Capacity));
                    if (amount == 0)
                        continue;

                    edge.Capacity -= amount;
                    _graph[edge.To][edge.ReverseIndex].Capacity += amount;
                    return amount;
                }

                return 0;
            }
        }

        private sealed class FlowEdge
        {
            public int To { get; }
            public int ReverseIndex { get; }
            public int Capacity { get; set; }
            public int InitialCapacity { get; }

            public FlowEdge(int to, int capacity, int reverseIndex)
            {
                To = to;
                Capacity = capacity;
                InitialCapacity = capacity;
                ReverseIndex = reverseIndex;
            }
        }
    }

    public class TransferDemandResult
    {
        /// <summary>
        /// All-day Domestic departure passengers used as the denominator for
        /// the Domestic transfer share. Peak values are reported separately.
        /// </summary>
        public int DomesticDepartureTotal { get; set; }

        /// <summary>
        /// All-day International departure passengers used as the denominator
        /// for the International transfer share.
        /// </summary>
        public int InternationalDepartureTotal { get; set; }

        public int DomesticDeparturePeak { get; set; }
        public int DomesticDeparturePeakAt { get; set; }
        public int InternationalDeparturePeak { get; set; }
        public int InternationalDeparturePeakAt { get; set; }

        /// <summary>Busiest rolling 60-minute domestic transfer departure window.</summary>
        public int DomesticDepartureTransferPeak { get; set; }
        public int DomesticDepartureTransferPeakAt { get; set; }

        /// <summary>Busiest rolling 60-minute international transfer departure window.</summary>
        public int InternationalDepartureTransferPeak { get; set; }
        public int InternationalDepartureTransferPeakAt { get; set; }

        /// <summary>Busiest rolling 60-minute domestic transfer arrival window.</summary>
        public int DomesticArrivalTransferPeak { get; set; }
        public int DomesticArrivalTransferPeakAt { get; set; }

        /// <summary>Busiest rolling 60-minute international transfer arrival window.</summary>
        public int InternationalArrivalTransferPeak { get; set; }
        public int InternationalArrivalTransferPeakAt { get; set; }

        public int RequestedDomesticTransferPassengers { get; set; }
        public int RequestedInternationalTransferPassengers { get; set; }
        public int EffectiveDomesticTransferPassengers { get; set; }
        public int EffectiveInternationalTransferPassengers { get; set; }

        /// <summary>
        /// Outbound transfer passengers actually tagged in the generated
        /// population. This can be lower than the effective schedule demand
        /// when a Passenger Group/profile is missing for a flight leg.
        /// </summary>
        public int TaggedDomesticTransferPassengers { get; set; }
        public int TaggedInternationalTransferPassengers { get; set; }

        public int TaggedTransferPassengers =>
            TaggedDomesticTransferPassengers + TaggedInternationalTransferPassengers;

        public int EffectiveTransferPassengers =>
            EffectiveDomesticTransferPassengers + EffectiveInternationalTransferPassengers;

        public TransferScheduleCapacity Capacity { get; set; }
        public Dictionary<TransferScenario, TransferScenarioDemand> ByScenario { get; set; } =
            new Dictionary<TransferScenario, TransferScenarioDemand>();
        public List<TransferScheduleAssignment> ScheduleAssignments { get; set; } =
            new List<TransferScheduleAssignment>();
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Required/bypassed counts by service for the passengers actually
        /// tagged. A bypass means the scenario rule explicitly says the
        /// service is not required; it does not include passengers missing
        /// because a group/profile was unwired or capacity was unavailable.
        /// </summary>
        public TransferServiceUsageSummary ServiceUsage { get; set; } =
            new TransferServiceUsageSummary();

        /// <summary>
        /// Service counts restricted to the busiest transfer windows. Arrival
        /// services use arrival windows and departure services use departure
        /// windows. PHP/aggregate populations are treated as one peak cohort.
        /// </summary>
        public TransferServiceUsageSummary PeakServiceUsage { get; set; } =
            new TransferServiceUsageSummary();
    }

    public class TransferScenarioDemand
    {
        public TransferScenario Scenario { get; set; }
        public int RequestedPassengers { get; set; }
        public int FeasibleMaximum { get; set; }
        public int EffectivePassengers { get; set; }

        /// <summary>
        /// Outbound passengers actually tagged for this scenario. A shortfall
        /// against <see cref="EffectivePassengers"/> means generated cohorts
        /// were missing, not that the scenario rule bypassed a service.
        /// </summary>
        public int TaggedPassengers { get; set; }
    }

    public class TransferScheduleCapacity
    {
        public int DomesticOutbound { get; set; }
        public int InternationalOutbound { get; set; }
        public int DomesticInbound { get; set; }
        public int InternationalInbound { get; set; }
        public int Total { get; set; }
        public int EligibleArrivalPax { get; set; }
        public int EligibleDeparturePax { get; set; }
        public Dictionary<TransferScenario, int> ByScenario { get; set; } =
            new Dictionary<TransferScenario, int>();

        public double DomesticDeparturePeakShareCap(int domesticDeparturePeak)
        {
            if (domesticDeparturePeak <= 0)
                return 0.0;

            return Math.Min(100.0, DomesticOutbound * 100.0 / domesticDeparturePeak);
        }

        public double InternationalDeparturePeakShareCap(int internationalDeparturePeak)
        {
            if (internationalDeparturePeak <= 0)
                return 0.0;

            return Math.Min(100.0, InternationalOutbound * 100.0 / internationalDeparturePeak);
        }

        /// <summary>
        /// Maximum Domestic transfer share of the all-day Domestic departure
        /// market that the schedule can support.
        /// </summary>
        public double DomesticDepartureShareCap(int domesticDepartureTotal)
        {
            if (domesticDepartureTotal <= 0)
                return 0.0;

            return Math.Min(100.0, DomesticOutbound * 100.0 / domesticDepartureTotal);
        }

        /// <summary>
        /// Maximum International transfer share of the all-day International
        /// departure market that the schedule can support.
        /// </summary>
        public double InternationalDepartureShareCap(int internationalDepartureTotal)
        {
            if (internationalDepartureTotal <= 0)
                return 0.0;

            return Math.Min(100.0, InternationalOutbound * 100.0 / internationalDepartureTotal);
        }
    }

    public class TransferScheduleAssignment
    {
        public TransferScenario Scenario { get; set; }
        public FlightLeg Arrival { get; set; }
        public FlightLeg Departure { get; set; }
        public int PassengerCount { get; set; }
    }
}
