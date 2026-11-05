using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// One aircraft code's PHP peak-hour stand demand.
    ///
    /// The stand is apron; the lounge is terminal. The two are counted
    /// separately and never added together.
    /// </summary>
    public class GateCodeInput
    {
        /// <summary>
        /// Annex 14 code letter: A, B, C, D, E or F.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Arriving movements in the domestic peak hour.</summary>
        public int DomesticAtms { get; set; }

        /// <summary>Arriving movements in the international peak hour.</summary>
        public int InternationalAtms { get; set; }

        /// <summary>
        /// Minutes an aircraft of this code holds a stand in the domestic
        /// sector, on-block to off-block.
        /// </summary>
        public double DomesticOccupancyMinutes { get; set; }

        /// <summary>Stand occupancy minutes in the international sector.</summary>
        public double InternationalOccupancyMinutes { get; set; }

    }

    /// <summary>
    /// One aircraft code's passenger and lounge assumptions. Kept separate
    /// from PHP movements so the same profiles can size either demand method.
    /// </summary>
    public class GateLoungeProfile
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>Average seats on one aircraft of this code.</summary>
        public int Seats { get; set; }

        /// <summary>Share of seats filled, from 0 to 1.</summary>
        public double LoadFactor { get; set; }

        /// <summary>Share of the planeload seated in the lounge.</summary>
        public double SeatedPct { get; set; }

        /// <summary>Share of the planeload standing in the lounge.</summary>
        public double StandingPct { get; set; }

        /// <summary>Share of the planeload waiting in the boarding queue.</summary>
        public double BoardingQueuePct { get; set; }

        /// <summary>Extra area for seated passengers using the next seat.</summary>
        public double NextSeatAllowancePct { get; set; }

        /// <summary>Hold-area reduction for an open lounge.</summary>
        public double OpenLoungeReductionPct { get; set; }

        /// <summary>Boarding podium positions in one lounge of this code.</summary>
        public int PodiumsPerLounge { get; set; }
    }

    /// <summary>
    /// The policy and the space standards a gate area is built from.
    /// </summary>
    public class GateStandards
    {
        /// <summary>
        /// Minutes added to every aircraft's occupancy: a stand cannot turn
        /// from one aircraft to the next at no cost.
        /// </summary>
        public double BufferMinutes { get; set; } = 10.0;

        /// <summary>Spare stands, as a share of the peak requirement.</summary>
        public double SparesPct { get; set; } = 0.0;

        /// <summary>Stands of each code able to serve either sector.</summary>
        public Dictionary<string, int> SwingStands { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Share of the domestic group's active stands that are contact.</summary>
        public double ContactPctDomestic { get; set; } = 0.0;

        /// <summary>Share of the swing group's active stands that are contact.</summary>
        public double ContactPctSwing { get; set; } = 0.0;

        /// <summary>Share of the international group's active stands that are contact.</summary>
        public double ContactPctInternational { get; set; } = 0.0;

        /// <summary>Remote stands one bus gate serves in active use.</summary>
        public double RemoteStandsPerBusGate { get; set; } = 1.0;

        /// <summary>
        /// All stands including overnight parking, divided by the active
        /// stands. A combined ratio.
        /// </summary>
        public double MaxStandsRatio { get; set; } = 1.0;

        /// <summary>Space for one seated passenger, m².</summary>
        public double SeatedSpacePerPax { get; set; } = 1.7;

        /// <summary>Space for one standing passenger, m².</summary>
        public double StandingSpacePerPax { get; set; } = 1.2;

        /// <summary>Space for one passenger waiting to board, m².</summary>
        public double BoardingQueueSpacePerPax { get; set; } = 1.2;

        /// <summary>Area of one boarding podium, m².</summary>
        public double PodiumAreaEach { get; set; } = 0.0;

        /// <summary>The swing stands stated for one code.</summary>
        public int SwingFor(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || SwingStands == null)
                return 0;

            return SwingStands.TryGetValue(code.Trim(), out int value) ? value : 0;
        }
    }

    /// <summary>
    /// Aircraft-on-ground demand measured from a linked design-day schedule.
    /// The three peaks are measured from the 5-minute domestic, international
    /// and mixed-sector curves, using the same rotation rules as the TypeScript
    /// source implementation.
    /// </summary>
    public class GateScheduleCodeDemand
    {
        public string Code { get; set; } = string.Empty;
        public int DomesticPeak { get; set; }
        public int InternationalPeak { get; set; }
        public int CombinedPeak { get; set; }
        public int MixedPeak { get; set; }
        public int PeakSlot { get; set; }
        public int Swing { get; set; }
        public int DedicatedDomestic { get; set; }
        public int DedicatedInternational { get; set; }
        public bool MixedFloor { get; set; }

        /// <summary>
        /// Sum of the two sector peaks (domestic + international). What a
        /// sector-dedicated terminal would build; reported for comparison, never
        /// used to size, because stands serve both sectors at different times.
        /// </summary>
        public int SectorSum { get; set; }

        /// <summary>
        /// True when the sized total meets the concurrent (combined) peak: the
        /// concurrent floor holds.
        /// </summary>
        public bool ConcurrentFloorMet { get; set; }
    }

    /// <summary>Schedule-derived stand demand for one terminal or the airport.</summary>
    public class GateScheduleDemand
    {
        public string ScheduleName { get; set; } = string.Empty;
        public string Terminal { get; set; } = string.Empty;
        public bool TerminalIsAirport { get; set; }
        public int RotationCount { get; set; }
        public int SplitTerminalRotations { get; set; }
        public List<GateScheduleCodeDemand> Codes { get; set; } = new List<GateScheduleCodeDemand>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// One aircraft code's stand requirement in one sector.
    /// </summary>
    public class GateCodeStands
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Stand-hours the peak hour demands, before rounding: movements x
        /// (occupancy + buffer) / 60.
        /// </summary>
        public double StandHours { get; set; }

        /// <summary>Stands this code needs, rounded up on its own.</summary>
        public int Rounded { get; set; }

        /// <summary>Stands swung to the other sector.</summary>
        public int Swing { get; set; }

        /// <summary>Stands serving this sector only.</summary>
        public int Dedicated { get; set; }

        /// <summary>Stands of this code with a bridge attached.</summary>
        public int Contact { get; set; }

        /// <summary>Non-contact stands of this code. In schedule mode these
        /// are remote stands; in PHP mode they are bus gates.</summary>
        public int Bus { get; set; }
    }

    /// <summary>
    /// The stands of one group: domestic, swing or international.
    /// </summary>
    public class GateBucketStands
    {
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Stands in use at the peak, of every code.</summary>
        public int Active { get; set; }

        /// <summary>Stands with a bridge attached.</summary>
        public int Contact { get; set; }

        /// <summary>Stands served by a bus gate.</summary>
        public int Bus { get; set; }

        /// <summary>
        /// Non-contact stands of this group. In schedule mode these are remote
        /// stands and <see cref="Bus"/> holds the derived bus gates; in PHP
        /// mode the non-contact stands are themselves bus gates.
        /// </summary>
        public int Remote { get; set; }

        /// <summary>
        /// The sector requirement rounded once over the sum of its codes,
        /// before the per-code split.
        /// </summary>
        public int SectorTotal { get; set; }

        /// <summary>
        /// What the per-code split adds over the sector total. The two can
        /// differ by a stand or two, because a stand serves one code.
        /// </summary>
        public int SplitExtra { get; set; }

        /// <summary>One line per code in this group.</summary>
        public List<GateCodeStands> Codes { get; set; } = new List<GateCodeStands>();
    }

    /// <summary>
    /// One gate lounge: a gate, a hold room, a podium and a boarding queue.
    /// </summary>
    public class GateLounge
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>The group the lounge belongs to.</summary>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>How many lounges of this kind.</summary>
        public int Count { get; set; }

        /// <summary>Passengers in one lounge.</summary>
        public double Passengers { get; set; }

        /// <summary>Hold room area of one lounge, m².</summary>
        public double HoldArea { get; set; }

        /// <summary>Podium area of one lounge, m².</summary>
        public double PodiumArea { get; set; }

        /// <summary>Boarding queue area of one lounge, m².</summary>
        public double QueueArea { get; set; }

        /// <summary>Total area of one lounge, m².</summary>
        public double AreaEach
        {
            get { return HoldArea + PodiumArea + QueueArea; }
        }

        /// <summary>Total area of every lounge of this kind, m².</summary>
        public double Area
        {
            get { return AreaEach * Count; }
        }
    }

    /// <summary>
    /// The result of sizing the stands and the lounges.
    /// </summary>
    public class GateSizingResult
    {
        /// <summary>True when stand demand came from schedule-derived AOG peaks.</summary>
        public bool ScheduleMode { get; set; }

        /// <summary>The three groups of stands.</summary>
        public List<GateBucketStands> Buckets { get; set; } = new List<GateBucketStands>();

        /// <summary>Stands in use at the peak.</summary>
        public int ActiveTotal { get; set; }

        /// <summary>Stands with a bridge attached.</summary>
        public int ContactTotal { get; set; }

        /// <summary>Stands served by a bus gate.</summary>
        public int BusTotal { get; set; }

        /// <summary>Remote stands the bus gates use.</summary>
        public int RemoteActive { get; set; }

        /// <summary>All stands, including overnight parking.</summary>
        public int MaxStands { get; set; }

        /// <summary>Max stands less the contact stands.</summary>
        public int NetRemote { get; set; }

        /// <summary>
        /// Sum of the per-code sector peaks (domestic + international) in
        /// schedule mode. What a sector-dedicated terminal would build; the
        /// sized <see cref="ActiveTotal"/> shares stands across sectors
        /// instead. Zero outside schedule mode.
        /// </summary>
        public int SectorSumTotal { get; set; }

        /// <summary>
        /// The concurrent floor: the busiest simultaneous stand count in
        /// schedule mode. Equals <see cref="ActiveTotal"/> when the floor holds.
        /// Zero outside schedule mode.
        /// </summary>
        public int ConcurrentPeakTotal { get; set; }

        /// <summary>The lounges.</summary>
        public List<GateLounge> Lounges { get; set; } = new List<GateLounge>();

        /// <summary>Number of lounges of every kind.</summary>
        public int LoungeCount
        {
            get { return Lounges.Sum(l => l.Count); }
        }

        /// <summary>Hold room area of every lounge, m².</summary>
        public double HoldArea { get; set; }

        /// <summary>Podium area of every lounge, m².</summary>
        public double PodiumArea { get; set; }

        /// <summary>Boarding queue area of every lounge, m².</summary>
        public double QueueArea { get; set; }

        /// <summary>Net lounge area before the allowances, m².</summary>
        public double NetArea { get; set; }

        /// <summary>Anything the reader needs to know.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
