using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Area
{
    /// <summary>
    /// One installed unit of a facility: the floor footprint it occupies, and
    /// optionally the queue space the passengers waiting at it need.
    ///
    /// Standards are keyed by a facility key ("Counter", "Kiosk",
    /// "BagDrop") so a facility that routes passengers through several kinds
    /// of equipment can be sized from the units it actually has, rather than
    /// from one generic footprint applied to a total count.
    /// </summary>
    public class FacilityUnitStandard
    {
        /// <summary>
        /// Name of the unit this standard describes. Matched case
        /// insensitively against a pool name.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Floor area of one installed unit, m². This is the footprint of the
        /// unit and its own working envelope - general circulation is added
        /// later as an allowance, so it must not be included here.
        /// </summary>
        public double AreaPerUnit { get; set; }

        /// <summary>
        /// Queue space for one waiting passenger at this unit, m². Zero (or
        /// less) means the standard set's default is used instead.
        /// </summary>
        public double QueueSpacePerPax { get; set; }

        /// <summary>Free-text note, for provenance on the definition.</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// A named area that is added as a lump rather than sized from a count:
    /// a reconciliation room, an isolation suite, a store.
    /// </summary>
    public class NamedArea
    {
        public string Name { get; set; } = string.Empty;

        public double Area { get; set; }
    }

    /// <summary>
    /// The standards and allowances one facility's area is built from.
    ///
    /// Kept as plain data so the values can be authored in a Grasshopper
    /// definition and edited without touching the sizing code, and so the
    /// same set can be reused across facilities.
    /// </summary>
    public class FacilityAreaStandardSet
    {
        /// <summary>One entry per kind of installed unit.</summary>
        public List<FacilityUnitStandard> Units { get; set; } =
            new List<FacilityUnitStandard>();

        /// <summary>
        /// Queue space per waiting passenger, m², used when a unit standard
        /// does not state its own.
        /// </summary>
        public double DefaultQueueSpacePerPax { get; set; } = 1.2;

        /// <summary>Circulation as a percentage of the net area.</summary>
        public double CirculationPercent { get; set; } = 15.0;

        /// <summary>
        /// Mechanical, electrical and plumbing as a percentage of the area
        /// including circulation: plant rooms, risers and services.
        /// </summary>
        public double MepPercent { get; set; } = 10.0;

        /// <summary>Structure as a percentage of the area including MEP.</summary>
        public double StructurePercent { get; set; } = 5.0;

        /// <summary>Areas added as lumps, outside the unit counts.</summary>
        public List<NamedArea> FixedAreas { get; set; } = new List<NamedArea>();

        /// <summary>Total of the fixed areas, m².</summary>
        public double FixedAreaTotal
        {
            get
            {
                if (FixedAreas == null)
                    return 0.0;

                return FixedAreas
                    .Where(a => a != null)
                    .Sum(a => Math.Max(0.0, a.Area));
            }
        }

        /// <summary>
        /// The standard for a facility key, matched case insensitively.
        /// Null when the key is unknown.
        /// </summary>
        public FacilityUnitStandard Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || Units == null)
                return null;

            string trimmed = key.Trim();

            return Units.FirstOrDefault(u =>
                u != null && !string.IsNullOrWhiteSpace(u.Key) &&
                string.Equals(u.Key.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }
}
