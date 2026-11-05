using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// Sizes aircraft stands and terminal gate lounges.
    ///
    /// A STAND IS APRON AND A LOUNGE IS TERMINAL. The two are counted
    /// separately here, because adding a stand to a floor area would be
    /// meaningless.
    ///
    /// Stand demand is either the ICAO peak-hour model or aircraft-on-ground
    /// peaks from a linked design-day schedule. Schedule demand takes priority.
    ///
    /// The lounge is sized on the planeload the aircraft brings: seats
    /// standing, seats sitting, the queue waiting to board, and a podium.
    ///
    /// Lounge sizing is shared by both demand methods and uses each aircraft
    /// code's own passenger profile.
    /// </summary>
    public static class GateCalculator
    {
        /// <summary>The three groups of stands.</summary>
        public static readonly string[] BucketNames = { "Domestic", "Swing", "International" };

        /// <summary>
        /// Sizes the stands and the lounges.
        /// </summary>
        public static GateSizingResult Calculate(
            List<GateCodeInput> codes,
            GateStandards standards,
            GateScheduleDemand scheduleDemand = null,
            List<GateLoungeProfile> loungeProfiles = null)
        {
            GateSizingResult result = new GateSizingResult();

            if ((codes == null || codes.Count == 0) && scheduleDemand == null)
            {
                result.Warnings.Add("No aircraft codes were given, so no stands or lounges were sized.");

                return result;
            }

            if (standards == null)
            {
                result.Warnings.Add("No gate standards were given, so no stands or lounges were sized.");

                return result;
            }

            List<GateCodeInput> usable = (codes ?? new List<GateCodeInput>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Code))
                .GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            Dictionary<string, GateLoungeProfile> profiles = (loungeProfiles ?? new List<GateLoungeProfile>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Code))
                .GroupBy(p => p.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (usable.Count == 0 && scheduleDemand == null)
            {
                result.Warnings.Add("Every aircraft code was blank, so nothing was sized.");

                return result;
            }

            result.ScheduleMode = scheduleDemand != null;

            // A linked schedule replaces peak ATMs, occupancy, manual swing,
            // and max/active ratio. Lounge profiles and shared standards stay.
            SectorStands domestic;
            SectorStands international;
            Dictionary<string, int> swing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (scheduleDemand != null)
            {
                if (scheduleDemand.Warnings != null)
                    result.Warnings.AddRange(scheduleDemand.Warnings);

                domestic = BuildScheduleSector(scheduleDemand, standards, true, swing);
                international = BuildScheduleSector(scheduleDemand, standards, false, swing);
            }
            else
            {
                domestic = BuildSector(usable, standards, true);
                international = BuildSector(usable, standards, false);

                foreach (GateCodeInput code in usable)
                {
                    int stated = standards.SwingFor(code.Code);
                    int cap = Math.Min(domestic.Rounded(code.Code), international.Rounded(code.Code));

                    if (stated > cap)
                    {
                        result.Warnings.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Swing stated for code {0} ({1}) is more than the smaller sector can give ({2}); it was capped.",
                            code.Code,
                            stated,
                            cap));
                    }

                    swing[code.Code] = Math.Max(0, Math.Min(stated, cap));
                }
            }

            GateBucketStands domBucket = BuildBucket(
                "Domestic", domestic, swing, standards.ContactPctDomestic);

            GateBucketStands swingBucket = BuildBucket(
                "Swing", domestic, swing, standards.ContactPctSwing);

            GateBucketStands intlBucket = BuildBucket(
                "International", international, swing, standards.ContactPctInternational);

            result.Buckets.Add(domBucket);
            result.Buckets.Add(swingBucket);
            result.Buckets.Add(intlBucket);

            result.ActiveTotal = domBucket.Active + swingBucket.Active + intlBucket.Active;
            result.ContactTotal = domBucket.Contact + swingBucket.Contact + intlBucket.Contact;
            int nonContact = domBucket.Bus + swingBucket.Bus + intlBucket.Bus;
            double remotePerBusGate = Math.Max(0.0, standards.RemoteStandsPerBusGate);

            GateBucketStands[] bucketList = { domBucket, swingBucket, intlBucket };

            if (scheduleDemand != null)
            {
                // Schedule mode: the non-contact stands are remote stands, and
                // the bus gates are derived facility-wide from the ratio. The
                // group split carries the remote stands; the bus gates are a
                // facility total.
                foreach (GateBucketStands bucket in bucketList)
                    bucket.Remote = bucket.Bus;

                result.RemoteActive = nonContact;
                result.BusTotal = remotePerBusGate > 0.0
                    ? (int)Math.Ceiling(nonContact / remotePerBusGate - 1e-9)
                    : nonContact;
            }
            else
            {
                // PHP mode: every non-contact stand is itself a bus gate; the
                // remote stands follow from the ratio at facility level.
                foreach (GateBucketStands bucket in bucketList)
                    bucket.Remote = bucket.Bus;

                result.BusTotal = nonContact;
                result.RemoteActive = (int)Math.Ceiling(result.BusTotal * remotePerBusGate - 1e-9);
            }

            result.MaxStands = (int)Math.Ceiling(
                result.ActiveTotal * (scheduleDemand != null ? 1.0 : Math.Max(0.0, standards.MaxStandsRatio)) - 1e-9);

            result.NetRemote = Math.Max(0, result.MaxStands - result.ContactTotal);

            if (scheduleDemand?.Codes != null)
            {
                result.SectorSumTotal = scheduleDemand.Codes
                    .Where(c => c != null)
                    .Sum(c => Math.Max(0, c.SectorSum));

                result.ConcurrentPeakTotal = scheduleDemand.Codes
                    .Where(c => c != null)
                    .Sum(c => Math.Max(0, c.CombinedPeak));
            }

            // ---- lounges ----
            BuildLounges(
                result,
                profiles,
                standards,
                scheduleDemand != null,
                result.BusTotal,
                domBucket,
                swingBucket,
                intlBucket);

            result.NetArea = result.HoldArea + result.PodiumArea + result.QueueArea;

            return result;
        }

        /// <summary>
        /// The stand requirement of one sector, per code.
        ///
        /// The sector's requirement is rounded once over the sum, so
        /// fractions of different codes can share a stand. The split into
        /// codes is rounded on its own, because a stand serves one code. The
        /// two can differ by a stand or two, and the difference is reported
        /// rather than hidden.
        /// </summary>
        private static SectorStands BuildSector(
            List<GateCodeInput> codes,
            GateStandards standards,
            bool domestic)
        {
            SectorStands sector = new SectorStands();

            double totalNeed = 0.0;

            foreach (GateCodeInput code in codes)
            {
                int movements = domestic ? code.DomesticAtms : code.InternationalAtms;
                double occupancy = domestic
                    ? code.DomesticOccupancyMinutes
                    : code.InternationalOccupancyMinutes;

                double standHours = movements > 0
                    ? (movements * Math.Max(0.0, occupancy + standards.BufferMinutes)) / 60.0
                    : 0.0;

                double need = standHours * (1.0 + Math.Max(0.0, standards.SparesPct));

                totalNeed += need;

                sector.Rows.Add(new GateCodeStands
                {
                    Code = code.Code,
                    StandHours = standHours,
                    Rounded = (int)Math.Ceiling(need - 1e-9)
                });
            }

            sector.Total = (int)Math.Ceiling(totalNeed - 1e-9);
            sector.SplitTotal = sector.Rows.Sum(r => r.Rounded);

            return sector;
        }

        private static SectorStands BuildScheduleSector(
            GateScheduleDemand demand,
            GateStandards standards,
            bool domestic,
            Dictionary<string, int> swingByCode)
        {
            SectorStands sector = new SectorStands();

            if (demand.Codes == null)
                return sector;

            foreach (GateScheduleCodeDemand code in demand.Codes
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Code)))
            {
                int combinedPeak = Math.Max(0, code.CombinedPeak);
                int spare = (int)Math.Ceiling(
                    combinedPeak * (1.0 + Math.Max(0.0, standards.SparesPct)) - 1e-9) - combinedPeak;
                int swing = Math.Max(0, code.Swing) + Math.Max(0, spare);
                int dedicated = domestic ? code.DedicatedDomestic : code.DedicatedInternational;
                int perSector = Math.Max(0, dedicated) + swing;
                string codeKey = code.Code.Trim().ToUpperInvariant();

                sector.Rows.Add(new GateCodeStands
                {
                    Code = codeKey,
                    Rounded = perSector,
                    StandHours = 0.0
                });

                swingByCode[codeKey] = swing;
                sector.Total += perSector;
                sector.SplitTotal += perSector;
            }

            return sector;
        }

        /// <summary>
        /// One group of stands, with its contact and bus split.
        /// </summary>
        private static GateBucketStands BuildBucket(
            string name,
            SectorStands sector,
            Dictionary<string, int> swing,
            double contactPct)
        {
            GateBucketStands bucket = new GateBucketStands
            {
                Bucket = name,
                SectorTotal = sector.Total
            };

            double share = Math.Min(1.0, Math.Max(0.0, contactPct));

            foreach (GateCodeStands row in sector.Rows)
            {
                int swung = swing.TryGetValue(row.Code, out int value) ? value : 0;

                int active = name == "Swing"
                    ? swung
                    : row.Rounded - swung;

                if (active < 0)
                    active = 0;

                int contact = (int)Math.Ceiling(active * share - 1e-9);

                if (contact > active)
                    contact = active;

                GateCodeStands code = new GateCodeStands
                {
                    Code = row.Code,
                    StandHours = row.StandHours,
                    Rounded = active,
                    Swing = name == "Swing" ? swung : 0,
                    Dedicated = name == "Swing" ? 0 : active,
                    Contact = contact,
                    Bus = active - contact
                };

                bucket.Codes.Add(code);
                bucket.Active += active;
                bucket.Contact += contact;
                bucket.Bus += active - contact;
            }

            // The sector's requirement is rounded once over the sum of its
            // codes, and the per-code split is rounded on its own, so the two
            // can differ. The swing group is not a sector, so it carries no
            // split of its own.
            bucket.SplitExtra = name == "Swing" ? 0 : sector.SplitTotal - sector.Total;

            return bucket;
        }

        /// <summary>
        /// The lounges: one per contact gate, sized by its own code, and one
        /// per bus gate, sized as the bus lounge code.
        /// </summary>
        private static void BuildLounges(
            GateSizingResult result,
            Dictionary<string, GateLoungeProfile> profiles,
            GateStandards standards,
            bool scheduleMode,
            int scheduleBusGates,
            params GateBucketStands[] buckets)
        {
            HashSet<string> missingProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GateBucketStands bucket in buckets)
            {
                foreach (GateCodeStands code in bucket.Codes)
                {
                    if (code.Rounded <= 0)
                        continue;

                    GateLoungeProfile input = profiles.TryGetValue(code.Code, out GateLoungeProfile profile)
                        ? profile
                        : null;

                    if (input == null)
                    {
                        if (missingProfiles.Add(code.Code))
                            result.Warnings.Add($"No per-code lounge profile was supplied for aircraft code {code.Code}; its lounges have no area.");

                        if (code.Contact > 0)
                            AddUnprofiledLounge(result, code.Code, bucket.Bucket, code.Contact);

                        if (!scheduleMode && code.Bus > 0 &&
                            !profiles.ContainsKey("C"))
                        {
                            if (missingProfiles.Add("C"))
                                result.Warnings.Add("No Code C lounge profile was supplied; bus-gate lounges have no area.");

                            AddUnprofiledLounge(result, "C", bucket.Bucket, code.Bus);
                        }

                        continue;
                    }

                    if (code.Contact > 0)
                    {
                        AddLounge(result, input, code.Code, bucket.Bucket, code.Contact, standards);
                    }

                    if (!scheduleMode && code.Bus > 0)
                    {
                        profiles.TryGetValue("C", out GateLoungeProfile busInput);

                        if (busInput == null)
                        {
                            if (missingProfiles.Add("C"))
                                result.Warnings.Add("No Code C lounge profile was supplied; bus-gate lounges have no area.");

                            AddUnprofiledLounge(result, "C", bucket.Bucket, code.Bus);
                        }
                        else
                        {
                            AddLounge(result, busInput, "C", bucket.Bucket, code.Bus, standards);
                        }
                    }
                }
            }

            if (scheduleMode && scheduleBusGates > 0)
            {
                profiles.TryGetValue("C", out GateLoungeProfile busProfile);

                if (busProfile == null)
                {
                    if (missingProfiles.Add("C"))
                        result.Warnings.Add("No Code C lounge profile was supplied; bus-gate lounges have no area.");

                    AddUnprofiledLounge(result, "C", "Bus", scheduleBusGates);
                }
                else
                {
                    AddLounge(result, busProfile, "C", "Bus", scheduleBusGates, standards);
                }
            }
        }

        private static void AddLounge(
            GateSizingResult result,
            GateLoungeProfile code,
            string loungeCode,
            string bucket,
            int count,
            GateStandards standards)
        {
            GateLounge lounge = BuildLounge(code, loungeCode, bucket, count, standards);

            result.Lounges.Add(lounge);
            result.HoldArea += lounge.HoldArea * lounge.Count;
            result.PodiumArea += lounge.PodiumArea * lounge.Count;
            result.QueueArea += lounge.QueueArea * lounge.Count;
        }

        private static void AddUnprofiledLounge(
            GateSizingResult result,
            string loungeCode,
            string bucket,
            int count)
        {
            if (count <= 0)
                return;

            result.Lounges.Add(new GateLounge
            {
                Code = loungeCode,
                Bucket = bucket,
                Count = count
            });
        }

        /// <summary>
        /// One lounge: the hold room, the podium and the boarding queue.
        /// </summary>
        private static GateLounge BuildLounge(
            GateLoungeProfile code,
            string loungeCode,
            string bucket,
            int count,
            GateStandards standards)
        {
            double passengers = Math.Max(0, code.Seats) * Math.Min(1.0, Math.Max(0.0, code.LoadFactor));

            double seated = passengers * Math.Min(1.0, Math.Max(0.0, code.SeatedPct));
            double standing = passengers * Math.Min(1.0, Math.Max(0.0, code.StandingPct));
            double queuePax = passengers * Math.Min(1.0, Math.Max(0.0, code.BoardingQueuePct));

            double seatedArea = seated * Math.Max(0.0, standards.SeatedSpacePerPax) *
                (1.0 + Math.Max(0.0, code.NextSeatAllowancePct));
            double standingArea = standing * Math.Max(0.0, standards.StandingSpacePerPax);

            double holdArea = (seatedArea + standingArea) *
                (1.0 - Math.Min(1.0, Math.Max(0.0, code.OpenLoungeReductionPct)));

            double podiumArea = Math.Max(0, code.PodiumsPerLounge) * Math.Max(0.0, standards.PodiumAreaEach);

            double queueArea = queuePax * Math.Max(0.0, standards.BoardingQueueSpacePerPax);

            return new GateLounge
            {
                Code = loungeCode,
                Bucket = bucket,
                Count = count,
                Passengers = passengers,
                HoldArea = holdArea,
                PodiumArea = podiumArea,
                QueueArea = queueArea
            };
        }

        private class SectorStands
        {
            public List<GateCodeStands> Rows = new List<GateCodeStands>();

            /// <summary>The sector requirement, rounded once over the sum.</summary>
            public int Total;

            /// <summary>The per-code split, rounded on its own.</summary>
            public int SplitTotal;

            public int Rounded(string code)
            {
                GateCodeStands row = Rows.FirstOrDefault(r => r.Code == code);

                return row != null ? row.Rounded : 0;
            }
        }
    }
}
