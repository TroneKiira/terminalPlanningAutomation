using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Generation
{
    public class MultiIntervalPassengerGenerator
    {
        public PassengerGenerationResult Generate(
            List<PassengerGroup> groups,
            WalkingProfile walkingProfile,
            WaveSettings waves,
            int seed,
            ShowUpProfile showUpProfile = null,
            IChannelAssigner channelAssigner = null,
            Action<string> warn = null,
            CheckInWindowRules checkInWindows = null,
            ArrivalProcessDelay arrivalDelay = null,
            TransferDemandProfile transferDemand = null)
        {
            if (groups == null || groups.Count == 0)
                throw new ArgumentNullException(nameof(groups));

            if (walkingProfile == null)
                throw new ArgumentNullException(nameof(walkingProfile));

            // DA / IA are optional upstream, so null slots never reach the
            // generator core: drop them here and require at least one group.
            List<PassengerGroup> active = groups.Where(g => g != null).ToList();

            if (active.Count == 0)
                throw new ArgumentException(
                    "At least one passenger group is required.", nameof(groups));

            foreach (PassengerGroup group in active)
            {
                if (group.DHP == null)
                    throw new ArgumentException(
                        "Every supplied passenger group needs a DHP.", nameof(groups));
            }

            // All supplied DHPs share one analysis window: same basis, step
            // count, design-hour index and warm-up timing. Demand levels and
            // curves may differ per type.
            ValidateSharedWindow(active);

            DemandTimeBasis demandBasis = active[0].DHP.DemandBasis;

            checkInWindows = checkInWindows ?? CheckInWindowRules.CreateDefault();
            arrivalDelay = arrivalDelay ?? new ArrivalProcessDelay();

            List<string> checkInErrors = checkInWindows.GetValidationErrors();
            List<string> delayErrors = arrivalDelay.GetValidationErrors();

            if (checkInErrors.Count > 0)
                throw new ArgumentException(
                    string.Join(" ", checkInErrors), nameof(checkInWindows));

            if (delayErrors.Count > 0)
                throw new ArgumentException(
                    string.Join(" ", delayErrors), nameof(arrivalDelay));

            waves = waves ?? new WaveSettings();

            string showUpSignature = showUpProfile != null
                ? showUpProfile.GetSignature()
                : "none";

            List<string> warnings = new List<string>();

            if (demandBasis == DemandTimeBasis.FlightHour &&
                checkInWindows.Overrides != null &&
                checkInWindows.Overrides.Count > 0)
            {
                warnings.Add(
                    "Airline/flight check-in overrides are not applied to flight-hour " +
                    "demand because the sampled flights carry no airline or flight number. " +
                    "The sector-default check-in windows are used.");
            }

            Random rnd = new Random(seed);

            Random channelRnd = channelAssigner != null
                ? new Random(unchecked(seed * 31 + 7))
                : null;

            PassengerGenerationResult result = new PassengerGenerationResult();
            List<Passenger> passengers = new List<Passenger>();

            int nextId = 1;
            int waveCounter = 1;

            int analysisPeriodHours = active[0].DHP.StepCount;
            int designHourIndex = active[0].DHP.DesignHourIndex;

            bool warmUpEnabled =
                demandBasis == DemandTimeBasis.TerminalArrival &&
                active[0].DHP.WarmUpEnabled;

            int warmUpMinutes = active[0].DHP.WarmUpMinutes;

            // Flight-hour demand is placed relative to its STD/STA, so the
            // run naturally starts before the first demand step. There is no
            // synthetic warm-up segment in that basis.
            double windowStart =
                (warmUpEnabled && warmUpMinutes > 0) ? warmUpMinutes : 0.0;

            // ---------------------------------------------------------
            // Warm-up segment (legacy terminal-arrival basis only)
            // ---------------------------------------------------------
            if (windowStart > 0.0)
            {
                foreach (PassengerGroup group in active)
                {
                    int warmTotal = (int)Math.Round(group.DHP.GetWarmUpDemand());

                    if (warmTotal <= 0)
                        continue;

                    GenerateGroup(
                        warmTotal, group, 0.0, warmUpMinutes, -1, -1,
                        true, false,
                        waves, showUpProfile, showUpSignature, walkingProfile, rnd, channelAssigner, channelRnd,
                        passengers, result.ArrivalSimulations,
                        ref nextId, ref waveCounter, warnings);
                }
            }

            // ---------------------------------------------------------
            // Analysis window steps
            // ---------------------------------------------------------
            for (int step = 0; step < analysisPeriodHours; step++)
            {
                double stepStartAbs = windowStart + step * 60.0;
                int hourIndex = step;

                foreach (PassengerGroup group in active)
                {
                    int stepTotal = (int)Math.Round(group.DHP.GetStepDemand(step));

                    if (stepTotal <= 0)
                        continue;

                    if (demandBasis == DemandTimeBasis.FlightHour)
                    {
                        GenerateFlightGroup(
                            stepTotal, group, stepStartAbs, 60, step,
                            step == designHourIndex,
                            showUpProfile, walkingProfile,
                            checkInWindows, arrivalDelay,
                            rnd, channelAssigner, channelRnd,
                            passengers, result.ArrivalSimulations,
                            ref nextId);
                    }
                    else
                    {
                        GenerateGroup(
                            stepTotal, group, stepStartAbs, 60, step, hourIndex,
                            false, step == designHourIndex,
                            waves, showUpProfile, showUpSignature, walkingProfile, rnd, channelAssigner, channelRnd,
                            passengers, result.ArrivalSimulations,
                            ref nextId, ref waveCounter, warnings);
                    }
                }
            }

            if (warn != null)
            {
                foreach (string message in warnings.Distinct())
                    warn(message);
            }

            result.Passengers = passengers.OrderBy(p => p.ArrivalTime).ToList();

            result.TransferDemand = TransferPassengerTagger.ApplyAggregate(
                result.Passengers,
                transferDemand,
                channelAssigner);

            foreach (Passenger passenger in result.Passengers)
                passenger.QueueArrivalTime = passenger.ArrivalTime;

            return result;
        }

        /// <summary>
        /// Every supplied group must share one analysis window: same demand
        /// basis, step count, design-hour index and warm-up timing. Demand
        /// levels and demand-factor curves may differ per type.
        /// </summary>
        private static void ValidateSharedWindow(List<PassengerGroup> active)
        {
            DHP reference = active[0].DHP;

            for (int i = 1; i < active.Count; i++)
            {
                DHP candidate = active[i].DHP;
                string label = candidate.Type.ToString();

                if (candidate.DemandBasis != reference.DemandBasis)
                    throw new ArgumentException(
                        $"Passenger group '{label}' uses demand basis " +
                        $"{candidate.DemandBasis} but '{reference.Type}' uses " +
                        $"{reference.DemandBasis}. All supplied groups must share " +
                        $"the same demand basis.");

                if (candidate.StepCount != reference.StepCount)
                    throw new ArgumentException(
                        $"Passenger group '{label}' has {candidate.StepCount} analysis steps " +
                        $"but '{reference.Type}' has {reference.StepCount}. All supplied groups " +
                        $"must share the same analysis window.");

                if (candidate.DesignHourIndex != reference.DesignHourIndex)
                    throw new ArgumentException(
                        $"Passenger group '{label}' uses design-hour index {candidate.DesignHourIndex} " +
                        $"but '{reference.Type}' uses {reference.DesignHourIndex}. All supplied groups " +
                        $"must share the same design hour.");

                if (reference.DemandBasis == DemandTimeBasis.TerminalArrival &&
                    (candidate.WarmUpEnabled != reference.WarmUpEnabled ||
                     candidate.WarmUpMinutes != reference.WarmUpMinutes))
                    throw new ArgumentException(
                        $"Passenger group '{label}' has different warm-up timing " +
                        $"(enabled={candidate.WarmUpEnabled}, minutes={candidate.WarmUpMinutes}) " +
                        $"than '{reference.Type}' (enabled={reference.WarmUpEnabled}, " +
                        $"minutes={reference.WarmUpMinutes}). All supplied groups must share " +
                        $"the same warm-up timing.");
            }
        }

        private void GenerateGroup(
            int count,
            PassengerGroup group,
            double stepStartAbs,
            int stepMinutes,
            int stepIndex,
            int hourIndex,
            bool isWarmUp,
            bool isDesignStep,
            WaveSettings waves,
            ShowUpProfile showUpProfile,
            string showUpSignature,
            WalkingProfile walkingProfile,
            Random rnd,
            IChannelAssigner channelAssigner,
            Random channelRnd,
            List<Passenger> passengers,
            List<ArrivalSimulationResult> simulations,
            ref int nextId,
            ref int waveCounter,
            List<string> warnings)
        {
            if (count <= 0 || group.Profiles == null || group.Profiles.Count == 0)
                return;

            List<PassengerAllocation> allocations =
                AllocatePassengers(count, group.Profiles);

            ArrivalTimeSet times = GenerateStepTimes(
                count, stepStartAbs, stepMinutes, showUpProfile, waves,
                rnd, ref waveCounter, warnings);

            Shuffle(times, rnd);

            simulations.Add(BuildArrivalSimulation(
                times,
                stepStartAbs,
                stepMinutes,
                group.DHP.Type,
                stepIndex,
                isWarmUp,
                isDesignStep));

            int issued = 0;

            for (int a = 0; a < allocations.Count; a++)
            {
                PassengerAllocation allocation = allocations[a];

                int taken = 0;

                for (int k = 0; k < allocation.PassengerCount && taken < times.Times.Count; k++, taken++)
                {
                    double arrival = times.Times[taken];
                    int waveId = times.WaveIds[taken];

                    Passenger passenger = PassengerFactory.Create(
                        nextId++,
                        allocation.Profile,
                        walkingProfile,
                        arrival,
                        rnd,
                        channelAssigner,
                        channelRnd,
                        group.DHP.Type);

                    passenger.DemandStepIndex = stepIndex;
                    passenger.DemandHourIndex = hourIndex;
                    passenger.IsWarmUp = isWarmUp;
                    passenger.IsDesignStep = isDesignStep;
                    passenger.WaveId = waveId;
                    passenger.ShowUpSignature = showUpSignature ?? "none";
                    passenger.PassengerType =
                        (group.DHP.Type == DHPType.DomesticDeparture ||
                         group.DHP.Type == DHPType.DomesticArrival)
                            ? PassengerType.Domestic
                            : PassengerType.International;
                    passenger.ScheduledDepartureTime =
                        passenger.ArrivalTime + SampleAdvance(allocation.Profile, rnd);

                    passengers.Add(passenger);
                }

                issued += taken;
            }
        }

        /// <summary>
        /// Flight-hour demand: the step count is passengers on flights whose
        /// STD (departures) or STA (arrivals) falls inside this hour. Flight
        /// identities are not synthesized, so each passenger samples a single
        /// flight time inside the hour (Option A).
        ///
        /// Departures are placed backwards from the sampled STD exactly as the
        /// schedule generator does: the profile's advance interval is stretched
        /// through the show-up curve. Arrivals are placed forwards from the
        /// sampled STA by the explicit arrival-process delay.
        /// </summary>
        private void GenerateFlightGroup(
            int count,
            PassengerGroup group,
            double stepStartAbs,
            int stepMinutes,
            int stepIndex,
            bool isDesignStep,
            ShowUpProfile fallbackShowUp,
            WalkingProfile walkingProfile,
            CheckInWindowRules checkInWindows,
            ArrivalProcessDelay arrivalDelay,
            Random rnd,
            IChannelAssigner channelAssigner,
            Random channelRnd,
            List<Passenger> passengers,
            List<ArrivalSimulationResult> simulations,
            ref int nextId)
        {
            if (count <= 0 || group.Profiles == null || group.Profiles.Count == 0)
                return;

            DHPType type = group.DHP.Type;
            bool departure = IsDepartureType(type);
            bool domestic = IsDomesticType(type);

            List<PassengerAllocation> allocations =
                AllocatePassengers(count, group.Profiles);

            List<double> arrivalTimes = new List<double>(count);

            for (int a = 0; a < allocations.Count; a++)
            {
                PassengerAllocation allocation = allocations[a];

                if (allocation.PassengerCount <= 0 || allocation.Profile == null)
                    continue;

                PassengerProfile profile = allocation.Profile;
                ShowUpProfile showUp = null;
                string signature = "none";

                double minAdvance = 0.0;
                double maxAdvance = 0.0;

                if (departure)
                {
                    minAdvance = profile.MinAdvanceMinutes;
                    maxAdvance = profile.MaxAdvanceMinutes;

                    if (maxAdvance < minAdvance)
                    {
                        double swap = minAdvance;
                        minAdvance = maxAdvance;
                        maxAdvance = swap;
                    }

                    showUp = profile.DepartureShowUp ??
                             fallbackShowUp ??
                             ShowUpProfile.Mix();

                    signature = showUp.GetSignature();
                }
                else
                {
                    signature =
                        $"arrival-process|{arrivalDelay.MinimumMinutes:R}|" +
                        $"{arrivalDelay.MaximumMinutes:R}";
                }

                for (int i = 0; i < allocation.PassengerCount; i++)
                {
                    double flightTime =
                        stepStartAbs + rnd.NextDouble() * stepMinutes;

                    double arrivalTime;

                    if (departure)
                    {
                        arrivalTime = SampleDepartureArrival(
                            flightTime, minAdvance, maxAdvance, showUp, rnd);
                    }
                    else
                    {
                        arrivalTime = flightTime + arrivalDelay.Sample(rnd);
                    }

                    Passenger passenger = PassengerFactory.Create(
                        nextId++,
                        profile,
                        walkingProfile,
                        arrivalTime,
                        rnd,
                        channelAssigner,
                        channelRnd,
                        type);

                    passenger.DemandStepIndex = stepIndex;
                    passenger.DemandHourIndex = stepIndex;
                    passenger.IsWarmUp = false;
                    passenger.IsDesignStep = isDesignStep;
                    passenger.WaveId = -1;
                    passenger.ShowUpSignature = signature;
                    passenger.PassengerType = domestic
                        ? PassengerType.Domestic
                        : PassengerType.International;
                    passenger.QueueArrivalTime = arrivalTime;

                    if (departure)
                    {
                        CheckInWindow window = checkInWindows.ResolveSector(
                            type, flightTime);

                        passenger.TerminalEntryArrivalTime = arrivalTime;
                        passenger.ScheduledDepartureTime = flightTime;
                        passenger.CheckInOpenTime = window.OpensAt;
                        passenger.CheckInLastAdmissionTime = window.LastAdmissionAt;
                        passenger.ArrivedBeforeCheckInOpen =
                            arrivalTime < window.OpensAt;
                        passenger.IsLateForCheckIn =
                            arrivalTime > window.LastAdmissionAt;
                    }
                    else
                    {
                        passenger.ScheduledArrivalTime = flightTime;
                    }

                    passengers.Add(passenger);
                    arrivalTimes.Add(arrivalTime);
                }
            }

            if (arrivalTimes.Count > 0)
            {
                simulations.Add(BuildFlightArrivalSimulation(
                    arrivalTimes,
                    type,
                    stepIndex,
                    isDesignStep));
            }
        }

        private static double SampleDepartureArrival(
            double scheduledDeparture,
            double minAdvance,
            double maxAdvance,
            ShowUpProfile showUp,
            Random rnd)
        {
            double earliest = scheduledDeparture - maxAdvance;
            double latest = scheduledDeparture - minAdvance;
            double interval = latest - earliest;

            if (interval <= 0.0)
                return latest;

            double fraction = showUp.SampleNormalizedElapsed(rnd);

            return earliest + interval * fraction;
        }

        private static bool IsDepartureType(DHPType type)
        {
            return type == DHPType.DomesticDeparture ||
                   type == DHPType.InternationalDeparture;
        }

        private static bool IsDomesticType(DHPType type)
        {
            return type == DHPType.DomesticDeparture ||
                   type == DHPType.DomesticArrival;
        }

        private ArrivalTimeSet GenerateStepTimes(
            int count,
            double stepStartAbs,
            int stepMinutes,
            ShowUpProfile showUp,
            WaveSettings waves,
            Random rnd,
            ref int waveCounter,
            List<string> warnings)
        {
            if (count <= 0)
                return new ArrivalTimeSet();

            if (showUp != null && showUp.Bands != null &&
                showUp.Bands.Count == ShowUpProfile.ChunkCount)
            {
                return GenerateBandTimes(
                    count, stepStartAbs, stepMinutes,
                    showUp, waves, rnd, ref waveCounter, warnings);
            }

            return GenerateUniformTimes(count, stepStartAbs, stepMinutes, rnd);
        }

        private ArrivalTimeSet GenerateBandTimes(
            int count,
            double stepStartAbs,
            int stepMinutes,
            ShowUpProfile showUp,
            WaveSettings waves,
            Random rnd,
            ref int waveCounter,
            List<string> warnings)
        {
            ArrivalTimeSet set = new ArrivalTimeSet();

            if (count <= 0)
                return set;

            const int bands = ShowUpProfile.ChunkCount;

            List<double> shares = showUp.Bands;

            double bandLength = stepMinutes / (double)bands;

            int[] bandCounts = new int[bands];

            int assigned = 0;
            double running = 0.0;

            for (int b = 0; b < bands; b++)
            {
                running += shares[b];

                int target = b == bands - 1
                    ? count
                    : (int)Math.Floor(count * running + 1e-9);

                if (target > count)
                    target = count;

                if (target < assigned)
                    target = assigned;

                bandCounts[b] = target - assigned;
                assigned = target;
            }

            bool waveMode = waves != null && waves.IsWaveActive;

            for (int b = 0; b < bands; b++)
            {
                int volume = bandCounts[b];

                if (volume <= 0)
                    continue;

                double bandStart = b * bandLength;

                if (waveMode)
                {
                    int waveCount = Math.Max(1, (int)Math.Floor(bandLength / 5.0 + 1e-9));

                    DistributeWaves(
                        set, volume, stepStartAbs, bandStart, bandLength,
                        stepMinutes, waveCount, waves, rnd, ref waveCounter);
                }
                else
                {
                    for (int i = 0; i < volume; i++)
                    {
                        set.Times.Add(stepStartAbs + bandStart + rnd.NextDouble() * bandLength);
                        set.WaveIds.Add(-1);
                    }
                }
            }

            return set;
        }

        private ArrivalTimeSet GenerateUniformTimes(
            int count,
            double stepStartAbs,
            int stepMinutes,
            Random rnd)
        {
            ArrivalTimeSet set = new ArrivalTimeSet();

            for (int i = 0; i < count; i++)
            {
                set.Times.Add(stepStartAbs + rnd.NextDouble() * stepMinutes);
                set.WaveIds.Add(-1);
            }

            return set;
        }

        private void DistributeWaves(
            ArrivalTimeSet set,
            int volume,
            double stepStartAbs,
            double blockStart,
            double blockLen,
            double stepLen,
            int waveCount,
            WaveSettings waves,
            Random rnd,
            ref int waveCounter)
        {
            if (volume <= 0 || waveCount <= 0)
                return;

            double spacing = blockLen / waveCount;

            for (int w = 0; w < waveCount; w++)
            {
                int size = volume / waveCount + (w < volume % waveCount ? 1 : 0);

                if (size <= 0)
                    continue;

                double instant = blockStart + spacing * (w + 0.5);

                if (waves.ScheduleVariabilityMinutes > 0.0)
                    instant += (rnd.NextDouble() * 2.0 - 1.0) * waves.ScheduleVariabilityMinutes;

                int waveId = waveCounter++;

                for (int k = 0; k < size; k++)
                {
                    double dispersion = 0.0;

                    if (waves.WalkDispersionMinutes > 0.0)
                        dispersion = (rnd.NextDouble() * 2.0 - 1.0) * waves.WalkDispersionMinutes;

                    double offset = Clamp(instant + dispersion, 0.0, stepLen);
                    set.Times.Add(stepStartAbs + offset);
                    set.WaveIds.Add(waveId);
                }
            }
        }

        private static double SampleAdvance(PassengerProfile profile, Random rnd)
        {
            double min = profile.MinAdvanceMinutes;
            double max = profile.MaxAdvanceMinutes;

            if (max < min)
            {
                double temp = min;
                min = max;
                max = temp;
            }

            if (max <= 0.0)
            {
                min = 60.0;
                max = 150.0;
            }

            if (Math.Abs(max - min) < 1e-9)
                return min;

            return min + rnd.NextDouble() * (max - min);
        }

        private static ArrivalSimulationResult BuildArrivalSimulation(
            ArrivalTimeSet set,
            double stepStartAbs,
            int stepMinutes,
            DHPType type,
            int stepIndex,
            bool isWarmUp,
            bool isDesignStep)
        {
            ArrivalSimulationResult simulation = new ArrivalSimulationResult();

            int[] histogram = new int[Math.Max(1, stepMinutes)];

            foreach (double time in set.Times)
            {
                int minute = (int)Math.Floor(time);
                simulation.ArrivalMinutes.Add(minute);

                int bucket = (int)Math.Floor(time - stepStartAbs);

                if (bucket >= 0 && bucket < histogram.Length)
                    histogram[bucket]++;
            }

            simulation.Histogram.AddRange(histogram);

            simulation.StepStartMinute = stepStartAbs;
            simulation.StepMinutes = stepMinutes;
            simulation.StepIndex = stepIndex;
            simulation.IsWarmUp = isWarmUp;
            simulation.IsDesignStep = isDesignStep;
            simulation.Label = BuildSimulationLabel(type, stepIndex, isWarmUp, isDesignStep);

            return simulation;
        }

        /// <summary>
        /// Histogram for flight-hour demand. A demand step counts flights in
        /// its hour, but the derived terminal arrivals can fall before the
        /// window (departures) and after it (arrivals). The histogram is
        /// therefore anchored to the realized times instead of a fixed 60
        /// buckets from the step start, so negative and overflow minutes are
        /// retained rather than silently dropped.
        /// </summary>
        private static ArrivalSimulationResult BuildFlightArrivalSimulation(
            List<double> arrivalTimes,
            DHPType type,
            int stepIndex,
            bool isDesignStep)
        {
            ArrivalSimulationResult simulation = new ArrivalSimulationResult();

            int firstMinute = int.MaxValue;
            int lastMinute = int.MinValue;

            foreach (double time in arrivalTimes)
            {
                int minute = (int)Math.Floor(time);
                firstMinute = Math.Min(firstMinute, minute);
                lastMinute = Math.Max(lastMinute, minute);
            }

            int length = lastMinute - firstMinute + 1;
            int[] histogram = new int[Math.Max(1, length)];

            foreach (double time in arrivalTimes)
            {
                int minute = (int)Math.Floor(time);
                simulation.ArrivalMinutes.Add(minute);

                int bucket = minute - firstMinute;

                if (bucket >= 0 && bucket < histogram.Length)
                    histogram[bucket]++;
            }

            simulation.Histogram.AddRange(histogram);

            simulation.StepStartMinute = firstMinute;
            simulation.StepMinutes = histogram.Length;
            simulation.StepIndex = stepIndex;
            simulation.IsWarmUp = false;
            simulation.IsDesignStep = isDesignStep;
            simulation.Label = BuildSimulationLabel(
                type, stepIndex, false, isDesignStep);

            return simulation;
        }

        private static string BuildSimulationLabel(
            DHPType type,
            int stepIndex,
            bool isWarmUp,
            bool isDesignStep)
        {
            string kind = isWarmUp ? "Warm-up" : $"Step {stepIndex}";

            if (isDesignStep)
                kind += " (design hour)";

            return $"{type} - {kind}";
        }

        private static void Shuffle(ArrivalTimeSet set, Random rnd)
        {
            for (int i = set.Times.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);

                double tempTime = set.Times[i];
                set.Times[i] = set.Times[j];
                set.Times[j] = tempTime;

                int tempWave = set.WaveIds[i];
                set.WaveIds[i] = set.WaveIds[j];
                set.WaveIds[j] = tempWave;
            }
        }

        private static List<PassengerAllocation> AllocatePassengers(
            int totalPassengers,
            List<PassengerProfile> profiles)
        {
            List<PassengerAllocation> allocations = new List<PassengerAllocation>();
            int allocated = 0;

            for (int i = 0; i < profiles.Count; i++)
            {
                int count;

                if (i == profiles.Count - 1)
                {
                    count = totalPassengers - allocated;
                }
                else
                {
                    count = (int)Math.Round(totalPassengers * profiles[i].Percentage / 100.0);
                    allocated += count;
                }

                allocations.Add(new PassengerAllocation
                {
                    Profile = profiles[i],
                    PassengerCount = Math.Max(0, count)
                });
            }

            return allocations;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private class ArrivalTimeSet
        {
            public List<double> Times { get; } = new List<double>();

            public List<int> WaveIds { get; } = new List<int>();
        }
    }
}
