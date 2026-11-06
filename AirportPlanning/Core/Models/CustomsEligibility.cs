using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Eligibility for the arrival-side customs hand-baggage stage.
    ///
    /// The stage serves passengers ENTERING THE COUNTRY: international arrivals,
    /// including International→Domestic transfers. Domestic arrivals never
    /// reach it, and International→International transfers stay airside, so
    /// neither clears customs. This mirrors the TypeScript demand rule
    /// (international arriving peak less int→int transfers) and is the single
    /// definition shared by the solver, the narration and the diagnostics.
    /// </summary>
    public static class CustomsEligibility
    {
        /// <summary>
        /// True when the passenger must clear hand-baggage customs at this
        /// terminal. An arriving transfer without a resolved scenario is kept
        /// eligible: only an explicit International→International flow proves
        /// the passenger remains airside.
        /// </summary>
        public static bool RequiresHandBaggageCustoms(Passenger passenger)
        {
            if (passenger == null)
                return false;

            if (passenger.DHPType != DHPType.InternationalArrival)
                return false;

            if (passenger.Journey == PassengerJourney.Transfer &&
                passenger.TransferScenario == TransferScenario.InternationalToInternational)
            {
                return false;
            }

            return true;
        }
    }
}
