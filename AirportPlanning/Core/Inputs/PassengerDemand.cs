using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Inputs
{
    public class PassengerDemand
    {
        public int DesignHourDemand { get; set; }

        public double DeparturePercentage { get; set; }

        public double DomesticPercentage { get; set; }

        public int DeparturePassengers
        {
            get
            {
                return (int)Math.Round(
                    DesignHourDemand *
                    DeparturePercentage / 100.0);
            }
        }

        public int DomesticPassengers
        {
            get
            {
                return (int)Math.Round(
                    DeparturePassengers *
                    DomesticPercentage / 100.0);
            }
        }

        public int InternationalPassengers
        {
            get
            {
                return DeparturePassengers -
                       DomesticPassengers;
            }
        }
    }
}
