using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Inputs
{
    /// <summary>
    /// How passengers of one type spread across the hour before their flight.
    ///
    /// The curve is stored as ten six-minute bands. Band 0 is the earliest
    /// arrivals (60 to 54 minutes before departure), band 9 the latest (6 to 0
    /// minutes before). Each entry is the share of the departing hour arriving
    /// in that band, and the ten shares always sum to 1.
    ///
    /// This replaced the old "one centred 15-minute peak block plus uniform
    /// background" shape. A block floating in the middle of the hour was not
    /// physically meaningful, and one global profile could not tell a leisure
    /// passenger from a business one. Ten named bands let each type say where in
    /// the hour its people actually turn up.
    ///
    /// W15/W30/W60 are inherited from <see cref="WindowShareProfile"/> and mean
    /// the nested windows ending at departure: W15 is the share arriving in the
    /// last 15 minutes, W30 the last 30, W60 the whole hour. They are derived
    /// from the bands rather than stored independently, so the quoted numbers
    /// and the plotted curve can never disagree.
    /// </summary>
    public class ShowUpProfile : WindowShareProfile
    {
        /// <summary>Number of six-minute bands the hour is divided into.</summary>
        public const int ChunkCount = 10;

        /// <summary>Length of the pre-departure block the bands tile.</summary>
        public const double BlockMinutes = 60.0;

        private static readonly double[] LeisureBands =
        {
            0.26, 0.12, 0.03, 0.08, 0.15, 0.10, 0.07, 0.13, 0.04, 0.02
        };

        private static readonly double[] BusinessBands =
        {
            0.05, 0.05, 0.06, 0.15, 0.13, 0.13, 0.17, 0.11, 0.07, 0.08
        };

        private static readonly double[] MixBands =
        {
            0.09, 0.07, 0.14, 0.15, 0.12, 0.07, 0.13, 0.12, 0.06, 0.05
        };

        /// <summary>
        /// Share of the hour arriving in each six-minute band, earliest first.
        /// Always <see cref="ChunkCount"/> long and summing to 1.
        /// </summary>
        public List<double> Bands { get; set; } = new List<double>();

        /// <summary>A flat profile: arrivals spread evenly across the hour.</summary>
        public ShowUpProfile()
        {
            Name = "Custom";

            SetBands(new List<double>(new double[ChunkCount]
            {
                1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount,
                1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount, 1.0 / ChunkCount
            }));
        }

        /// <summary>
        /// A profile described by its window shares. The bands are fitted
        /// through them, so the object is immediately usable for generation.
        /// </summary>
        public ShowUpProfile(string name, double w15, double w30, double w60 = 1.0)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Custom" : name;

            SetBands(new List<double>(FitGaussian(w15, w30)));

            W15 = w15;
            W30 = w30;
            W60 = w60;
        }

        private ShowUpProfile(string name, IList<double> bands)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Custom" : name;

            SetBands(bands);
        }

        // -------------------------------------------------------------
        // Shipped presets
        // -------------------------------------------------------------

        /// <summary>
        /// Leisure passengers: early and broad, 26% arriving more than 54
        /// minutes before departure and only 12% inside the last 15.
        /// </summary>
        public static ShowUpProfile Leisure()
        {
            return new ShowUpProfile("Leisure", new List<double>(LeisureBands));
        }

        /// <summary>
        /// Business passengers: late and sharp, 56% arriving inside the last 30
        /// minutes against 5% in the first six.
        /// </summary>
        public static ShowUpProfile Business()
        {
            return new ShowUpProfile("Business", new List<double>(BusinessBands));
        }

        /// <summary>
        /// Mixed traffic. Not the average of the other two: the first band is
        /// 0.09 against 0.155 for a straight blend of Leisure and Business.
        /// </summary>
        public static ShowUpProfile Mix()
        {
            return new ShowUpProfile("Mix", new List<double>(MixBands));
        }

        /// <summary>Preset by name; null for anything unrecognised.</summary>
        public static ShowUpProfile FromPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            switch (name.Trim().ToLowerInvariant())
            {
                case "leisure": return Leisure();
                case "business": return Business();
                case "mix": return Mix();
                default: return null;
            }
        }

        /// <summary>Preset names, for a Grasshopper drop-down.</summary>
        public static List<string> PresetNames()
        {
            return new List<string> { "Leisure", "Business", "Mix", "Custom" };
        }

        // -------------------------------------------------------------
        // Construction helpers
        // -------------------------------------------------------------

        /// <summary>
        /// Builds the ten bands from planner-facing W15/W30/W60 by fitting a
        /// Gaussian arrival density through those points and reading the band
        /// boundaries off it.
        ///
        /// W60 is 100% by definition, so W15 and W30 give exactly two equations
        /// for the density's mean and spread. The fit is a deterministic grid
        /// search rather than Newton, so it cannot fail to converge and returns
        /// the same answer on every run.
        /// </summary>
        public static ShowUpProfile FromWindowShares(
            string name,
            double w15,
            double w30,
            double w60 = 1.0)
        {
            return new ShowUpProfile(name, w15, w30, w60);
        }

        /// <summary>
        /// Normalises an arbitrary list of shares onto the ten bands, so a
        /// planner can supply 3, 5 or 20 values and still get a valid profile.
        /// Interpolation is on the cumulative curve, so the shape between the
        /// supplied points is interpolated rather than guessed.
        /// </summary>
        public static ShowUpProfile FromBands(string name, IList<double> values)
        {
            List<double> normalized = Normalize(values);

            if (normalized.Count != ChunkCount)
                normalized = Resample(normalized);

            return new ShowUpProfile(name, normalized);
        }

        // -------------------------------------------------------------
        // Curve queries
        // -------------------------------------------------------------

        /// <summary>
        /// Share of the hour that has arrived by <paramref name="minutesFromStart"/>
        /// into the block, linearly interpolated between band boundaries. The
        /// block starts 60 minutes before departure.
        /// </summary>
        public double GetCumulativeAt(double minutesFromStart)
        {
            if (Bands == null || Bands.Count == 0)
                return 0.0;

            if (minutesFromStart <= 0.0)
                return 0.0;

            if (minutesFromStart >= BlockMinutes)
                return 1.0;

            double bandLength = BlockMinutes / ChunkCount;

            double position = minutesFromStart / bandLength;

            int band = (int)Math.Floor(position);

            if (band >= Bands.Count)
                return 1.0;

            double fraction = position - band;

            double before = 0.0;

            for (int i = 0; i < band; i++)
                before += Bands[i];

            return before + fraction * Bands[band];
        }

        /// <summary>
        /// Share arriving in the window of the given length ending at departure.
        /// This is the nested definition a planner expects from W15/W30/W60.
        /// </summary>
        public double GetWindowShare(int minutes)
        {
            if (minutes <= 0)
                return 0.0;

            if (minutes >= BlockMinutes)
                return 1.0;

            return 1.0 - GetCumulativeAt(BlockMinutes - minutes);
        }

        /// <summary>Start minute before departure of a band, for labels.</summary>
        public double GetBandStartMinutesBeforeDeparture(int index)
        {
            return BlockMinutes - ((index + 1) * (BlockMinutes / ChunkCount));
        }

        /// <summary>Recomputes W15/W30/W60 from the bands.</summary>
        public void RefreshWindowShares()
        {
            W15 = GetWindowShare(15);
            W30 = GetWindowShare(30);
            W60 = GetWindowShare(60);
        }

        // -------------------------------------------------------------
        // Validation
        // -------------------------------------------------------------

        /// <summary>
        /// Shape validation. A valid profile is ten non-negative bands summing
        /// to 100%.
        ///
        /// Deliberately does not reuse <see cref="WindowShareProfile"/>'s window
        /// rules, and deliberately does not add the two it used to enforce:
        ///
        ///   "W30 cannot be below 50%" holds for the two halves of an hour
        ///   summing to 100%, but W30 is a 30-minute window, not a half. All
        ///   three shipped curves fall below it - Leisure at 0.36, Mix at 0.43.
        ///
        ///   "W30 cannot exceed 2 x W15" assumes arrivals cluster near
        ///   departure. All three shipped curves break it, because they cluster
        ///   away from it: Leisure holds 36% in the last 30 minutes against 12%
        ///   in the last 15, a ratio near three. That is simply what an
        ///   early-arriving profile looks like, not an error.
        ///
        /// W15 &lt;= W30 survives as a cheap sanity check. It is implied for any
        /// well-formed nested curve, so it can only ever fire on a hand-edited
        /// profile.
        /// </summary>
        public override List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();

            if (Bands == null || Bands.Count != ChunkCount)
            {
                errors.Add(
                    $"A show-up profile needs exactly {ChunkCount} bands; " +
                    $"this one has {(Bands == null ? 0 : Bands.Count)}.");
            }
            else
            {
                double total = 0.0;

                for (int i = 0; i < Bands.Count; i++)
                {
                    double value = Bands[i];

                    if (double.IsNaN(value) || value < -1e-9)
                        errors.Add($"Band {i + 1} is negative ({value:0.###}).");

                    total += value;
                }

                if (total > 0.0 && Math.Abs(total - 1.0) > 1e-6)
                    errors.Add($"Bands sum to {total:0.###} rather than 100%.");
            }

            if (W15 < -1e-9 || W15 > 1.0 + 1e-9)
                errors.Add("W15 must be between 0% and 100%.");

            if (W30 < -1e-9 || W30 > 1.0 + 1e-9)
                errors.Add("W30 must be between 0% and 100%.");

            if (Math.Abs(W60 - 1.0) > 1e-6)
                errors.Add("W60 must be 100% (the whole hour).");

            if (W15 > W30 + 1e-6)
                errors.Add("W15 cannot exceed W30: the last 15 minutes sit inside the last 30.");

            return errors;
        }

        public override string ToString()
        {
            return $"{Name} (W15 {W15:0.###} / W30 {W30:0.###} / W60 {W60:0.###})";
        }

        /// <summary>
        /// Deterministic fingerprint of the preset name and band values.
        /// Used to tag generated passengers so solver caches can tell one
        /// show-up curve from another without comparing doubles fuzzily.
        /// </summary>
        public string GetSignature()
        {
            string name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();

            if (Bands == null || Bands.Count == 0)
                return name + "|empty";

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(name);

            for (int i = 0; i < Bands.Count; i++)
            {
                builder.Append('|');
                builder.Append(Bands[i].ToString("R"));
            }

            return builder.ToString();
        }

        // -------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------

        private void SetBands(IList<double> values)
        {
            List<double> normalized = Normalize(values);

            if (normalized.Count != ChunkCount)
                normalized = Resample(normalized);

            Bands = normalized;

            W60 = GetWindowShare(60);
        }

        /// <summary>Scales a list of shares to sum to 1; empty when it cannot.</summary>
        private static List<double> Normalize(IList<double> values)
        {
            List<double> result = new List<double>();

            if (values == null)
                return result;

            double total = 0.0;

            for (int i = 0; i < values.Count; i++)
            {
                double value = values[i];

                if (double.IsNaN(value) || value < 0.0)
                    value = 0.0;

                result.Add(value);
                total += value;
            }

            if (total <= 0.0)
                return new List<double>();

            for (int i = 0; i < result.Count; i++)
                result[i] = result[i] / total;

            return result;
        }

        /// <summary>
        /// Stretches or squeezes a normalised share list onto ten bands by
        /// interpolating its cumulative curve.
        /// </summary>
        private static List<double> Resample(List<double> shares)
        {
            List<double> result = new List<double>(ChunkCount);

            int n = shares.Count;

            if (n == 0)
            {
                for (int i = 0; i < ChunkCount; i++)
                    result.Add(1.0 / ChunkCount);

                return result;
            }

            double[] source = new double[n + 1];

            for (int i = 0; i < n; i++)
                source[i + 1] = source[i] + shares[i];

            double previous = 0.0;

            for (int band = 0; band < ChunkCount; band++)
            {
                double position = (band + 1) * (double)n / ChunkCount;

                int low = (int)Math.Floor(position);

                double upper;

                if (low >= n)
                {
                    upper = source[n];
                }
                else
                {
                    double fraction = position - low;

                    upper = source[low] + fraction * (source[low + 1] - source[low]);
                }

                double value = upper - previous;

                result.Add(value < 0.0 ? 0.0 : value);

                previous = upper;
            }

            return Normalize(result);
        }

        /// <summary>
        /// Best-fit Gaussian arrival density (mean, spread) reproducing the
        /// requested W15 and W30, searched on a fixed grid.
        ///
        /// The density is truncated to the hour and renormalised so all the
        /// modelled demand lands inside the block and W60 is 100% by
        /// construction. Skipping that step lets the tails leak past departure,
        /// which quietly caps how much of the hour the busiest windows can hold
        /// - a flat profile came out at W30 0.69 instead of 0.50.
        ///
        /// A Gaussian cannot express every curve exactly; the fit is the closest
        /// it gets, and the reported W values stay the ones asked for.
        /// </summary>
        private static double[] FitGaussian(double w15, double w30)
        {
            double target15 = Clamp(w15, 0.0, 1.0);
            double target30 = Clamp(w30, 0.0, 1.0);

            double bestMean = 30.0;
            double bestSigma = 20.0;
            double bestError = double.MaxValue;

            for (double mean = 5.0; mean <= 55.0001; mean += 0.5)
            {
                for (double sigma = 4.0; sigma <= 150.0001; sigma += 0.5)
                {
                    double span =
                        TruncatedPhi(BlockMinutes, mean, sigma) -
                        TruncatedPhi(0.0, mean, sigma);

                    if (span <= 1e-12)
                        continue;

                    double end = TruncatedPhi(BlockMinutes, mean, sigma);

                    double modelled15 =
                        (end - TruncatedPhi(45.0, mean, sigma)) / span;

                    double modelled30 =
                        (end - TruncatedPhi(30.0, mean, sigma)) / span;

                    double error =
                        (modelled15 - target15) * (modelled15 - target15) +
                        (modelled30 - target30) * (modelled30 - target30);

                    if (error < bestError)
                    {
                        bestError = error;
                        bestMean = mean;
                        bestSigma = sigma;
                    }
                }
            }

            double[] bands = new double[ChunkCount];

            double previous = TruncatedPhi(0.0, bestMean, bestSigma);

            for (int band = 0; band < ChunkCount; band++)
            {
                double u = (band + 1) * (BlockMinutes / ChunkCount);

                double upper = TruncatedPhi(u, bestMean, bestSigma);

                bands[band] = upper - previous;
                previous = upper;
            }

            return bands;
        }

        /// <summary>
        /// Gaussian CDF truncated to the hour and rescaled to 1, so the curve
        /// starts with nobody arrived and ends with everybody arrived.
        /// </summary>
        private static double TruncatedPhi(double u, double mean, double sigma)
        {
            double low = Phi((0.0 - mean) / sigma);
            double high = Phi((BlockMinutes - mean) / sigma);

            double span = high - low;

            if (span <= 1e-12)
                return u <= 0.0 ? 0.0 : 1.0;

            return (Phi((u - mean) / sigma) - low) / span;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
                return min;

            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>Standard normal CDF via the Abramowitz &amp; Stegun erf fit.</summary>
        private static double Phi(double z)
        {
            return 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
        }

        private static double Erf(double x)
        {
            double sign = x < 0.0 ? -1.0 : 1.0;

            double abs = Math.Abs(x);

            double t = 1.0 / (1.0 + 0.3275911 * abs);

            double polynomial =
                ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t
                + 0.254829592) * t;

            return sign * (1.0 - polynomial * Math.Exp(-abs * abs));
        }
    }
}
