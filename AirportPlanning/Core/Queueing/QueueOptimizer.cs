using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Result of sizing a single server pool (one channel, or the shared pool).
    /// </summary>
    public class ResourceSearchOutcome
    {
        public ChannelResourcePlan Plan { get; set; }

        /// <summary>Physical resource count the search settled on.</summary>
        public int OptimalPhysicalResources { get; set; }

        /// <summary>Staffed (active) servers actually simulated for that count.</summary>
        public int OptimalActiveResources { get; set; }

        /// <summary>Wait95 the winning capacity produced.</summary>
        public double Wait95 { get; set; }

        /// <summary>Ratio of Wait95 to target when it landed inside the band, else -1.</summary>
        public double Fitness { get; set; }

        /// <summary>True when a capacity within the range met the target.</summary>
        public bool Success { get; set; }

        /// <summary>Number of candidate capacities simulated.</summary>
        public int Iterations { get; set; }

        /// <summary>Every (capacity, wait95) point evaluated during the search.</summary>
        public List<SearchTracePoint> SearchTrace { get; set; } = new List<SearchTracePoint>();
    }

    public class SearchTracePoint
    {
        public int Capacity { get; set; }
        public double Wait95 { get; set; }
    }

    /// <summary>
    /// Finds the smallest physical capacity that meets a target Wait95 for each
    /// pool, inside the bounds declared by <see cref="ChannelResourcePlan"/>.
    ///
    /// Wait95 falls as capacity rises, so the search is a lower-bound binary
    /// search rather than the previous linear 1..cap scan. That turns up to
    /// ~1200 simulations into ~log2(range) (about 11 for a 1..1200 window),
    /// with a short downward refinement to absorb the mild non-monotonicity a
    /// discrete queue can show. Independent pools are searched in parallel.
    /// </summary>
    public class QueueOptimizer
    {
        /// <summary>How many extra steps below the bisection hit to re-check for non-monotonicity.</summary>
        private const int RefineWindow = 4;

        /// <summary>
        /// Sizes every supplied pool. <paramref name="evaluate"/> receives the plan
        /// and a physical capacity and must return that capacity's Wait95; it is
        /// called with different capacities and must not mutate shared state, so
        /// pools can be searched concurrently.
        /// </summary>
        public List<ResourceSearchOutcome> Optimize(
            IList<ChannelResourcePlan> plans,
            double staffingPercentage,
            Func<ChannelResourcePlan, int, double> evaluate,
            Action<string> warn = null)
        {
            List<ResourceSearchOutcome> outcomes = new List<ResourceSearchOutcome>();

            if (plans == null || plans.Count == 0 || evaluate == null)
                return outcomes;

            if (plans.Count == 1)
            {
                outcomes.Add(SearchOne(plans[0], staffingPercentage, evaluate));
                return outcomes;
            }

            ResourceSearchOutcome[] results = new ResourceSearchOutcome[plans.Count];

            Parallel.For(
                0,
                plans.Count,
                index =>
                {
                    ChannelResourcePlan plan = plans[index];

                    try
                    {
                        results[index] = SearchOne(plan, staffingPercentage, evaluate);
                    }
                    catch (Exception ex)
                    {
                        warn?.Invoke($"Optimization failed for '{plan}': {ex.Message}");
                        results[index] = FailedOutcome(plan, staffingPercentage);
                    }
                });

            foreach (ResourceSearchOutcome outcome in results)
            {
                if (outcome != null)
                    outcomes.Add(outcome);
            }

            return outcomes;
        }

        private ResourceSearchOutcome SearchOne(
            ChannelResourcePlan plan,
            double staffingPercentage,
            Func<ChannelResourcePlan, int, double> evaluate)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ResourceSearchRange range = (plan.Range ?? new ResourceSearchRange()).Normalized();
            double target = plan.TargetWait95;

            int evaluations = 0;
            var trace = new List<SearchTracePoint>();

            if (target <= 0.0)
            {
                // No wait target: keep the plan's nominal capacity instead of
                // driving the search to the domain maximum. The nominal is still
                // clamped into the declared domain so it stays a legal capacity.
                int nominal = Math.Max(range.Min, Math.Min(range.Max, plan.PhysicalResources));

                double waitAtNominal = evaluate(plan, nominal);
                evaluations++;
                trace.Add(new SearchTracePoint { Capacity = nominal, Wait95 = waitAtNominal });

                stopwatch.Stop();

                ResourceSearchOutcome kept = BuildOutcome(
                    plan, staffingPercentage, nominal, waitAtNominal, evaluations, true);
                kept.SearchTrace = trace;

                return kept;
            }

            double waitAtMax = evaluate(plan, range.Max);
            evaluations++;
            trace.Add(new SearchTracePoint { Capacity = range.Max, Wait95 = waitAtMax });

            if (waitAtMax > target)
            {
                stopwatch.Stop();

                ResourceSearchOutcome unreachable = BuildOutcome(
                    plan, staffingPercentage, range.Max, waitAtMax, evaluations, false);
                unreachable.SearchTrace = trace;

                return unreachable;
            }

            int low = range.Min;
            int high = range.Max;

            while (low < high)
            {
                int mid = low + ((high - low) / 2);

                double wait = evaluate(plan, mid);
                evaluations++;
                trace.Add(new SearchTracePoint { Capacity = mid, Wait95 = wait });

                if (wait <= target)
                    high = mid;
                else
                    low = mid + 1;
            }

            int best = low;

            for (int candidate = best - 1; candidate >= range.Min && candidate >= best - RefineWindow; candidate--)
            {
                double wait = evaluate(plan, candidate);
                evaluations++;
                trace.Add(new SearchTracePoint { Capacity = candidate, Wait95 = wait });

                if (wait <= target)
                    best = candidate;
                else
                    break;
            }

            double waitAtBest = evaluate(plan, best);
            evaluations++;
            trace.Add(new SearchTracePoint { Capacity = best, Wait95 = waitAtBest });

            stopwatch.Stop();

            ResourceSearchOutcome outcome = BuildOutcome(plan, staffingPercentage, best, waitAtBest, evaluations, true);
            outcome.SearchTrace = trace;
            return outcome;
        }

        private static ResourceSearchOutcome BuildOutcome(
            ChannelResourcePlan plan,
            double staffingPercentage,
            int physical,
            double wait95,
            int evaluations,
            bool success)
        {
            double target = plan != null ? plan.TargetWait95 : 0.0;

            double fitness = -1.0;

            if (success && target > 0.0)
            {
                double x = wait95 / target;

                if (x <= 1.0 && x >= 0.5)
                    fitness = x;
            }

            return new ResourceSearchOutcome
            {
                Plan = plan,
                OptimalPhysicalResources = physical,
                OptimalActiveResources = ChannelResourcePlan.ToActiveResources(physical, staffingPercentage),
                Wait95 = wait95,
                Fitness = fitness,
                Success = success,
                Iterations = evaluations
            };
        }

        private static ResourceSearchOutcome FailedOutcome(
            ChannelResourcePlan plan,
            double staffingPercentage)
        {
            return BuildOutcome(
                plan,
                staffingPercentage,
                Math.Max(1, plan != null && plan.Range != null ? plan.Range.Max : 1),
                0.0,
                0,
                false);
        }
    }
}
