using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Walking;

namespace AirportPlanning.Core.Generation
{
    /// <summary>
    /// Generates passengers from a normalized design-day flight schedule.
    ///
    /// Departure passengers are placed backwards from each leg's actual STD.
    /// Each Passenger Profile supplies its own Min/Max advance interval and,
    /// optionally, its own ShowUpProfile; the selection is drawn through that
    /// curve and stretched over the interval. Arrival passengers are placed
    /// forwards from STA using a separate, explicit arrival-process delay.
    ///
    /// The queue simulator is untouched: the output is the existing Passenger
    /// population with additional flight and check-in diagnostics.
    /// </summary>
    public class SchedulePassengerGenerator
    {
        public SchedulePassengerGenerationResult Generate(
            FlightScheduleResult schedule,
            IList<PassengerGroup> groups,
            WalkingProfile walkingProfile,
            int seed,
            PassengerArrivalBasis arrivalBasis = PassengerArrivalBasis.TerminalEntry,
            bool applyEntryWalk = false,
            double entryWalkDistanceMeters = 0.0,
            CheckInWindowRules checkInWindows = null,
            ArrivalProcessDelay arrivalDelay = null,
            ShowUpProfile fallbackShowUp = null,
            IChannelAssigner channelAssigner = null,
            int designHourIndex = -1,
            Action<string> warn = null)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            if (walkingProfile == null)
                throw new ArgumentNullException(nameof(walkingProfile));

