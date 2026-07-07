using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class PassengerVerificationTemplate : FacilityTemplate
    {
        public PassengerVerificationTemplate() : base()
        {
            PreferredTopology = "Linear";
            PreferredAxis = Models.PreferredAxis.Major;
            Relationship = Models.Relationship.Security;
        }
    }
}
