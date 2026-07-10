using System;
using System.Collections.Generic;
using AirportPlanning.Facility_Sizing.Models;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class RetailTemplate : FacilityTemplate
    {
        public RetailTemplate() : base()
        {
            AvailableTopologies = new List<string>
            {
                "Walk-Through",
                "Central Plaza",
                "Decentralized Nodes"
            };

            PreferredTopology = "Walk-Through";
            PreferredAxis = PreferredAxis.Major;
            Relationship = Relationship.Security; // Sits immediately after the security bottleneck
        }
    }
}
