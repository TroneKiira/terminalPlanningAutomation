using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Rhino.UI.Controls.RenderContentMenu;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class PassengerVerificationSizer : FacilitySizerBase
    {
        protected override void CalculateQueueArea()
        {
            // Mathematical sizing only. E-gates assume continuous flow, no static queue area allocated.
            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null)
            {
                queueZone.Area = 0.0;
            }
        }

        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                procZone.Area = Context.OperationalData.ProcessorCount * (Context.Standards.Width * Context.Standards.Depth);
            }
        }
    }
}
