using AirportPlanning.Core.Area;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Base
{
    public abstract class FacilitySizerBase
    {
        protected FacilitySizingContext Context;
        protected List<FunctionalZone> Zones;
        protected FacilitySizingResult Result;

        public FacilitySizingResult SizeFacility(FacilitySizingContext context)
        {
            Context = context;
            Zones = new List<FunctionalZone>();
            Result = new FacilitySizingResult();

            Prepare();
            ReadContext();
            CreateZones();
            CalculateAreas();
            CalculateFixedAreas();
            CalculateGrossUp();

            return BuildResult();
        }

        protected virtual void Prepare() { }

        protected virtual void ReadContext() { }

        protected virtual void CreateZones()
        {
            Zones.Add(new FunctionalZone(ZoneType.Queue));
            Zones.Add(new FunctionalZone(ZoneType.Processing));
            Zones.Add(new FunctionalZone(ZoneType.Support));
            Zones.Add(new FunctionalZone(ZoneType.Fixed));
            Zones.Add(new FunctionalZone(ZoneType.Circulation));
            Zones.Add(new FunctionalZone(ZoneType.Mechanical));
            Zones.Add(new FunctionalZone(ZoneType.Structure));
        }

        protected FunctionalZone GetZone(ZoneType type)
        {
            return Zones.FirstOrDefault(z => z.ZoneType == type);
        }

        /// <summary>
        /// The area of one zone, or zero when the zone is not present.
        /// </summary>
        protected double ZoneArea(ZoneType type)
        {
            FunctionalZone zone = GetZone(type);

            return zone != null ? zone.Area : 0.0;
        }

        protected void SetZone(ZoneType type, double area)
        {
            FunctionalZone zone = GetZone(type);

            if (zone != null)
                zone.Area = area;
        }

        /// <summary>
        /// The area of the facility itself.
        ///
        /// Two ways to get there. The default is the facility's own rule -
        /// one generic footprint applied to the processor count, and the
        /// queue area from the peak queue the simulation reported. The routed
        /// path sizes each pool from its own unit standard instead, so a
        /// facility that sends passengers through counters, kiosks and
        /// bag-drop units is built from the units it actually has.
        /// </summary>
        protected virtual void CalculateAreas()
        {
            if (TryCalculateRoutedAreas())
                return;

            CalculateQueueArea();
            CalculateProcessingArea();
            CalculateSupportArea();
        }

        protected virtual void CalculateQueueArea()
        {
            if (Context.OperationalData.AnalysisMethod == AnalysisMethod.Analytical) return;

            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null && Context.Standards != null)
            {
                queueZone.Area = Context.OperationalData.PeakQueue * Context.Standards.QueueAreaPerPassenger;
            }
        }

        protected virtual void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                double areaPerProcessor = Context.Standards.Width * Context.Standards.Depth;
                procZone.Area = Context.OperationalData.ProcessorCount * areaPerProcessor;
            }
        }

        protected virtual void CalculateSupportArea()
        {
            var supportZone = GetZone(ZoneType.Support);
            if (supportZone != null && Context.Standards != null)
            {
                var procZone = GetZone(ZoneType.Processing);
                double processingArea = procZone != null ? procZone.Area : 0.0;
                supportZone.Area = processingArea * (Context.Standards.SupportPercent / 100.0);
            }
        }

        /// <summary>
        /// Areas that are added as a lump rather than sized from a count: a
        /// reconciliation room, an isolation suite, a store. They sit in
        /// their own zone so they are never confused with the MEP allowance.
        /// </summary>
        protected virtual void CalculateFixedAreas()
        {
            FacilityAreaStandardSet standards = Context.AreaStandards;

            if (standards == null || standards.FixedAreas == null)
                return;

            double total = 0.0;

            foreach (NamedArea area in standards.FixedAreas)
            {
                if (area == null)
                    continue;

                total += Math.Max(0.0, area.Area);

                Result.AreaLines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Fixed: {0} = {1:0} m2",
                    string.IsNullOrWhiteSpace(area.Name) ? "(unnamed)" : area.Name,
                    area.Area));
            }

            SetZone(ZoneType.Fixed, total);
        }

        /// <summary>
        /// Net area to gross: circulation, then mechanical, then structure.
        ///
        /// The three are applied in that order and each one is a share of the
        /// area that already includes the ones before it, so they compound.
        /// Adding them to the net instead would understate the gross.
        ///
        /// The percentages come from the area standards when they are
        /// supplied, and from the facility's own standards otherwise, so a
        /// definition that has not been given MEP or structure gets neither.
        /// </summary>
        protected virtual void CalculateGrossUp()
        {
            double net = ZoneArea(ZoneType.Queue) +
                         ZoneArea(ZoneType.Processing) +
                         ZoneArea(ZoneType.Support) +
                         ZoneArea(ZoneType.Fixed);

            if (Context.ExpansionFactor > 0.0)
                net *= (1.0 + Context.ExpansionFactor);

            GrossArea gross = AreaGrossUp.Calculate(
                net, CirculationPercent, MepPercent, StructurePercent);

            SetZone(ZoneType.Circulation, gross.CirculationArea);
            SetZone(ZoneType.Mechanical, gross.MepArea);
            SetZone(ZoneType.Structure, gross.StructureArea);

            Result.NetArea = gross.Net;
            Result.CirculationArea = gross.CirculationArea;
            Result.MepArea = gross.MepArea;
            Result.StructureArea = gross.StructureArea;
            Result.GrossArea = gross.Total;

            Result.AreaLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Net = {0:0} m2 (queue {1:0} + processing {2:0} + support {3:0} + fixed {4:0})",
                net,
                ZoneArea(ZoneType.Queue),
                ZoneArea(ZoneType.Processing),
                ZoneArea(ZoneType.Support),
                ZoneArea(ZoneType.Fixed)));

            Result.AreaLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "+ {0:0.#}% circulation = {1:0} m2",
                CirculationPercent,
                ZoneArea(ZoneType.Circulation)));

            Result.AreaLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "+ {0:0.#}% MEP = {1:0} m2",
                MepPercent,
                gross.MepArea));

            Result.AreaLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "+ {0:0.#}% structure = {1:0} m2",
                StructurePercent,
                gross.StructureArea));

            Result.AreaLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Gross = {0:0} m2",
                Result.GrossArea));
        }

        /// <summary>Circulation as a percentage of the net area.</summary>
        protected double CirculationPercent
        {
            get
            {
                if (Context.AreaStandards != null)
                    return Context.AreaStandards.CirculationPercent;

                return Context.Standards != null ? Context.Standards.CirculationPercent : 0.0;
            }
        }

        /// <summary>Mechanical allowance as a percentage of the circulated area.</summary>
        protected double MepPercent
        {
            get { return Context.AreaStandards != null ? Context.AreaStandards.MepPercent : 0.0; }
        }

        /// <summary>Structure allowance as a percentage of the MEP area.</summary>
        protected double StructurePercent
        {
            get { return Context.AreaStandards != null ? Context.AreaStandards.StructurePercent : 0.0; }
        }

        /// <summary>
        /// Whether this facility can be sized from routed pool standards.
        ///
        /// Only a facility whose area is the units it has, plus the queue in
        /// front of them, can be sized that way. A facility with equipment
        /// of its own - boarding lounges, security screening booths, retail
        /// zones - keeps its own rule until it has a routed design of its own.
        /// </summary>
        protected virtual bool SupportsRoutedPoolAreas
        {
            get { return false; }
        }

        /// <summary>
        /// Sizes each routed pool from its own unit standard.
        ///
        /// Returns false - and leaves the facility's own rules in place - when
        /// routed sizing was not asked for, when the facility does not support
        /// it, when no standards were supplied, or when no pool matched a
        /// standard. The warnings say which.
        /// </summary>
        protected bool TryCalculateRoutedAreas()
        {
            if (!Context.UseRoutedPoolAreas)
                return false;

            if (!SupportsRoutedPoolAreas)
            {
                Result.Warnings.Add(
                    "Routed pool areas are not available for this facility type, so its own sizing rules were used.");

                return false;
            }

            FacilityAreaStandardSet standards = Context.AreaStandards;

            if (standards == null)
            {
                Result.Warnings.Add(
                    "Routed pool areas were requested but no area standards were supplied, so the facility's own sizing rules were used.");

                return false;
            }

            FacilityAreaInput input = new FacilityAreaInput
            {
                PoolNames = Context.PoolNames ?? new List<string>(),
                PhysicalPerPool = Context.PhysicalPerPool ?? new List<int>(),
                ActivePerPool = Context.ActivePerPool ?? new List<int>(),
                FacilityKeyPerPool = Context.FacilityKeyPerPool ?? new List<string>(),
                NoPhysicalAreaPerPool = Context.NoPhysicalAreaPerPool ?? new List<bool>(),
                Pools = Context.ProcessorPools ?? new List<Core.Models.ProcessorPoolInfo>(),
                PeakQueueByChannel = Context.PeakQueueByChannel ??
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                GlobalPeakQueue = Context.GlobalPeakQueue,
                Standards = standards,
                FallbackAreaPerUnit = FallbackAreaPerUnit(),
                FallbackQueueSpacePerPax = FallbackQueueSpacePerPax()
            };

            FacilityAreaResult area = FacilityAreaCalculator.Calculate(input);

            if (!area.UsedStandards || area.Lines.Count == 0)
            {
                Result.Warnings.Add(
                    "Routed pool areas were requested but no pool matched an area standard, so the facility's own sizing rules were used.");

                foreach (string warning in area.Warnings)
                    Result.Warnings.Add(warning);

                return false;
            }

            SetZone(ZoneType.Queue, area.QueueArea);
            SetZone(ZoneType.Processing, area.EquipmentArea);

            CalculateSupportArea();

            Result.PoolAreas.AddRange(area.Describe());

            // The facility's own equipment rule is not run in this mode, so
            // the counts it would have produced are stated per pool here.
            foreach (FacilityAreaLine line in area.Lines)
            {
                string key = string.IsNullOrEmpty(line.FacilityKey)
                    ? line.PoolName
                    : line.FacilityKey;

                Result.EquipmentCounts[key + " units (installed)"] = line.PhysicalUnits;
                Result.EquipmentCounts[key + " units (staffed)"] = line.ActiveUnits;
            }

            foreach (string warning in area.Warnings)
                Result.Warnings.Add(warning);

            return true;
        }

        /// <summary>
        /// The facility's own single-unit footprint, used for a pool no
        /// standard matched so the pool is still sized rather than dropped.
        /// </summary>
        private double FallbackAreaPerUnit()
        {
            if (Context.Standards == null)
                return 0.0;

            return Context.Standards.Width * Context.Standards.Depth;
        }

        private double FallbackQueueSpacePerPax()
        {
            if (Context.AreaStandards != null && Context.AreaStandards.DefaultQueueSpacePerPax > 0.0)
                return Context.AreaStandards.DefaultQueueSpacePerPax;

            return Context.Standards != null ? Context.Standards.QueueAreaPerPassenger : 0.0;
        }

        protected virtual FacilitySizingResult BuildResult()
        {
            Result.QueueArea = ZoneArea(ZoneType.Queue);
            Result.ProcessingArea = ZoneArea(ZoneType.Processing);
            Result.SupportArea = ZoneArea(ZoneType.Support);
            Result.CirculationArea = ZoneArea(ZoneType.Circulation);

            // The gross total is the buildable area: net, then circulation,
            // then mechanical, then structure.
            Result.TotalArea = Result.GrossArea;

            Result.ProcessorCount = Context.OperationalData.ProcessorCount;
            Result.PeakDemand = Context.PassengerSummary.TotalPassengers;

            // 🚨 THE FIX: Save the baseline into the result!
            Result.BaselineProcessorCount = Context.OperationalData.BaselineProcessorCount;

            return Result;
        }
    }
}
