using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class Passenger
    {
        // ============================================================
        // Passenger Identity
        // ============================================================

        public int Id { get; set; }

        // ============================================================
        // Passenger Profile
        // ============================================================

        public PassengerType PassengerType { get; set; }

        public PassengerJourney Journey { get; set; }

        public bool UsesOnlineCheckIn { get; set; }

        public bool HasCheckedBag { get; set; }

        // ============================================================
        // Passenger Movement
        // ============================================================

        /// <summary>
        /// Current arrival time at the active facility.
        /// This value advances as the passenger moves through
        /// the terminal.
        /// </summary>
        public double ArrivalTime { get; set; }

        /// <summary>
        /// Arrival time at the current facility.
        /// Used by the optimizer to reset passengers between
        /// optimization iterations.
        /// </summary>
        public double QueueArrivalTime { get; set; }

        public double WalkingSpeed { get; set; }

        public double WalkingTime { get; set; }

        // ============================================================
        // Queue Simulation
        // ============================================================

        public double ServiceTime { get; set; }

        public double StartServiceTime { get; set; }

        public double EndServiceTime { get; set; }

        public double WaitTime { get; set; }

        public int AssignedCounter { get; set; } = -1;

        /// <summary>
        /// Stores a permanent record of the passenger's wait times and service times 
        /// at every facility they visit.
        /// </summary>
        public Dictionary<FacilityType, FacilityRecord> History { get; set; } = new Dictionary<FacilityType, FacilityRecord>();

        // ============================================================
        // Passenger Behaviour
        // ============================================================

        public bool RequiresCheckIn
        {
            get
            {
                return !UsesOnlineCheckIn;
            }
        }


        public bool RequiresBagDrop
        {
            get
            {
                return UsesOnlineCheckIn &&
                       HasCheckedBag;
            }
        }

        // ============================================================
        // Utilities
        // ============================================================

        /// <summary>
        /// Restores the passenger to the state it had when it
        /// entered the current facility.
        /// Used during queue optimization.
        /// </summary>
        public void ResetQueueState()
        {
            ArrivalTime = QueueArrivalTime;

            StartServiceTime = 0.0;

            EndServiceTime = 0.0;

            WaitTime = 0.0;

            AssignedCounter = -1;
        }
    }

    public class PassengerAllocation
    {
        public PassengerProfile Profile { get; set; }

        public int PassengerCount { get; set; }
    }

    public enum PassengerType
    {
        Domestic,
        International
    }

    public enum PassengerJourney
    {
        Origin,
        Transfer,
        Destination
    }
}
