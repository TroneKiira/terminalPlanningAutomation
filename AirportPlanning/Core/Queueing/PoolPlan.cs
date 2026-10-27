using System;
using System.Collections.Generic;
using System.Globalization;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// One pool's sizing plan: nominal count, target Wait95, and resource bounds.
    /// This is the single source of pool-level standards supplied to a facility
    /// solver. It is keyed by Channel (and optional PoolKey), not by position,
    /// so a planner can supply rows for exactly the pools their facility uses.
    /// </summary>
    public class PoolPlan
    {
        /// <summary>Channel/pool this plan serves. Null or empty marks the shared pool.</summary>
        public string Channel { get; set; }

        /// <summary>
        /// Optional subdivision key within the channel, e.g. "DomesticDeparture|Economy".
        /// Null or empty means the whole channel is one pool.
        /// </summary>
        public string PoolKey { get; set; }

        /// <summary>
        /// Nominal physical resources (PNR). Used as the fixed count when
        /// optimization is off and retained as a benchmark when optimization is on.
        /// </summary>
        public int PhysicalResources { get; set; }

        /// <summary>
        /// Target whole-run 95th percentile wait in minutes (PTW / MQT).
        /// </summary>
        public double TargetWait95 { get; set; }

        /// <summary>
        /// Inclusive resource bounds (PDomain): Min and Max physical resources
        /// for the optimizer's search. Null means "inherit the solver's fallback
        /// domain"; set it explicitly when this pool has its own bounds.
        /// </summary>
        public ResourceSearchRange Range { get; set; }

        public PoolPlan()
        {
            Range = null;
            PhysicalResources = ChannelResourcePlanResolver.DefaultPhysicalResources;
            TargetWait95 = ChannelResourcePlanResolver.DefaultTargetWait95;
        }

        /// <summary>
        /// Composite pool identity matching <see cref="ChannelResourcePlan.PoolIdentity"/>.
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

        public override string ToString()
        {
            string bounds = Range != null ? Range.ToString() : "default-domain";

            string identity = string.IsNullOrWhiteSpace(Channel)
                ? "shared"
                : (!string.IsNullOrWhiteSpace(PoolKey) ? $"{Channel}|{PoolKey}" : Channel);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} [PNR={1}, PTW={2:0.##}, domain={3}]",
                identity,
                PhysicalResources,
                TargetWait95,
                bounds);
        }
    }
}
