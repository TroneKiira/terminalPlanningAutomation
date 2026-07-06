using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Generation
{
    public class PassengerGenerationResult
    {
        public List<Passenger> Passengers { get; set; }

        public List<ArrivalSimulationResult> ArrivalSimulations { get; set; }

        public PassengerGenerationResult()
        {
            Passengers = new List<Passenger>();
            ArrivalSimulations =
                new List<ArrivalSimulationResult>();
        }
    }
}
