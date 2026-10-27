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
    /// The stand count is the ICAO peak-hour method: a stand is held while
    /// an aircraft is on it, so the requirement is movements multiplied by
    /// how long each holds one.
    ///
    /// The lounge is sized on the planeload the aircraft brings: seats
    /// standing, seats sitting, the queue waiting to board, and a podium.
    ///
    /// Nothing here depends on a flight schedule. A schedule-derived
    /// aircraft-on-ground count is a different method and would replace the
    /// movements, not the method.
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
            GateStandards standards)
        {
            GateSizingResult result = new GateSizingResult();

            if (codes == null || codes.Count == 0)
            {
                result.Warnings.Add("No aircraft codes were given, so no stands or lounges were sized.");

                return result;
            }

            if (standards == null)
            {
                result.Warnings.Add("No gate standards were given, so no stands or lounges were sized.");

                return result;
            }

            List<GateCodeInput> usable = codes
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Code))
                .ToList();

            if (usable.Count == 0)
            {
                result.Warnings.Add("Every aircraft code was blank, so nothing was sized.");

                return result;
            }

            // ---- stands, per sector ----
            SectorStands domestic = BuildSector(usable, standards, true);
            SectorStands international = BuildSector(usable, standards, false);

            // ---- swing, capped at the smaller sector ----
            Dictionary<string, int> swing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
            result.BusTotal = domBucket.Bus + swingBucket.Bus + intlBucket.Bus;

            result.RemoteActive = (int)Math.Ceiling(
                result.BusTotal * Math.Max(0.0, standards.RemoteStandsPerBusGate));

            result.MaxStands = (int)Math.Ceiling(
                result.ActiveTotal * Math.Max(0.0, standards.MaxStandsRatio));

            result.NetRemote = Math.Max(0, result.MaxStands - result.ContactTotal);

            // ---- lounges ----
            BuildLounges(result, usable, standards, domBucket, swingBucket, intlBucket);

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

                double standHours = movements > 0
                    ? (movements * Math.Max(0.0, code.OccupancyMinutes + standards.BufferMinutes)) / 60.0
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
            List<GateCodeInput> codes,
            GateStandards standards,
            params GateBucketStands[] buckets)
        {
            foreach (GateBucketStands bucket in buckets)
            {
                foreach (GateCodeStands code in bucket.Codes)
                {
                    if (code.Rounded <= 0)
                        continue;

                    GateCodeInput input = codes.FirstOrDefault(c => c.Code == code.Code);

                    if (input == null)
                        continue;

                    if (code.Contact > 0)
                    {
                        AddLounge(result, input, code.Code, bucket.Bucket, code.Contact, standards);
                    }

                    if (code.Bus > 0)
                    {
                        GateCodeInput busInput = codes.FirstOrDefault(c =>
                            string.Equals(c.Code, standards.BusLoungeCode, StringComparison.OrdinalIgnoreCase)) ?? input;

                        AddLounge(result, busInput, standards.BusLoungeCode, bucket.Bucket, code.Bus, standards);
                    }
                }
            }
        }

        private static void AddLounge(
            GateSizingResult result,
            GateCodeInput code,
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

        /// <summary>
        /// One lounge: the hold room, the podium and the boarding queue.
        /// </summary>
        private static GateLounge BuildLounge(
            GateCodeInput code,
            string loungeCode,
            string bucket,
            int count,
            GateStandards standards)
        {
            double passengers = Math.Max(0, code.Seats) * Math.Min(1.0, Math.Max(0.0, standards.LoadFactor));

            double seated = passengers * Math.Min(1.0, Math.Max(0.0, code.SeatedPct));
            double standing = passengers * Math.Min(1.0, Math.Max(0.0, code.StandingPct));
            double queuePax = passengers * Math.Min(1.0, Math.Max(0.0, code.BoardingQueuePct));

            double seatedArea = seated * Math.Max(0.0, standards.SeatedSpacePerPax);
            double standingArea = standing * Math.Max(0.0, standards.StandingSpacePerPax);

            double holdArea = (seatedArea + standingArea) *
                (1.0 + Math.Max(0.0, standards.NextSeatAllowancePct)) *
                (1.0 - Math.Min(1.0, Math.Max(0.0, standards.OpenLoungeReductionPct)));

            double podiumArea = Math.Max(0, standards.PodiumsPerLounge) * Math.Max(0.0, standards.PodiumAreaEach);

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
