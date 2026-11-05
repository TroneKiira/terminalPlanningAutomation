using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirportPlanning.Core.Inputs;

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
        /// Assigned PRIMARY check-in method (e.g. "Counter", "Kiosk", "Web").
        /// Bag Drop is a separate service stage, never a primary channel here.
        /// Assigned at generation; user-defined names, nothing hardcoded.
        /// </summary>
        public string Channel { get; set; } = string.Empty;

        // ============================================================
        // Passenger Profile
        // ============================================================

        public PassengerType PassengerType { get; set; }

        public PassengerJourney Journey { get; set; }

        /// <summary>The inbound-sector to outbound-sector flow, when known.</summary>
        public TransferScenario? TransferScenario { get; set; }

        /// <summary>
        /// Scenario processing rules resolved during generation. Null keeps
        /// legacy check-in and bag-drop behavior unchanged.
        /// </summary>
        public TransferScenarioRules TransferProcessing { get; set; }

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
        /// Scheduled flight departure time in simulation minutes. In schedule
        /// mode this is the flight leg's actual STD, not a sampled time.
        /// </summary>
        public double ScheduledDepartureTime { get; set; }

        /// <summary>
        /// Arrival at the terminal-entry checkpoint in simulation minutes.
        /// NaN when the passenger was not generated on a terminal-entry basis.
        /// </summary>
        public double TerminalEntryArrivalTime { get; set; } = double.NaN;

        /// <summary>Walk from terminal entry to check-in, in minutes.</summary>
        public double TerminalEntryWalkMinutes { get; set; }

        /// <summary>The route assigned at terminal entry, e.g. Manual Verification or DigiYatra.</summary>
        public string TerminalEntryRoute { get; set; } = string.Empty;

        /// <summary>The processor route assigned at emigration, e.g. EGate or Counter.</summary>
        public string EmigrationRoute { get; set; } = string.Empty;

        /// <summary>
        /// Route through the security entry stage, for example "EGate". Empty
        /// when security ran as one stage (series mode off).
        /// </summary>
        public string SecurityEntryRoute { get; set; } = string.Empty;

        /// <summary>
        /// Route through the security screening stage, for example "ATRS" or
        /// "Transfer Security". Empty when security ran as one stage.
        /// </summary>
        public string SecurityScreeningRoute { get; set; } = string.Empty;

        /// <summary>Queue wait at the security entry stage, in minutes.</summary>
        public double SecurityEntryWait { get; set; }

        /// <summary>Queue wait at the security screening stage, in minutes.</summary>
        public double SecurityScreeningWait { get; set; }

        /// <summary>
        /// Scheduled flight arrival time in simulation minutes for arrival-leg
        /// passengers. Zero for departure passengers and unscheduled runs.
        /// </summary>
        public double ScheduledArrivalTime { get; set; }

        /// <summary>Stable flight-leg identity from the schedule import.</summary>
        public string FlightLegId { get; set; } = string.Empty;

        /// <summary>Shared identity of the arrival/departure turnaround.</summary>
        public string FlightLinkId { get; set; } = string.Empty;

        public string Airline { get; set; } = string.Empty;

        public string FlightNumber { get; set; } = string.Empty;

        /// <summary>
        /// Check-in counter opening time for this passenger's flight, in
        /// absolute schedule minutes. NaN when no window was applied.
        /// </summary>
        public double CheckInOpenTime { get; set; } = double.NaN;

        /// <summary>
        /// Last-admission time for this passenger's flight, in absolute
        /// schedule minutes. NaN when no window was applied.
        /// </summary>
        public double CheckInLastAdmissionTime { get; set; } = double.NaN;

        /// <summary>
        /// True when the projected check-in arrival is after last admission.
        /// The passenger's time is retained and flagged; it is not silently
        /// pulled forward.
        /// </summary>
        public bool IsLateForCheckIn { get; set; }

        /// <summary>
        /// True when the projected check-in arrival is before the counters open.
        /// The passenger may wait for service to begin.
        /// </summary>
        public bool ArrivedBeforeCheckInOpen { get; set; }

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
        /// Identity of the server pool that served this passenger in the most
        /// recent facility solve, for example
        /// "Counter|DomesticDeparture|Economy" or "Shared". Empty when the
        /// result predates pool-level allocation. This keeps a passenger tied
        /// to the exact pool when several pools share one channel.
        /// </summary>
        public string AssignedPool { get; set; } = string.Empty;

        /// <summary>
        /// Stores a permanent record of the passenger's wait times and service times 
        /// at every facility they visit.
        /// </summary>
        public Dictionary<FacilityType, FacilityRecord> History { get; set; } = new Dictionary<FacilityType, FacilityRecord>();

        // ============================================================
        // Passenger Behaviour
        // ============================================================

        /// <summary>
        /// True when the passenger must visit a physical Check-In processor.
        /// Web check-in has no queue. Counter and Kiosk are physical methods,
        /// even when the profile also has an online-check-in probability, so
        /// the assigned primary channel is the source of truth.
        /// </summary>
        public bool RequiresCheckIn
        {
            get
            {
                if (DHPType != DHPType.DomesticDeparture &&
                    DHPType != DHPType.InternationalDeparture)
                {
                    return false;
                }

                if (TransferProcessing != null)
                    return TransferProcessing.RequiresCheckIn;

                if (DefaultChannelAssigner.IsWebChannel(Channel))
                    return false;

                if (DefaultChannelAssigner.IsCounterChannel(Channel) ||
                    DefaultChannelAssigner.IsKioskChannel(Channel))
                    return true;

                // Legacy/unassigned channel: preserve the old online rule.
                return !UsesOnlineCheckIn;
            }
        }

        /// <summary>
        /// True when the passenger's checked bag must be handled somewhere.
        /// This includes bags accepted at a staffed counter.
        /// </summary>
        public bool RequiresBagDrop
        {
            get
            {
                if (DHPType != DHPType.DomesticDeparture &&
                    DHPType != DHPType.InternationalDeparture)
                {
                    return false;
                }

                return TransferProcessing != null
                    ? TransferProcessing.RequiresBagDrop
                    : HasCheckedBag;
            }
        }

        /// <summary>
        /// True when the bag must be dropped at a SEPARATE Bag Drop facility.
        /// A staffed counter accepts the bag as part of check-in; Web and Kiosk
        /// passengers with a checked bag continue to the Bag Drop stage.
        /// A transfer rule that requires bag drop always uses the separate
        /// stage, whatever primary channel it was assigned.
        /// </summary>
        public bool RequiresSeparateBagDrop
        {
            get
            {
                if (!RequiresBagDrop)
                    return false;

                if (TransferProcessing != null)
                    return true;

                return !DefaultChannelAssigner.IsCounterChannel(Channel);
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
            AssignedPool = string.Empty;
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
                TransferScenario = this.TransferScenario,
                TransferProcessing = this.TransferProcessing != null
                    ? this.TransferProcessing.Clone()
                    : null,

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
                AssignedPool = this.AssignedPool,
                DemandStepIndex = this.DemandStepIndex,
                DemandHourIndex = this.DemandHourIndex,
                IsWarmUp = this.IsWarmUp,
                IsDesignStep = this.IsDesignStep,
                WaveId = this.WaveId,
                DHPType = this.DHPType,
                ScheduledDepartureTime = this.ScheduledDepartureTime,
                TerminalEntryArrivalTime = this.TerminalEntryArrivalTime,
                TerminalEntryWalkMinutes = this.TerminalEntryWalkMinutes,
                TerminalEntryRoute = this.TerminalEntryRoute,
                EmigrationRoute = this.EmigrationRoute,
                SecurityEntryRoute = this.SecurityEntryRoute,
                SecurityScreeningRoute = this.SecurityScreeningRoute,
                SecurityEntryWait = this.SecurityEntryWait,
                SecurityScreeningWait = this.SecurityScreeningWait,
                ScheduledArrivalTime = this.ScheduledArrivalTime,
                FlightLegId = this.FlightLegId,
                FlightLinkId = this.FlightLinkId,
                Airline = this.Airline,
                FlightNumber = this.FlightNumber,
                CheckInOpenTime = this.CheckInOpenTime,
                CheckInLastAdmissionTime = this.CheckInLastAdmissionTime,
                IsLateForCheckIn = this.IsLateForCheckIn,
                ArrivedBeforeCheckInOpen = this.ArrivedBeforeCheckInOpen,
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
