using System;
using System.Collections.Generic;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// Which schedule legs may carry transfer demand in Design Day mode.
    /// AllPassengerLegs matches any passenger arrival/departure pair inside the
    /// scenario connection windows; ViaQualifiedLegs additionally requires the
    /// leg's Via to differ from its Origin (arrivals) or Destination
    /// (departures). The Via rule is a data-quality restriction, not a
    /// passenger behaviour: a direct inbound flight still delivers connecting
    /// passengers, so AllPassengerLegs is the default.
    /// </summary>
    public enum TransferScheduleEligibility
    {
        /// <summary>Every passenger leg is an eligible transfer leg. Default.</summary>
        AllPassengerLegs = 0,
        /// <summary>Only legs whose Via differs from Origin/Destination qualify.</summary>
        ViaQualifiedLegs = 1
    }

    /// <summary>
    /// Planner-entered transfer assumptions shared by aggregate and
    /// schedule-driven generation. Overall percentages are shares of the
    /// corresponding daily departure market in Design Day mode (the generated
    /// peak population in PHP mode). The four scenario percentages allocate
    /// those transfer totals by outbound market.
    /// </summary>
    public class TransferDemandProfile
    {
        /// <summary>
        /// Which schedule legs Design Day matching may use. See
        /// <see cref="TransferScheduleEligibility"/>.
        /// </summary>
        public TransferScheduleEligibility ScheduleEligibility { get; set; }
            = TransferScheduleEligibility.AllPassengerLegs;

        public double DomesticTransferPercent { get; set; }
        public double InternationalTransferPercent { get; set; }

        public double DomesticToDomesticPercent { get; set; }
        public double DomesticToInternationalPercent { get; set; }
        public double InternationalToDomesticPercent { get; set; }
        public double InternationalToInternationalPercent { get; set; }

        public Dictionary<TransferScenario, TransferScenarioRules> ScenarioRules { get; set; } =
            CreateDefaultRules();

        public double AllocationPercent(TransferScenario scenario)
        {
            switch (scenario)
            {
                case TransferScenario.DomesticToDomestic:
                    return DomesticToDomesticPercent;
                case TransferScenario.DomesticToInternational:
                    return DomesticToInternationalPercent;
                case TransferScenario.InternationalToDomestic:
                    return InternationalToDomesticPercent;
                case TransferScenario.InternationalToInternational:
                    return InternationalToInternationalPercent;
                default:
                    return 0.0;
            }
        }

        public double DepartureTransferPercent(bool domestic)
        {
            return domestic ? DomesticTransferPercent : InternationalTransferPercent;
        }

        public TransferScenarioRules RulesFor(TransferScenario scenario)
        {
            if (ScenarioRules != null && ScenarioRules.TryGetValue(scenario, out TransferScenarioRules rules))
                return rules ?? new TransferScenarioRules();

            return new TransferScenarioRules();
        }

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (ScheduleEligibility != TransferScheduleEligibility.AllPassengerLegs &&
                ScheduleEligibility != TransferScheduleEligibility.ViaQualifiedLegs)
            {
                errors.Add(
                    "Schedule eligibility must be 0 (all passenger legs) or " +
                    "1 (via-qualified legs only).");
            }

            ValidatePercent(DomesticTransferPercent, "Domestic transfer percentage", errors);
            ValidatePercent(InternationalTransferPercent, "International transfer percentage", errors);
            ValidatePercent(DomesticToDomesticPercent, "Domestic-to-domestic allocation", errors);
            ValidatePercent(DomesticToInternationalPercent, "Domestic-to-international allocation", errors);
            ValidatePercent(InternationalToDomesticPercent, "International-to-domestic allocation", errors);
            ValidatePercent(InternationalToInternationalPercent, "International-to-international allocation", errors);

            if (DomesticToDomesticPercent + InternationalToDomesticPercent > 100.000001)
            {
                errors.Add(
                    "Domestic-outbound scenario allocations (Domestic→Domestic + International→Domestic) " +
                    "must not exceed 100% of the Domestic transfer total.");
            }

            if (DomesticToInternationalPercent + InternationalToInternationalPercent > 100.000001)
            {
                errors.Add(
                    "International-outbound scenario allocations (Domestic→International + " +
                    "International→International) must not exceed 100% of the International transfer total.");
            }

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                TransferScenarioRules rule = RulesFor(scenario);
                if (rule == null)
                {
                    errors.Add($"No connection rules supplied for {scenario.ToFlowLabel()}.");
                    continue;
                }

                if (!IsFinite(rule.MinimumConnectionMinutes) || rule.MinimumConnectionMinutes < 0.0)
                    errors.Add($"{scenario.ToFlowLabel()}: minimum connection time must be non-negative and finite.");

                if (!IsFinite(rule.MaximumConnectionMinutes) || rule.MaximumConnectionMinutes < 0.0)
                    errors.Add($"{scenario.ToFlowLabel()}: maximum connection time must be non-negative and finite.");

                if (rule.MaximumConnectionMinutes < rule.MinimumConnectionMinutes)
                    errors.Add($"{scenario.ToFlowLabel()}: maximum connection time must be at least the minimum.");

                if (rule.SecurityRouting != TransferFacilityRouting.Integrated &&
                    rule.SecurityRouting != TransferFacilityRouting.Separate)
                    errors.Add($"{scenario.ToFlowLabel()}: security routing must be 0 (Integrated) or 1 (Separate).");

                if (rule.EmigrationRouting != TransferFacilityRouting.Integrated &&
                    rule.EmigrationRouting != TransferFacilityRouting.Separate)
                    errors.Add($"{scenario.ToFlowLabel()}: emigration routing must be 0 (Integrated) or 1 (Separate).");

                if (rule.ImmigrationRouting != TransferFacilityRouting.Integrated &&
                    rule.ImmigrationRouting != TransferFacilityRouting.Separate)
                    errors.Add($"{scenario.ToFlowLabel()}: immigration routing must be 0 (Integrated) or 1 (Separate).");
            }

            return errors;
        }

        private static void ValidatePercent(double value, string label, List<string> errors)
        {
            if (!IsFinite(value) || value < 0.0 || value > 100.0)
                errors.Add($"{label} must be between 0% and 100%.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static Dictionary<TransferScenario, TransferScenarioRules> CreateDefaultRules()
        {
            Dictionary<TransferScenario, TransferScenarioRules> rules =
                new Dictionary<TransferScenario, TransferScenarioRules>();

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
            {
                rules[scenario] = new TransferScenarioRules
                {
                    Scenario = scenario,
                    MinimumConnectionMinutes = 0.0,
                    MaximumConnectionMinutes = 1440.0
                };
            }

            return rules;
        }
    }

    /// <summary>
    /// Where a transfer flow clears a process it requires. Integrated sends the
    /// cohort through the same facility as originating passengers; Separate
    /// reserves a dedicated transfer pool for them. The choice is carried on
    /// each tagged passenger inside <see cref="TransferScenarioRules"/>, so
    /// every solver can apply it without extra wiring.
    /// </summary>
    public enum TransferFacilityRouting
    {
        /// <summary>Use the existing (shared) facility flow. Default.</summary>
        Integrated = 0,
        /// <summary>Use a dedicated transfer pool, keyed by the solver's transfer channel.</summary>
        Separate = 1
    }

    /// <summary>Connection and process policy for one transfer scenario.</summary>
    public class TransferScenarioRules
    {
        public TransferScenario Scenario { get; set; }
        public bool HasProcessingRules { get; set; }
        public double MinimumConnectionMinutes { get; set; }
        public double MaximumConnectionMinutes { get; set; }

        public bool RequiresTransferDesk { get; set; }
        public bool RequiresSecurityScreening { get; set; }
        public bool RequiresImmigration { get; set; }
        public bool RequiresEmigration { get; set; }
        public bool RequiresBaggageRecheck { get; set; }
        public bool RequiresCheckIn { get; set; }
        public bool RequiresBagDrop { get; set; }

        /// <summary>
        /// Where the flow clears security when
        /// <see cref="RequiresSecurityScreening"/> is set: the shared security
        /// facility, or a dedicated transfer-security pool.
        /// </summary>
        public TransferFacilityRouting SecurityRouting { get; set; }

        /// <summary>
        /// Where the flow clears emigration when <see cref="RequiresEmigration"/>
        /// is set: the shared emigration hall, or a dedicated transfer pool.
        /// </summary>
        public TransferFacilityRouting EmigrationRouting { get; set; }

        /// <summary>
        /// Where the flow clears immigration when <see cref="RequiresImmigration"/>
        /// is set: the shared immigration hall, or a dedicated transfer pool.
        /// Relevant to International→Domestic transfers.
        /// </summary>
        public TransferFacilityRouting ImmigrationRouting { get; set; }

        public TransferScenarioRules Clone()
        {
            return new TransferScenarioRules
            {
                Scenario = Scenario,
                HasProcessingRules = HasProcessingRules,
                MinimumConnectionMinutes = MinimumConnectionMinutes,
                MaximumConnectionMinutes = MaximumConnectionMinutes,
                RequiresTransferDesk = RequiresTransferDesk,
                RequiresSecurityScreening = RequiresSecurityScreening,
                RequiresImmigration = RequiresImmigration,
                RequiresEmigration = RequiresEmigration,
                RequiresBaggageRecheck = RequiresBaggageRecheck,
                RequiresCheckIn = RequiresCheckIn,
                RequiresBagDrop = RequiresBagDrop,
                SecurityRouting = SecurityRouting,
                EmigrationRouting = EmigrationRouting,
                ImmigrationRouting = ImmigrationRouting
            };
        }
    }
}
