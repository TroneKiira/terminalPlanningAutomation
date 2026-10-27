using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// One aircraft type: the movements it makes in the peak hour and the
    /// people it brings.
    ///
    /// The stand is apron; the lounge is terminal. The two are counted
    /// separately and never added together.
    /// </summary>
    public class GateCodeInput
    {
        /// <summary>
        /// Annex 14 code letter: A, B, C, D, E or F. A code B or D aircraft
        /// is folded into the next stand up by the planner before it is
        /// typed here.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Arriving movements in the domestic peak hour.</summary>
        public int DomesticAtms { get; set; }

        /// <summary>Arriving movements in the international peak hour.</summary>
        public int InternationalAtms { get; set; }

        /// <summary>
        /// Minutes an aircraft of this code holds a stand: on-block to
        /// off-block. The same figure is used for both sectors here; where a
        /// domestic and an international turn differ, size them on the longer.
        /// </summary>
        public double OccupancyMinutes { get; set; }

        /// <summary>Seats on one aircraft of this code.</summary>
        public int Seats { get; set; }

        /// <summary>Share of the planeload seated in the lounge, 0..1.</summary>
        public double SeatedPct { get; set; }

        /// <summary>Share of the planeload standing in the lounge, 0..1.</summary>
        public double StandingPct { get; set; }

        /// <summary>
        /// Share of the planeload waiting to board, 0..1. The seated,
        /// standing and boarding shares need not total 100%.
        /// </summary>
        public double BoardingQueuePct { get; set; }
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

        /// <summary>Share of the seats filled.</summary>
        public double LoadFactor { get; set; } = 0.85;

        /// <summary>Stands of each code able to serve either sector.</summary>
        public Dictionary<string, int> SwingStands { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Share of the domestic group's active stands that are contact.</summary>
        public double ContactPctDomestic { get; set; } = 0.8;

        /// <summary>Share of the swing group's active stands that are contact.</summary>
        public double ContactPctSwing { get; set; } = 0.8;

        /// <summary>Share of the international group's active stands that are contact.</summary>
        public double ContactPctInternational { get; set; } = 0.8;

        /// <summary>Remote stands one bus gate serves in active use.</summary>
        public double RemoteStandsPerBusGate { get; set; } = 2.0;

        /// <summary>
        /// All stands including overnight parking, divided by the active
        /// stands. A combined ratio.
        /// </summary>
        public double MaxStandsRatio { get; set; } = 1.35;

        /// <summary>Space for one seated passenger, m².</summary>
        public double SeatedSpacePerPax { get; set; } = 1.7;

        /// <summary>Space for one standing passenger, m².</summary>
        public double StandingSpacePerPax { get; set; } = 1.2;

        /// <summary>Space for one passenger waiting to board, m².</summary>
        public double BoardingQueueSpacePerPax { get; set; } = 1.2;

        /// <summary>
        /// Extra seated space for passengers putting belongings on the next
        /// seat, as a share of the seated area.
        /// </summary>
        public double NextSeatAllowancePct { get; set; } = 0.0;

        /// <summary>
        /// Reduction where a lounge is open to the concourse and borrows its
        /// space, as a share of the hold area.
        /// </summary>
        public double OpenLoungeReductionPct { get; set; } = 0.0;

        /// <summary>Boarding podiums in one lounge.</summary>
        public int PodiumsPerLounge { get; set; } = 1;

        /// <summary>Area of one boarding podium, m².</summary>
        public double PodiumAreaEach { get; set; } = 0.0;

        /// <summary>
        /// The code a bus gate's lounge is sized as. A bus gate serves
        /// whatever it serves, so it is sized as the smallest lounge.
        /// </summary>
        public string BusLoungeCode { get; set; } = "C";

        /// <summary>The swing stands stated for one code.</summary>
        public int SwingFor(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || SwingStands == null)
                return 0;

            return SwingStands.TryGetValue(code.Trim(), out int value) ? value : 0;
        }
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

        /// <summary>Stands of this code served by a bus gate.</summary>
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
