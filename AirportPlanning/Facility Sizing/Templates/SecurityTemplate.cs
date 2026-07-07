using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class SecurityTemplate : FacilityTemplate
    {
        public SecurityTemplate() : base()
        {
            AvailableTopologies = new System.Collections.Generic.List<string> { "Centralized", "Decentralized" };
            PreferredTopology = "Centralized"; // The default
            PreferredAxis = Models.PreferredAxis.Major;
            Relationship = Models.Relationship.DepartureLounge;
        }
    }
}
