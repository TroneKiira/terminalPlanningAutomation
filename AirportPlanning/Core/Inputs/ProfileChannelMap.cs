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
    ///
    /// The map assigns the passenger's PRIMARY check-in method: Counter, Web or
    /// Kiosk. Bag Drop is a separate service stage, so it is never assigned as
    /// a primary channel here. A legacy BagDrop share is treated as Web: the
    /// passenger has no physical check-in queue and may still need the separate
    /// Bag Drop stage, which is derived from their checked-bag flag.
    /// </summary>
    public class ProfileChannelMap : IChannelAssigner
    {
        public string DefaultChannel { get; set; } = DefaultChannelAssigner.CounterChannel;

        public List<ProfileChannelEntry> Entries { get; set; } = new List<ProfileChannelEntry>();

        /// <summary>
        /// Legacy setting retained for saved definitions. Bag Drop is now a
        /// separate service stage and is no longer routed as a primary channel,
        /// so this value does not change the assignment.
        /// </summary>
        public bool EnforceBagDropRouting { get; set; } = true;

        /// <summary>
        /// Legacy lookup retained for saved definitions: the first configured
        /// channel that names a bag-drop desk, or null. Bag Drop routing is now
        /// derived from the passenger's bag-drop requirement, not this name.
        /// </summary>
        public string BagDropRoutingChannel
        {
            get
            {
                if (Entries == null)
                    return null;

                foreach (ProfileChannelEntry entry in Entries)
                {
                    if (entry?.Shares == null)
                        continue;

                    foreach (ChannelShare share in entry.Shares)
                    {
                        if (share == null ||
                            share.Share <= 0.0 ||
                            string.IsNullOrWhiteSpace(share.Channel))
                            continue;

                        if (DefaultChannelAssigner.IsBagDropChannel(share.Channel))
                            return share.Channel;
                    }
                }

                return null;
            }
        }

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

                        string channel = DefaultChannelAssigner.NormalizePrimaryChannel(
                            share.Channel);
                        if (channel.Length > 0 && seen.Add(channel))
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
                return DefaultChannelAssigner.NormalizePrimaryChannel(DefaultChannel);

            double total = 0.0;

            foreach (ChannelShare share in entry.Shares)
            {
                if (share != null && share.Share > 0.0 && !string.IsNullOrEmpty(share.Channel))
                    total += share.Share;
            }

            if (total <= 0.0)
                return DefaultChannelAssigner.NormalizePrimaryChannel(DefaultChannel);

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
                    return DefaultChannelAssigner.NormalizePrimaryChannel(share.Channel);
            }

            // Floating-point safety: return the last usable channel.
            return last != null
                ? DefaultChannelAssigner.NormalizePrimaryChannel(last.Channel)
                : DefaultChannelAssigner.NormalizePrimaryChannel(DefaultChannel);
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
                builder.Append(";bagDropRouting=");
                builder.Append(EnforceBagDropRouting ? "on" : "off");

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
    /// Reproduces the terminal's built-in PRIMARY routing when no channel map
    /// is wired:
    ///
    ///   not online-checked-in -> Counter
    ///   online-checked-in     -> Web
    ///
    /// Bag Drop is a separate service stage. An online passenger with a
    /// checked bag has no physical check-in queue but is still eligible for
    /// that separate Bag Drop visit. Deterministic (no RNG draw).
    /// </summary>
    public class DefaultChannelAssigner : IChannelAssigner
    {
        public static readonly DefaultChannelAssigner Instance = new DefaultChannelAssigner();

        public const string CounterChannel = "Counter";
        public const string KioskChannel = "Kiosk";
        public const string BagDropChannel = "BagDrop";
        public const string WebChannel = "Web";

        public string AssignChannel(Passenger passenger, Random rnd)
        {
            if (passenger == null)
                return WebChannel;

            return passenger.UsesOnlineCheckIn ? WebChannel : CounterChannel;
        }

        /// <summary>
        /// The primary channel used for a passenger. A legacy BagDrop share is
        /// treated as Web because bag drop is no longer a primary check-in
        /// method.
        /// </summary>
        public static string NormalizePrimaryChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return string.Empty;

            string trimmed = channel.Trim();

            return IsBagDropChannel(trimmed) ? WebChannel : trimmed;
        }

        public static bool IsCounterChannel(string channel)
        {
            return Matches(channel, CounterChannel);
        }

        public static bool IsKioskChannel(string channel)
        {
            return Matches(channel, KioskChannel);
        }

        public static bool IsWebChannel(string channel)
        {
            return Matches(channel, WebChannel);
        }

        public static bool IsBagDropChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return false;

            // Accept "BagDrop", "Bag Drop", "bag-drop" and similar labels.
            string normalized = new string(
                channel.Where(char.IsLetterOrDigit).ToArray());

            return string.Equals(
                normalized,
                BagDropChannel,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(string channel, string expected)
        {
            return !string.IsNullOrWhiteSpace(channel) &&
                   string.Equals(
                       channel.Trim(),
                       expected,
                       StringComparison.OrdinalIgnoreCase);
        }

        public string CacheKey
        {
            get { return "default-routing"; }
        }
    }
}
