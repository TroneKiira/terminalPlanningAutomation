using System;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Tests
{
    public static partial class Program
    {
        private static void RunDeplaningProfileCategories()
        {
            Console.WriteLine("== Deplaning profile categories ==");

            DeplaningProfileMode mode;

            Check(DeplaningProfile.TryParseMode("Auto", out mode) &&
                  mode == DeplaningProfileMode.Auto,
                "Mode parser accepts Auto");
            Check(DeplaningProfile.TryParseMode("small", out mode) &&
                  mode == DeplaningProfileMode.Small,
                "Mode parser accepts small case-insensitively");
            Check(DeplaningProfile.TryParseMode("Custom", out mode) &&
                  mode == DeplaningProfileMode.Custom,
                "Mode parser accepts Custom");
            Check(DeplaningProfile.TryParseMode("1", out mode) &&
                  mode == DeplaningProfileMode.Small,
                "Mode parser maps legacy preset 1 to Small");
            Check(!DeplaningProfile.TryParseMode("Huge", out mode),
                "Mode parser rejects an unknown category");

            Check(DeplaningProfile.BandForMppa(10.0) == AirportSizeBand.Small,
                "Exactly 10 mppa is still Small");
            Check(DeplaningProfile.BandForMppa(10.01) == AirportSizeBand.Medium,
                "Just above 10 mppa is Medium");
            Check(DeplaningProfile.BandForMppa(25.0) == AirportSizeBand.Medium,
                "Exactly 25 mppa is still Medium");
            Check(DeplaningProfile.BandForMppa(25.01) == AirportSizeBand.Large,
                "Just above 25 mppa is Large");

            DeplaningProfile small = DeplaningProfile.FromMode(
                DeplaningProfileMode.Auto, 8.0, "ignored", 0.10, 0.20);
            Check(Math.Abs(small.W15 - 0.45) < 1e-9 &&
                  Math.Abs(small.W30 - 0.70) < 1e-9 &&
                  Math.Abs(small.W60 - 1.00) < 1e-9,
                "Auto at 8 mppa resolves the Small 45/70/100 profile");
            Check(DeplaningProfile.CategoryForMode(DeplaningProfileMode.Auto, 8.0) == "Small",
                "Auto reports Small as the selected category");

            DeplaningProfile medium = DeplaningProfile.FromMode(
                DeplaningProfileMode.Auto, 20.0, "ignored", 0.10, 0.20);
            Check(Math.Abs(medium.W15 - 0.40) < 1e-9 &&
                  Math.Abs(medium.W30 - 0.60) < 1e-9,
                "Auto at 20 mppa resolves the Medium 40/60/100 profile");

            DeplaningProfile large = DeplaningProfile.FromMode(
                DeplaningProfileMode.Auto, 30.0, "ignored", 0.10, 0.20);
            Check(Math.Abs(large.W15 - 0.35) < 1e-9 &&
                  Math.Abs(large.W30 - 0.50) < 1e-9,
                "Auto at 30 mppa resolves the Large 35/50/100 profile");

            DeplaningProfile forcedLarge = DeplaningProfile.FromMode(
                DeplaningProfileMode.Large, 5.0, "ignored", 0.10, 0.20);
            Check(Math.Abs(forcedLarge.W15 - 0.35) < 1e-9,
                "An explicit Large mode ignores the annual volume");

            DeplaningProfile custom = DeplaningProfile.FromMode(
                DeplaningProfileMode.Custom, 30.0, "My curve", 0.42, 0.66);
            Check(custom.Name == "My curve" &&
                  Math.Abs(custom.W15 - 0.42) < 1e-9 &&
                  Math.Abs(custom.W30 - 0.66) < 1e-9 &&
                  Math.Abs(custom.W60 - 1.00) < 1e-9,
                "Custom mode uses W15/W30 and fixes W60 at 100%");
            Check(custom.GetValidationErrors().Count == 0,
                "A valid Custom profile passes validation");
            Check(DeplaningProfile.CategoryForMode(
                      DeplaningProfileMode.Custom, 30.0) == "Custom",
                "Custom reports Custom as the selected category");

            Check(DeplaningProfile.FromMode(
                      DeplaningProfileMode.Custom, 30.0, "", 0.60, 0.50)
                      .GetValidationErrors().Count > 0,
                "Custom W15 > W30 is rejected");
            Check(DeplaningProfile.FromMode(
                      DeplaningProfileMode.Custom, 30.0, "", 0.20, 0.45)
                      .GetValidationErrors().Count > 0,
                "Custom W30 below 50% is rejected");
            Check(DeplaningProfile.FromMode(
                      DeplaningProfileMode.Custom, 30.0, "", 0.10, 0.30)
                      .GetValidationErrors().Count > 0,
                "Custom W30 above 2 x W15 is rejected");

            DeplaningProfile phpProfile = DeplaningProfile.FromMode(
                DeplaningProfileMode.Medium, 20.0, "ignored", 0.0, 0.0);
            Check(Math.Abs(phpProfile.Peak15Passengers(1000.0) - 400.0) < 1e-9,
                "PHP x W15 gives the peak 15-minute passenger count");
            Check(Math.Abs(phpProfile.Peak30Passengers(1000.0) - 600.0) < 1e-9,
                "PHP x W30 gives the peak 30-minute passenger count");
            Check(Math.Abs(phpProfile.Peak60Passengers(1000.0) - 1000.0) < 1e-9,
                "PHP x W60 returns the peak-hour passenger count");
        }
    }
}
