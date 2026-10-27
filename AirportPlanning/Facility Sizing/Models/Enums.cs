using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public enum ZoneType
    {
        Queue,
        Processing,
        Support,

        /// <summary>Named areas added as a lump, outside the unit counts.</summary>
        Fixed,

        Circulation,

        /// <summary>Mechanical, electrical and plumbing allowance.</summary>
        Mechanical,

        /// <summary>Structure allowance.</summary>
        Structure
    }

    public enum PreferredAxis
    {
        Major,
        Minor
    }

    public enum AnalysisMethod
    {
        Simulation,
        Analytical
    }

    public enum DesignMode
    {
        Conservative,
        Balanced,
        MaximumCapacity
    }

    public enum Relationship
    {
        None,
        PassengerVerification,
        CheckIn,
        BagDrop,
        Security,
        Emigration,
        DepartureLounge,
        Boarding,
        TerminalEntry,
        Exit
    }
}
