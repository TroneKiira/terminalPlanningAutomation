using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Transfer
{
    /// <summary>
    /// Temporary per-flow pool routing for the transfer facilities.
    ///
    /// The TypeScript model sizes every transfer flow separately: three desk
    /// flows (Domestic→Domestic, Domestic→International, International→
    /// International) plus the baggage-recheck flow (International→Domestic).
    /// To reproduce that in the DES solver, served transfer arrivals are
    /// re-channeled to a per-scenario channel before pool selection and their
    /// original channels are restored after the solve. The passenger's
    /// persistent channel therefore keeps its downstream meaning: the queue
    /// classification exists only inside the facility, exactly like the
    /// separate-pool routing in Security, Immigration and Emigration.
    /// </summary>
    public static class TransferFlowPools
    {
        public const string DeskPoolPrefix = "Transfer Desk";
        public const string RecheckPoolPrefix = "Baggage Recheck";

        /// <summary>
        /// Channel identity for one transfer flow inside a transfer facility,
        /// for example "Transfer Desk (Domestic to international)". The flow
        /// label matches <see cref="TransferScenarioExtensions.ToFlowLabel"/> so
        /// Pool Plans can be keyed by the name the Pool Names output reports.
        /// </summary>
        public static string FlowChannelName(string prefix, TransferScenario scenario)
        {
            return $"{prefix} ({scenario.ToFlowLabel()})";
        }

        /// <summary>
        /// Re-channels the served passengers to per-flow pools when
        /// <paramref name="splitPoolsPerFlow"/> is on. Returns the saved
        /// original channels so they can be restored after the solve.
        /// A shared pool keeps the original channels untouched.
        /// </summary>
        public static Dictionary<Passenger, string> ApplyFlowRouting(
            IEnumerable<Passenger> passengers,
            Func<Passenger, bool> servedPredicate,
            string poolPrefix,
            bool splitPoolsPerFlow)
        {
            Dictionary<Passenger, string> originalChannels =
                new Dictionary<Passenger, string>();

            if (passengers == null || !splitPoolsPerFlow)
                return originalChannels;

            foreach (Passenger passenger in passengers)
            {
                if (passenger == null || !servedPredicate(passenger))
                    continue;

                originalChannels[passenger] = passenger.Channel;

                if (passenger.TransferScenario.HasValue)
                {
                    passenger.Channel = FlowChannelName(
                        poolPrefix,
                        passenger.TransferScenario.Value);
                }
                else
                {
                    // A transfer without a concrete scenario keeps one shared
                    // flow pool so it is still sized and served.
                    passenger.Channel = poolPrefix;
                }
            }

            return originalChannels;
        }

        /// <summary>Restores the original channels saved by
        /// <see cref="ApplyFlowRouting"/> on both the originals and their
        /// solved clones.</summary>
        public static void RestoreFlowChannels(
            Dictionary<Passenger, string> originalChannels,
            Dictionary<Passenger, Passenger> clones)
        {
            if (originalChannels == null)
                return;

            foreach (KeyValuePair<Passenger, string> pair in originalChannels)
            {
                pair.Key.Channel = pair.Value;

                if (clones != null && clones.TryGetValue(pair.Key, out Passenger clone))
                    clone.Channel = pair.Value;
            }
        }

        /// <summary>
        /// The per-flow channel identities present in a routed population, in
        /// stable flow order. Used to auto-create one dedicated pool per flow
        /// when the caller supplies no explicit channels.
        /// </summary>
        public static List<string> PresentFlowChannels(
            IEnumerable<Passenger> passengers,
            Func<Passenger, bool> servedPredicate,
            string poolPrefix,
            bool splitPoolsPerFlow)
        {
            List<string> channels = new List<string>();

            if (passengers == null || !splitPoolsPerFlow)
                return channels;

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                bool used = passengers.Any(p =>
                    p != null &&
                    servedPredicate(p) &&
                    (p.TransferScenario.HasValue
                        ? p.TransferScenario.Value == scenario
                        : false));

                if (used)
                    channels.Add(FlowChannelName(poolPrefix, scenario));
            }

            bool unspecified = passengers.Any(p =>
                p != null &&
                servedPredicate(p) &&
                !p.TransferScenario.HasValue);

            if (unspecified)
                channels.Add(poolPrefix);

            return channels;
        }

        /// <summary>
        /// Warns when a passenger's scenario rules require both the transfer
        /// desk and baggage recheck. The TypeScript model treats the recheck
        /// counter as the desk facility for the International→Domestic flow
        /// (one facility serves that flow), so a desk queue that also serves it
        /// would double-count the flow. The solvers therefore apply
        /// substitution and this warning explains the policy.
        /// </summary>
        public static void WarnOnDeskAndRecheckOverlap(
            IEnumerable<Passenger> passengers,
            Action<string> warn)
        {
            if (passengers == null)
                return;

            int overlapping = passengers.Count(p =>
                p != null &&
                p.Journey == PassengerJourney.Transfer &&
                p.TransferProcessing != null &&
                p.TransferProcessing.RequiresTransferDesk &&
                p.TransferProcessing.RequiresBaggageRecheck);

            if (overlapping > 0)
            {
                warn?.Invoke(
                    $"{overlapping} transfer passenger(s) require both a transfer desk and " +
                    "baggage recheck. Following the TypeScript model, baggage recheck is the " +
                    "desk facility for International to Domestic flows, so these passengers " +
                    "are served by the recheck queue only. Clear the desk flag for that flow " +
                    "if the overlap is unintended.");
            }
        }
    }
}
