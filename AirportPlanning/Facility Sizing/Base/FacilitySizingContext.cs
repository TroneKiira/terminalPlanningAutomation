using AirportPlanning.Core.Area;
using AirportPlanning.Core.Models;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Base
{
    public class FacilitySizingContext
    {
        public FacilityOperationalData OperationalData { get; set; }
        public PassengerSummaryData PassengerSummary { get; set; }
        public FacilityStandards Standards { get; set; }
        public PlanningSettings Settings { get; set; }

        /// <summary>
        /// Keyed unit standards and the area allowances. Supplied from the
        /// definition rather than from code, so the numbers are visible and
        /// editable.
        /// </summary>
        public FacilityAreaStandardSet AreaStandards { get; set; }

        /// <summary>
        /// Sized each routed pool from its own unit standard instead of from
        /// the facility's single-unit rule. The queue simulation is unchanged;
        /// this only changes what the counts are worth in square metres.
        /// </summary>
        public bool UseRoutedPoolAreas { get; set; }

        /// <summary>Pool names in output order, when wired explicitly.</summary>
        public List<string> PoolNames { get; set; }

        /// <summary>Installed units per pool, in the same order.</summary>
        public List<int> PhysicalPerPool { get; set; }

        /// <summary>Staffed units per pool, in the same order.</summary>
        public List<int> ActivePerPool { get; set; }

        /// <summary>
        /// The unit standard each pool is sized with, in the same order. An
        /// empty entry means the pool is matched by its own name.
        /// </summary>
        public List<string> FacilityKeyPerPool { get; set; }

        /// <summary>
        /// Pools with no physical counterpart, in the same order: a route
        /// that is not a place passengers queue at.
        /// </summary>
        public List<bool> NoPhysicalAreaPerPool { get; set; }

        /// <summary>Processor pool metadata from the queue results.</summary>
        public List<ProcessorPoolInfo> ProcessorPools { get; set; }

        /// <summary>Peak queue length per channel.</summary>
        public Dictionary<string, int> PeakQueueByChannel { get; set; }

        /// <summary>Peak queue length of the whole facility.</summary>
        public int GlobalPeakQueue { get; set; }

        /// <summary>
        /// Uplift applied to the net area before the allowances, as a
        /// fraction (0.10 = 10%). Zero leaves the net alone.
        /// </summary>
        public double ExpansionFactor { get; set; }
    }
}