            if (entryWalkDistanceMeters < 0.0 ||
                double.IsNaN(entryWalkDistanceMeters) ||
                double.IsInfinity(entryWalkDistanceMeters))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entryWalkDistanceMeters),
                    "Entry walking distance must be a non-negative number of metres.");
            }

            checkInWindows = checkInWindows ?? CheckInWindowRules.CreateDefault();
            arrivalDelay = arrivalDelay ?? new ArrivalProcessDelay();

            List<string> checkInErrors = checkInWindows.GetValidationErrors();

            if (checkInErrors.Count > 0)
                throw new ArgumentException(string.Join(" ", checkInErrors), nameof(checkInWindows));

            List<string> delayErrors = arrivalDelay.GetValidationErrors();

            if (delayErrors.Count > 0)
                throw new ArgumentException(string.Join(" ", delayErrors), nameof(arrivalDelay));

            Dictionary<DHPType, PassengerGroup> groupsByType =
                BuildGroupMap(groups);

            SchedulePassengerGenerationResult result =
                new SchedulePassengerGenerationResult();

            HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Random random = new Random(seed);
            Random channelRandom = channelAssigner != null
                ? new Random(unchecked(seed * 31 + 7))
                : null;

            int nextId = 1;

            if (schedule.Legs != null)
            {
                foreach (FlightLeg leg in schedule.Legs)
                {
                    result.ScheduleLegCount++;

                    if (leg == null || leg.ServiceType != LegServiceType.Passenger)
                        continue;

                    result.PassengerLegCount++;

                    if (!leg.HasTime)
                    {
                        AddWarning(result, warned, warn,
                            $"Row {leg.SourceRow} ({leg.Direction}): no usable schedule time; leg skipped.");
                        continue;
                    }

                    if (!TryGetDHPType(leg, out DHPType type))
                    {
                        AddWarning(result, warned, warn,
                            $"Row {leg.SourceRow} ({leg.Direction}): sector is unknown; leg skipped.");
                        continue;
                    }

                    int passengerCount = ResolvePassengerCount(leg);

                    if (passengerCount <= 0)
                    {
                        if (passengerCount < 0)
                        {
                            AddWarning(result, warned, warn,
                                $"Row {leg.SourceRow} ({leg.Direction}): negative passenger count; leg skipped.");
                        }

                        continue;
                    }

                    ScheduleFlightDemand flightDemand = new ScheduleFlightDemand
                    {
                        Leg = leg,
                        Type = type,
                        PassengerCount = passengerCount
                    };

                    result.FlightDemands.Add(flightDemand);

                    if (!groupsByType.TryGetValue(type, out PassengerGroup group) ||
                        group == null ||
                        group.Profiles == null ||
                        group.Profiles.Count == 0)
                    {
                        flightDemand.Issue = "No Passenger Group/profile supplied for this type.";

                        if (leg.Direction == LegDirection.Departure)
                            result.DepartureLegsWithoutGroup++;
                        else
                            result.ArrivalLegsWithoutGroup++;

                        AddWarning(result, warned, warn,
                            $"{type}: no Passenger Group or profiles supplied; " +
                            $"{passengerCount} passengers on row {leg.SourceRow} were skipped.");
                        continue;
                    }

                    List<string> profileErrors = ValidateProfiles(group.Profiles);

                    if (profileErrors.Count > 0)
                    {
                        throw new ArgumentException(
                            $"{type}: {string.Join(" ", profileErrors)}", nameof(groups));
                    }

                    List<ProfileAllocation> allocations =
                        AllocatePassengers(passengerCount, group.Profiles);

                    int before = result.Passengers.Count;

                    if (leg.Direction == LegDirection.Departure)
                    {
                        GenerateDepartureLeg(
                            leg, type, allocations, arrivalBasis, applyEntryWalk,
                            entryWalkDistanceMeters, checkInWindows, fallbackShowUp,
                            walkingProfile, channelAssigner, channelRandom, random,
                            result, ref nextId);
                    }
                    else
                    {
                        GenerateArrivalLeg(
                            leg, type, allocations, arrivalDelay,
                            walkingProfile, channelAssigner, channelRandom, random,
                            result, ref nextId);
                    }

                    flightDemand.GeneratedCount = result.Passengers.Count - before;

                    if (flightDemand.GeneratedCount > 0)
                        result.FlightsWithPassengers++;
                }
            }

            FinishResult(result, schedule, designHourIndex);

            if (warn != null)
            {
                foreach (string message in result.GenerationWarnings)
                    warn(message);
            }

            return result;
        }

        private void GenerateDepartureLeg(
            FlightLeg leg,
            DHPType type,
            List<ProfileAllocation> allocations,
            PassengerArrivalBasis arrivalBasis,
            bool applyEntryWalk,
            double entryWalkDistanceMeters,
            CheckInWindowRules checkInWindows,
            ShowUpProfile fallbackShowUp,
            WalkingProfile walkingProfile,
            IChannelAssigner channelAssigner,
            Random channelRandom,
            Random random,
            SchedulePassengerGenerationResult result,
            ref int nextId)
        {
            double std = leg.TotalMinutesFromMidnight;
            CheckInWindow window = checkInWindows.Resolve(leg);

            foreach (ProfileAllocation allocation in allocations)
            {
                if (allocation.Count <= 0)
                    continue;

                PassengerProfile profile = allocation.Profile;

                double minAdvance = profile.MinAdvanceMinutes;
                double maxAdvance = profile.MaxAdvanceMinutes;

                if (maxAdvance < minAdvance)
                {
                    double swap = minAdvance;
                    minAdvance = maxAdvance;
                    maxAdvance = swap;
                }

                double earliest = std - maxAdvance;
                double latest = std - minAdvance;
                double interval = latest - earliest;

                ShowUpProfile showUp =
                    profile.DepartureShowUp ?? fallbackShowUp ?? ShowUpProfile.Mix();

                string signature = showUp.GetSignature();

                for (int i = 0; i < allocation.Count; i++)
                {
                    double fraction = interval <= 0.0
                        ? 1.0
                        : showUp.SampleNormalizedElapsed(random);

                    double terminalEntryTime = earliest + interval * fraction;
                    double facilityTime = terminalEntryTime;
                    double entryWalkMinutes = 0.0;

                    Passenger passenger = PassengerFactory.Create(
                        nextId++,
                        profile,
                        walkingProfile,
                        facilityTime,
                        random,
                        channelAssigner,
                        channelRandom,
                        type);

                    if (arrivalBasis == PassengerArrivalBasis.TerminalEntry &&
                        applyEntryWalk &&
                        entryWalkDistanceMeters > 0.0)
                    {
                        entryWalkMinutes = entryWalkDistanceMeters / passenger.WalkingSpeed / 60.0;
                        passenger.WalkingTime = entryWalkMinutes;
                        facilityTime += entryWalkMinutes;
                        passenger.ArrivalTime = facilityTime;
                    }

                    passenger.TerminalEntryArrivalTime =
                        arrivalBasis == PassengerArrivalBasis.TerminalEntry
                            ? terminalEntryTime
                            : double.NaN;
                    passenger.TerminalEntryWalkMinutes = entryWalkMinutes;

                    bool checkInTimeKnown =
                        arrivalBasis == PassengerArrivalBasis.CheckInQueue ||
                        (arrivalBasis == PassengerArrivalBasis.TerminalEntry && applyEntryWalk);

                    passenger.QueueArrivalTime = facilityTime;
                    passenger.PassengerType = IsDomestic(type)
                        ? PassengerType.Domestic
                        : PassengerType.International;
                    passenger.DHPType = type;
                    passenger.ScheduledDepartureTime = std;
                    passenger.FlightLegId = leg.Id ?? string.Empty;
                    passenger.FlightLinkId = leg.LinkId ?? string.Empty;
                    passenger.Airline = leg.Airline ?? string.Empty;
                    passenger.FlightNumber = leg.FlightNumber ?? string.Empty;
                    passenger.CheckInOpenTime = window.OpensAt;
                    passenger.CheckInLastAdmissionTime = window.LastAdmissionAt;
                    passenger.ShowUpSignature = signature;
                    passenger.IsWarmUp = false;
                    passenger.WaveId = -1;

                    if (checkInTimeKnown)
                    {
                        passenger.ArrivedBeforeCheckInOpen = facilityTime < window.OpensAt;
                        passenger.IsLateForCheckIn = facilityTime > window.LastAdmissionAt;
                    }

                    result.Passengers.Add(passenger);
                }
            }
        }

        private void GenerateArrivalLeg(
            FlightLeg leg,
            DHPType type,
            List<ProfileAllocation> allocations,
            ArrivalProcessDelay arrivalDelay,
            WalkingProfile walkingProfile,
            IChannelAssigner channelAssigner,
            Random channelRandom,
            Random random,
            SchedulePassengerGenerationResult result,
            ref int nextId)
        {
            double sta = leg.TotalMinutesFromMidnight;

            foreach (ProfileAllocation allocation in allocations)
            {
                if (allocation.Count <= 0)
                    continue;

                PassengerProfile profile = allocation.Profile;

                for (int i = 0; i < allocation.Count; i++)
                {
                    double processTime = sta + arrivalDelay.Sample(random);

                    Passenger passenger = PassengerFactory.Create(
                        nextId++,
                        profile,
                        walkingProfile,
                        processTime,
                        random,
                        channelAssigner,
                        channelRandom,
                        type);

                    passenger.QueueArrivalTime = processTime;
                    passenger.PassengerType = IsDomestic(type)
                        ? PassengerType.Domestic
                        : PassengerType.International;
                    passenger.DHPType = type;
                    passenger.ScheduledArrivalTime = sta;
                    passenger.FlightLegId = leg.Id ?? string.Empty;
                    passenger.FlightLinkId = leg.LinkId ?? string.Empty;
                    passenger.Airline = leg.Airline ?? string.Empty;
                    passenger.FlightNumber = leg.FlightNumber ?? string.Empty;
                    passenger.ShowUpSignature =
                        $"arrival-process|{arrivalDelay.MinimumMinutes:R}|{arrivalDelay.MaximumMinutes:R}";
                    passenger.IsWarmUp = false;
                    passenger.WaveId = -1;

                    result.Passengers.Add(passenger);
                }
            }
        }

        private static Dictionary<DHPType, PassengerGroup> BuildGroupMap(
            IList<PassengerGroup> groups)
        {
            Dictionary<DHPType, PassengerGroup> map =
                new Dictionary<DHPType, PassengerGroup>();

            if (groups == null)
                return map;

            foreach (PassengerGroup group in groups)
            {
                if (group == null)
                    continue;

                map[group.ResolvedType] = group;
            }

            return map;
        }

        private static List<string> ValidateProfiles(List<PassengerProfile> profiles)
        {
            List<string> errors = new List<string>();

            double total = 0.0;

            for (int i = 0; i < profiles.Count; i++)
            {
                PassengerProfile profile = profiles[i];

                if (profile == null)
                {
                    errors.Add($"Profile {i + 1} is empty.");
                    continue;
                }

                if (profile.Percentage < 0.0)
                    errors.Add($"Profile '{profile.Name}' has a negative percentage.");

                total += profile.Percentage;
            }

            if (Math.Abs(total - 100.0) > 0.001)
                errors.Add($"Profile percentages must sum to 100% (got {total:0.###}%).");

            return errors;
        }

        private static List<ProfileAllocation> AllocatePassengers(
            int totalPassengers,
            List<PassengerProfile> profiles)
        {
            var allocations = new List<ProfileAllocation>();
            int remaining = totalPassengers;

            for (int i = 0; i < profiles.Count; i++)
            {
                int count;

                if (i == profiles.Count - 1)
                {
                    count = remaining;
                }
                else
                {
                    count = Math.Min(
                        remaining,
                        (int)Math.Round(
                            totalPassengers * profiles[i].Percentage / 100.0,
                            MidpointRounding.AwayFromZero));
                }

                count = Math.Max(0, count);
                remaining -= count;

                allocations.Add(new ProfileAllocation
                {
                    Profile = profiles[i],
                    Count = count
                });
            }

            return allocations;
        }

        private static int ResolvePassengerCount(FlightLeg leg)
        {
            if (leg.PaxRaw.HasValue)
                return leg.PaxRaw.Value;

            if (leg.SeatsRaw.HasValue && leg.LoadFactor.HasValue)
            {
                return (int)Math.Round(
                    leg.SeatsRaw.Value * leg.LoadFactor.Value,
                    MidpointRounding.AwayFromZero);
            }

            return 0;
        }

        private static bool TryGetDHPType(FlightLeg leg, out DHPType type)
        {
            if (leg.SectorRaw == LegSector.Unknown)
            {
                type = default(DHPType);
                return false;
            }

            bool domestic = leg.SectorRaw == LegSector.Domestic;

            if (leg.Direction == LegDirection.Departure)
                type = domestic ? DHPType.DomesticDeparture : DHPType.InternationalDeparture;
            else
                type = domestic ? DHPType.DomesticArrival : DHPType.InternationalArrival;

            return true;
        }

        private static bool IsDomestic(DHPType type)
        {
            return type == DHPType.DomesticDeparture ||
                   type == DHPType.DomesticArrival;
        }

        private static void FinishResult(
            SchedulePassengerGenerationResult result,
            FlightScheduleResult schedule,
            int requestedDesignHour)
        {
            result.Passengers = result.Passengers
                .OrderBy(p => p.ArrivalTime)
                .ThenBy(p => p.Id)
                .ToList();

            foreach (Passenger passenger in result.Passengers)
            {
                passenger.DemandStepIndex = HourOf(passenger.ArrivalTime);
                passenger.DemandHourIndex = passenger.DemandStepIndex;
            }

            result.DemandByHour = result.Passengers
                .GroupBy(p => p.DemandStepIndex)
                .OrderBy(group => group.Key)
                .Select(group => new ScheduleHourDemand
                {
                    Hour = group.Key,
                    Count = group.Count(),
                    ByType = group
                        .GroupBy(p => p.DHPType)
                        .ToDictionary(g => g.Key, g => g.Count())
                })
                .ToList();

            // Two rolling views, each 60 minutes wide at 5-minute steps:
            // raw flight-time demand and profile-adjusted passenger arrivals.
            result.ScheduleRolling = BuildScheduleSeries(schedule);
            result.PassengerRolling = BuildPassengerSeries(result.Passengers);

            ApplySchedulePeaks(result, result.ScheduleRolling);

            if (result.DemandByHour.Count > 0)
                result.PeakHourDemand = result.DemandByHour.Max(h => h.Count);

            // The default design hour comes from the schedule's combined peak
            // window. Fall back to the busiest generated hour when the schedule
            // has no usable movements.
            if (requestedDesignHour >= 0)
            {
                result.DesignStepIndex = requestedDesignHour;
            }
            else if (result.CombinedPeak > 0)
            {
                result.DesignStepIndex = HourOf(result.CombinedPeakAt);
            }
            else if (result.DemandByHour.Count > 0)
            {
                result.DesignStepIndex = result.DemandByHour
                    .OrderByDescending(h => h.Count)
                    .ThenBy(h => h.Hour)
                    .First()
                    .Hour;
            }
            else
            {
                result.DesignStepIndex = -1;
            }

            foreach (Passenger passenger in result.Passengers)
            {
                passenger.IsDesignStep =
                    result.DesignStepIndex >= 0 &&
                    passenger.DemandStepIndex == result.DesignStepIndex;

                if (passenger.IsLateForCheckIn)
                    result.LateCheckInCount++;

                if (passenger.ArrivedBeforeCheckInOpen)
                    result.BeforeCheckInOpenCount++;
            }

            foreach (DHPType type in Enum.GetValues(typeof(DHPType)))
            {
                result.DemandByType[type] =
                    result.Passengers.Count(p => p.DHPType == type);
            }

            result.GeneratedPassengerCount = result.Passengers.Count;
            result.ArrivalSimulations = BuildArrivalSimulations(
                result.Passengers,
                result.DesignStepIndex);
        }

        private static List<ScheduleRollingSeries> BuildRollingSeries(
            IEnumerable<(double Time, DHPType Type, int Count)> events)
        {
            const int bucketMinutes = 5;
            const int width = 60 / bucketMinutes;

            var buckets = new Dictionary<DHPType, Dictionary<int, int>>();

            foreach (DHPType type in Enum.GetValues(typeof(DHPType)))
                buckets[type] = new Dictionary<int, int>();

            int minBucket = int.MaxValue;
            int maxBucket = int.MinValue;

            foreach (var item in events)
            {
                if (item.Count <= 0)
                    continue;

                int bucket = (int)Math.Floor(item.Time / bucketMinutes);

                buckets[item.Type][bucket] =
                    GetOrZero(buckets[item.Type], bucket) + item.Count;

                minBucket = Math.Min(minBucket, bucket);
                maxBucket = Math.Max(maxBucket, bucket);
            }

            var series = new List<ScheduleRollingSeries>();

            if (minBucket > maxBucket)
                return series;

            // A rolling window that starts up to 59 minutes before the first
            // event still contains it, so the series starts that far back.
            int seriesStart = minBucket - width + 1;

            for (int i = seriesStart; i <= maxBucket; i++)
            {
                series.Add(new ScheduleRollingSeries
                {
                    Minute = i * bucketMinutes,
                    DomesticDeparture = SumWindow(buckets[DHPType.DomesticDeparture], i, width),
                    DomesticArrival = SumWindow(buckets[DHPType.DomesticArrival], i, width),
                    InternationalDeparture = SumWindow(buckets[DHPType.InternationalDeparture], i, width),
                    InternationalArrival = SumWindow(buckets[DHPType.InternationalArrival], i, width)
                });
            }

            return series;
        }

        private static int SumWindow(Dictionary<int, int> buckets, int start, int width)
        {
            int sum = 0;

            for (int k = 0; k < width; k++)
                sum += GetOrZero(buckets, start + k);

            return sum;
        }

        private static int GetOrZero(Dictionary<int, int> values, int key)
        {
            return values.TryGetValue(key, out int value) ? value : 0;
        }

        private static void ApplySchedulePeaks(
            SchedulePassengerGenerationResult result,
            List<ScheduleRollingSeries> series)
        {
            if (series == null || series.Count == 0)
                return;

            result.DomesticDeparturePeak = series.Max(s => s.DomesticDeparture);
            result.DomesticDeparturePeakAt = series
                .OrderByDescending(s => s.DomesticDeparture)
                .First().Minute;

            result.DomesticArrivalPeak = series.Max(s => s.DomesticArrival);
            result.DomesticArrivalPeakAt = series
                .OrderByDescending(s => s.DomesticArrival)
                .First().Minute;

            result.InternationalDeparturePeak = series.Max(s => s.InternationalDeparture);
            result.InternationalDeparturePeakAt = series
                .OrderByDescending(s => s.InternationalDeparture)
                .First().Minute;

            result.InternationalArrivalPeak = series.Max(s => s.InternationalArrival);
            result.InternationalArrivalPeakAt = series
                .OrderByDescending(s => s.InternationalArrival)
                .First().Minute;

            result.CombinedPeak = series.Max(s => s.Combined);
            result.CombinedPeakAt = series
                .OrderByDescending(s => s.Combined)
                .First().Minute;
        }

        private static List<ScheduleRollingSeries> BuildScheduleSeries(
            FlightScheduleResult schedule)
        {
            var events = new List<(double Time, DHPType Type, int Count)>();

            if (schedule.Legs != null)
            {
                foreach (FlightLeg leg in schedule.Legs)
                {
                    if (leg == null || leg.ServiceType != LegServiceType.Passenger)
                        continue;

                    if (!leg.HasTime)
                        continue;

                    if (!TryGetDHPType(leg, out DHPType type))
                        continue;

                    int pax = ResolvePassengerCount(leg);

                    if (pax > 0)
                        events.Add((leg.TotalMinutesFromMidnight, type, pax));
                }
            }

            return BuildRollingSeries(events);
        }

        private static List<ScheduleRollingSeries> BuildPassengerSeries(
            List<Passenger> passengers)
        {
            return BuildRollingSeries(
                passengers.Select(p => (p.ArrivalTime, p.DHPType, 1)));
        }

        private static int HourOf(double minute)
        {
            return (int)Math.Floor(minute / 60.0);
        }

        private static List<ArrivalSimulationResult> BuildArrivalSimulations(
            List<Passenger> passengers,
            int designStep)
        {
            var simulations = new List<ArrivalSimulationResult>();

            foreach (IGrouping<DHPType, Passenger> typeGroup in passengers.GroupBy(p => p.DHPType))
            {
                foreach (IGrouping<int, Passenger> hourGroup in
                    typeGroup.GroupBy(p => HourOf(p.ArrivalTime)).OrderBy(g => g.Key))
                {
                    ArrivalSimulationResult simulation = new ArrivalSimulationResult
                    {
                        StepStartMinute = hourGroup.Key * 60.0,
                        StepMinutes = 60,
                        StepIndex = hourGroup.Key,
                        IsDesignStep = hourGroup.Key == designStep,
                        IsWarmUp = false,
                        Label = $"{typeGroup.Key} - {SchedulePassengerGenerationResult.FormatHour(hourGroup.Key)}"
                    };

                    int[] histogram = new int[60];

                    foreach (Passenger passenger in hourGroup)
                    {
                        int minute = (int)Math.Floor(passenger.ArrivalTime);
                        simulation.ArrivalMinutes.Add(minute);

                        int bucket = minute - hourGroup.Key * 60;

                        if (bucket >= 0 && bucket < histogram.Length)
                            histogram[bucket]++;
                    }

                    simulation.Histogram.AddRange(histogram);
                    simulations.Add(simulation);
                }
            }

            return simulations;
        }

        private static void AddWarning(
            SchedulePassengerGenerationResult result,
            HashSet<string> warned,
            Action<string> warn,
            string message)
        {
            if (warned.Add(message))
                result.GenerationWarnings.Add(message);
        }

        private class ProfileAllocation
        {
            public PassengerProfile Profile { get; set; }

            public int Count { get; set; }
        }
    }
}
