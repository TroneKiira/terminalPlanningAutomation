using System;
using System.Collections.Generic;
using System.Globalization;
using AirportPlanning.Core.Inputs;
using Grasshopper.Kernel;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds the editable check-in operating rules used by the schedule-driven
    /// passenger generator. Defaults come from MODELS_REPORT.tex:
    /// domestic 120/60 and international 180/60 minutes before STD.
    /// </summary>
    public class CreateCheckInWindowRules : GH_Component
    {
        public CreateCheckInWindowRules()
          : base("Check-In Window Rules", "CIW",
            "Defines when check-in opens and when the last passenger may be admitted, " +
            "with sector defaults and optional airline/flight overrides.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
                "Domestic Opens", "DO",
                "Minutes before domestic STD that check-in opens. Default 120.",
                GH_ParamAccess.item, 120.0);

            pManager.AddNumberParameter(
                "Domestic Last Admission", "DC",
                "Minutes before domestic STD that check-in stops admitting new passengers. Default 60.",
                GH_ParamAccess.item, 60.0);

            pManager.AddNumberParameter(
                "International Opens", "IO",
                "Minutes before international STD that check-in opens. Default 180.",
                GH_ParamAccess.item, 180.0);

            pManager.AddNumberParameter(
                "International Last Admission", "IC",
                "Minutes before international STD that check-in stops admitting new passengers. Default 60.",
                GH_ParamAccess.item, 60.0);

            pManager.AddTextParameter(
                "Airline Overrides", "O",
                "Optional lines: Airline,Opens,LastAdmission or Airline,FlightNumber,Opens,LastAdmission. " +
                "Times are minutes before STD.",
                GH_ParamAccess.list);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Check-In Windows", "CIW",
                "Check-in window rules for the schedule-driven passenger generator.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Rules", "R",
                "Resolved defaults and overrides, one per line.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Issues", "I",
                "Invalid override lines or rule values.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double domesticOpen = 120.0;
            double domesticClose = 60.0;
            double internationalOpen = 180.0;
            double internationalClose = 60.0;
            var overrideLines = new List<string>();

            DA.GetData(0, ref domesticOpen);
            DA.GetData(1, ref domesticClose);
            DA.GetData(2, ref internationalOpen);
            DA.GetData(3, ref internationalClose);
            DA.GetDataList(4, overrideLines);

            CheckInWindowRules rules = new CheckInWindowRules
            {
                DomesticOpenMinutesBeforeDeparture = domesticOpen,
                DomesticLastAdmissionMinutesBeforeDeparture = domesticClose,
                InternationalOpenMinutesBeforeDeparture = internationalOpen,
                InternationalLastAdmissionMinutesBeforeDeparture = internationalClose
            };

            var issues = new List<string>();

            if (overrideLines != null)
            {
                for (int i = 0; i < overrideLines.Count; i++)
                {
                    string line = (overrideLines[i] ?? string.Empty).Trim();

                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    CheckInWindowOverride item = ParseOverride(line, i + 1, issues);

                    if (item != null)
                        rules.Overrides.Add(item);
                }
            }

            issues.AddRange(rules.GetValidationErrors());

            foreach (string issue in issues)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, issue);

            var summary = new List<string>
            {
                $"Domestic: opens STD-{domesticOpen:0.#}, last admission STD-{domesticClose:0.#}",
                $"International: opens STD-{internationalOpen:0.#}, last admission STD-{internationalClose:0.#}"
            };

            foreach (CheckInWindowOverride item in rules.Overrides)
            {
                string flight = string.IsNullOrWhiteSpace(item.FlightNumber)
                    ? "*"
                    : item.FlightNumber;

                summary.Add(
                    $"{item.Airline} {flight}: opens STD-{item.OpenMinutesBeforeDeparture:0.#}, " +
                    $"last admission STD-{item.LastAdmissionMinutesBeforeDeparture:0.#}");
            }

            DA.SetData(0, rules);
            DA.SetDataList(1, summary);
            DA.SetDataList(2, issues);
        }

        private static CheckInWindowOverride ParseOverride(
            string line,
            int lineNumber,
            List<string> issues)
        {
            string[] parts = line.Split(',');

            if (parts.Length != 3 && parts.Length != 4)
            {
                issues.Add(
                    $"Override line {lineNumber} must be Airline,Opens,LastAdmission " +
                    $"or Airline,FlightNumber,Opens,LastAdmission.");
                return null;
            }

            string airline = parts[0].Trim();

            if (airline.Length == 0)
            {
                issues.Add($"Override line {lineNumber} has no airline.");
                return null;
            }

            string flight = parts.Length == 4 ? parts[1].Trim() : string.Empty;
            string openText = parts[parts.Length - 2].Trim();
            string closeText = parts[parts.Length - 1].Trim();

            if (!TryParseMinutes(openText, out double open) ||
                !TryParseMinutes(closeText, out double close))
            {
                issues.Add(
                    $"Override line {lineNumber} has an invalid number. " +
                    "Use non-negative minutes before STD.");
                return null;
            }

            if (open < close)
            {
                issues.Add(
                    $"Override line {lineNumber}: check-in opens after its last admission.");
                return null;
            }

            return new CheckInWindowOverride
            {
                Airline = airline,
                FlightNumber = flight,
                OpenMinutesBeforeDeparture = open,
                LastAdmissionMinutesBeforeDeparture = close
            };
        }

        private static bool TryParseMinutes(string text, out double value)
        {
            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) &&
                !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value >= 0.0;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("8B7C6D5E-4F3A-4928-8176-5E4D3C2B1A90");
    }
}
