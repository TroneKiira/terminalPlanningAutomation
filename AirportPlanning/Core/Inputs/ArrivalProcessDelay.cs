using System;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// Where the generated departure-passenger time is measured.
    /// </summary>
    public enum PassengerArrivalBasis
    {
        /// <summary>The passenger has just entered the terminal.</summary>
        TerminalEntry = 0,

        /// <summary>The passenger has reached the first check-in queue.</summary>
        CheckInQueue = 1
    }

    /// <summary>
    /// Explicit, deliberately simple assumption for the time between an
    /// aircraft's scheduled arrival and the passenger reaching the first arrival
    /// process. Deplaning W15/W30/W60 profiles are sizing shapes and cannot
    /// uniquely define minute-by-minute arrival times, so this delay is kept as
    /// a separate, visible input instead of being inferred from them.
    /// </summary>
    public class ArrivalProcessDelay
    {
        public double MinimumMinutes { get; set; } = 0.0;

        public double MaximumMinutes { get; set; } = 30.0;

        public ArrivalProcessDelay()
        {
        }

        public ArrivalProcessDelay(double minimumMinutes, double maximumMinutes)
        {
            MinimumMinutes = minimumMinutes;
            MaximumMinutes = maximumMinutes;
        }

        public double Sample(Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            double min = MinimumMinutes;
            double max = MaximumMinutes;

            if (max < min)
            {
                double swap = min;
                min = max;
                max = swap;
            }

            if (Math.Abs(max - min) < 1e-9)
                return min;

            return min + random.NextDouble() * (max - min);
        }

        public System.Collections.Generic.List<string> GetValidationErrors()
        {
            var errors = new System.Collections.Generic.List<string>();

            if (double.IsNaN(MinimumMinutes) || double.IsInfinity(MinimumMinutes) ||
                MinimumMinutes < 0.0)
            {
                errors.Add("Arrival delay minimum must be a non-negative number of minutes.");
            }

            if (double.IsNaN(MaximumMinutes) || double.IsInfinity(MaximumMinutes) ||
                MaximumMinutes < 0.0)
            {
                errors.Add("Arrival delay maximum must be a non-negative number of minutes.");
            }

            return errors;
        }
    }
}
