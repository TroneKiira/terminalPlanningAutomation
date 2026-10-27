using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// One passenger occupying one processor for a stretch of simulation time.
    /// </summary>
    public class ProcessorServiceBlock
    {
        public int PassengerId { get; set; }

        public string ProfileName { get; set; } = string.Empty;

        /// <summary>Pool-local processor ID (0-based).</summary>
        public int ProcessorId { get; set; }

        public double StartTime { get; set; }

        public double EndTime { get; set; }

        public double Duration
        {
            get { return EndTime - StartTime; }
        }
    }

    /// <summary>
    /// Processor occupancy for one server pool, ready to be drawn as a
    /// processor-by-time matrix.
    /// </summary>
    public class ProcessorTimelinePool
    {
        public string Name { get; set; } = "Shared";

        public bool IsShared { get; set; }

        /// <summary>Staffed processors available in this pool.</summary>
        public int ProcessorCount { get; set; }

        public List<ProcessorServiceBlock> Blocks { get; set; } =
            new List<ProcessorServiceBlock>();
    }

    /// <summary>
    /// Turns a solved <see cref="QueueResults"/> into per-pool processor
    /// occupancy. Pure data preparation, kept out of the chart layer so it can
    /// be tested without OxyPlot or System.Drawing.
    ///
    /// Only passengers the facility actually served are considered; warm-up
    /// passengers are excluded by default so the occupancy lines up with the
    /// reported queue timeline, which also excludes them.
    ///
    /// Pool membership comes from <see cref="Passenger.AssignedPool"/> first,
    /// falling back to the bare channel for older results. The assignment is
    /// what keeps multiple keyed pools in one channel separate.
    /// </summary>
    public static class ProcessorTimeline
    {
        public static List<ProcessorTimelinePool> Build(
            QueueResults results,
            bool includeWarmUp = false)
        {
            List<ProcessorTimelinePool> pools = new List<ProcessorTimelinePool>();

            if (results == null)
                return pools;

            List<ProcessorPoolInfo> infos = NormalizePools(results);

            Dictionary<string, ProcessorTimelinePool> byName =
                new Dictionary<string, ProcessorTimelinePool>(StringComparer.OrdinalIgnoreCase);

            foreach (ProcessorPoolInfo info in infos)
            {
                ProcessorTimelinePool pool = new ProcessorTimelinePool
                {
                    Name = string.IsNullOrWhiteSpace(info.Name) ? "Shared" : info.Name,
                    IsShared = info.IsShared,
                    ProcessorCount = Math.Max(0, info.ResourceCount)
                };

                pools.Add(pool);

                if (!byName.ContainsKey(pool.Name))
                    byName[pool.Name] = pool;
            }

            if (pools.Count == 0)
                return pools;

            List<Passenger> processed = results.ProcessedPassengers;

            if (processed == null)
                return pools;

            foreach (Passenger passenger in processed)
            {
                if (passenger == null)
                    continue;

                if (!includeWarmUp && passenger.IsWarmUp)
                    continue;

                // No processor was assigned, or the stay is zero-length, so the
                // passenger never occupied a server for a visible interval.
                if (passenger.AssignedCounter < 0)
                    continue;

                if (passenger.EndServiceTime <= passenger.StartServiceTime)
                    continue;

                ProcessorTimelinePool pool = ResolvePool(pools, byName, passenger);

                if (pool == null)
                    continue;

                pool.Blocks.Add(new ProcessorServiceBlock
                {
                    PassengerId = passenger.Id,
                    ProfileName = string.IsNullOrWhiteSpace(passenger.ProfileName)
                        ? "(unassigned)"
                        : passenger.ProfileName,
                    ProcessorId = passenger.AssignedCounter,
                    StartTime = passenger.StartServiceTime,
                    EndTime = passenger.EndServiceTime
                });

                if (passenger.AssignedCounter + 1 > pool.ProcessorCount)
                    pool.ProcessorCount = passenger.AssignedCounter + 1;
            }

            foreach (ProcessorTimelinePool pool in pools)
            {
                pool.Blocks = pool.Blocks
                    .OrderBy(b => b.StartTime)
                    .ThenBy(b => b.ProcessorId)
                    .ThenBy(b => b.PassengerId)
                    .ToList();
            }

            return pools;
        }

        private static ProcessorTimelinePool ResolvePool(
            List<ProcessorTimelinePool> pools,
            Dictionary<string, ProcessorTimelinePool> byName,
            Passenger passenger)
        {
            // The solver records the exact pool that served the passenger.
            // This is required for keyed sub-pools: their names include the
            // pool key, while Passenger.Channel holds only the channel name.
            string assignedPool = passenger.AssignedPool;

            if (!string.IsNullOrWhiteSpace(assignedPool) &&
                byName.TryGetValue(assignedPool.Trim(), out ProcessorTimelinePool assignedMatch))
            {
                return assignedMatch;
            }

            // Fallback for results produced before pool identities were
            // recorded, and for one pool per bare channel.
            string channel = passenger.Channel;

            if (!string.IsNullOrWhiteSpace(channel) &&
                byName.TryGetValue(channel.Trim(), out ProcessorTimelinePool match))
            {
                return match;
            }

            // A single pool serves everyone, whatever channel the passenger was
            // routed to upstream.
            if (pools.Count == 1)
                return pools[0];

            // Merged results without a channel tag belong to the shared pool.
            for (int i = 0; i < pools.Count; i++)
            {
                if (pools[i].IsShared)
                    return pools[i];
            }

            return null;
        }

        private static List<ProcessorPoolInfo> NormalizePools(QueueResults results)
        {
            if (results.ProcessorPools != null && results.ProcessorPools.Count > 0)
            {
                return results.ProcessorPools
                    .Where(p => p != null)
                    .ToList();
            }

            int resources = results.ResourcesUsed > 0
                ? results.ResourcesUsed
                : (results.Resources != null ? results.Resources.Count : 0);

            return new List<ProcessorPoolInfo>
            {
                new ProcessorPoolInfo
                {
                    Name = "Shared",
                    ResourceCount = resources,
                    FirstResourceIndex = 0,
                    IsShared = true
                }
            };
        }
    }
}
