using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Inputs
{
    public class PlanningInputs
    {
        public int AnnualPassengers;

        public double DomesticShare;

        public double DepartureShare;

        public double StaffedDeskPercentage;

        public double CheckInUsagePercentage;

        public double AverageProcessingTime;

        public double MaximumWait;

        public LevelOfService LoS;
    }
     public enum LevelOfService
    {
        A,
        B,
        C,
        D,
        E,
        F
    }
}
