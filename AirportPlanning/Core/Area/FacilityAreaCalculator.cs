using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Area
{
    /// <summary>
    /// One routed pool's contribution to a facility's floor area.
    /// </summary>
    public class FacilityAreaLine
    {
        /// <summary>Pool name - the channel it serves, or "Shared".</summary>
        public string PoolName { get; set; } = string.Empty;

        /// <summary>
        /// Facility key the pool was sized with. Empty when no standard
        /// matched and the fallback footprint was used.
        /// </summary>
        public string FacilityKey { get; set; } = string.Empty;

        /// <summary>True when a unit standard was found for this pool.</summary>
        public bool HasStandard { get; set; }

        /// <summary>
        /// False for a route with no physical counterpart - an online
        /// check-in, for instance. Such a pool contributes no equipment area.
        /// </summary>
        public bool HasPhysicalArea { get; set; } = true;

        /// <summary>Installed units in this pool.</summary>
        public int PhysicalUnits { get; set; }

        /// <summary>Staffed units in this pool.</summary>
        public int ActiveUnits { get; set; }

        /// <summary>Area per installed unit applied, m².</summary>
        public double AreaPerUnit { get; set; }

        /// <summary>Equipment footprint of this pool, m².</summary>
        public double EquipmentArea { get; set; }

        /// <summary>Passengers waiting in this pool at its design peak.</summary>
        public int QueuePax { get; set; }

        /// <summary>Queue space per waiting passenger applied, m².</summary>
        public double QueueSpacePerPax { get; set; }

        /// <summary>Queue area of this pool, m².</summary>
        public double QueueArea { get; set; }

        /// <summary>Equipment plus queue, m².</summary>
        public double NetArea { get; set; }

        /// <summary>Anything the reader needs to know about this line.</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Everything the area calculator needs: how many units each routed pool
    /// has, how many passengers wait in it, and the standards to apply.
    /// </summary>
    public class FacilityAreaInput
    {
        /// <summary>
        /// Pool names in output order. When empty, the processor pool
        /// metadata carried by the queue results is used instead.
        /// </summary>
        public List<string> PoolNames { get; set; } = new List<string>();

        /// <summary>
        /// Installed units per pool, in the same order. A pool this list does
        /// not cover takes its count from the queue results instead.
        /// </summary>
        public List<int> PhysicalPerPool { get; set; } = new List<int>();

        /// <summary>
        /// Staffed units per pool, in the same order. A pool this list does
        /// not cover takes its count from the queue results instead.
        /// </summary>
        public List<int> ActivePerPool { get; set; } = new List<int>();

        /// <summary>
        /// The unit standard each pool is sized with, in the same order. An
        /// empty entry means the pool is matched by name instead.
        /// </summary>
        public List<string> FacilityKeyPerPool { get; set; } = new List<string>();

        /// <summary>
        /// Pools with no physical counterpart, in the same order: a route
        /// that is not a place passengers queue at.
        /// </summary>
        public List<bool> NoPhysicalAreaPerPool { get; set; } = new List<bool>();

        /// <summary>
        /// Processor pool metadata from the queue results. Used for the pool
        /// names, the counts and the shared/dedicated distinction.
        /// </summary>
        public List<ProcessorPoolInfo> Pools { get; set; } = new List<ProcessorPoolInfo>();

        /// <summary>Peak queue length per channel.</summary>
        public Dictionary<string, int> PeakQueueByChannel { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Peak queue length of the whole facility.</summary>
        public int GlobalPeakQueue { get; set; }

        /// <summary>The standards and allowances to apply.</summary>
        public FacilityAreaStandardSet Standards { get; set; }

        /// <summary>
        /// Footprint used for a pool no standard matched, m² per unit. This
        /// is the facility's own single-unit size, so an unmatched pool is
        /// still sized rather than dropped.
        /// </summary>
        public double FallbackAreaPerUnit { get; set; }

        /// <summary>Queue space used when no standard supplies one, m²/pax.</summary>
        public double FallbackQueueSpacePerPax { get; set; }
    }

    /// <summary>
    /// The area of a facility, broken down by routed pool.
    /// </summary>
    public class FacilityAreaResult
    {
        /// <summary>One line per routed pool.</summary>
        public List<FacilityAreaLine> Lines { get; set; } = new List<FacilityAreaLine>();

        /// <summary>Sum of the equipment footprints, m².</summary>
        public double EquipmentArea { get; set; }

        /// <summary>Sum of the queue areas, m².</summary>
        public double QueueArea { get; set; }

        /// <summary>Equipment plus queue, m².</summary>
        public double NetArea { get; set; }

        /// <summary>True when at least one pool was sized from a standard.</summary>
        public bool UsedStandards { get; set; }

        /// <summary>Anything the reader needs to know.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// The breakdown as readable lines, for display in Grasshopper.
        /// </summary>
        public List<string> Describe()
        {
            List<string> lines = new List<string>();

            foreach (FacilityAreaLine line in Lines)
            {
                string key = string.IsNullOrEmpty(line.FacilityKey)
                    ? "no standard"
                    : line.FacilityKey;

                string units = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} units x {2:0.##} m2 = {3:0} m2",
                    line.PoolName,
                    line.PhysicalUnits,
                    line.AreaPerUnit,
                    line.EquipmentArea);

                string queue = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} waiting x {2:0.##} m2 = {3:0} m2",
                    line.PoolName,
                    line.QueuePax,
                    line.QueueSpacePerPax,
                    line.QueueArea);

                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} [{1}] | {2} | {3}",
                    units,
                    key,
                    queue,
                    line.Note));
            }

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Net {0:0} m2 (equipment {1:0} + queue {2:0})",
                NetArea,
                EquipmentArea,
                QueueArea));

            return lines;
        }
    }

    /// <summary>
    /// Turns a facility's routed pools into floor area.
    ///
    /// The point of this class is that a facility which routes passengers
    /// through several kinds of equipment - counters, kiosks, bag-drop units -
    /// is sized from the units it actually has in each pool, not from one
    /// generic footprint multiplied by a total processor count. The queue
    /// simulation is not involved: it supplies the counts and the queue
    /// lengths, and this decides what those are worth in square metres.
    /// </summary>
    public static class FacilityAreaCalculator
    {
        /// <summary>
        /// Sizes every pool and returns the per-pool breakdown.
        /// </summary>
        public static FacilityAreaResult Calculate(FacilityAreaInput input)
        {
            FacilityAreaResult result = new FacilityAreaResult();

            if (input == null)
                return result;

            List<PoolRow> rows = BuildRows(input, result);

            if (rows.Count == 0)
                return result;

            FacilityAreaStandardSet standards = input.Standards;

            foreach (PoolRow row in rows)
            {
                FacilityAreaLine line = SizePool(input, standards, row, result);

                result.Lines.Add(line);
                result.EquipmentArea += line.EquipmentArea;
                result.QueueArea += line.QueueArea;
            }

            result.NetArea = result.EquipmentArea + result.QueueArea;

            result.UsedStandards = result.Lines.Any(l => l.HasStandard);

            return result;
        }

        private static FacilityAreaLine SizePool(
            FacilityAreaInput input,
            FacilityAreaStandardSet standards,
            PoolRow row,
            FacilityAreaResult result)
        {
            FacilityUnitStandard standard = ResolveStandard(standards, row, result);

            double areaPerUnit;
            double queueSpace;

            if (standard != null)
            {
                areaPerUnit = standard.AreaPerUnit;

                queueSpace = standard.QueueSpacePerPax > 0.0
                    ? standard.QueueSpacePerPax
                    : (standards.DefaultQueueSpacePerPax > 0.0
                        ? standards.DefaultQueueSpacePerPax
                        : input.FallbackQueueSpacePerPax);
            }
            else
            {
                areaPerUnit = input.FallbackAreaPerUnit;

                queueSpace = standards != null && standards.DefaultQueueSpacePerPax > 0.0
                    ? standards.DefaultQueueSpacePerPax
                    : input.FallbackQueueSpacePerPax;

                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Pool '{0}' has no area standard; the facility's own unit size ({1:0.##} m2) was used instead.",
                    row.Name,
                    areaPerUnit));
            }

            int queuePax = ResolveQueuePax(input, row, result);

            FacilityAreaLine line = new FacilityAreaLine
            {
                PoolName = row.Name,
                FacilityKey = standard != null ? standard.Key : row.FacilityKey,
                HasStandard = standard != null || !string.IsNullOrWhiteSpace(row.FacilityKey),
                HasPhysicalArea = !row.NoPhysicalArea,
                PhysicalUnits = row.Physical,
                ActiveUnits = row.Active,
                AreaPerUnit = areaPerUnit,
                QueuePax = queuePax,
                QueueSpacePerPax = queueSpace
            };

            if (row.NoPhysicalArea)
            {
                line.EquipmentArea = 0.0;

                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Pool '{0}' is marked as having no physical area, so it contributes no equipment area." +
                    (queuePax > 0
                        ? " It was simulated as a queue, so its waiting area is still counted."
                        : string.Empty),
                    row.Name));
            }
            else
            {
                line.EquipmentArea = row.Physical * areaPerUnit;
            }

            line.QueueArea = queuePax * queueSpace;
            line.NetArea = line.EquipmentArea + line.QueueArea;

            if (!row.NoPhysicalArea && row.Physical <= 0 && queuePax <= 0)
            {
                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Pool '{0}' has no units and no queue metric, so nothing was sized for it. " +
                    "Check the pool names against the facility's channels.",
                    row.Name));
            }

            if (row.NoPhysicalArea)
            {
                line.Note = "route with no physical area";
            }
            else if (row.IsShared)
            {
                line.Note = "shared queue, facility peak";
            }
            else if (row.Physical != row.Active)
            {
                line.Note = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} staffed of {1} installed",
                    row.Active,
                    row.Physical);
            }

            return line;
        }

        /// <summary>
        /// The standard a pool is sized with.
        ///
        /// An explicit mapping wins, so a pool named "Kiosk East" can be
        /// sized with the "Kiosk" standard. Without one, the pool is matched
        /// by its own name.
        /// </summary>
        private static FacilityUnitStandard ResolveStandard(
            FacilityAreaStandardSet standards,
            PoolRow row,
            FacilityAreaResult result)
        {
            if (standards == null)
                return null;

            if (!string.IsNullOrWhiteSpace(row.FacilityKey))
            {
                FacilityUnitStandard mapped = standards.Find(row.FacilityKey);

                if (mapped == null)
                {
                    result.Warnings.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Pool '{0}' is mapped to '{1}', which has no area standard.",
                        row.Name,
                        row.FacilityKey));
                }

                return mapped;
            }

            return standards.Find(row.Name);
        }

        /// <summary>
        /// The queue length that sizes this pool's waiting area.
        ///
        /// A shared pool is sized on the whole facility's peak, because
        /// everybody waits in the same queue. A dedicated pool is sized on
        /// its own channel's peak, so the areas of separate channels are not
        /// sized on each other's queues.
        /// </summary>
        private static int ResolveQueuePax(
            FacilityAreaInput input,
            PoolRow row,
            FacilityAreaResult result)
        {
            if (row.IsShared)
                return Math.Max(0, input.GlobalPeakQueue);

            if (input.PeakQueueByChannel != null &&
                input.PeakQueueByChannel.TryGetValue(row.Name, out int peak))
            {
                return Math.Max(0, peak);
            }

            result.Warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "No queue metric for channel '{0}'; the facility peak ({1}) was used for its waiting area.",
                row.Name,
                input.GlobalPeakQueue));

            return Math.Max(0, input.GlobalPeakQueue);
        }

        private static List<PoolRow> BuildRows(
            FacilityAreaInput input,
            FacilityAreaResult result)
        {
            List<PoolRow> rows = new List<PoolRow>();

            List<string> names = ResolveNames(input);

            if (names.Count == 0)
                return rows;

            WarnOnShortList(input.PhysicalPerPool, names.Count, "Physical per Pool", result);
            WarnOnShortList(input.ActivePerPool, names.Count, "Active per Pool", result);
            WarnOnShortList(input.FacilityKeyPerPool, names.Count, "Facility Key per Pool", result);
            WarnOnShortList(input.NoPhysicalAreaPerPool, names.Count, "No Physical Area", result);

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];

                ProcessorPoolInfo meta = FindPool(input.Pools, name);

                int physical = ValueAt(input.PhysicalPerPool, i);

                if (physical <= 0 && meta != null)
                {
                    physical = meta.PhysicalResourceCount > 0
                        ? meta.PhysicalResourceCount
                        : meta.ResourceCount;
                }

                int active = ValueAt(input.ActivePerPool, i);

                if (active <= 0 && meta != null)
                    active = meta.ResourceCount;

                rows.Add(new PoolRow
                {
                    Name = name,
                    Physical = physical,
                    Active = active,
                    FacilityKey = ValueAt(input.FacilityKeyPerPool, i),
                    NoPhysicalArea = ValueAt(input.NoPhysicalAreaPerPool, i),
                    IsShared = IsSharedPool(input, meta, name)
                });
            }

            return rows;
        }

        /// <summary>
        /// The pool names: the definition's when it names them, the queue
        /// results' otherwise.
        /// </summary>
        private static List<string> ResolveNames(FacilityAreaInput input)
        {
            List<string> names = new List<string>();

            if (input.PoolNames != null)
            {
                foreach (string name in input.PoolNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name.Trim());
                }
            }

            if (names.Count > 0)
                return names;

            if (input.Pools == null)
                return names;

            foreach (ProcessorPoolInfo pool in input.Pools)
            {
                if (pool == null)
                    continue;

                names.Add(string.IsNullOrWhiteSpace(pool.Name) ? "Shared" : pool.Name);
            }

            return names;
        }

        private static ProcessorPoolInfo FindPool(List<ProcessorPoolInfo> pools, string name)
        {
            if (pools == null)
                return null;

            return pools.FirstOrDefault(p =>
                p != null && !string.IsNullOrWhiteSpace(p.Name) &&
                string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSharedPool(
            FacilityAreaInput input,
            ProcessorPoolInfo meta,
            string name)
        {
            if (meta != null)
                return meta.IsShared;

            if (input.Pools != null && input.Pools.Count == 1)
                return true;

            return string.Equals(name, "Shared", StringComparison.OrdinalIgnoreCase);
        }

        private static void WarnOnShortList<T>(
            List<T> values,
            int poolCount,
            string label,
            FacilityAreaResult result)
        {
            if (values == null || values.Count == 0)
                return;

            if (values.Count >= poolCount)
                return;

            result.Warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' has {1} value(s) for {2} pool(s); the pools it does not cover take their values from the Queue Results.",
                label,
                values.Count,
                poolCount));
        }

        private static int ValueAt(List<int> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return 0;

            return Math.Max(0, values[index]);
        }

        private static string ValueAt(List<string> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return string.Empty;

            return values[index] ?? string.Empty;
        }

        private static bool ValueAt(List<bool> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return false;

            return values[index];
        }

        private class PoolRow
        {
            public string Name;
            public int Physical;
            public int Active;
            public string FacilityKey;
            public bool NoPhysicalArea;
            public bool IsShared;
        }
    }
}
