using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
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
            CalculateQueueArea();
            CalculateProcessingArea();
            CalculateSupportArea();
            CalculateCirculationArea();

            return BuildResult();
        }

        protected virtual void Prepare() { }

        protected virtual void ReadContext() { }

        protected virtual void CreateZones()
        {
            Zones.Add(new FunctionalZone(ZoneType.Queue));
            Zones.Add(new FunctionalZone(ZoneType.Processing));
            Zones.Add(new FunctionalZone(ZoneType.Support));
            Zones.Add(new FunctionalZone(ZoneType.Circulation));
        }

        protected FunctionalZone GetZone(ZoneType type)
        {
            return Zones.FirstOrDefault(z => z.ZoneType == type);
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

        protected virtual void CalculateCirculationArea()
        {
            var circZone = GetZone(ZoneType.Circulation);
            if (circZone != null && Context.Standards != null)
            {
                double subtotal = (GetZone(ZoneType.Queue)?.Area ?? 0) +
                                  (GetZone(ZoneType.Processing)?.Area ?? 0) +
                                  (GetZone(ZoneType.Support)?.Area ?? 0);

                circZone.Area = subtotal * (Context.Standards.CirculationPercent / 100.0);
            }
        }

        protected virtual FacilitySizingResult BuildResult()
        {
            Result.QueueArea = GetZone(ZoneType.Queue)?.Area ?? 0;
            Result.ProcessingArea = GetZone(ZoneType.Processing)?.Area ?? 0;
            Result.SupportArea = GetZone(ZoneType.Support)?.Area ?? 0;
            Result.CirculationArea = GetZone(ZoneType.Circulation)?.Area ?? 0;

            Result.TotalArea = Result.QueueArea + Result.ProcessingArea + Result.SupportArea + Result.CirculationArea;

            if (Context.Settings != null && Context.Settings.ExpansionFactor > 0)
            {
                Result.TotalArea *= (1.0 + Context.Settings.ExpansionFactor);
            }

            if (Context.Template != null)
            {
                Result.PreferredTopology = Context.Template.PreferredTopology;
                Result.PreferredAxis = Context.Template.PreferredAxis;
                Result.PlanningAdvice.AddRange(Context.Template.PlanningAdvice);
            }

            return Result;
        }
    }
}
