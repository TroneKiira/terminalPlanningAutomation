using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public PassengerProfile()
        {
            Percentage = 0;
            OnlineCheckInProbability = 0;
            CheckedBagProbability = 0;
            ServiceTimeMultiplier = 1.0;
        }
    }
}
