using AirportPlanning.Core.Models;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Queueing
{
    public class QueueOptimizer
    {
        public OptimizationResult Optimize<TInput>(
            TInput input,
            Func<TInput, QueueResults> solver,
            Action<TInput, int> setActiveServers,
            Func<TInput, int> getPhysicalServers,
            double targetWait95,
            double staffingPercentage)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            OptimizationResult result = new OptimizationResult();

            int physicalN = Math.Max(1, getPhysicalServers(input));

            double staffPct = (staffingPercentage <= 0.0 || staffingPercentage > 100.0) ? 100.0 : staffingPercentage;

            int startSearch = 1;

            // FIX 1: Increased the search space to handle massive passenger loads
            int endSearch = Math.Max(200, physicalN * 3);

            int chosenN = physicalN;
            int chosenActiveN = physicalN;
            double bestFitness = -1.0;
            QueueResults bestQueue = null;

            int fallbackN = -1;
            int fallbackActiveN = -1;
            QueueResults fallbackQueue = null;

            // FIX 2: Track the last simulation run so we NEVER return a null QueueResults
            int lastN = physicalN;
            int lastActiveN = physicalN;
            QueueResults lastQueue = null;

            int iterations = 0;

            for (int currentN = startSearch; currentN <= endSearch; currentN++)
            {
                iterations++;

                int activeN = (int)Math.Max(1, Math.Round(currentN * (staffPct / 100.0)));

                setActiveServers(input, activeN);
                QueueResults queue = solver(input);

                // Always store the absolute latest queue we tested
                lastQueue = queue;
                lastN = currentN;
                lastActiveN = activeN;

                double wait95 = queue.Wait95;

                double x = (targetWait95 > 0.0) ? (wait95 / targetWait95) : 0.0;
                double fitness = (x > 1.0 || x < 0.5) ? -1.0 : x;

                if (fitness > bestFitness)
                {
                    bestFitness = fitness;
                    chosenN = currentN;
                    chosenActiveN = activeN;
                    bestQueue = queue;
                }

                if (wait95 <= targetWait95 && (fallbackN == -1 || currentN < fallbackN))
                {
                    fallbackN = currentN;
                    fallbackActiveN = activeN;
                    fallbackQueue = queue;
                }

                if (x >= 0.0 && x < 0.5)
                {
                    break;
                }
            }

            if (bestFitness == -1.0 && fallbackN != -1)
            {
                chosenN = fallbackN;
                chosenActiveN = fallbackActiveN;
                bestQueue = fallbackQueue;
                result.Success = true;
            }
            else if (bestFitness != -1.0)
            {
                result.Success = true;
            }
            else
            {
                // FIX 3: Safety Net! If EVERYTHING failed to meet the target, 
                // return the highest capacity we tested so it doesn't crash with null.
                result.Success = false;
                chosenN = lastN;
                chosenActiveN = lastActiveN;
                bestQueue = lastQueue;
            }

            stopwatch.Stop();

            result.OptimalPhysicalResources = chosenN;
            result.OptimalActiveResources = chosenActiveN;
            result.Fitness = bestFitness;
            result.QueueResults = bestQueue;
            result.Iterations = iterations;
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

            return result;
        }
    }
}
