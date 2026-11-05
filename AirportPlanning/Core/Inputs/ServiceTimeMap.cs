using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// One keyed service-time entry. Keys may identify a profile, channel,
    /// profile+channel, or DHP type+profile (optionally also channel).
    /// </summary>
    public class ServiceTimeEntry
    {
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Optional facility tag, e.g. "Security" or "Emigration". A tagged row
        /// is only used by that facility, so the same key (for example "EGate")
        /// can carry a different service time at two facilities. Blank applies
        /// to every facility.
        /// </summary>
        public string Facility { get; set; }

        public ServiceProfile Profile { get; set; }
    }

    /// <summary>
    /// Maps a passenger to a service-time profile using keys built in
    /// Grasshopper. Resolution precedence, most specific first:
    ///
    ///   "channel|DHPType|profileName" -> "DHPType|profileName|channel" ->
    ///   "channel|profileName" -> "DHPType|profileName" ->
    ///   "profileName|channel" -> "profileName" -> "channel" -> Default
    ///
    /// The first form is the same identity the Pool Plans component uses, so
    /// the same key string works in both places. Nothing about the key
    /// vocabulary is hardcoded in C#.
    /// </summary>
    public class ServiceTimeMap : IServiceTimeProvider
    {
        /// <summary>Used when no key matches.</summary>
        public ServiceProfile Default { get; set; }

        public List<ServiceTimeEntry> Entries { get; set; } = new List<ServiceTimeEntry>();

        /// <summary>True once <see cref="ScopedTo"/> has been applied.</summary>
        public bool IsScoped { get; private set; }

        /// <summary>The facility this map was scoped to, or null.</summary>
        public string ScopedFacility { get; private set; }

        /// <summary>
        /// Returns a map scoped to one facility. Rows tagged with a different
        /// facility are dropped; untagged rows always apply. A facility-specific
        /// row wins over an untagged row with the same key, so the same key can
        /// carry a different service time at two facilities.
        ///
        /// Tags are matched leniently (case, spacing, planning-table spellings
        /// such as "Security Check" or "Departure Emigration"), and a tag that
        /// matches no facility at all is reported through
        /// <paramref name="warn"/> instead of being silently dropped.
        /// </summary>
        public ServiceTimeMap ScopedTo(string facility, Action<string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(facility))
                return this;

            ServiceTimeMap scoped = new ServiceTimeMap { Default = Default };

            HashSet<string> unknownTags =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int taggedRows = 0;

            foreach (ServiceTimeEntry entry in Entries ?? new List<ServiceTimeEntry>())
            {
                if (entry == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(entry.Facility))
                {
                    taggedRows++;

                    if (FacilityName.Canonicalize(entry.Facility) == null)
                    {
                        if (unknownTags.Add(entry.Facility.Trim()))
                        {
                            warn?.Invoke(
                                $"Service Time Map row '{entry.Key}' is tagged with facility " +
                                $"'{entry.Facility.Trim()}', which matches no facility. Valid names: " +
                                $"{FacilityName.DescribeValid()}. The row is ignored.");
                        }

                        continue;
                    }

                    if (!FacilityName.Matches(entry.Facility, facility))
                        continue;
                }

                scoped.Entries.Add(entry);
            }

            if (scoped.Entries.Count == 0 && Entries != null && Entries.Count > 0)
            {
                warn?.Invoke(
                    $"No Service Time Map row belongs to '{facility.Trim()}' " +
                    $"({(taggedRows > 0 ? taggedRows + " row(s) are facility-tagged" : "all rows untagged")}). " +
                    $"Valid facility names: {FacilityName.DescribeValid()}. " +
                    "Passengers resolve to the map's Default profile.");
            }

            WarnOnForeignStageKeys(scoped, facility, warn);

            // A facility-specific row wins over an untagged row with the same
            // key, mirroring the pool-plan preference rule.
            scoped.Entries.Sort((a, b) =>
            {
                bool aSpecific = a != null && !string.IsNullOrWhiteSpace(a.Facility);
                bool bSpecific = b != null && !string.IsNullOrWhiteSpace(b.Facility);

                if (aSpecific == bSpecific)
                    return 0;

                return aSpecific ? 1 : -1;
            });

            scoped.DeduplicatePreferSpecific();
            scoped.IsScoped = true;
            scoped.ScopedFacility = facility.Trim();

            return scoped;
        }

        /// <summary>
        /// Warns when an untagged row looks like another facility's stage key.
        /// The series stages share route identities across facilities - both
        /// Emigration and Security e-gates are channel "EGate" - so an untagged
        /// "ATRS|..." row (a Security-only identity) would be picked up by
        /// Emigration's map and silently retime its passengers. The warning
        /// names the row and the tag that fixes it.
        /// </summary>
        private static void WarnOnForeignStageKeys(
            ServiceTimeMap scoped,
            string facility,
            Action<string> warn)
        {
            if (scoped == null || scoped.Entries == null || warn == null)
                return;

            foreach (ServiceTimeEntry entry in scoped.Entries)
            {
                if (entry == null || !string.IsNullOrWhiteSpace(entry.Facility))
                    continue;

                string key = entry.Key;

                if (string.IsNullOrEmpty(key))
                    continue;

                int separator = key.IndexOf('|');

                if (separator <= 0)
                    continue;

                string channelPart = key.Substring(0, separator).Trim();

                string owningFacility = null;

                if (string.Equals(channelPart, "ATRS", StringComparison.OrdinalIgnoreCase))
                    owningFacility = FacilityName.Security;
                else if (string.Equals(channelPart, "DigiYatra", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(channelPart, "Manual Verification", StringComparison.OrdinalIgnoreCase))
                    owningFacility = FacilityName.TerminalEntry;

                if (owningFacility != null &&
                    !FacilityName.Matches(owningFacility, facility))
                {
                    warn?.Invoke(
                        $"Service Time Map row '{key}' is untagged, but '{channelPart}' is a " +
                        $"{owningFacility} stage identity. This facility would pick it up by mistake; " +
                        $"tag the row '{owningFacility}'.");
                }
            }
        }

        /// <summary>
        /// Undefines every untagged key that also exists as a facility-specific
        /// key. Called after scoping so an untagged "EGate" at 25 s cannot
        /// shadow a tagged "EGate" at 7 s.
        /// </summary>
        private void DeduplicatePreferSpecific()
        {
            if (Entries == null || Entries.Count < 2)
                return;

            HashSet<string> specificKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ServiceTimeEntry entry in Entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Facility) &&
                    !string.IsNullOrEmpty(entry.Key) && entry.Profile != null)
                {
                    specificKeys.Add(entry.Key);
                }
            }

            Entries.RemoveAll(e =>
                e != null && string.IsNullOrWhiteSpace(e.Facility) &&
                !string.IsNullOrEmpty(e.Key) && e.Profile != null &&
                specificKeys.Contains(e.Key));
        }

        public ServiceProfile Resolve(Passenger passenger)
        {
            ResolveOrFallback(passenger, out ServiceProfile profile);
            return profile;
        }

        /// <summary>
        /// Attempts to resolve a profile and reports whether any wired key
        /// actually matched. False means the lookup fell through every level;
        /// <paramref name="profile"/> is still the profile a
        /// <see cref="Resolve"/> call would return (the Default), so callers
        /// can both use the value and detect the miss.
        /// </summary>
        public bool TryResolve(Passenger passenger, out ServiceProfile profile)
        {
            return ResolveOrFallback(passenger, out profile);
        }

        private bool ResolveOrFallback(Passenger passenger, out ServiceProfile profile)
        {
            profile = null;

            if (passenger == null)
            {
                profile = Default;
                return false;
            }

            string profileName = passenger.ProfileName ?? string.Empty;
            string channel = passenger.Channel ?? string.Empty;
            string dhpType = passenger.DHPType.ToString();

            // Precedence, most specific first. Segments that parse as a DHP
            // type are demand-class constraints; the rest are names. The
            // levels mirror the Pool Plans grammar, so a segregation level
            // chosen there can be mirrored here - stage-wide ("EGate"),
            // sector-only ("EGate|DomesticDeparture"), profile-only
            // ("Domestic Economy") or the full identity.
            if (channel.Length > 0 && profileName.Length > 0)
                profile = Find(channel + "|" + dhpType + "|" + profileName);

            if (profile == null && profileName.Length > 0 && channel.Length > 0)
                profile = Find(dhpType + "|" + profileName + "|" + channel);

            if (profile == null && channel.Length > 0)
                profile = Find(channel + "|" + dhpType);

            if (profile == null && channel.Length > 0 && profileName.Length > 0)
                profile = Find(channel + "|" + profileName);

            if (profile == null && profileName.Length > 0)
                profile = Find(dhpType + "|" + profileName);

            if (profile == null && profileName.Length > 0 && channel.Length > 0)
                profile = Find(profileName + "|" + channel);

            if (profile == null && profileName.Length > 0)
                profile = Find(profileName);

            if (profile == null)
                profile = Find(dhpType);

            if (profile == null && channel.Length > 0)
                profile = Find(channel);

            bool matched = profile != null;

            if (!matched)
                profile = Default;

            return matched;
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
