using AirportPlanning.Core.Schedules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// Measures aircraft-on-ground demand from a design-day schedule using
    /// 5-minute samples. Paired legs form turns; unmatched legs use the
    /// configured one-leg ground time.
    /// </summary>
    public static class GateScheduleDemandBuilder
    {
        private const int SlotMinutes = 5;
        private const int SlotsPerDay = 1440 / SlotMinutes;
        private static readonly char[] Codes = { 'A', 'B', 'C', 'D', 'E', 'F' };

        public static GateScheduleDemand Build(
            FlightScheduleResult schedule,
            string terminal = null,
            double beforeOnBlockMinutes = 5.0,
            double afterOffBlockMinutes = 5.0,
            double oneLegGroundMinutes = 120.0)
        {
            GateScheduleDemand result = new GateScheduleDemand
            {
                ScheduleName = schedule?.ScheduleName ?? string.Empty,
                Terminal = terminal?.Trim() ?? string.Empty
            };

            if (schedule?.Legs == null)
            {
                result.Warnings.Add("No flight schedule was supplied.");
                return result;
            }

            List<Rotation> rotations = BuildRotations(
                schedule,
                Math.Max(0.0, beforeOnBlockMinutes),
                Math.Max(0.0, afterOffBlockMinutes),
                Math.Max(0.0, oneLegGroundMinutes),
                result.Warnings,
                out int splitTerminalRotations);

            result.SplitTerminalRotations = splitTerminalRotations;

            bool hasTaggedTerminal = rotations.Any(r => !string.IsNullOrWhiteSpace(r.Terminal));
            result.TerminalIsAirport = string.IsNullOrWhiteSpace(result.Terminal) || !hasTaggedTerminal;

            List<Rotation> selected = result.TerminalIsAirport
                ? rotations
                : rotations.Where(r => string.Equals(
                    r.Terminal,
                    result.Terminal,
                    StringComparison.OrdinalIgnoreCase)).ToList();

            Dictionary<char, List<Rotation>> byCode = Codes.ToDictionary(c => c, _ => new List<Rotation>());
            foreach (Rotation rotation in selected)
                byCode[rotation.Code].Add(rotation);

            foreach (char code in Codes)
                result.Codes.Add(MeasureCode(code, byCode[code]));

            result.RotationCount = selected.Count;
            if (!result.TerminalIsAirport && result.RotationCount == 0)
                result.Warnings.Add($"The linked schedule contains no counted rotations for terminal '{result.Terminal}'.");

            if (result.Codes.Any(c => c.MixedFloor))
                result.Warnings.Add("For at least one aircraft code, the mixed-sector peak exceeds the swing implied by the separate peaks; swing was increased to cover the mixed peak.");

            return result;
        }

        private static List<Rotation> BuildRotations(
            FlightScheduleResult schedule,
            double before,
            double after,
            double oneLeg,
            List<string> warnings,
            out int splitTerminalCount)
        {
            var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usable = new List<FlightLeg>();

            foreach (FlightLeg leg in schedule.Legs.Where(l => l != null))
            {
                string problem = Problem(leg);
                if (problem != null)
                    skipped[problem] = skipped.TryGetValue(problem, out int count) ? count + 1 : 1;
                else
                    usable.Add(leg);
            }

            List<Tuple<FlightLeg, FlightLeg>> pairs = new List<Tuple<FlightLeg, FlightLeg>>();
            var linked = usable
                .Where(l => !string.IsNullOrWhiteSpace(l.LinkId))
                .GroupBy(l => l.LinkId, StringComparer.OrdinalIgnoreCase);

            foreach (var group in linked)
            {
                List<FlightLeg> arrivals = group.Where(l => l.Direction == LegDirection.Arrival).ToList();
                List<FlightLeg> departures = group.Where(l => l.Direction == LegDirection.Departure).ToList();
                int count = Math.Max(arrivals.Count, departures.Count);

                for (int i = 0; i < count; i++)
                    pairs.Add(Tuple.Create(
                        i < arrivals.Count ? arrivals[i] : null,
                        i < departures.Count ? departures[i] : null));
            }

            foreach (FlightLeg leg in usable.Where(l => string.IsNullOrWhiteSpace(l.LinkId)))
                pairs.Add(leg.Direction == LegDirection.Arrival
                    ? Tuple.Create(leg, (FlightLeg)null)
                    : Tuple.Create((FlightLeg)null, leg));

            List<Rotation> rotations = new List<Rotation>();
            splitTerminalCount = 0;

            foreach (Tuple<FlightLeg, FlightLeg> pair in pairs)
            {
                FlightLeg arrival = pair.Item1;
                FlightLeg departure = pair.Item2;
                LegSector arrivalSector = KnownSector(arrival?.SectorRaw);
                LegSector departureSector = KnownSector(departure?.SectorRaw);
                LegSector effectiveArrival = arrivalSector != LegSector.Unknown ? arrivalSector : departureSector;
                LegSector effectiveDeparture = departureSector != LegSector.Unknown ? departureSector : arrivalSector;

                if (effectiveArrival == LegSector.Unknown || effectiveDeparture == LegSector.Unknown)
                {
                    int legs = (arrival == null ? 0 : 1) + (departure == null ? 0 : 1);
                    skipped["Sector unknown"] = skipped.TryGetValue("Sector unknown", out int count) ? count + legs : legs;
                    continue;
                }

                string rawCode = !string.IsNullOrWhiteSpace(arrival?.IcaoCodeRaw)
                    ? arrival.IcaoCodeRaw
                    : departure?.IcaoCodeRaw;
                char code;
                if (!TryCode(rawCode, out code))
                {
                    int legs = (arrival == null ? 0 : 1) + (departure == null ? 0 : 1);
                    skipped["Aircraft type not recognised"] = skipped.TryGetValue("Aircraft type not recognised", out int count) ? count + legs : legs;
                    continue;
                }

                int onBlock;
                int offBlock;
                if (arrival != null && departure != null)
                {
                    onBlock = AbsoluteMinute(arrival);
                    offBlock = AbsoluteMinute(departure);
                    while (offBlock < onBlock)
                        offBlock += 1440;
                }
                else if (arrival != null)
                {
                    onBlock = AbsoluteMinute(arrival);
                    offBlock = onBlock + (int)Math.Round(oneLeg, MidpointRounding.AwayFromZero);
                }
                else
                {
                    offBlock = AbsoluteMinute(departure);
                    onBlock = offBlock - (int)Math.Round(oneLeg, MidpointRounding.AwayFromZero);
                }

                string arrivalTerminal = arrival?.Terminal ?? string.Empty;
                string departureTerminal = departure?.Terminal ?? string.Empty;
                if (arrival != null && departure != null &&
                    !string.IsNullOrWhiteSpace(arrivalTerminal) &&
                    !string.IsNullOrWhiteSpace(departureTerminal) &&
                    !string.Equals(arrivalTerminal, departureTerminal, StringComparison.OrdinalIgnoreCase))
                    splitTerminalCount++;

                string terminal = arrival != null ? arrivalTerminal : departureTerminal;
                string bucket = effectiveArrival != effectiveDeparture
                    ? "mixed"
                    : effectiveArrival == LegSector.Domestic ? "domestic" : "international";

                rotations.Add(new Rotation
                {
                    Code = code,
                    Bucket = bucket,
                    Terminal = terminal,
                    Start = onBlock - before,
                    End = offBlock + after
                });
            }

            foreach (KeyValuePair<string, int> item in skipped)
                warnings.Add($"{item.Value} schedule leg(s) not counted: {item.Key}.");

            return rotations;
        }

        private static GateScheduleCodeDemand MeasureCode(char code, List<Rotation> rotations)
        {
            int[] domestic = new int[SlotsPerDay];
            int[] international = new int[SlotsPerDay];
            int[] mixed = new int[SlotsPerDay];

            foreach (Rotation rotation in rotations)
            {
                int[] curve = rotation.Bucket == "domestic"
                    ? domestic
                    : rotation.Bucket == "international" ? international : mixed;
                Mark(curve, rotation);
            }

            int domesticPeak = 0;
            int internationalPeak = 0;
            int mixedPeak = 0;
            int combinedPeak = 0;
            int peakSlot = 0;

            for (int i = 0; i < SlotsPerDay; i++)
            {
                domesticPeak = Math.Max(domesticPeak, domestic[i] + mixed[i]);
                internationalPeak = Math.Max(internationalPeak, international[i] + mixed[i]);
                mixedPeak = Math.Max(mixedPeak, mixed[i]);
                int combined = domestic[i] + international[i] + mixed[i];
                if (combined > combinedPeak)
                {
                    combinedPeak = combined;
                    peakSlot = i;
                }
            }

            SectorSharing.Result sharing = SectorSharing.Calculate(
                domesticPeak, internationalPeak, combinedPeak, mixedPeak);

            return new GateScheduleCodeDemand
            {
                Code = code.ToString(),
                DomesticPeak = domesticPeak,
                InternationalPeak = internationalPeak,
                CombinedPeak = combinedPeak,
                MixedPeak = mixedPeak,
                PeakSlot = peakSlot,
                Swing = sharing.Swing,
                DedicatedDomestic = sharing.DedicatedDomestic,
                DedicatedInternational = sharing.DedicatedInternational,
                MixedFloor = sharing.MixedFloor,
                SectorSum = sharing.SectorSum,
                ConcurrentFloorMet = sharing.ConcurrentFloorMet
            };
        }

        private static void Mark(int[] curve, Rotation rotation)
        {
            double duration = rotation.End - rotation.Start;
            for (int slot = 0; slot < SlotsPerDay; slot++)
            {
                double sinceStart = Mod(slot * SlotMinutes - rotation.Start, 1440.0);
                if (duration >= 1440.0 || sinceStart < duration)
                    curve[slot]++;
            }
        }

        private static string Problem(FlightLeg leg)
        {
            if (leg.ServiceType != LegServiceType.Passenger)
                return "Freight or other non-passenger flight";

            if (!leg.HasTime || (leg.Issues ?? new List<LegIssue>()).Any(i =>
                string.Equals(i.Field, "time", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase)))
                return "Time could not be read";

            return null;
        }

        private static LegSector KnownSector(LegSector? sector)
        {
            return sector == LegSector.Domestic || sector == LegSector.International
                ? sector.Value
                : LegSector.Unknown;
        }

        private static bool TryCode(string raw, out char code)
        {
            string value = (raw ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 1 && Codes.Contains(value[0]))
            {
                code = value[0];
                return true;
            }

            code = '\0';
            return false;
        }

        private static int AbsoluteMinute(FlightLeg leg)
        {
            return leg.TotalMinutesFromMidnight;
        }

        private static double Mod(double value, double divisor)
        {
            double result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private sealed class Rotation
        {
            public char Code;
            public string Bucket;
            public string Terminal;
            public double Start;
            public double End;
        }
    }
}
