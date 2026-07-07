using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class PassengerSummaryData
    {
        public int TotalPassengers { get; set; }
        public int DomesticPassengers { get; set; }
        public int InternationalPassengers { get; set; }
        public int CheckedBagPassengers { get; set; }
        public int CabinBagPassengers { get; set; }
        public int TransferPassengers { get; set; }

        // NEW
        public int MalePassengers { get; set; }
        public int FemalePassengers { get; set; }
    }
}
