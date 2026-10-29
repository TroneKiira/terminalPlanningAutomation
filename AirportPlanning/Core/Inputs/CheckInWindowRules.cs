using System;
using System.Collections.Generic;
using AirportPlanning.Core.Schedules;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// When check-in opens and when the last passenger may be admitted, both
    /// expressed as minutes before scheduled departure.
    ///
    /// Closing is the last-admission time: a passenger admitted before it may
    /// finish service. It is not the moment the counters stop working.
    /// </summary>
    public class CheckInWindow
    {
        public double OpensAt { get; set; }

        public double LastAdmissionAt { get; set; }

        public CheckInWindow(double opensAt, double lastAdmissionAt)
        {
            OpensAt = opensAt;
            LastAdmissionAt = lastAdmissionAt;
        }
    }

    /// <summary>
    /// One optional airline or flight override of the sector defaults. A blank
    /// flight number applies to every flight of that airline.
    /// </summary>
    public class CheckInWindowOverride
    {
        public string Airline { get; set; } = string.Empty;

        public string FlightNumber { get; set; } = string.Empty;

        public double OpenMinutesBeforeDeparture { get; set; }

        public double LastAdmissionMinutesBeforeDeparture { get; set; }

        public bool Matches(FlightLeg leg)
        {
            if (leg == null)
                return false;

            if (!string.Equals(
                    (Airline ?? string.Empty).Trim(),
                    (leg.Airline ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string flight = (FlightNumber ?? string.Empty).Trim();

            return flight.Length == 0 ||
                   flight == "*" ||
                   string.Equals(
                       flight,
                       (leg.FlightNumber ?? string.Empty).Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sector defaults plus optional airline/flight overrides. The defaults are
    /// the values recorded in MODELS_REPORT.tex and are deliberately editable.
    /// </summary>
    public class CheckInWindowRules
    {
        public double DomesticOpenMinutesBeforeDeparture { get; set; } = 120.0;

        public double DomesticLastAdmissionMinutesBeforeDeparture { get; set; } = 60.0;

        public double InternationalOpenMinutesBeforeDeparture { get; set; } = 180.0;

        public double InternationalLastAdmissionMinutesBeforeDeparture { get; set; } = 60.0;

        public List<CheckInWindowOverride> Overrides { get; set; } =
            new List<CheckInWindowOverride>();

        public static CheckInWindowRules CreateDefault()
        {
            return new CheckInWindowRules();
        }

        public CheckInWindow Resolve(FlightLeg leg)
        {
            if (leg == null)
                throw new ArgumentNullException(nameof(leg));

            double open;
            double close;

            if (leg.SectorRaw == LegSector.International)
            {
                open = InternationalOpenMinutesBeforeDeparture;
                close = InternationalLastAdmissionMinutesBeforeDeparture;
            }
            else
            {
                open = DomesticOpenMinutesBeforeDeparture;
                close = DomesticLastAdmissionMinutesBeforeDeparture;
            }

            if (Overrides != null)
            {
                foreach (CheckInWindowOverride item in Overrides)
                {
                    if (item != null && item.Matches(leg))
                    {
                        open = item.OpenMinutesBeforeDeparture;
                        close = item.LastAdmissionMinutesBeforeDeparture;
                        break;
                    }
                }
            }

            return new CheckInWindow(
                leg.TotalMinutesFromMidnight - open,
                leg.TotalMinutesFromMidnight - close);
        }

        /// <summary>
        /// Sector-default window for a sampled scheduled departure. The
        /// flight-hour demand method has no flight identity, so airline and
        /// flight-number overrides cannot be resolved and are not applied.
        /// </summary>
        public CheckInWindow ResolveSector(
            DHPType type,
            double scheduledDepartureMinutes)
        {
            bool international =
                type == DHPType.InternationalDeparture ||
                type == DHPType.InternationalArrival;

            double open = international
                ? InternationalOpenMinutesBeforeDeparture
                : DomesticOpenMinutesBeforeDeparture;

            double close = international
                ? InternationalLastAdmissionMinutesBeforeDeparture
                : DomesticLastAdmissionMinutesBeforeDeparture;

            return new CheckInWindow(
                scheduledDepartureMinutes - open,
                scheduledDepartureMinutes - close);
        }

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            AddWindowErrors(errors, "Domestic", DomesticOpenMinutesBeforeDeparture,
                DomesticLastAdmissionMinutesBeforeDeparture);

            AddWindowErrors(errors, "International", InternationalOpenMinutesBeforeDeparture,
                InternationalLastAdmissionMinutesBeforeDeparture);

            if (Overrides != null)
            {
                for (int i = 0; i < Overrides.Count; i++)
                {
                    CheckInWindowOverride item = Overrides[i];

                    if (item == null)
                    {
                        errors.Add($"Override {i + 1} is empty.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.Airline))
                        errors.Add($"Override {i + 1} has no airline.");

                    AddWindowErrors(errors, $"Override {i + 1}",
                        item.OpenMinutesBeforeDeparture,
                        item.LastAdmissionMinutesBeforeDeparture);
                }
            }

            return errors;
        }

        private static void AddWindowErrors(
            List<string> errors,
            string label,
            double open,
            double close)
        {
            if (double.IsNaN(open) || double.IsInfinity(open) || open < 0.0)
                errors.Add($"{label} opening lead time must be a non-negative number of minutes.");

            if (double.IsNaN(close) || double.IsInfinity(close) || close < 0.0)
                errors.Add($"{label} last-admission lead time must be a non-negative number of minutes.");

            if (open < close)
                errors.Add($"{label} check-in opens after its last admission.");
        }
    }
}
