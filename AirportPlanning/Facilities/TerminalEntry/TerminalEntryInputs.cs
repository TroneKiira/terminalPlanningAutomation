using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.TerminalEntry
{
    public class TerminalEntryInputs
    {
        public List<Passenger> Passengers { get; set; } = new List<Passenger>();

        /// <summary>Share of departing passengers assigned to DigiYatra, from 0 to 100.</summary>
        public double DigiYatraSharePercentage { get; set; } = 37.5;

        /// <summary>
        /// Fallback service-time profile used when no Service Time Map is wired.
        /// A route-keyed map is preferred so manual and DigiYatra service times
        /// can differ.
        /// </summary>
        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>
        /// Optional per-passenger service-time mapping (Service Time Map).
        /// Supersedes <see cref="ServiceProfile"/> when supplied.
        /// </summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        public int ManualPhysicalPositions { get; set; } = 3;
        public int DigiYatraPhysicalPositions { get; set; } = 1;

        public ResourceSearchRange ResourceDomain { get; set; } =
            new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

        public List<PoolPlan> PoolPlans { get; set; } = new List<PoolPlan>();
        public bool OptimizeResources { get; set; }
        public double StaffingPercentage { get; set; } = 100.0;
        public int Seed { get; set; } = 1;
    }
}
