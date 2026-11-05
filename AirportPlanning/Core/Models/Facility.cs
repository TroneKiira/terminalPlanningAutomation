using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class FacilityRecord
    {
        public double ArrivalTime { get; set; }
        public double StartServiceTime { get; set; }
        public double EndServiceTime { get; set; }
        public double WaitTime { get; set; }
        public int AssignedResource { get; set; }

        /// <summary>
        /// Server pool that served the passenger at this facility, for example
        /// "Counter|DomesticDeparture|Economy" or "Shared". Unlike
        /// <see cref="Passenger.AssignedPool"/>, which only holds the most
        /// recent facility, this keeps the pool for this visit so the whole
        /// journey stays readable. Empty when the visit recorded no pool.
        /// </summary>
        public string PoolName { get; set; } = string.Empty;

        /// <summary>
        /// True when this visit carries an upstream stage recorded alongside
        /// it - security's series mode queues the same passengers through the
        /// entry e-gates (this record) before the screening lanes, and only
        /// the screening visit becomes the facility's own record.
        /// </summary>
        public bool HasEntryStage { get; set; }

        /// <summary>Arrival at the entry stage (series facilities only).</summary>
        public double EntryArrivalTime { get; set; }

        /// <summary>Service start at the entry stage (series facilities only).</summary>
        public double EntryStartServiceTime { get; set; }

        /// <summary>Service end at the entry stage (series facilities only).</summary>
        public double EntryEndServiceTime { get; set; }

        /// <summary>Queue wait at the entry stage (series facilities only).</summary>
        public double EntryWaitTime { get; set; }

        /// <summary>Entry-stage resource that served the passenger (series facilities only).</summary>
        public int EntryAssignedResource { get; set; }

        /// <summary>Entry-stage pool identity, for example "EGate|DomesticDeparture" (series facilities only).</summary>
        public string EntryPoolName { get; set; } = string.Empty;

        // NEW: Deep clone method
        public FacilityRecord Clone()
        {
            return new FacilityRecord
            {
                ArrivalTime = this.ArrivalTime,
                StartServiceTime = this.StartServiceTime,
                EndServiceTime = this.EndServiceTime,
                WaitTime = this.WaitTime,
                AssignedResource = this.AssignedResource,
                PoolName = this.PoolName,
                HasEntryStage = this.HasEntryStage,
                EntryArrivalTime = this.EntryArrivalTime,
                EntryStartServiceTime = this.EntryStartServiceTime,
                EntryEndServiceTime = this.EntryEndServiceTime,
                EntryWaitTime = this.EntryWaitTime,
                EntryAssignedResource = this.EntryAssignedResource,
                EntryPoolName = this.EntryPoolName
            };
        }
    }

    public enum FacilityType
    {
        CheckIn,
        BagDrop,
        Security,
        Emigration,
        PassengerVerification,
        DepartureLounge, // NEW: Added for retail dwell time
        Boarding,
        TerminalEntry,
        Immigration, // Arrivals passport control; also serves I→D transfer flows
        TransferDesk, // Arrival-side transfer desk; serves transfer flows whose scenario rules require it
        BaggageRecheck // Arrival-side baggage recheck; serves transfer flows whose scenario rules require it
    }
}
