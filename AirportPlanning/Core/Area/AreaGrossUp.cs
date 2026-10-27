using System;

namespace AirportPlanning.Core.Area
{
    /// <summary>
    /// The net-to-gross build-up, in one place.
    /// </summary>
    public class GrossArea
    {
        /// <summary>Area before the allowances, m².</summary>
        public double Net { get; set; }

        /// <summary>Circulation allowance, m².</summary>
        public double CirculationArea { get; set; }

        /// <summary>Mechanical, electrical and plumbing allowance, m².</summary>
        public double MepArea { get; set; }

        /// <summary>Structure allowance, m².</summary>
        public double StructureArea { get; set; }

        /// <summary>Total buildable area, m².</summary>
        public double Total { get; set; }
    }

    /// <summary>
    /// Turns a net area into a buildable one.
    ///
    /// Three allowances, applied in order: circulation, then mechanical, then
    /// structure. Each is a share of the area that already includes the ones
    /// before it, so they compound - adding them to the net instead would
    /// understate the gross.
    ///
    /// Every facility's area goes through here, so one process cannot quietly
    /// report a net figure as if it were buildable.
    /// </summary>
    public static class AreaGrossUp
    {
        public static GrossArea Calculate(
            double net,
            double circulationPercent,
            double mepPercent,
            double structurePercent)
        {
            GrossArea area = new GrossArea { Net = net };

            area.CirculationArea = net * (Math.Max(0.0, circulationPercent) / 100.0);

            double afterCirculation = area.Net + area.CirculationArea;

            area.MepArea = afterCirculation * (Math.Max(0.0, mepPercent) / 100.0);

            double afterMep = afterCirculation + area.MepArea;

            area.StructureArea = afterMep * (Math.Max(0.0, structurePercent) / 100.0);

            area.Total = afterMep + area.StructureArea;

            return area;
        }
    }
}
