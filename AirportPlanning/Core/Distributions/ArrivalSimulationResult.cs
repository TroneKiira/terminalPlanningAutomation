using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Distributions
{
    public class ArrivalSimulationResult
    {
        /// <summary>
        /// Passenger arrival minute relative to the simulation horizon.
        /// </summary>
        public List<int> ArrivalMinutes { get; set; }

        /// <summary>
        /// Number of passengers arriving each minute. Bucket i covers the
        /// absolute minute <see cref="StepStartMinute"/> + i, so the histogram
        /// is only meaningful alongside that offset.
        /// </summary>
        public List<int> Histogram { get; set; }

        /// <summary>
        /// Gaussian arrival intensity for each minute.
        /// </summary>
        public List<double> LambdaValues { get; set; }

        /// <summary>
        /// Absolute simulation minute that histogram bucket 0 covers. Set by the
        /// windowed generator; left at 0 by the legacy single-horizon path.
        /// </summary>
        public double StepStartMinute { get; set; }

        /// <summary>Length of the analysis step in minutes.</summary>
        public int StepMinutes { get; set; }

        /// <summary>Demand step index (-1 for the warm-up segment).</summary>
        public int StepIndex { get; set; } = -1;

        /// <summary>True when this simulation describes the warm-up segment.</summary>
        public bool IsWarmUp { get; set; }

        /// <summary>True when this simulation describes the design hour step.</summary>
        public bool IsDesignStep { get; set; }

        /// <summary>
        /// Human-readable series label (e.g. "Domestic - Step 2"). Used as the
        /// legend entry when the result is plotted.
        /// </summary>
        public string Label { get; set; }

        public ArrivalSimulationResult()
        {
            ArrivalMinutes =
                new List<int>();

            Histogram =
                new List<int>();

            LambdaValues =
                new List<double>();
        }

        /// <summary>
        /// Bucket minute for a histogram index, in absolute simulation minutes.
        /// </summary>
        public double GetAbsoluteMinute(int bucketIndex)
        {
            return StepStartMinute + bucketIndex;
        }
    }
}
