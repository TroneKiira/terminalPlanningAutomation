using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class BagDropTemplate : FacilityTemplate
    {
        public BagDropTemplate() : base()
        {
            AvailableTopologies = new List<string> { "Island", "Linear", "Retrofit" };
            PreferredTopology = "Island";
            PreferredAxis = Models.PreferredAxis.Major;
            Relationship = Models.Relationship.Security;
        }
    }
}
