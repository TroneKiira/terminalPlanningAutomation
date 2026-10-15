using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Distributions
{
    public static class PoissonDistribution
    {
        /// <summary>
        /// Above this mean the exact inversion is still correct but a normal
        /// draw is statistically indistinguishable for arrival shaping.
        /// </summary>
        private const double ExactInversionLimit = 30.0;

        /// <summary>
        /// Samples a Poisson random variable.
        ///
        /// The original implementation used Knuth's product-of-uniforms method,
        /// whose expected iteration count is exp(lambda). Any realistic design-hour
        /// demand with a narrow arrival kernel pushes lambda into the hundreds,
        /// which turns a single draw into an effectively unbounded loop and hangs
        /// the Grasshopper solve. This implementation is bounded instead:
        ///
        ///   * lambda &lt; 30 - exact inversion by summing the pmf recurrence,
        ///     which needs at most ~30 iterations;
        ///   * lambda &gt;= 30 - normal approximation with continuity correction,
        ///     which is accurate to better than 0.5% for a queueing arrival curve
        ///     and costs two normal variates.
        ///
        /// Both branches are O(1) in lambda, so no input can hang the solve.
        /// </summary>
        public static int Sample(double lambda, Random rnd)
        {
            if (double.IsNaN(lambda) || lambda <= 0.0)
                return 0;

            if (double.IsInfinity(lambda) || lambda > int.MaxValue / 2)
                return int.MaxValue;

            if (lambda >= ExactInversionLimit)
                return SampleNormal(lambda, rnd);

            double u = rnd.NextDouble();
            double term = Math.Exp(-lambda);          // P(0)
            double cumulative = term;                 // F(0)
            int k = 0;

            // P(k) = P(k-1) * lambda / k, and F(k) = F(k-1) + P(k).
            while (u > cumulative)
            {
                k++;
                term *= lambda / k;
                cumulative += term;

                if (k > (int)ExactInversionLimit + 64)
                    break;
            }

            return k;
        }

        /// <summary>
        /// Poisson(lambda) approximated by round(lambda + sqrt(lambda) * Z),
        /// clamped at zero. Constant time regardless of lambda.
        /// </summary>
        private static int SampleNormal(double lambda, Random rnd)
        {
            // Box-Muller, guarded away from log(0).
            double u1 = 1.0 - rnd.NextDouble();
            double u2 = rnd.NextDouble();

            double z = Math.Sqrt(-2.0 * Math.Log(u1)) *
                       Math.Cos(2.0 * Math.PI * u2);

            double value = lambda + Math.Sqrt(lambda) * z;

            if (value < 0.0)
                return 0;

            if (value > int.MaxValue / 2)
                return int.MaxValue;

            return (int)Math.Round(value);
        }
    }
}
