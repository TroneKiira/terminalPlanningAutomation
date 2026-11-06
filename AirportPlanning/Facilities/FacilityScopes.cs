namespace AirportPlanning.Facilities
{
    /// <summary>
    /// Canonical facility names used to scope pool plans and service-time rows.
    ///
    /// A pool plan (or service-time row) tagged with one of these names is only
    /// used by that facility, so the same identity or key - for example
    /// "EGate" at Emigration and "EGate" at security entry - can carry
    /// completely different values in one shared list. Untagged rows apply to
    /// every facility, which keeps older definitions working unchanged.
    /// </summary>
    public static class FacilityScopes
    {
        public const string TerminalEntry = "Terminal Entry";
        public const string CheckIn = "Check-In";
        public const string BagDrop = "Bag Drop";
        public const string Security = "Security";
        public const string Emigration = "Emigration";
        public const string Immigration = "Immigration";
        public const string TransferDesk = "Transfer Desk";
        public const string BaggageRecheck = "Baggage Recheck";
        public const string Customs = "Customs Hand Baggage";
    }
}
