using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Analysis
{
    /// <summary>
    /// Pure, headless passenger segregation used by the Passenger Filter
    /// component and the test harness.
    ///
    /// Two independent dimensions are split:
    ///
    ///   - <see cref="DHPType"/>: the flight leg - domestic or international,
    ///     departure or arrival. <see cref="PassengerType"/> is the sector
    ///     only; the leg direction lives on <see cref="Passenger.DHPType"/>.
    ///   - <see cref="PassengerJourney"/>: the journey role - Origin,
    ///     Transfer or Destination.
    ///
    /// The dimensions overlap by design: a transfer departure belongs to both
    /// the Transfer flow and the Departures flow. Within one dimension the
    /// split is always an exact partition: no passenger is lost or duplicated.
    /// </summary>
    public static class PassengerSegregation
    {
        // ---- flight leg (DHPType) ----------------------------------------

        public static List<Passenger> DomesticDepartures(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.DomesticDeparture);
        }

        public static List<Passenger> InternationalDepartures(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.InternationalDeparture);
        }

        public static List<Passenger> DomesticArrivals(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.DomesticArrival);
        }

        public static List<Passenger> InternationalArrivals(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.InternationalArrival);
        }

        /// <summary>Every departing passenger, domestic or international.</summary>
        public static List<Passenger> Departures(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.DomesticDeparture, DHPType.InternationalDeparture);
        }

        /// <summary>Every arriving passenger, domestic or international.</summary>
        public static List<Passenger> Arrivals(IEnumerable<Passenger> passengers)
        {
            return WhereDhp(passengers, DHPType.DomesticArrival, DHPType.InternationalArrival);
        }

        // ---- journey role (PassengerJourney) ------------------------------

        public static List<Passenger> Origin(IEnumerable<Passenger> passengers)
        {
            return WhereJourney(passengers, PassengerJourney.Origin);
        }

        public static List<Passenger> Transfer(IEnumerable<Passenger> passengers)
        {
            return WhereJourney(passengers, PassengerJourney.Transfer);
        }

        public static List<Passenger> Destination(IEnumerable<Passenger> passengers)
        {
            return WhereJourney(passengers, PassengerJourney.Destination);
        }

        // ---- sector (PassengerType) - the legacy filter outputs -----------

        public static List<Passenger> Domestic(IEnumerable<Passenger> passengers)
        {
            return passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null && p.PassengerType == PassengerType.Domestic).ToList();
        }

        public static List<Passenger> International(IEnumerable<Passenger> passengers)
        {
            return passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null && p.PassengerType == PassengerType.International).ToList();
        }

        // ---- helpers -------------------------------------------------------

        private static List<Passenger> WhereDhp(
            IEnumerable<Passenger> passengers,
            params DHPType[] types)
        {
            if (passengers == null || types == null || types.Length == 0)
                return new List<Passenger>();

            return passengers
                .Where(p => p != null && types.Contains(p.DHPType))
                .ToList();
        }

        private static List<Passenger> WhereJourney(
            IEnumerable<Passenger> passengers,
            PassengerJourney journey)
        {
            return passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null && p.Journey == journey).ToList();
        }
    }
}
