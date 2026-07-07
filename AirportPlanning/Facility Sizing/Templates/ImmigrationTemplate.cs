using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class ImmigrationTemplate : FacilityTemplate
    {
        public ImmigrationTemplate() : base()
        {
            AvailableTopologies = new List<string> { "Linear", "Staggered", "E-Gate Bank" };
            PreferredTopology = "Linear";
            PreferredAxis = Models.PreferredAxis.Minor;
            Relationship = Models.Relationship.Security;
        }
    }
}
