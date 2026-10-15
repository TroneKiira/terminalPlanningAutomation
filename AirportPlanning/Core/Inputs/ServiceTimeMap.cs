using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// One keyed service-time entry. The key is supplied by the planner; it may
    /// be a passenger-profile name ("Domestic Economy"), a channel name
    /// ("Kiosk"), or a composite "profile|channel".
    /// </summary>
    public class ServiceTimeEntry
    {
        public string Key { get; set; } = string.Empty;

        public ServiceProfile Profile { get; set; }
    }

    /// <summary>
    /// Maps a passenger to a service-time profile using keys built in
    /// Grasshopper. Resolution precedence, most specific first:
    ///
    ///   "profileName|channel"  ->  "profileName"  ->  "channel"  ->  Default
    ///
    /// Nothing about the key vocabulary is hardcoded in C#.
    /// </summary>
    public class ServiceTimeMap : IServiceTimeProvider
    {
        /// <summary>Used when no key matches.</summary>
        public ServiceProfile Default { get; set; }

        public List<ServiceTimeEntry> Entries { get; set; } = new List<ServiceTimeEntry>();

        public ServiceProfile Resolve(Passenger passenger)
        {
            if (passenger == null)
                return Default;

            string profileName = passenger.ProfileName ?? string.Empty;
            string channel = passenger.Channel ?? string.Empty;

            ServiceProfile profile = null;

            if (profileName.Length > 0 && channel.Length > 0)
                profile = Find(profileName + "|" + channel);

            if (profile == null && profileName.Length > 0)
                profile = Find(profileName);

            if (profile == null && channel.Length > 0)
                profile = Find(channel);

            return profile ?? Default;
        }

        private ServiceProfile Find(string key)
        {
            if (Entries == null || string.IsNullOrEmpty(key))
                return null;

            foreach (ServiceTimeEntry entry in Entries)
            {
                if (entry == null || entry.Profile == null || string.IsNullOrEmpty(entry.Key))
                    continue;

                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Profile;
            }

            return null;
        }

        public string CacheKey
        {
            get
            {
                StringBuilder builder = new StringBuilder();

                builder.Append("default=");
                AppendProfile(builder, Default);

                if (Entries != null)
                {
                    foreach (ServiceTimeEntry entry in Entries
                        .Where(e => e != null && !string.IsNullOrEmpty(e.Key))
                        .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        builder.Append('|');
                        builder.Append(entry.Key);
                        builder.Append('=');
                        AppendProfile(builder, entry.Profile);
                    }
                }

                return builder.ToString();
            }
        }

        private static void AppendProfile(StringBuilder builder, ServiceProfile profile)
        {
            if (profile == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append(profile.MeanServiceTime.ToString("R"));
            builder.Append(',');
            builder.Append(profile.StandardDeviation.ToString("R"));
        }
    }

    /// <summary>
    /// Wraps a single <see cref="ServiceProfile"/> so the legacy single-profile
    /// input keeps working exactly as before.
    /// </summary>
    public class ConstantServiceProvider : IServiceTimeProvider
    {
        public ServiceProfile Profile { get; }

        public ConstantServiceProvider(ServiceProfile profile)
        {
            Profile = profile;
        }

        public ServiceProfile Resolve(Passenger passenger)
        {
            return Profile;
        }

        public string CacheKey
        {
            get
            {
                if (Profile == null)
                    return "constant=null";

                return "constant=" +
                    Profile.MeanServiceTime.ToString("R") + "," +
                    Profile.StandardDeviation.ToString("R");
            }
        }
    }
}
