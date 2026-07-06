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
        /// Number of passengers arriving each minute.
        /// </summary>
        public List<int> Histogram { get; set; }

        /// <summary>
        /// Gaussian arrival intensity for each minute.
        /// </summary>
        public List<double> LambdaValues { get; set; }

        public ArrivalSimulationResult()
        {
            ArrivalMinutes =
                new List<int>();

            Histogram =
                new List<int>();

            LambdaValues =
                new List<double>();
        }
    }
}
