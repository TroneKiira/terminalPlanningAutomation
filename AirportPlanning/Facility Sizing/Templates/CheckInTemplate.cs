using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class CheckInTemplate : FacilityTemplate
    {
        public CheckInTemplate() : base()
        {
            AvailableTopologies = new System.Collections.Generic.List<string> { "Island", "Linear", "Flow-Through" };
            PreferredTopology = "Island"; // The default
            PreferredAxis = Models.PreferredAxis.Major;
            Relationship = Models.Relationship.Security;
        }
    }
}
