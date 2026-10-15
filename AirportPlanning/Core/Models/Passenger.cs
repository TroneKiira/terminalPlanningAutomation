using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{

    public enum PassengerGender
    {
        Male,
        Female
    }
    public class Passenger
    {
        // ============================================================
        // Passenger Identity
        // ============================================================

        public int Id { get; set; }

        /// <summary>
        /// Name of the passenger profile this passenger was generated from.
        /// The modular key for per-type service times and channel mapping.
        /// </summary>
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>
        /// Assigned service channel (e.g. "Counter", "Kiosk", "Web", "BagDrop").
        /// Assigned at generation; user-defined names, nothing hardcoded.
        /// </summary>
        public string Channel { get; set; } = string.Empty;

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
        // Demand / Window Tagging
        // ============================================================

        /// <summary>
        /// Time step index of the demand window the passenger belongs to
        /// (-1 when not generated from a windowed demand profile).
        /// </summary>
        public int DemandStepIndex { get; set; } = -1;

        /// <summary>
        /// Hour index within the analysis window (-1 when not windowed).
        /// </summary>
        public int DemandHourIndex { get; set; } = -1;

        /// <summary>
        /// True when the passenger belongs to the pre-window warm-up segment.
        /// Warm-up passengers are simulated but excluded from reported metrics.
        /// </summary>
        public bool IsWarmUp { get; set; }

        /// <summary>
        /// True when the passenger belongs to the design hour step. Used to
        /// evaluate design-hour performance downstream.
        /// </summary>
        public bool IsDesignStep { get; set; }

        /// <summary>
        /// Transit wave identifier (-1 when the passenger did not arrive in a
        /// batch/wave).
        /// </summary>
        public int WaveId { get; set; } = -1;

        /// <summary>
        /// The demand type this passenger belongs to (Domestic Departure,
        /// Domestic Arrival, International Departure, International Arrival).
        /// </summary>
        public Inputs.DHPType DHPType { get; set; }

        /// <summary>
        /// Scheduled flight departure time in simulation minutes. Assigned from
        /// the passenger profile's advance range during windowed generation.
        /// </summary>
        public double ScheduledDepartureTime { get; set; }

        /// <summary>
        /// Deterministic fingerprint of the show-up profile that shaped this
        /// passenger's arrival time ("none" when no show-up curve was used).
        /// Carried on the passenger so downstream solver caches can tell a
        /// Leisure run from a Business run even when the realized arrival
        /// times happen to look similar. Preserved by <see cref="Clone"/>.
        /// </summary>
        public string ShowUpSignature { get; set; } = "none";

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
        /// 
        public PassengerGender Gender { get; set; } 
        public void ResetQueueState()
        {
            ArrivalTime = QueueArrivalTime;

            StartServiceTime = 0.0;

            EndServiceTime = 0.0;

            WaitTime = 0.0;

            AssignedCounter = -1;
        }

        // NEW: Deep Clone to prevent Grasshopper pass-by-reference mutation bugs
        public Passenger Clone()
        {
            Passenger p = new Passenger
            {
                Id = this.Id,
                ProfileName = this.ProfileName,
                Channel = this.Channel,
                PassengerType = this.PassengerType,
                Journey = this.Journey,

                //  THIS IS THE CRITICAL LINE THAT SAVES THE FEMALES!
                Gender = this.Gender,

                UsesOnlineCheckIn = this.UsesOnlineCheckIn,
                HasCheckedBag = this.HasCheckedBag,
                ArrivalTime = this.ArrivalTime,
                QueueArrivalTime = this.QueueArrivalTime,
                WalkingSpeed = this.WalkingSpeed,
                WalkingTime = this.WalkingTime,
                ServiceTime = this.ServiceTime,
                StartServiceTime = this.StartServiceTime,
                EndServiceTime = this.EndServiceTime,
                WaitTime = this.WaitTime,
                AssignedCounter = this.AssignedCounter,
                DemandStepIndex = this.DemandStepIndex,
                DemandHourIndex = this.DemandHourIndex,
                IsWarmUp = this.IsWarmUp,
                IsDesignStep = this.IsDesignStep,
                WaveId = this.WaveId,
                DHPType = this.DHPType,
                ScheduledDepartureTime = this.ScheduledDepartureTime,
                ShowUpSignature = this.ShowUpSignature,
                History = new Dictionary<FacilityType, FacilityRecord>()
            };

            foreach (var kvp in this.History)
            {
                p.History.Add(kvp.Key, kvp.Value.Clone());
            }

            return p;
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
