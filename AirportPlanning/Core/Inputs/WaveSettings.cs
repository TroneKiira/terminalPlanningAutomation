using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Inputs
{
    public enum WaveCoverage
    {
        Off,
        PeakBlock,
        FullStep
    }

    public class WaveSettings
    {
        public double ScheduleVariabilityMinutes { get; set; } = 0.0;

        public double WalkDispersionMinutes { get; set; } = 0.0;

        public WaveCoverage Coverage { get; set; } = WaveCoverage.Off;

        public bool IsWaveActive
        {
            get { return Coverage != WaveCoverage.Off; }
        }

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (ScheduleVariabilityMinutes < 0.0)
                errors.Add("Schedule Variability cannot be negative.");

            if (WalkDispersionMinutes < 0.0)
                errors.Add("Walk Dispersion cannot be negative.");

            return errors;
        }
    }
}
