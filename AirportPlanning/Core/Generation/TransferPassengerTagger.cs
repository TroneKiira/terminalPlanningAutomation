using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Generation
{
    /// <summary>Assigns aggregate transfer assumptions to generated passengers.</summary>
    public static class TransferPassengerTagger
    {
        public static TransferDemandResult ApplyAggregate(
            IList<Passenger> passengers,
            TransferDemandProfile profile,
            IChannelAssigner channelAssigner = null)
        {
            if (profile == null)
                return null;

            List<Passenger> population = passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null).ToList();

            ResetDepartureClassification(population);

            int domestic = population.Count(p =>
                p.DHPType == DHPType.DomesticDeparture);
            int international = population.Count(p =>
                p.DHPType == DHPType.InternationalDeparture);

            TransferDemandResult result = TransferDemandCalculator.CalculatePhp(
                domestic,
                international,
                profile);

            List<string> errors = profile.GetValidationErrors();
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(profile));

            TagDepartures(population, profile, result.ByScenario);
            result.EffectiveDomesticTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToDomestic].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToDomestic].EffectivePassengers;
            result.EffectiveInternationalTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToInternational].EffectivePassengers +
                result.ByScenario[TransferScenario.InternationalToInternational].EffectivePassengers;

            CaptureTaggedCounts(population, result);
            CaptureServiceUsage(population, result, scheduleBased: false);
            return result;
        }

        public static TransferDemandResult ApplySchedule(
            IList<Passenger> passengers,
            FlightScheduleResult schedule,
            TransferDemandProfile profile,
            IChannelAssigner channelAssigner = null)
        {
            if (profile == null)
                return null;

            TransferDemandResult result = TransferDemandCalculator.CalculateSchedule(schedule, profile);
            List<Passenger> population = passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null).ToList();

            ResetScheduleClassification(population);

            TagScheduleDepartures(population, profile, result);
            TagScheduleArrivals(population, schedule, profile, result);

            CaptureTaggedCounts(population, result);
            CaptureServiceUsage(population, result, scheduleBased: true);
            AddTaggingWarnings(result);
            AddUnspecifiedRuleWarning(population, result);
            return result;
        }

        /// <summary>
        /// Counts the outbound passengers that were actually tagged. Arrival
        /// records are deliberately excluded: one transfer journey produces
        /// an arrival and a departure leg, while transfer demand is reported
        /// once by outbound market.
        /// </summary>
        private static void CaptureTaggedCounts(
            List<Passenger> population,
            TransferDemandResult result)
        {
            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                int tagged = population.Count(p =>
                    IsDeparture(p) &&
                    p.Journey == PassengerJourney.Transfer &&
                    p.TransferScenario == scenario);

                if (!result.ByScenario.TryGetValue(scenario, out TransferScenarioDemand demand))
                {
                    demand = new TransferScenarioDemand { Scenario = scenario };
                    result.ByScenario[scenario] = demand;
                }

                demand.TaggedPassengers = tagged;
            }

            result.TaggedDomesticTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToDomestic].TaggedPassengers +
                result.ByScenario[TransferScenario.InternationalToDomestic].TaggedPassengers;
            result.TaggedInternationalTransferPassengers =
                result.ByScenario[TransferScenario.DomesticToInternational].TaggedPassengers +
                result.ByScenario[TransferScenario.InternationalToInternational].TaggedPassengers;
        }

        private static void CaptureServiceUsage(
            List<Passenger> population,
            TransferDemandResult result,
            bool scheduleBased)
        {
            result.ServiceUsage = TransferServiceUsageCalculator.Calculate(population);
            result.PeakServiceUsage = scheduleBased
                ? TransferServiceUsageCalculator.CalculatePeak(
                    population,
                    TransferPeakWindows.From(result))
                : TransferServiceUsageCalculator.CalculatePeak(
                    population,
                    TransferPeakWindows.AllInPeak());
        }

        /// <summary>
        /// A schedule can allocate transfer demand to legs whose generated
        /// cohort is missing because a Passenger Group/profile was not wired.
        /// That is different from a scenario rule bypassing a service, so it is
        /// reported as a tagging shortfall and never counted as a bypass.
        /// </summary>
        private static void AddTaggingWarnings(TransferDemandResult result)
        {
            int effective = result.EffectiveTransferPassengers;
            int tagged = result.TaggedTransferPassengers;

            if (tagged >= effective)
                return;

            List<string> shortfalls = result.ByScenario
                .Where(pair => pair.Value.TaggedPassengers < pair.Value.EffectivePassengers)
                .OrderBy(pair => (int)pair.Key)
                .Select(pair =>
                    $"{pair.Key.ToFlowLabel()} {pair.Value.TaggedPassengers}/" +
                    $"{pair.Value.EffectivePassengers}")
                .ToList();

            string detail = shortfalls.Count > 0
                ? " " + string.Join(", ", shortfalls) + "."
                : string.Empty;

            result.Warnings.Add(
                $"Transfer demand could only tag {tagged} of {effective} schedule-effective " +
                "outbound passengers. This usually means a Passenger Group/profile is missing " +
                "for one or more flight legs; it is not caused by scenario service rules or " +
                "facility capacity." + detail);
        }

        /// <summary>
        /// Tagged transfers without <see cref="TransferScenarioRules.HasProcessingRules"/>
        /// keep normal passenger behaviour. They are not bypasses, but the
        /// planner should know why a service report shows "without explicit
        /// transfer rules" instead of an explicit required/bypassed split.
        /// </summary>
        private static void AddUnspecifiedRuleWarning(
            List<Passenger> population,
            TransferDemandResult result)
        {
            int unspecified = population.Count(p =>
                IsDeparture(p) &&
                p.Journey == PassengerJourney.Transfer &&
                p.TransferProcessing == null);

            if (unspecified <= 0)
                return;

            result.Warnings.Add(
                $"{unspecified} tagged outbound transfer passenger(s) have no explicit processing " +
                "rules and keep normal passenger behaviour. Enable Has Processing Rules on their " +
                "scenario to make service bypasses explicit; they are reported as 'without explicit " +
                "transfer rules', not as bypasses.");
        }

        private static bool IsDeparture(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticDeparture ||
                    passenger.DHPType == DHPType.InternationalDeparture);
        }

        private static void TagDepartures(
            List<Passenger> passengers,
            TransferDemandProfile profile,
            Dictionary<TransferScenario, TransferScenarioDemand> demand)
        {
            List<Passenger> domestic = passengers
                .Where(IsDomesticDeparture)
                .OrderBy(p => p.Id)
                .ToList();
            List<Passenger> international = passengers
                .Where(IsInternationalDeparture)
                .OrderBy(p => p.Id)
                .ToList();

            TagScenarios(domestic, profile, demand, new[]
            {
                TransferScenario.DomesticToDomestic,
                TransferScenario.InternationalToDomestic
            });

            TagScenarios(international, profile, demand, new[]
            {
                TransferScenario.DomesticToInternational,
                TransferScenario.InternationalToInternational
            });
        }

        private static void TagScheduleDepartures(
            List<Passenger> passengers,
            TransferDemandProfile profile,
            TransferDemandResult demand)
        {
            Dictionary<string, int> remainingCapacity = demand.ScheduleAssignments
                .SelectMany(a => new[] { a.Departure })
                .Where(leg => leg != null && !string.IsNullOrEmpty(leg.Id))
                .Distinct()
                .ToDictionary(leg => leg.Id, leg => Math.Max(0, leg.PaxRaw ?? 0), StringComparer.Ordinal);
            Dictionary<string, int> usedPassengerSlots = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                List<TransferScheduleAssignment> scenarioAssignments = demand.ScheduleAssignments
                    .Where(a => a.Scenario == scenario && a.PassengerCount > 0)
                    .ToList();
                List<FlightLeg> legs = scenarioAssignments
                    .Select(a => a.Departure)
                    .Where(leg => leg != null)
                    .Distinct()
                    .OrderBy(leg => leg.TotalMinutesFromMidnight)
                    .ThenBy(leg => leg.Id, StringComparer.Ordinal)
                    .ToList();

                Dictionary<string, int> allocation = scenarioAssignments
                    .GroupBy(a => a.Departure.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.PassengerCount), StringComparer.Ordinal);
                TagLegPassengers(passengers, legs, allocation, remainingCapacity, usedPassengerSlots, profile, scenario);
            }
        }

        private static void TagScheduleArrivals(
            List<Passenger> passengers,
            FlightScheduleResult schedule,
            TransferDemandProfile profile,
            TransferDemandResult demand)
        {
            Dictionary<string, int> remainingCapacity = demand.ScheduleAssignments
                .Select(a => a.Arrival)
                .Where(leg => leg != null && !string.IsNullOrEmpty(leg.Id))
                .Distinct()
                .ToDictionary(leg => leg.Id, leg => Math.Max(0, leg.PaxRaw ?? 0), StringComparer.Ordinal);
            Dictionary<string, int> usedPassengerSlots = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                List<TransferScheduleAssignment> scenarioAssignments = demand.ScheduleAssignments
                    .Where(a => a.Scenario == scenario && a.PassengerCount > 0)
                    .ToList();
                List<FlightLeg> legs = scenarioAssignments
                    .Select(a => a.Arrival)
                    .Where(leg => leg != null)
                    .Distinct()
                    .OrderBy(leg => leg.TotalMinutesFromMidnight)
                    .ThenBy(leg => leg.Id, StringComparer.Ordinal)
                    .ToList();

                Dictionary<string, int> allocation = scenarioAssignments
                    .GroupBy(a => a.Arrival.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.PassengerCount), StringComparer.Ordinal);
                TagLegPassengers(passengers, legs, allocation, remainingCapacity, usedPassengerSlots, profile, scenario);
            }
        }

        private static void TagLegPassengers(
            List<Passenger> passengers,
            List<FlightLeg> legs,
            Dictionary<string, int> allocation,
            Dictionary<string, int> remainingCapacity,
            Dictionary<string, int> usedPassengerSlots,
            TransferDemandProfile profile,
            TransferScenario scenario)
        {
            foreach (FlightLeg leg in legs)
            {
                if (leg == null || string.IsNullOrEmpty(leg.Id) ||
                    !allocation.TryGetValue(leg.Id, out int count) || count <= 0)
                    continue;

                List<Passenger> legPassengers = passengers
                    .Where(p => string.Equals(p.FlightLegId, leg.Id, StringComparison.Ordinal))
                    .OrderBy(p => p.Id)
                    .Skip(usedPassengerSlots.TryGetValue(leg.Id, out int used) ? used : 0)
                    .Take(count)
                    .ToList();

                foreach (Passenger passenger in legPassengers)
                    ApplyScenario(passenger, profile, scenario);

                int assigned = legPassengers.Count;
                usedPassengerSlots[leg.Id] =
                    (usedPassengerSlots.TryGetValue(leg.Id, out int prior) ? prior : 0) + assigned;
                remainingCapacity[leg.Id] =
                    Math.Max(0, remainingCapacity[leg.Id] - assigned);
            }
        }

        private static void TagScenarios(
            List<Passenger> marketPassengers,
            TransferDemandProfile profile,
            Dictionary<TransferScenario, TransferScenarioDemand> demand,
            IEnumerable<TransferScenario> scenarios)
        {
            int cursor = 0;
            foreach (TransferScenario scenario in scenarios)
            {
                int count = demand.TryGetValue(scenario, out TransferScenarioDemand entry)
                    ? entry.EffectivePassengers
                    : 0;

                for (int i = 0; i < count && cursor < marketPassengers.Count; i++, cursor++)
                    ApplyScenario(marketPassengers[cursor], profile, scenario);
            }
        }

        private static void ApplyScenario(
            Passenger passenger,
            TransferDemandProfile profile,
            TransferScenario scenario)
        {
            passenger.Journey = PassengerJourney.Transfer;
            passenger.TransferScenario = scenario;
            TransferScenarioRules rules = profile.RulesFor(scenario);
            passenger.TransferProcessing = rules.HasProcessingRules
                ? rules.Clone()
                : null;
        }

        private static bool IsDomesticDeparture(Passenger passenger)
        {
            return passenger.DHPType == DHPType.DomesticDeparture;
        }

        private static bool IsInternationalDeparture(Passenger passenger)
        {
            return passenger.DHPType == DHPType.InternationalDeparture;
        }

        private static void ResetDepartureClassification(List<Passenger> passengers)
        {
            foreach (Passenger passenger in passengers.Where(p =>
                p.DHPType == DHPType.DomesticDeparture ||
                p.DHPType == DHPType.InternationalDeparture))
            {
                passenger.Journey = PassengerJourney.Origin;
                passenger.TransferScenario = null;
                passenger.TransferProcessing = null;
            }
        }

        private static void ResetScheduleClassification(List<Passenger> passengers)
        {
            foreach (Passenger passenger in passengers)
            {
                if (passenger.DHPType == DHPType.DomesticDeparture ||
                    passenger.DHPType == DHPType.InternationalDeparture)
                    passenger.Journey = PassengerJourney.Origin;
                else if (passenger.DHPType == DHPType.DomesticArrival ||
                         passenger.DHPType == DHPType.InternationalArrival)
                    passenger.Journey = PassengerJourney.Destination;

                passenger.TransferScenario = null;
                passenger.TransferProcessing = null;
            }
        }
    }
}
