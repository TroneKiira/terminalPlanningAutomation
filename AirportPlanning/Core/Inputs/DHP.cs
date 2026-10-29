using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// What the demand factors describe.
    /// </summary>
    public enum DemandTimeBasis
    {
        /// <summary>
        /// Legacy: each factor is the terminal-arrival demand in that hour.
        /// Passengers are spread inside the same hour.
        /// </summary>
        TerminalArrival = 0,

        /// <summary>
        /// Each factor is the passenger demand on flights whose STD (departures)
        /// or STA (arrivals) falls in that hour. Terminal arrival is derived
        /// from the flight time, the profile's advance window and the show-up
        /// curve.
        /// </summary>
        FlightHour = 1
    }

    public class DHP
    {
        public DHPType Type { get; set; }

        /// <summary>
        /// Whether <see cref="DemandFactors"/> describe terminal arrivals or
        /// flight-hour demand. Defaults to the legacy terminal-arrival basis so
        /// existing programmatic callers keep their behaviour.
        /// </summary>
        public DemandTimeBasis DemandBasis { get; set; } = DemandTimeBasis.TerminalArrival;

        public int DesignHourDemand { get; set; }

        public List<double> DemandFactors { get; set; } = new List<double>();

        public int AnalysisPeriodHours { get; set; } = 5;

        public int DesignHourIndex { get; set; }

        /// <summary>When true, a pre-window warm-up segment is simulated.</summary>
        public bool WarmUpEnabled { get; set; } = true;

        /// <summary>Duration of the warm-up segment in minutes.</summary>
        public int WarmUpMinutes { get; set; } = 60;

        /// <summary>
        /// Demand factor of the warm-up segment, as a percentage of the DHD
        /// hourly rate.
        /// </summary>
        public double WarmUpFactor { get; set; } = 50.0;

        public int StepCount => AnalysisPeriodHours > 0 ? AnalysisPeriodHours : 0;

        public double GetStepDemand(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= DemandFactors.Count)
                return 0;

            return DesignHourDemand * (DemandFactors[stepIndex] / 100.0);
        }

        public double GetWarmUpDemand()
        {
            return DesignHourDemand * (WarmUpFactor / 100.0) * (WarmUpMinutes / 60.0);
        }

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (!Enum.IsDefined(typeof(DemandTimeBasis), DemandBasis))
                errors.Add("Demand Basis must be TerminalArrival (0) or FlightHour (1).");

            if (AnalysisPeriodHours <= 0)
                errors.Add("Analysis Period must be greater than 0 hours.");

            int expected = StepCount;

            if (DemandFactors == null || DemandFactors.Count != expected)
            {
                errors.Add(
                    $"Demand Factors must contain {expected} values, one per hour " +
                    $"of the {AnalysisPeriodHours}h window.");
            }
            else
            {
                for (int i = 0; i < DemandFactors.Count; i++)
                {
                    if (DemandFactors[i] < 0.0)
                        errors.Add($"Demand Factor for step {i + 1} cannot be negative.");
                }
            }

            int maxIndex = Math.Max(0, StepCount - 1);

            if (DesignHourIndex < 0 || DesignHourIndex > maxIndex)
                errors.Add($"Design Hour Index must be between 0 and {maxIndex}.");

            if (DemandBasis == DemandTimeBasis.TerminalArrival)
            {
                if (WarmUpEnabled && WarmUpMinutes < 0)
                    errors.Add("Warm-Up Duration cannot be negative.");

                if (WarmUpFactor < 0.0)
                    errors.Add("Warm-Up Factor cannot be negative.");
            }

            return errors;
        }
    }
}
