using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>One channel and its share of a profile's passengers.</summary>
    public class ChannelShare
    {
        public string Channel { get; set; } = string.Empty;

        public double Share { get; set; }
    }

    /// <summary>The channel split for one passenger profile.</summary>
    public class ProfileChannelEntry
    {
        public string ProfileName { get; set; } = string.Empty;

        public List<ChannelShare> Shares { get; set; } = new List<ChannelShare>();
    }

    /// <summary>
    /// Per-passenger channel assignment driven by a per-profile share table
    /// built in Grasshopper. Shares for a profile are normalised at draw time;
    /// a profile with no entry falls back to the default channel.
    /// </summary>
    public class ProfileChannelMap : IChannelAssigner
    {
        public string DefaultChannel { get; set; } = DefaultChannelAssigner.CounterChannel;

        public List<ProfileChannelEntry> Entries { get; set; } = new List<ProfileChannelEntry>();

        /// <summary>
        /// Returns the unique configured route channels in first-seen order,
        /// followed by the default channel if it was not explicitly listed.
        /// This lets downstream facilities consume the routing table without
        /// asking the user to re-enter its channel names.
        /// </summary>
        public List<string> GetChannels()
        {
            List<string> channels = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Entries != null)
            {
                foreach (ProfileChannelEntry entry in Entries)
                {
                    if (entry?.Shares == null)
                        continue;

                    foreach (ChannelShare share in entry.Shares)
                    {
                        if (share == null || share.Share <= 0.0 || string.IsNullOrWhiteSpace(share.Channel))
                            continue;

                        string channel = share.Channel.Trim();
                        if (seen.Add(channel))
                            channels.Add(channel);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(DefaultChannel))
            {
                string defaultChannel = DefaultChannel.Trim();
                if (seen.Add(defaultChannel))
                    channels.Add(defaultChannel);
            }

            return channels;
        }

        public string AssignChannel(Passenger passenger, Random rnd)
        {
            string profileName = passenger != null ? passenger.ProfileName : null;
            ProfileChannelEntry entry = Find(profileName);

            if (entry == null || entry.Shares == null || entry.Shares.Count == 0)
                return DefaultChannel;

            double total = 0.0;

            foreach (ChannelShare share in entry.Shares)
            {
                if (share != null && share.Share > 0.0 && !string.IsNullOrEmpty(share.Channel))
                    total += share.Share;
            }

            if (total <= 0.0)
                return DefaultChannel;

            double draw = rnd.NextDouble() * total;
            double cumulative = 0.0;
            ChannelShare last = null;

            foreach (ChannelShare share in entry.Shares)
            {
                if (share == null || share.Share <= 0.0 || string.IsNullOrEmpty(share.Channel))
                    continue;

                last = share;
                cumulative += share.Share;

                if (draw < cumulative)
                    return share.Channel;
            }

            // Floating-point safety: return the last usable channel.
            return last != null ? last.Channel : DefaultChannel;
        }

        private ProfileChannelEntry Find(string profileName)
        {
            if (Entries == null || string.IsNullOrEmpty(profileName))
                return null;

            foreach (ProfileChannelEntry entry in Entries)
            {
                if (entry != null &&
                    string.Equals(entry.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        public string CacheKey
        {
            get
            {
                StringBuilder builder = new StringBuilder();

                builder.Append("default=");
                builder.Append(DefaultChannel);

                if (Entries != null)
                {
                    foreach (ProfileChannelEntry entry in Entries
                        .Where(e => e != null && !string.IsNullOrEmpty(e.ProfileName))
                        .OrderBy(e => e.ProfileName, StringComparer.OrdinalIgnoreCase))
                    {
                        builder.Append('|');
                        builder.Append(entry.ProfileName);

                        if (entry.Shares != null)
                        {
                            foreach (ChannelShare share in entry.Shares
                                .Where(s => s != null && !string.IsNullOrEmpty(s.Channel))
                                .OrderBy(s => s.Channel, StringComparer.OrdinalIgnoreCase))
                            {
                                builder.Append(';');
                                builder.Append(share.Channel);
                                builder.Append(':');
                                builder.Append(share.Share.ToString("R"));
                            }
                        }
                    }
                }

                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// Reproduces the terminal's built-in routing when no channel map is wired:
    ///
    ///   not online-checked-in            -> Counter
    ///   online-checked-in with a bag     -> BagDrop
    ///   otherwise                        -> Web
    ///
    /// Deterministic (no RNG draw), so existing numbers are untouched.
    /// </summary>
    public class DefaultChannelAssigner : IChannelAssigner
    {
        public static readonly DefaultChannelAssigner Instance = new DefaultChannelAssigner();

        public const string CounterChannel = "Counter";
        public const string BagDropChannel = "BagDrop";
        public const string WebChannel = "Web";

        public string AssignChannel(Passenger passenger, Random rnd)
        {
            if (passenger == null)
                return WebChannel;

            if (!passenger.UsesOnlineCheckIn)
                return CounterChannel;

            return passenger.HasCheckedBag ? BagDropChannel : WebChannel;
        }

        public string CacheKey
        {
            get { return "default-routing"; }
        }
    }
}
