using System.Collections.Generic;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// The arrival deplaning curve — the share of the landed peak hour that
    /// reaches the first arrival process in its busiest 15, 30 and 60 minutes.
    ///
    /// This is a different thing in kind from the show-up curve: nobody chooses
    /// when to deplane, so the curve is the deplaning rate convolved with the
    /// walk. It is the profile-based planning route: the component resolves the
    /// shares and scales them by peak-hour passengers. Event-by-event simulation
    /// still runs through the flight-linked DES.
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

    /// <summary>
    /// How the deplaning profile was chosen. <see cref="Auto"/> resolves the
    /// airport size band from annual passenger volume; the three explicit band
    /// modes override that choice; <see cref="Custom"/> uses the supplied
    /// W15/W30 shares.
    /// </summary>
    public enum DeplaningProfileMode
    {
        Auto,
        Small,
        Medium,
        Large,
        Custom
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
            return FromBand(BandForMppa(targetMppa));
        }

        /// <summary>
        /// Authored deplaning sizing default for an explicit airport size band.
        /// </summary>
        public static DeplaningProfile FromBand(AirportSizeBand band)
        {
            switch (band)
            {
                case AirportSizeBand.Small:
                    return new DeplaningProfile("Small (Profile 1)", 0.45, 0.70, 1.00);
                case AirportSizeBand.Large:
                    return new DeplaningProfile("Large (Profile 3)", 0.35, 0.50, 1.00);
                default:
                    return new DeplaningProfile("Medium (Profile 2)", 0.40, 0.60, 1.00);
            }
        }

        /// <summary>
        /// Parses a user-facing mode label. Accepts the category names and the
        /// legacy numeric presets (0 = Custom, 1 = Small, 2 = Medium, 3 = Large).
        /// </summary>
        public static bool TryParseMode(string value, out DeplaningProfileMode mode)
        {
            mode = DeplaningProfileMode.Auto;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "auto":
                    mode = DeplaningProfileMode.Auto;
                    return true;
                case "small":
                case "1":
                    mode = DeplaningProfileMode.Small;
                    return true;
                case "medium":
                case "2":
                    mode = DeplaningProfileMode.Medium;
                    return true;
                case "large":
                case "3":
                    mode = DeplaningProfileMode.Large;
                    return true;
                case "custom":
                case "0":
                    mode = DeplaningProfileMode.Custom;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Resolves the profile for a mode. Auto and the explicit bands use the
        /// authored shares; Custom uses the supplied fractions with W60 fixed
        /// at 100%.
        /// </summary>
        public static DeplaningProfile FromMode(
            DeplaningProfileMode mode,
            double targetMppa,
            string name,
            double w15,
            double w30)
        {
            switch (mode)
            {
                case DeplaningProfileMode.Auto:
                    return FromAirportSize(targetMppa);
                case DeplaningProfileMode.Small:
                    return FromBand(AirportSizeBand.Small);
                case DeplaningProfileMode.Medium:
                    return FromBand(AirportSizeBand.Medium);
                case DeplaningProfileMode.Large:
                    return FromBand(AirportSizeBand.Large);
                default:
                    string label = string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim();
                    return new DeplaningProfile(label, w15, w30, 1.00);
            }
        }

        /// <summary>
        /// The category label shown to the planner. Auto resolves to the band
        /// selected from annual volume.
        /// </summary>
        public static string CategoryForMode(DeplaningProfileMode mode, double targetMppa)
        {
            if (mode == DeplaningProfileMode.Auto)
                return BandForMppa(targetMppa).ToString();

            return mode.ToString();
        }

        /// <summary>Passengers in the busiest 15 minutes for a given PHP.</summary>
        public double Peak15Passengers(double peakHourPassengers)
        {
            return peakHourPassengers * W15;
        }

        /// <summary>Passengers in the busiest 30 minutes for a given PHP.</summary>
        public double Peak30Passengers(double peakHourPassengers)
        {
            return peakHourPassengers * W30;
        }

        /// <summary>Passengers in the peak hour for a given PHP.</summary>
        public double Peak60Passengers(double peakHourPassengers)
        {
            return peakHourPassengers * W60;
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
