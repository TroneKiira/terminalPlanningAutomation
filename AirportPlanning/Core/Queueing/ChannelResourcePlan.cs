using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Inclusive bounds, in physical resource units, for the optimizer's search.
    /// Kept free of any Rhino/Grasshopper types so the domain layer stays testable
    /// on its own.
    /// </summary>
    public class ResourceSearchRange
    {
        /// <summary>Lowest number of physical resources to consider (>= 1).</summary>
        public int Min { get; set; }

        /// <summary>Highest number of physical resources to consider (>= Min).</summary>
        public int Max { get; set; }

        public ResourceSearchRange()
        {
        }

        public ResourceSearchRange(int min, int max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Normalises the bounds so the range is always usable: a minimum of at
        /// least one resource, and a maximum that is never below the minimum.
        /// </summary>
        public ResourceSearchRange Normalized()
        {
            int min = Min < 1 ? 1 : Min;
            int max = Max < min ? min : Max;

            return new ResourceSearchRange(min, max);
        }

        public override string ToString()
        {
            return $"{Min}-{Max}";
        }
    }

    /// <summary>
    /// The sizing plan for one server pool. A pool is either a single facility-wide
    /// queue (legacy behaviour) or the dedicated queue of one passenger channel.
    /// A pool can be further subdivided by a <see cref="PoolKey"/>, which splits
    /// one channel's passengers into separate queues (e.g. Counter split by
    /// sector and class: Domestic Economy, Domestic Business, International
    /// Economy, International Business).
    /// </summary>
    public class ChannelResourcePlan
    {
        /// <summary>
        /// Channel this pool serves. Null or empty marks the legacy single-pool
        /// case, where every passenger shares one queue.
        /// </summary>
        public string Channel { get; set; }

        /// <summary>
        /// Optional sub-key within a channel that splits this channel's
        /// passengers into separate pools. The expected format is
        /// "DHPType|ProfileName" (e.g. "DomesticDeparture|Economy"). When null
        /// or empty, the whole channel forms one pool.
        /// </summary>
        public string PoolKey { get; set; }

        /// <summary>
        /// Physical resources to simulate when optimization is switched off.
        /// </summary>
        public int PhysicalResources { get; set; }

        /// <summary>
        /// Target 95th percentile wait in minutes used as the acceptance
        /// criterion while searching.
        /// </summary>
        public double TargetWait95 { get; set; }

        /// <summary>Inclusive search bounds for the physical resource count.</summary>
        public ResourceSearchRange Range { get; set; }

        public ChannelResourcePlan()
        {
            Range = new ResourceSearchRange(1, 1);
        }

        /// <summary>True when this plan represents one shared facility-wide pool.</summary>
        public bool IsSharedPool
        {
            get { return string.IsNullOrWhiteSpace(Channel); }
        }

        /// <summary>
        /// Composite identity used for cache keys, pool names and per-pool
        /// wait lookups. When a PoolKey is set the identity is
        /// "Channel|PoolKey"; otherwise it is just the channel name.
        /// </summary>
        public string PoolIdentity
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(PoolKey))
                    return $"{Channel}|{PoolKey}";
                return Channel ?? string.Empty;
            }
        }

        /// <summary>
        /// Server count actually simulated, derived from the physical count and
        /// the staffing percentage.
        /// </summary>
        public static int ToActiveResources(int physicalResources, double staffingPercentage)
        {
            double staffPct =
                (staffingPercentage <= 0.0 || staffingPercentage > 100.0)
                    ? 100.0
                    : staffingPercentage;

            int active = (int)Math.Round(Math.Max(1, physicalResources) * (staffPct / 100.0));

            return active < 1 ? 1 : active;
        }

        public override string ToString()
        {
            if (IsSharedPool)
                return $"shared[{Range}]";

            string identity = !string.IsNullOrWhiteSpace(PoolKey)
                ? $"{Channel}|{PoolKey}"
                : Channel;

            return $"{identity}[{Range}]";
        }
    }
}
