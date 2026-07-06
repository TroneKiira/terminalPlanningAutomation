using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Distributions
{
    public class LogNormalServiceTimeGenerator
    {
        public List<double> Generate(
            int passengerCount,
            double mean,
            double standardDeviation,
             Random rnd)
        {
            List<double> serviceTimes =
                new List<double>();

           

            for (int i = 0; i < passengerCount; i++)
            {
                double s =
                    SampleLogNormal(
                        mean,
                        standardDeviation,
                        rnd);

                serviceTimes.Add(s);
            }

            return serviceTimes;
        }

        private double SampleLogNormal(
            double mean,
            double std,
            Random rnd)
        {
            double v = std * std;
            double s = Math.Sqrt(Math.Log(1 + (v / (mean * mean))));
            double m = Math.Log(mean) - 0.5 * s * s;

            double u1 = rnd.NextDouble();
            double u2 = rnd.NextDouble();

            // NEW: Add safety margin from GH Script
            if (u1 < 1e-9) u1 = 1e-9;

            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            return Math.Exp(m + s * z);
        }
    }
}
