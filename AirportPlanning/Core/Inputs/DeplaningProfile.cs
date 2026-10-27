using System.Collections.Generic;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// The arrival deplaning curve — the share of the landed peak hour that
    /// reaches the first arrival process in its busiest 15, 30 and 60 minutes.
    ///
    /// This is a different thing in kind from the show-up curve: nobody chooses
    /// when to deplane, so the curve is the deplaning rate convolved with the
    /// walk. It is currently a DATA MODEL ONLY — reserved for arrival-side
    /// facilities and not yet consumed by the simulation.
    ///
    /// Presets are neutral (Profile 1/2/3); Profile 1 is the most concentrated,
    /// Profile 3 the flattest and sits on the 0.50 floor at 30 minutes.
    /// </summary>
    public enum AirportSizeBand
    {
        Small,
        Medium,
        Large
    }

    public class DeplaningProfile : WindowShareProfile
    {
        /// <summary>
        /// Report default boundary: small airports are up to 10 mppa.
        /// </summary>
        public const double SmallUpperMppa = 10.0;

        /// <summary>
        /// Report default boundary: medium airports are above 10 and up to 25 mppa.
        /// </summary>
        public const double MediumUpperMppa = 25.0;

        public DeplaningProfile()
        {
            Name = "Custom";
        }

        public DeplaningProfile(string name, double w15, double w30, double w60 = 1.0)
            : base(name, w15, w30, w60)
        {
        }

        /// <summary>Most concentrated shipped curve (45 / 70 / 100).</summary>
        public static DeplaningProfile Profile1()
        {
            return new DeplaningProfile("Profile 1", 0.45, 0.70, 1.00);
        }

        /// <summary>
        /// Selects the authored default band from a target annual passenger
        /// throughput in millions. This is a preset for sizing windows, not a
        /// minute-by-minute arrival curve.
        /// </summary>
        public static AirportSizeBand BandForMppa(double targetMppa)
        {
            // No usable target: the report starts new projects on Medium.
            if (double.IsNaN(targetMppa) || double.IsInfinity(targetMppa) || targetMppa <= 0.0)
                return AirportSizeBand.Medium;

            if (targetMppa <= SmallUpperMppa)
                return AirportSizeBand.Small;

            if (targetMppa <= MediumUpperMppa)
                return AirportSizeBand.Medium;

            return AirportSizeBand.Large;
        }

        /// <summary>
        /// Authored deplaning sizing default for the selected airport size.
        /// Values come from MODELS_REPORT.tex and remain editable after prefill.
        /// </summary>
        public static DeplaningProfile FromAirportSize(double targetMppa)
        {
            switch (BandForMppa(targetMppa))
            {
                case AirportSizeBand.Small:
                    return new DeplaningProfile("Small (Profile 1)", 0.45, 0.70, 1.00);
                case AirportSizeBand.Large:
                    return new DeplaningProfile("Large (Profile 3)", 0.35, 0.50, 1.00);
                default:
                    return new DeplaningProfile("Medium (Profile 2)", 0.40, 0.60, 1.00);
            }
        }

        /// <summary>Middle shipped curve (40 / 60 / 100).</summary>
        public static DeplaningProfile Profile2()
        {
            return new DeplaningProfile("Profile 2", 0.40, 0.60, 1.00);
        }

        /// <summary>Flattest shipped curve (35 / 50 / 100); sits on the 0.50 floor.</summary>
        public static DeplaningProfile Profile3()
        {
            return new DeplaningProfile("Profile 3", 0.35, 0.50, 1.00);
        }

        /// <summary>
        /// Shipped preset by index: 1, 2 or 3. Any other value returns null
        /// (the caller builds a custom profile).
        /// </summary>
        public static DeplaningProfile FromPreset(int index)
        {
            switch (index)
            {
                case 1: return Profile1();
                case 2: return Profile2();
                case 3: return Profile3();
                default: return null;
            }
        }

        /// <summary>
        /// Adds the two window heuristics that <see cref="WindowShareProfile"/>
        /// no longer enforces.
        ///
        /// They were lifted out of the base class because every shipped show-up
        /// curve breaks them - a profile whose mass sits early in the hour
        /// necessarily holds more of the hour in its last 30 than twice its last
        /// 15. Neither objection applies here: deplaning is not chosen, so the
        /// curve is the deplaning rate convolved with the walk, and it is
        /// expected to concentrate near arrival. Profile 3 deliberately sits on
        /// the 0.50 floor.
        /// </summary>
        public override List<string> GetValidationErrors()
        {
            List<string> errors = base.GetValidationErrors();

            if (W30 < 0.5 - 1e-9)
                errors.Add("W30 cannot be below 50%: the busier half of an hour always holds at least half.");

            if (W30 > 2.0 * W15 + 1e-9)
                errors.Add("W30 cannot exceed twice W15: the busiest 30 minutes cannot beat two busiest 15s.");

            return errors;
        }
    }
}
