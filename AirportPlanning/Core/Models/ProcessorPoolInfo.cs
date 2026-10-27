using System;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// One server pool behind a facility result.
    ///
    /// A dedicated channel pool starts its processor IDs at zero, so
    /// <see cref="FirstResourceIndex"/> records where that pool's resources
    /// begin in the merged <see cref="QueueResults.Resources"/> list. The chart
    /// uses it to keep pools separate instead of collapsing colliding IDs.
    /// </summary>
    public class ProcessorPoolInfo
    {
        /// <summary>Channel name, or "Shared" for a single facility-wide queue.</summary>
        public string Name { get; set; } = "Shared";

        /// <summary>Staffed processors simulated for this pool.</summary>
        public int ResourceCount { get; set; }

        /// <summary>
        /// Installed units in this pool - the physical counters, lanes or
        /// positions the terminal provides. This is the count a floor area is
        /// built from; <see cref="ResourceCount"/> is how many of them were
        /// open and staffed.
        /// </summary>
        public int PhysicalResourceCount { get; set; }

        /// <summary>Index of this pool's first resource in the merged resource list.</summary>
        public int FirstResourceIndex { get; set; }

        /// <summary>True when the pool serves every routed passenger in one queue.</summary>
        public bool IsShared { get; set; }
    }
}
