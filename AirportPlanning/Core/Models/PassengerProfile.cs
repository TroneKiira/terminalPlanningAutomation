using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.CheckIn;

namespace AirportPlanning.Core.Models
{
    public class PassengerProfile
    {
        /// <summary>
        /// Profile name (e.g. Domestic Business, International Leisure)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Percentage of the total passenger demand represented by this profile (0-100)
        /// </summary>
        public double Percentage { get; set; }

        /// <summary>
        /// Domestic or International passenger
        /// </summary>
    

        /// <summary>
        /// Origin, Transfer or Destination passenger
        /// </summary>
        public PassengerJourney Journey { get; set; }

        /// <summary>
        /// Probability that a passenger has already checked in online (0-1)
        /// </summary>
        public double OnlineCheckInProbability { get; set; }

        /// <summary>
        /// Probability that a passenger is travelling with checked baggage (0-1)
        /// </summary>
        public double CheckedBagProbability { get; set; }

        /// <summary>
        /// Relative service time multiplier for this passenger type.
        /// Example:
        /// 1.0 = average
        /// 1.2 = 20% slower
        /// 0.8 = 20% faster
        /// </summary>
        public double ServiceTimeMultiplier { get; set; }

        public ArrivalProfile ArrivalProfile { get; set; }

        public double MaleProbability { get; set; }

        /// <summary>
        /// Minimum time before scheduled departure that this profile arrives at
        /// the terminal (minutes). Used to assign a per-passenger departure time
        /// in windowed generation.
        /// </summary>
        public double MinAdvanceMinutes { get; set; } = 60.0;

        /// <summary>
        /// Maximum time before scheduled departure that this profile arrives at
        /// the terminal (minutes).
        /// </summary>
        public double MaxAdvanceMinutes { get; set; } = 150.0;

        public PassengerProfile()
        {
            Percentage = 0;
            OnlineCheckInProbability = 0;
            CheckedBagProbability = 0;
            MaleProbability = 0.5; // Default to 50/50
            ServiceTimeMultiplier = 1.0;
            MinAdvanceMinutes = 60.0;
            MaxAdvanceMinutes = 150.0;
        }
    }
}
