using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// A named within-hour demand shape, described by the share of the peak
    /// hour's passengers that arrive (or deplane) in its busiest 15, 30 and 60
    /// minutes. Shares are fractions; W60 is 1.0 by definition.
    /// </summary>
    public class WindowShareProfile
    {
        /// <summary>Descriptive name shown to the planner.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Share of the peak hour in its busiest 15 minutes (0-1).</summary>
        public double W15 { get; set; } = 0.25;

        /// <summary>Share of the peak hour in its busiest 30 minutes (0-1).</summary>
        public double W30 { get; set; } = 0.50;

        /// <summary>Share of the peak hour in its busiest 60 minutes; always 1.0.</summary>
        public double W60 { get; set; } = 1.00;

        public WindowShareProfile()
        {
        }

        public WindowShareProfile(string name, double w15, double w30, double w60 = 1.0)
        {
            Name = name;
            W15 = w15;
            W30 = w30;
            W60 = w60;
        }

        /// <summary>
        /// Shape validation. Empty when valid.
        ///
        /// W15 &lt;= W30 and W60 == 1 are kept. The two "busiest window" rules
        /// that used to live here were removed: "W30 cannot be below 50%" only
        /// holds when W30 denotes a half-hour, and "W30 cannot exceed 2 x W15"
        /// assumes arrivals cluster near departure. Every shipped show-up curve
        /// breaks one or both - see <see cref="ShowUpProfile"/> - so
        /// <see cref="ShowUpProfile"/> overrides this with band-based validation.
        /// </summary>
        public virtual List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (W15 < 0.0 || W15 > 1.0)
                errors.Add("W15 must be between 0 and 100%.");

            if (W30 < 0.0 || W30 > 1.0)
                errors.Add("W30 must be between 0 and 100%.");

            if (Math.Abs(W60 - 1.0) > 1e-6)
                errors.Add("W60 must be 100% (the whole hour).");

            if (W15 > W30 + 1e-9)
                errors.Add("W15 cannot exceed W30: the busiest 15 minutes sit inside the busiest 30.");

            return errors;
        }
    }
}
