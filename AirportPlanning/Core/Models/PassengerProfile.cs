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
        /// Optional transfer connection type. When assigned, generated
        /// passengers are tagged as transfers of this flow.
        /// </summary>
        public TransferScenario? TransferScenario { get; set; }

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

        /// <summary>
        /// Legacy Gaussian arrival profile. Only the legacy Passenger Generator
        /// consumes it; the windowed and schedule-driven generators ignore it.
        /// </summary>
        public ArrivalProfile ArrivalProfile { get; set; }

        /// <summary>
        /// Departure show-up curve used by the schedule-driven generator. It is
        /// stretched over this profile's MinAdvance/MaxAdvance interval. Null
        /// means the generator's fallback curve is used.
        /// </summary>
        public ShowUpProfile DepartureShowUp { get; set; }

        public double MaleProbability { get; set; }

        /// <summary>
        /// Earliest time before scheduled departure that this profile reaches its
        /// selected arrival reference (minutes). In schedule mode this is the end
        /// of the CDF interval (STD - MinAdvance).
        /// </summary>
        public double MinAdvanceMinutes { get; set; } = 60.0;

        /// <summary>
        /// Latest time before scheduled departure that this profile reaches its
        /// selected arrival reference (minutes). In schedule mode this is the
        /// start of the CDF interval (STD - MaxAdvance).
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
