using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public class BoardingTemplate : FacilityTemplate
    {
        public BoardingTemplate() : base()
        {
            // Define the architectural layouts available for Boarding Gates
            AvailableTopologies = new List<string>
            {
                "Gate Lounge",   // Enclosed area specific to one gate
                "Holdroom",      // Shared open seating area for multiple gates
                "Open Plan",     // Integrated with retail/circulation (common in Europe)
                "Pier"           // Long corridor seating
            };

            // Set the defaults
            PreferredTopology = "Gate Lounge";
            PreferredAxis = PreferredAxis.Minor; // Usually perpendicular to the main terminal axis
            Relationship = Relationship.Exit;    // Leads to the Aircraft/Jetbridge
        }
    }
}
