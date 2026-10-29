using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Generation
{
    /// <summary>
    /// A transfer service that can be required or explicitly bypassed by a
    /// <see cref="TransferScenarioRules"/> object.
    /// </summary>
    public enum TransferService
    {
        TransferDesk = 0,
        CheckIn = 1,
        BagDrop = 2,
        Security = 3,
        Immigration = 4,
        Emigration = 5,
        BaggageRecheck = 6
    }

    /// <summary>
    /// Required/bypassed counts for one transfer service. A bypass is counted
    /// only when the passenger has an explicit transfer rule and that rule says
    /// the service is not required. Missing groups/profiles and capacity
    /// shortfalls are not bypasses.
    /// </summary>
    public class TransferServiceUsage
    {
        public TransferService Service { get; set; }

        /// <summary>Transfer passengers whose rule explicitly requires the service.</summary>
        public int Required { get; set; }

        /// <summary>Transfer passengers whose rule explicitly says the service is not required.</summary>
        public int Bypassed { get; set; }

        /// <summary>
        /// Transfer passengers with no explicit processing rules. They keep the
        /// normal profile/facility behaviour and are not counted as bypasses.
        /// </summary>
        public int Unspecified { get; set; }

        public int Applicable => Required + Bypassed + Unspecified;

        public string Label => Service.ToLabel();

        public string Describe()
        {
            string text = $"{Label}: {Bypassed} bypassed by scenario rule, {Required} required";

            if (Unspecified > 0)
                text += $", {Unspecified} without explicit transfer rules";

            return text;
        }
    }

    /// <summary>Per-service transfer usage for a generated population.</summary>
    public class TransferServiceUsageSummary
    {
        public Dictionary<TransferService, TransferServiceUsage> ByService { get; set; } =
            CreateEmpty();

        public TransferServiceUsage this[TransferService service]
        {
            get
            {
                if (ByService == null)
                    ByService = CreateEmpty();

                if (!ByService.TryGetValue(service, out TransferServiceUsage usage))
                {
                    usage = new TransferServiceUsage { Service = service };
                    ByService[service] = usage;
                }

                return usage;
            }
        }

        public int TotalBypassed =>
            ByService == null ? 0 : ByService.Values.Sum(usage => usage.Bypassed);

        public int TotalRequired =>
            ByService == null ? 0 : ByService.Values.Sum(usage => usage.Required);

        public int TotalUnspecified =>
            ByService == null ? 0 : ByService.Values.Sum(usage => usage.Unspecified);

        public List<string> CreateSummaryLines()
        {
            return Enum.GetValues(typeof(TransferService))
                .Cast<TransferService>()
                .Select(service => this[service])
                .Where(usage => usage.Applicable > 0)
                .Select(usage => usage.Describe())
                .ToList();
        }

        public List<string> CreateBypassLines()
        {
            return Enum.GetValues(typeof(TransferService))
                .Cast<TransferService>()
                .Select(service => this[service])
                .Where(usage => usage.Bypassed > 0)
                .Select(usage => usage.Describe())
                .ToList();
        }

        private static Dictionary<TransferService, TransferServiceUsage> CreateEmpty()
        {
            Dictionary<TransferService, TransferServiceUsage> usage =
                new Dictionary<TransferService, TransferServiceUsage>();

            foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
            {
                usage[service] = new TransferServiceUsage
                {
                    Service = service
                };
            }

            return usage;
        }
    }

    /// <summary>One rolling 60-minute transfer window used for peak reporting.</summary>
    public class TransferPeakWindow
    {
        public int StartMinute { get; set; }

        public int PassengerCount { get; set; }

        public bool Contains(double minute)
        {
            return PassengerCount > 0 &&
                   !double.IsNaN(minute) &&
                   !double.IsInfinity(minute) &&
                   minute >= StartMinute &&
                   minute < StartMinute + 60.0;
        }
    }

    /// <summary>
    /// Peak windows for the four transfer flight directions. Departure-side
    /// services use the departure windows; arrival-side services use the
    /// arrival windows, so transfer-desk and immigration counts are not
    /// compared against the outbound peak.
    /// </summary>
    public class TransferPeakWindows
    {
        public TransferPeakWindow DomesticDeparture { get; set; } = new TransferPeakWindow();
        public TransferPeakWindow InternationalDeparture { get; set; } = new TransferPeakWindow();
        public TransferPeakWindow DomesticArrival { get; set; } = new TransferPeakWindow();
        public TransferPeakWindow InternationalArrival { get; set; } = new TransferPeakWindow();

        /// <summary>True for PHP/aggregate populations, which are already a peak cohort.</summary>
        public bool AllPassengersInPeak { get; set; }

        public static TransferPeakWindows From(TransferDemandResult result)
        {
            if (result == null)
                return new TransferPeakWindows();

            return new TransferPeakWindows
            {
                DomesticDeparture = new TransferPeakWindow
                {
                    StartMinute = result.DomesticDepartureTransferPeakAt,
                    PassengerCount = result.DomesticDepartureTransferPeak
                },
                InternationalDeparture = new TransferPeakWindow
                {
                    StartMinute = result.InternationalDepartureTransferPeakAt,
                    PassengerCount = result.InternationalDepartureTransferPeak
                },
                DomesticArrival = new TransferPeakWindow
                {
                    StartMinute = result.DomesticArrivalTransferPeakAt,
                    PassengerCount = result.DomesticArrivalTransferPeak
                },
                InternationalArrival = new TransferPeakWindow
                {
                    StartMinute = result.InternationalArrivalTransferPeakAt,
                    PassengerCount = result.InternationalArrivalTransferPeak
                }
            };
        }

        public static TransferPeakWindows AllInPeak()
        {
            return new TransferPeakWindows { AllPassengersInPeak = true };
        }

        public bool Contains(TransferService service, Passenger passenger)
        {
            if (AllPassengersInPeak)
                return true;

            if (passenger == null)
                return false;

            if (TransferServiceUsageCalculator.IsDepartureService(service))
            {
                if (passenger.DHPType == DHPType.DomesticDeparture)
                    return DomesticDeparture.Contains(passenger.ScheduledDepartureTime);

                if (passenger.DHPType == DHPType.InternationalDeparture)
                    return InternationalDeparture.Contains(passenger.ScheduledDepartureTime);

                return false;
            }

            if (passenger.DHPType == DHPType.DomesticArrival)
                return DomesticArrival.Contains(passenger.ScheduledArrivalTime);

            if (passenger.DHPType == DHPType.InternationalArrival)
                return InternationalArrival.Contains(passenger.ScheduledArrivalTime);

            return false;
        }
    }

    /// <summary>
    /// Computes service usage from the passengers that were actually tagged.
    /// Arrival services are counted on the arrival leg and departure services
    /// on the departure leg, so one transfer journey is not double-counted for
    /// a single facility.
    /// </summary>
    public static class TransferServiceUsageCalculator
    {
        public static TransferServiceUsageSummary Calculate(IEnumerable<Passenger> passengers)
        {
            return Build(passengers, null);
        }

        public static TransferServiceUsageSummary CalculatePeak(
            IEnumerable<Passenger> passengers,
            TransferPeakWindows windows)
        {
            return Build(passengers, windows ?? new TransferPeakWindows());
        }

        public static TransferServiceUsage ForService(
            IEnumerable<Passenger> passengers,
            TransferService service)
        {
            return Calculate(passengers)[service];
        }

        public static bool IsBypassed(Passenger passenger, TransferService service)
        {
            return passenger != null &&
                   passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   AppliesTo(service, passenger) &&
                   !Requires(passenger.TransferProcessing, service);
        }

        public static bool IsRequired(Passenger passenger, TransferService service)
        {
            return passenger != null &&
                   passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing != null &&
                   AppliesTo(service, passenger) &&
                   Requires(passenger.TransferProcessing, service);
        }

        /// <summary>
        /// True when the passenger has no explicit transfer processing rules.
        /// Such passengers follow normal passenger behaviour and are reported
        /// separately from rule-based bypasses.
        /// </summary>
        public static bool IsUnspecified(Passenger passenger, TransferService service)
        {
            return passenger != null &&
                   passenger.Journey == PassengerJourney.Transfer &&
                   passenger.TransferProcessing == null &&
                   AppliesTo(service, passenger);
        }

        /// <summary>
        /// The single service-use predicate shared by the usage report and the
        /// facility solvers. An explicit transfer rule decides whether a tagged
        /// transfer uses the service; an unspecified transfer or an originating
        /// passenger keeps the normal facility behaviour. This is what prevents
        /// a rule-based bypass from being silently queued downstream.
        /// </summary>
        public static bool UsesService(Passenger passenger, TransferService service)
        {
            if (passenger == null || !AppliesTo(service, passenger))
                return false;

            if (passenger.Journey == PassengerJourney.Transfer &&
                passenger.TransferProcessing != null)
            {
                return Requires(passenger.TransferProcessing, service);
            }

            return NormalRequires(passenger, service);
        }

        public static List<TransferService> GetBypassedServices(Passenger passenger)
        {
            List<TransferService> services = new List<TransferService>();

            if (passenger == null || passenger.Journey != PassengerJourney.Transfer)
                return services;

            foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
            {
                if (IsBypassed(passenger, service))
                    services.Add(service);
            }

            return services;
        }

        /// <summary>True for services that happen on the outbound departure leg.</summary>
        public static bool IsDepartureService(TransferService service)
        {
            return service == TransferService.CheckIn ||
                   service == TransferService.BagDrop ||
                   service == TransferService.Security ||
                   service == TransferService.Emigration;
        }

        /// <summary>
        /// Which leg of the journey is responsible for the service. This keeps
        /// an arrival record from being reported as bypassing check-in, and a
        /// departure record from being reported as bypassing immigration.
        /// </summary>
        public static bool AppliesTo(TransferService service, Passenger passenger)
        {
            if (passenger == null)
                return false;

            bool departure = passenger.DHPType == DHPType.DomesticDeparture ||
                             passenger.DHPType == DHPType.InternationalDeparture;
            bool arrival = passenger.DHPType == DHPType.DomesticArrival ||
                           passenger.DHPType == DHPType.InternationalArrival;

            switch (service)
            {
                case TransferService.CheckIn:
                case TransferService.BagDrop:
                case TransferService.Security:
                    return departure;

                case TransferService.Emigration:
                    return departure && IsInternational(passenger);

                case TransferService.Immigration:
                    return arrival && IsInternational(passenger);

                case TransferService.TransferDesk:
                case TransferService.BaggageRecheck:
                    return arrival;

                default:
                    return false;
            }
        }

        private static TransferServiceUsageSummary Build(
            IEnumerable<Passenger> passengers,
            TransferPeakWindows peakWindows)
        {
            TransferServiceUsageSummary summary = new TransferServiceUsageSummary();

            if (passengers == null)
                return summary;

            foreach (Passenger passenger in passengers)
            {
                if (passenger == null || passenger.Journey != PassengerJourney.Transfer)
                    continue;

                foreach (TransferService service in Enum.GetValues(typeof(TransferService)))
                {
                    if (!AppliesTo(service, passenger))
                        continue;

                    if (peakWindows != null && !peakWindows.Contains(service, passenger))
                        continue;

                    TransferServiceUsage usage = summary[service];

                    if (passenger.TransferProcessing == null)
                    {
                        usage.Unspecified++;
                    }
                    else if (Requires(passenger.TransferProcessing, service))
                    {
                        usage.Required++;
                    }
                    else
                    {
                        usage.Bypassed++;
                    }
                }
            }

            return summary;
        }

        private static bool IsInternational(Passenger passenger)
        {
            return passenger.PassengerType == PassengerType.International ||
                   passenger.DHPType == DHPType.InternationalDeparture ||
                   passenger.DHPType == DHPType.InternationalArrival;
        }

        /// <summary>
        /// Normal behaviour for passengers without an explicit transfer rule.
        /// <see cref="AppliesTo"/> has already limited the service to the
        /// correct journey leg, so departure services only need their normal
        /// passenger-level condition here.
        /// </summary>
        private static bool NormalRequires(Passenger passenger, TransferService service)
        {
            switch (service)
            {
                case TransferService.CheckIn:
                    return passenger.RequiresCheckIn;
                case TransferService.BagDrop:
                    return passenger.RequiresBagDrop;
                case TransferService.Security:
                    return true;
                case TransferService.Immigration:
                    return true;
                case TransferService.Emigration:
                    return true;
                case TransferService.TransferDesk:
                case TransferService.BaggageRecheck:
                    return false;
                default:
                    return false;
            }
        }

        private static bool Requires(
            TransferScenarioRules rules,
            TransferService service)
        {
            if (rules == null)
                return false;

            switch (service)
            {
                case TransferService.TransferDesk:
                    return rules.RequiresTransferDesk;
                case TransferService.CheckIn:
                    return rules.RequiresCheckIn;
                case TransferService.BagDrop:
                    return rules.RequiresBagDrop;
                case TransferService.Security:
                    return rules.RequiresSecurityScreening;
                case TransferService.Immigration:
                    return rules.RequiresImmigration;
                case TransferService.Emigration:
                    return rules.RequiresEmigration;
                case TransferService.BaggageRecheck:
                    return rules.RequiresBaggageRecheck;
                default:
                    return false;
            }
        }
    }

    public static class TransferServiceLabels
    {
        public static string ToLabel(this TransferService service)
        {
            switch (service)
            {
                case TransferService.TransferDesk:
                    return "Transfer desk";
                case TransferService.CheckIn:
                    return "Check-in";
                case TransferService.BagDrop:
                    return "Bag drop";
                case TransferService.Security:
                    return "Security";
                case TransferService.Immigration:
                    return "Immigration";
                case TransferService.Emigration:
                    return "Emigration";
                case TransferService.BaggageRecheck:
                    return "Baggage recheck";
                default:
                    return service.ToString();
            }
        }
    }
}
