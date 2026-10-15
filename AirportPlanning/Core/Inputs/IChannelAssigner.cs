using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// Assigns a channel to a passenger at generation time. Channels are
    /// user-defined names ("Counter", "Kiosk", "Web", "BagDrop", ...); nothing
    /// about them is hardcoded. The mapping is built in Grasshopper, and a
    /// custom implementation can be supplied from a script component.
    /// </summary>
    public interface IChannelAssigner
    {
        /// <summary>
        /// Channel for this passenger. May draw from <paramref name="rnd"/> to
        /// realise a share table.
        /// </summary>
        string AssignChannel(Passenger passenger, Random rnd);

        /// <summary>
        /// Stable signature of the assignment rules. Used in solver cache keys,
        /// so it must change whenever the routing changes.
        /// </summary>
        string CacheKey { get; }
    }
}
