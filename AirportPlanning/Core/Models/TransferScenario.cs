namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Direction of a transfer flow, from the arriving sector to the departing
    /// sector.
    /// </summary>
    public enum TransferScenario
    {
        DomesticToDomestic,
        DomesticToInternational,
        InternationalToDomestic,
        InternationalToInternational
    }

    public static class TransferScenarioExtensions
    {
        public static bool IsDomesticArrival(this TransferScenario scenario)
        {
            return scenario == TransferScenario.DomesticToDomestic ||
                   scenario == TransferScenario.DomesticToInternational;
        }

        public static bool IsDomesticDeparture(this TransferScenario scenario)
        {
            return scenario == TransferScenario.DomesticToDomestic ||
                   scenario == TransferScenario.InternationalToDomestic;
        }

        public static string ToFlowLabel(this TransferScenario scenario)
        {
            switch (scenario)
            {
                case TransferScenario.DomesticToDomestic:
                    return "Domestic to domestic";
                case TransferScenario.DomesticToInternational:
                    return "Domestic to international";
                case TransferScenario.InternationalToDomestic:
                    return "International to domestic";
                case TransferScenario.InternationalToInternational:
                    return "International to international";
                default:
                    return scenario.ToString();
            }
        }
    }
}
