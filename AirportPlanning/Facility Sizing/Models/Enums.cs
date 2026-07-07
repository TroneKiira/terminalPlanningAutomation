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
        Circulation
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
        Immigration,
        DepartureLounge,
        Boarding,
        Exit
    }
}
