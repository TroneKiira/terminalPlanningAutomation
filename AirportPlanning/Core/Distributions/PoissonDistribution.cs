using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Distributions
{
    public static class PoissonDistribution
    {
        public static int Sample(double lambda, Random rnd)
        {
            if (lambda <= 0.0)
                return 0;

            double L = Math.Exp(-lambda);
            int k = 0;
            double p = 1.0;

            do
            {
                k++;
                p *= rnd.NextDouble();
            }
            while (p > L);

            return k - 1;
        }
    }
}
