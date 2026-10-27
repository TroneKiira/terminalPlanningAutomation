using AirportPlanning.Core.Schedules;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    /// <summary>
    /// Imports a flight schedule from an Excel workbook and normalizes it into
    /// arrival and departure legs.
    ///
    /// One row per aircraft turnaround: arrival columns on the left, departure
    /// columns on the right. Each row produces two legs with a shared link ID.
    /// A row with only one side produces one leg.
    ///
    /// This component only parses and validates the schedule. It does not
    /// generate passengers or run the queue simulator; those are separate
    /// stages.
    /// </summary>
    public class FlightScheduleImportComponent : GH_Component
    {
        public FlightScheduleImportComponent()
          : base("Flight Schedule Import", "FSI",
            "Imports a flight schedule from an Excel workbook into normalized arrival and departure legs.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Excel Path", "F",
                "Full path to the flight schedule workbook (.xlsx).",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Sheet Name", "S",
                "Worksheet name. Leave empty to use the first worksheet containing a schedule header.",
                GH_ParamAccess.item, "");
            pManager[1].Optional = true;

            pManager.AddTextParameter(
                "Base Date", "D",
                "Operating date for time-only schedules, for example 2026-10-19. Leave empty when the workbook carries dates.",
                GH_ParamAccess.item, "");
            pManager[2].Optional = true;

            pManager.AddBooleanParameter(
                "Linked Rows", "L",
                "On when one row contains both the arrival and departure legs of a turnaround. " +
                "A shared Rotation ID also links split rows.",
                GH_ParamAccess.item, true);

            pManager.AddTextParameter(
                "Column Overrides", "O",
                "Optional lines in the form Field=ColumnOrHeader, for example ArrivalPax=L. " +
                "Common fields: ArrivalPax, DeparturePax, ArrivalTime, DepartureTime, " +
                "ArrivalTerminal, DepartureTerminal, ArrivalAirline, DepartureAirline. " +
                "Leave empty to use header detection.",
                GH_ParamAccess.list);
            pManager[4].Optional = true;

            pManager.AddTextParameter(
                "Fallback Terminal", "FT",
                "Terminal applied to passenger legs whose terminal is missing or cannot be resolved " +
                "from the workbook. Default T1. Freight legs use Cargo. Leave empty to keep unresolved " +
                "terminals blank and report each one individually.",
                GH_ParamAccess.item, "T1");
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Flight Schedule", "FS",
                "Normalized flight schedule.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Rotations", "R",
                "One record per aircraft turnaround, pairing arrival and departure.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Arrival Legs", "A",
                "Normalized arrival legs.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Departure Legs", "D",
                "Normalized departure legs.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Schedule Summary", "SM",
                "Movement, passenger and import-quality totals.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Import Issues", "I",
                "Row-level and workbook-level warnings from parsing and validation.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Column Map", "CM",
                "Resolved field-to-Excel-column bindings actually used by the parser, " +
                "one per line (for example ArrivalPax = L).",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = null;
            string sheetName = null;
            string baseDate = null;
            bool linkedRows = true;
            string fallbackTerminal = "T1";
            var overrideLines = new List<string>();

            if (!DA.GetData(0, ref filePath) || string.IsNullOrWhiteSpace(filePath))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Excel path received. Provide the full path to a flight schedule workbook.");
                return;
            }

            filePath = filePath.Trim().Trim('"');

            DA.GetData(1, ref sheetName);
            DA.GetData(2, ref baseDate);
            DA.GetData(3, ref linkedRows);
            DA.GetDataList(4, overrideLines);
            DA.GetData(5, ref fallbackTerminal);

            if (overrideLines == null)
                overrideLines = new List<string>();

            if (!File.Exists(filePath))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Workbook not found: '{filePath}'.");
                return;
            }

            byte[] fileBytes;

            try
            {
                fileBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Could not read '{filePath}': {ex.Message}");
                return;
            }

            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var overrideIssues = new List<LegIssue>();

            for (int i = 0; i < overrideLines.Count; i++)
            {
                var line = (overrideLines[i] ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separator = line.IndexOf('=');
                if (separator < 0)
                    separator = line.IndexOf(':');

                if (separator <= 0 || separator >= line.Length - 1)
                {
                    overrideIssues.Add(new LegIssue
                    {
                        Field = "Column Overrides",
                        Message = $"Line {i + 1} is not in the form Field=ColumnOrHeader: '{line}'.",
                        Severity = "Warning"
                    });
                    continue;
                }

                var field = line.Substring(0, separator).Trim();
                var column = line.Substring(separator + 1).Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(column))
                {
                    overrideIssues.Add(new LegIssue
                    {
                        Field = "Column Overrides",
                        Message = $"Line {i + 1} has an empty field or column: '{line}'.",
                        Severity = "Warning"
                    });
                    continue;
                }

                overrides[field] = column;
            }

            var options = new ScheduleImportOptions
            {
                SourceFile = filePath,
                ScheduleName = Path.GetFileNameWithoutExtension(filePath),
                SheetName = string.IsNullOrWhiteSpace(sheetName) ? string.Empty : sheetName.Trim(),
                BaseDate = string.IsNullOrWhiteSpace(baseDate) ? string.Empty : baseDate.Trim(),
                FallbackTerminal = string.IsNullOrWhiteSpace(fallbackTerminal) ? string.Empty : fallbackTerminal.Trim(),
                LinkedRows = linkedRows,
                ColumnOverrides = overrides
            };

            FlightScheduleResult result;

            try
            {
                result = FlightScheduleParser.Parse(fileBytes, options);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Schedule could not be parsed: {ex.Message}");
                return;
            }

            if (overrideIssues.Count > 0)
                result.GlobalIssues.AddRange(overrideIssues);

            AddImportMessages(result);

            DA.SetData(0, result);
            DA.SetDataList(1, result.Rotations());
            DA.SetDataList(2, result.Legs.Where(l => l.Direction == LegDirection.Arrival).ToList());
            DA.SetDataList(3, result.Legs.Where(l => l.Direction == LegDirection.Departure).ToList());
            DA.SetData(4, result.CreateSummary());

            var issues = result.GlobalIssues
                .Select(i => $"{i.Severity} · Workbook · {i.Field}: {i.Message}")
                .Concat(result.Legs.SelectMany(l => l.Issues.Select(i =>
                    $"{i.Severity} · Row {l.SourceRow} · {l.Direction} · {i.Field}: {i.Message}")))
                .ToList();

            DA.SetDataList(5, issues);
            DA.SetDataList(6, result.ColumnMapLines());
        }

        private void AddImportMessages(FlightScheduleResult result)
        {
            int warningCount = result.WarningCount;
            int errorCount = result.ErrorCount;

            if (errorCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"{errorCount} import error(s). See the Import Issues output.");
            }

            if (warningCount > 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"{warningCount} import warning(s). See the Import Issues output.");
            }

            var examples = result.GlobalIssues
                .Select(i => $"Workbook · {i.Field}: {i.Message}")
                .Concat(result.Legs.SelectMany(l => l.Issues.Select(i =>
                    $"Row {l.SourceRow} · {l.Direction} · {i.Field}: {i.Message}")))
                .Take(6)
                .ToList();

            foreach (var example in examples)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, example);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("E1F2A3B4-C5D6-4E7F-8A9B-0C1D2E3F4A5B");
    }
}
