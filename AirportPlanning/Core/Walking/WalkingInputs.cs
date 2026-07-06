using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirportPlanning.Core.Models;


namespace AirportPlanning.Core.Walking
{
    public class WalkingInputs
    {
        public List<Passenger> Passengers { get; set; }

        public double Distance { get; set; }

        public WalkingProfile WalkingProfile { get; set; }

        public string FromFacility { get; set; }

        public string ToFacility { get; set; }

        public WalkingInputs()
        {
            Passengers = new List<Passenger>();

            WalkingProfile = new WalkingProfile
            {
                AverageSpeed = 1.35,
                StandardDeviation = 0.0
            };

            FromFacility = string.Empty;

            ToFacility = string.Empty;
        }
    }
}
