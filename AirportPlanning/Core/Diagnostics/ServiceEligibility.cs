using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Diagnostics
{
    public enum ServiceEligibilityReason
    {
        Eligible = 0,
        NotApplicable = 1,
        RuleBypass = 2,
        OtherBypass = 3,
        AlreadyServed = 4,
        LateCheckIn = 5,
        OnlineCheckIn = 6,
        NoCheckedBag = 7,
        TransferBypass = 8,
        NoEntryTimestamp = 9
    }

    public class ServiceExpectation
    {
        public bool IsEligible { get; set; }

        public ServiceEligibilityReason Reason { get; set; }

        public string Note { get; set; } = string.Empty;

        public static ServiceExpectation Eligible(string note = "")
        {
            return new ServiceExpectation
            {
                IsEligible = true,
                Reason = ServiceEligibilityReason.Eligible,
                Note = note
            };
        }

        public static ServiceExpectation NotApplicable(string note = "")
        {
            return new ServiceExpectation
            {
                IsEligible = false,
                Reason = ServiceEligibilityReason.NotApplicable,
                Note = note
            };
        }

        public static ServiceExpectation Bypass(ServiceEligibilityReason reason, string note = "")
        {
            return new ServiceExpectation
            {
                IsEligible = false,
                Reason = reason,
                Note = note
            };
        }
    }

    /// <summary>
    /// Independent statement of what each service is expected to serve. It
    /// mirrors the public passenger flags and the transfer-rule predicate used
    /// by the facility solvers, so a result can be audited without re-running
    /// the solver.
    /// </summary>
    public static class ServiceEligibility
    {
        public static ServiceExpectation Evaluate(
            Passenger passenger,
            DiagnosticsServiceKind kind,
            ISet<int> processedIds = null)
        {
            if (passenger == null)
                return ServiceExpectation.NotApplicable("No passenger.");

            FacilityType? historyKey = kind.ToFacilityType();

            if (historyKey.HasValue &&
                passenger.History != null &&
                passenger.History.ContainsKey(historyKey.Value) &&
                (processedIds == null || !processedIds.Contains(passenger.Id)))
            {
                return ServiceExpectation.Bypass(
                    ServiceEligibilityReason.AlreadyServed,
                    "A " + kind.ToLabel() + " history record already exists on this passenger.");
            }

            bool departure = IsDeparture(passenger);
            bool arrival = IsArrival(passenger);

            switch (kind)
            {
                case DiagnosticsServiceKind.TerminalEntry:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Not a departing passenger.");
                    if (passenger.Journey == PassengerJourney.Transfer)
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.TransferBypass,
                            "Transfer passengers are already airside.");
                    if (!string.IsNullOrWhiteSpace(passenger.FlightLegId) &&
                        IsNaN(passenger.TerminalEntryArrivalTime))
                    {
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.NoEntryTimestamp,
                            "Schedule passenger has no terminal-entry timestamp; the entry stage passes it through.");
                    }
                    return ServiceExpectation.Eligible();

                case DiagnosticsServiceKind.CheckIn:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Check-In applies only to departing passengers.");
                    if (passenger.IsLateForCheckIn)
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.LateCheckIn,
                            "Passenger reached check-in after last admission.");
                    return EvaluateCheckIn(passenger);

                case DiagnosticsServiceKind.BagDrop:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Bag Drop applies only to departing passengers.");
                    if (!passenger.HasCheckedBag && !NeedsTransferBagDrop(passenger))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.NoCheckedBag,
                            "Passenger has no checked bag.");
                    if (UsesTransferService(passenger, TransferService.BagDrop))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.BagDrop))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require bag drop.");
                    return ServiceExpectation.Bypass(
                        ServiceEligibilityReason.OtherBypass,
                        "Passenger does not require bag drop.");

                case DiagnosticsServiceKind.Security:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Security applies only to departing passengers.");
                    if (TransferServiceUsageCalculator.UsesService(passenger, TransferService.Security))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.Security))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require security screening.");
                    return ServiceExpectation.Bypass(
                        ServiceEligibilityReason.OtherBypass,
                        "Passenger does not require security screening.");

                case DiagnosticsServiceKind.Emigration:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Emigration applies only to departing passengers.");
                    // The Emigration solver keys eligibility on PassengerType,
                    // not on the DHPType sector. Mirroring it avoids reporting a
                    // data inconsistency as a missing service.
                    if (passenger.PassengerType != PassengerType.International)
                        return ServiceExpectation.NotApplicable("Emigration applies only to international passengers.");
                    if (TransferServiceUsageCalculator.UsesService(passenger, TransferService.Emigration))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.Emigration))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require emigration.");
                    return ServiceExpectation.Bypass(
                        ServiceEligibilityReason.OtherBypass,
                        "Passenger does not require emigration.");

                case DiagnosticsServiceKind.Immigration:
                    if (!arrival)
                        return ServiceExpectation.NotApplicable("Immigration applies only to arriving passengers.");
                    if (TransferServiceUsageCalculator.UsesService(passenger, TransferService.Immigration))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.Immigration))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require immigration.");
                    return ServiceExpectation.NotApplicable(
                        "Passenger does not require immigration (domestic arrival or rule-free flow).");

                case DiagnosticsServiceKind.Verification:
                    return ServiceExpectation.Eligible();

                case DiagnosticsServiceKind.Dwell:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Dwell applies only to departing passengers.");
                    return ServiceExpectation.Eligible();

                case DiagnosticsServiceKind.Boarding:
                    if (!departure)
                        return ServiceExpectation.NotApplicable("Boarding applies only to departing passengers.");
                    return ServiceExpectation.Eligible();

                case DiagnosticsServiceKind.TransferDesk:
                    if (TransferServiceUsageCalculator.UsesService(passenger, TransferService.TransferDesk))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.TransferDesk))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require a transfer desk.");
                    return ServiceExpectation.NotApplicable("No transfer desk requirement.");

                case DiagnosticsServiceKind.BaggageRecheck:
                    if (TransferServiceUsageCalculator.UsesService(passenger, TransferService.BaggageRecheck))
                        return ServiceExpectation.Eligible();
                    if (IsTransferRuleBypass(passenger, TransferService.BaggageRecheck))
                        return ServiceExpectation.Bypass(
                            ServiceEligibilityReason.RuleBypass,
                            "Transfer scenario rule does not require baggage recheck.");
                    return ServiceExpectation.NotApplicable("No baggage recheck requirement.");

                default:
                    return ServiceExpectation.NotApplicable("Unknown service type.");
            }
        }

        private static ServiceExpectation EvaluateCheckIn(Passenger passenger)
        {
            bool needsCheckIn =
                TransferServiceUsageCalculator.UsesService(passenger, TransferService.CheckIn);

            bool bagDropChannel = IsBagDropChannel(passenger.Channel);
            bool needsBagDropHere =
                bagDropChannel &&
                TransferServiceUsageCalculator.UsesService(passenger, TransferService.BagDrop);

            if (needsCheckIn || needsBagDropHere)
                return ServiceExpectation.Eligible();

            if (passenger.Journey == PassengerJourney.Transfer &&
                passenger.TransferProcessing != null)
            {
                bool checkInBypassed =
                    TransferServiceUsageCalculator.IsBypassed(passenger, TransferService.CheckIn);
                bool bagDropBypassed =
                    !bagDropChannel ||
                    TransferServiceUsageCalculator.IsBypassed(passenger, TransferService.BagDrop);

                if (checkInBypassed && bagDropBypassed)
                {
                    return ServiceExpectation.Bypass(
                        ServiceEligibilityReason.RuleBypass,
                        "Transfer scenario rule does not require check-in or bag drop.");
                }
            }

            if (passenger.UsesOnlineCheckIn)
                return ServiceExpectation.Bypass(
                    ServiceEligibilityReason.OnlineCheckIn,
                    "Passenger checked in online and has no bag-drop requirement at check-in.");

            if (!passenger.HasCheckedBag)
                return ServiceExpectation.Bypass(
                    ServiceEligibilityReason.NoCheckedBag,
                    "Passenger has no checked bag and does not require check-in.");

            return ServiceExpectation.Bypass(
                ServiceEligibilityReason.OtherBypass,
                "Passenger does not require check-in.");
        }

        private static bool NeedsTransferBagDrop(Passenger passenger)
        {
            return passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   passenger.TransferProcessing.RequiresBagDrop;
        }

        private static bool UsesTransferService(Passenger passenger, TransferService service)
        {
            return TransferServiceUsageCalculator.UsesService(passenger, service);
        }

        private static bool IsTransferRuleBypass(Passenger passenger, TransferService service)
        {
            return passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   TransferServiceUsageCalculator.IsBypassed(passenger, service);
        }

        private static bool IsDeparture(Passenger passenger)
        {
            return passenger.DHPType == DHPType.DomesticDeparture ||
                   passenger.DHPType == DHPType.InternationalDeparture;
        }

        private static bool IsArrival(Passenger passenger)
        {
            return passenger.DHPType == DHPType.DomesticArrival ||
                   passenger.DHPType == DHPType.InternationalArrival;
        }

        private static bool IsNaN(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }

        /// <summary>
        /// Matches the Check-In solver's BagDrop channel convention (for
        /// example "BagDrop", "Bag Drop", "bag-drop").
        /// </summary>
        public static bool IsBagDropChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return false;

            string normalized = new string(channel
                .Where(char.IsLetterOrDigit)
                .ToArray());

            return string.Equals(normalized, "BagDrop", StringComparison.OrdinalIgnoreCase);
        }
    }
}
