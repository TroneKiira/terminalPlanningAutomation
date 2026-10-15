using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// Resolves the service-time distribution that applies to a passenger.
    ///
    /// This is the modular seam for per-passenger-type service times. Nothing
    /// about Economy / Business / Domestic / International is hardcoded: the
    /// mapping is built in Grasshopper, and a custom implementation can be
    /// supplied from a script component.
    /// </summary>
    public interface IServiceTimeProvider
    {
        /// <summary>The distribution that serves this passenger.</summary>
        ServiceProfile Resolve(Passenger passenger);

        /// <summary>
        /// Stable signature of everything that affects Resolve. Used in solver
        /// cache keys, so it must change whenever the mapping changes.
        /// </summary>
        string CacheKey { get; }
    }
}
