using AirportPlanning.Facilities;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Distributions
{
    public class GaussianArrivalGenerator
    {
        public ArrivalSimulationResult Generate(
            int passengerCount,
            int departureMinute,
            int horizon,
            ArrivalProfile profile,
            Random rnd)
        {
            

            ArrivalSimulationResult result =
                new ArrivalSimulationResult();

            double totalWeight = 0.0;

            // Defensive guard: a very narrow (or degenerate) Gaussian profile can
            // collapse the kernel to a tiny total weight, which makes the Poisson
            // scale explode exponentially. Rather than hang silently, warn loudly.
            if (profile == null || profile.Sigma <= 0.0)
            {
                // If the profile is missing or invalid, fall back to a uniform
                // spread across the horizon rather than crashing.
                double safeSigma = (profile != null && profile.Sigma > 0.0)
                    ? profile.Sigma
                    : Math.Max(15.0, horizon / 6.0);

                double safePeak = (profile != null && profile.Peak > 0.0)
                    ? profile.Peak
                    : horizon / 2.0;

                profile = new ArrivalProfile { Peak = safePeak, Sigma = safeSigma };
            }

            if (horizon <= 0)
                horizon = 1;

            for (int t = horizon; t >= 1; t--)
            {
                double z = (t - profile.Peak) / profile.Sigma;
                double w = Math.Exp(-0.5 * z * z);

                result.LambdaValues.Add(w);
                totalWeight += w;
            }

            if (totalWeight <= 1e-6)
            {
                // Degenerate kernel: normalise to zero rather than dividing by
                // ~0, which would produce an unbounded Poisson mean.
                for (int i = 0; i < result.LambdaValues.Count; i++)
                    result.LambdaValues[i] = 0.0;

                for (int i = 0; i < horizon; i++)
                    result.Histogram.Add(0);

                return result;
            }

            double scale = passengerCount / totalWeight;

            for (int i = 0; i < result.LambdaValues.Count; i++)
                result.LambdaValues[i] *= scale;

            int generated = 0;

            for (int i = 0; i < horizon; i++)
            {
                double lam = result.LambdaValues[i];

                int count =
                    PoissonDistribution.Sample(lam, rnd);

                if (generated + count > passengerCount)
                    count = passengerCount - generated;

                result.Histogram.Add(count);

                int minutesBeforeDeparture =
                    horizon - i;

                int arrivalMinute =
                    departureMinute - minutesBeforeDeparture;

                for (int k = 0; k < count; k++)
                    result.ArrivalMinutes.Add(
                    (int)(arrivalMinute + rnd.NextDouble()));

                generated += count;

                if (generated >= passengerCount)
                {
                    for (int j = i + 1; j < horizon; j++)
                        result.Histogram.Add(0);

                    break;
                }
            }

            return result;
        }
    }
}
