using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AirportPlanning.Core.Diagnostics
{
    /// <summary>
    /// Turns a <see cref="DiagnosticsReport"/> into text tables and a
    /// long-format CSV. Text is the primary output because Grasshopper panels
    /// are where the planner reads it; CSV carries one row per item so the
    /// tables can be filtered in Excel.
    /// </summary>
    public static class DiagnosticsFormatter
    {
        private const string CsvHeader =
            "Section,Item,Code,Status,Severity,Expected,Actual,Variance,Unit,PassengerIds,Message";

        // -------------------------------------------------------------
        // Summary and issues
        // -------------------------------------------------------------

        public static List<string> CreateSummaryLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
            {
                lines.Add("MODEL DIAGNOSTICS - NOT WIRED");
                lines.Add("No diagnostics report was produced.");
                return lines;
            }

            lines.Add("MODEL DIAGNOSTICS - " + report.OverallStatus.ToDisplayLabel());
            lines.Add(string.Empty);
            lines.Add("Findings: " + report.ErrorCount + " error, " +
                      report.WarningCount + " warning, " + report.InfoCount + " info");
            lines.Add("Population: " + report.PopulationCount + " records from " +
                      report.PopulationSource);
            lines.Add("Warm-up records: " + report.WarmUpCount +
                      " | design-step records: " + report.DesignStepCount);
            lines.Add("Transfer legs: " + report.OutboundTransferLegs + " outbound, " +
                      report.InboundTransferLegs + " inbound");
            lines.Add("Service results: " + report.ServiceResultCount +
                      " | gate results: " + report.GateRows.Count +
                      " rows | sizing results: " + report.SizingRows.Count);

            if (report.Services.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Services at a glance:");
                foreach (DiagnosticsServiceRow row in report.Services)
                {
                    lines.Add("  " + Pad(row.Status.ToDisplayLabel(), 9) + " " +
                              Pad(row.Label, 24) +
                              " expected " + row.Expected +
                              ", processed " + row.Processed +
                              " (warm-up " + row.WarmUpProcessed + ")");

                    if (row.BagDrop != null && row.BagDrop.Active)
                    {
                        lines.Add("      bag drop: expected " + row.BagDrop.Expected +
                                  ", recorded " + row.BagDrop.Recorded +
                                  ", missing " + row.BagDrop.Missing);
                    }
                }
            }

            if (report.Transfers.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Transfers at a glance:");
                foreach (DiagnosticsTransferRow row in report.Transfers)
                {
                    lines.Add("  " + Pad(row.Status.ToDisplayLabel(), 9) + " " +
                              Pad(row.Scenario, 32) +
                              " effective " + row.Effective +
                              ", tagged " + row.Tagged +
                              ", arrival legs " + row.ArrivalLegs);
                }
            }

            if (report.CoverageLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Coverage:");
                foreach (string line in report.CoverageLines)
                    lines.Add("  - " + line);
            }

            List<string> next = CreateNextStepLines(report);
            if (next.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Start here:");
                foreach (string line in next)
                    lines.Add("  - " + line);
            }

            return lines;
        }

        private static List<string> CreateNextStepLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            foreach (DiagnosticFinding finding in report.Findings
                .Where(f => f.Severity == DiagnosticSeverity.Error)
                .Take(5))
            {
                lines.Add(finding.Code + " " + finding.Title +
                          (string.IsNullOrWhiteSpace(finding.ServiceLabel)
                              ? string.Empty
                              : " (" + finding.ServiceLabel + ")"));
            }

            if (lines.Count == 0)
            {
                DiagnosticFinding warning = report.Findings
                    .FirstOrDefault(f => f.Severity == DiagnosticSeverity.Warning);

                if (warning != null)
                {
                    lines.Add(warning.Code + " " + warning.Title);
                }
            }

            foreach (DiagnosticsServiceRow row in report.Services
                .Where(r => r.Status == DiagnosticStatus.Fail)
                .Take(3))
            {
                string text = row.Label + ": " + row.MissingExpected +
                              " missing and " + row.ExtraProcessed + " unexpected";

                if (row.MissingPassengerIds.Count > 0)
                    text += "; first IDs " + string.Join(", ", row.MissingPassengerIds.Take(5));

                string channelNote = row.Notes?.FirstOrDefault(n =>
                    n != null && n.Contains("expected record(s) were not processed"));

                if (!string.IsNullOrWhiteSpace(channelNote))
                    text += "; " + channelNote;

                if (row.BagDrop != null && row.BagDrop.Active)
                {
                    text += "; bag drop " + row.BagDrop.Missing + " missing";
                }

                lines.Add(text);
            }

            if (lines.Count == 0 && report.PopulationCount > 0)
            {
                lines.Add("No blocking issue was found. Open the service and transfer " +
                          "tables to confirm the counts you expect.");
            }

            return lines;
        }

        public static List<string> CreateIssueLines(DiagnosticsReport report, bool includeInfo)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            IEnumerable<DiagnosticFinding> findings = report.Findings
                .Where(f => includeInfo || f.Severity != DiagnosticSeverity.Info)
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.Code);

            bool any = false;

            foreach (DiagnosticFinding finding in findings)
            {
                if (!any)
                {
                    lines.Add("ISSUES");
                    any = true;
                }

                lines.Add(finding.Describe());
                lines.Add(string.Empty);
            }

            if (!any)
            {
                lines.Add("ISSUES");
                lines.Add("  No " + (includeInfo ? string.Empty : "warning or error ") +
                          "findings.");
            }

            return lines;
        }

        // -------------------------------------------------------------
        // Tables
        // -------------------------------------------------------------

        public static List<string> CreateServiceTableLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("SERVICES (passenger records, warm-up included in Processed)");

            if (report.Services.Count == 0)
            {
                lines.Add("  No service results were wired.");
                return lines;
            }

            List<string[]> rows = report.Services
                .Select(row => new[]
                {
                    row.Label,
                    row.KindLabel,
                    row.Incoming.ToString(CultureInfo.InvariantCulture),
                    row.Applicable.ToString(CultureInfo.InvariantCulture),
                    row.Expected.ToString(CultureInfo.InvariantCulture),
                    row.Processed.ToString(CultureInfo.InvariantCulture),
                    row.WarmUpProcessed.ToString(CultureInfo.InvariantCulture),
                    row.Bypassed.ToString(CultureInfo.InvariantCulture),
                    row.MissingExpected.ToString(CultureInfo.InvariantCulture),
                    row.ExtraProcessed.ToString(CultureInfo.InvariantCulture),
                    row.MissingHistory.ToString(CultureInfo.InvariantCulture),
                    row.PoolPassengers.ToString(CultureInfo.InvariantCulture),
                    Format(row.Wait95),
                    row.Status.ToDisplayLabel()
                })
                .ToList();

            lines.AddRange(Table(
                new[]
                {
                    "Service", "Kind", "In", "Appl", "Exp", "Proc", "Warm", "Byp",
                    "Miss", "Extra", "NoHist", "Pool", "W95", "Status"
                },
                rows));

            foreach (DiagnosticsServiceRow row in report.Services
                .Where(r => r.Notes != null && r.Notes.Count > 0))
            {
                foreach (string note in row.Notes.Where(n => !string.IsNullOrWhiteSpace(n)))
                    lines.Add("  " + row.Label + ": " + note);
            }

            foreach (DiagnosticsServiceRow row in report.Services
                .Where(r => r.Kind == DiagnosticsServiceKind.CheckIn ||
                            r.Kind == DiagnosticsServiceKind.BagDrop))
            {
                string breakdown = CreateCheckInBreakdownLines(row);
                if (!string.IsNullOrWhiteSpace(breakdown))
                {
                    lines.Add(string.Empty);
                    lines.Add(breakdown);
                }
            }

            return lines;
        }

        /// <summary>
        /// Compact, aggregate view of a physical departure stage row. Detail
        /// arrays keep the passenger IDs; this text only shows counts so a
        /// panel stays readable.
        /// </summary>
        private static string CreateCheckInBreakdownLines(DiagnosticsServiceRow row)
        {
            StringBuilder builder = new StringBuilder();

            string header = row.Kind == DiagnosticsServiceKind.BagDrop
                ? "BAG DROP BREAKDOWN"
                : "CHECK-IN BREAKDOWN";

            builder.AppendLine(header + " - " + row.Label);

            DiagnosticsBagDropBreakdown bagDrop = row.BagDrop;

            string stageLabel = row.Kind == DiagnosticsServiceKind.BagDrop
                ? "Bag Drop"
                : "Check-In";

            if (bagDrop != null && bagDrop.Active)
            {
                builder.AppendLine(
                    "  Bag drop: expected " + bagDrop.Expected +
                    ", recorded " + bagDrop.Recorded +
                    ", missing " + bagDrop.Missing +
                    ", late " + bagDrop.LateBypassed);

                AppendBreakdown(builder, "Bag drop by primary channel", bagDrop.Channels, false);
                AppendBreakdown(builder, "Bag drop missing by primary channel",
                    bagDrop.MissingChannels, true);
                AppendBreakdown(builder, "Bag drop missing by passenger mix",
                    bagDrop.MissingMix, true);
                AppendBreakdown(builder, "Bag drop missing by departure type",
                    bagDrop.MissingDepartureTypes, true);
                AppendBreakdown(builder, "Bag drop missing by journey",
                    bagDrop.MissingJourneys, true);
            }

            AppendBreakdown(builder, stageLabel + " expected by channel", row.Channels, false);
            AppendBreakdown(builder, stageLabel + " missing by channel", row.Channels, true);
            AppendBreakdown(builder, stageLabel + " expected by passenger mix",
                row.PassengerMix, false);
            AppendBreakdown(builder, stageLabel + " missing by passenger mix",
                row.PassengerMix, true);
            AppendBreakdown(builder, stageLabel + " expected by departure type",
                row.DepartureTypes, false);
            AppendBreakdown(builder, stageLabel + " expected by journey",
                row.Journeys, false);

            List<DiagnosticsBreakdownRow> exceptions = (row.Exceptions ??
                new List<DiagnosticsBreakdownRow>())
                .Where(r => r != null && r.Count > 0)
                .ToList();

            if (exceptions.Count > 0)
            {
                builder.AppendLine("  Excluded by rule:");
                foreach (DiagnosticsBreakdownRow exception in exceptions)
                    builder.AppendLine("    " + exception.Label + ": " + exception.Count);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendBreakdown(
            StringBuilder builder,
            string title,
            List<DiagnosticsBreakdownRow> rows,
            bool missingOnly)
        {
            if (rows == null || rows.Count == 0)
                return;

            List<DiagnosticsBreakdownRow> selected = rows
                .Where(row => row != null && (!missingOnly || row.Missing > 0))
                .ToList();

            if (selected.Count == 0)
                return;

            builder.AppendLine("  " + title + ":");
            foreach (DiagnosticsBreakdownRow row in selected)
            {
                builder.Append("    " + Pad(row.Label, 34));

                if (row.Count > 0 && row.Expected == row.Count && row.Processed == 0)
                {
                    builder.AppendLine(row.Count.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.AppendLine(
                        "expected " + row.Expected +
                        ", processed " + row.Processed +
                        ", missing " + row.Missing);
                }
            }
        }

        public static List<string> CreateTransferTableLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("TRANSFER DEMAND");

            if (report.Transfers.Count == 0)
            {
                lines.Add("  No transfer demand result or transfer records were wired.");
                return lines;
            }

            List<string[]> rows = report.Transfers
                .Select(row => new[]
                {
                    row.Scenario,
                    row.Requested.ToString(CultureInfo.InvariantCulture),
                    row.FeasibleMaximum.ToString(CultureInfo.InvariantCulture),
                    row.Effective.ToString(CultureInfo.InvariantCulture),
                    row.Tagged.ToString(CultureInfo.InvariantCulture),
                    row.TaggedPopulation.ToString(CultureInfo.InvariantCulture),
                    row.DepartureLegs.ToString(CultureInfo.InvariantCulture),
                    row.ArrivalLegs.ToString(CultureInfo.InvariantCulture),
                    row.Shortfall.ToString(CultureInfo.InvariantCulture),
                    row.Excess.ToString(CultureInfo.InvariantCulture),
                    row.Status.ToDisplayLabel()
                })
                .ToList();

            lines.AddRange(Table(
                new[]
                {
                    "Scenario", "Requested", "Feasible", "Effective", "Tagged",
                    "PopTag", "DepLegs", "ArrLegs", "Short", "Excess", "Status"
                },
                rows));

            lines.Add(string.Empty);
            lines.Add("  Requested = share applied to the modelled market. Feasible = schedule " +
                      "capacity limit.");
            lines.Add("  Effective = demand after feasibility. Tagged = outbound records tagged " +
                      "by the generator.");
            lines.Add("  PopTag = outbound records found in the wired population. ArrLegs are " +
                      "separate arrival records.");

            foreach (DiagnosticsTransferRow row in report.Transfers
                .Where(r => r.Notes != null && r.Notes.Count > 0))
            {
                foreach (string note in row.Notes.Where(n => !string.IsNullOrWhiteSpace(n)))
                    lines.Add("  " + row.Scenario + ": " + note);
            }

            return lines;
        }

        public static List<string> CreateTransferServiceTableLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("TRANSFER SERVICES (rules versus facility history)");

            if (report.TransferServices.Count == 0)
            {
                lines.Add("  No transfer records were wired.");
                return lines;
            }

            List<string[]> rows = report.TransferServices
                .Select(row => new[]
                {
                    row.Service,
                    row.Applicable.ToString(CultureInfo.InvariantCulture),
                    row.Required.ToString(CultureInfo.InvariantCulture),
                    row.Bypassed.ToString(CultureInfo.InvariantCulture),
                    row.Unspecified.ToString(CultureInfo.InvariantCulture),
                    row.Processed.ToString(CultureInfo.InvariantCulture),
                    row.MissingRequired.ToString(CultureInfo.InvariantCulture),
                    row.ServedBypassed.ToString(CultureInfo.InvariantCulture),
                    row.ResultWired ? "yes" : "no",
                    row.Status.ToDisplayLabel()
                })
                .ToList();

            lines.AddRange(Table(
                new[]
                {
                    "Service", "Appl", "Required", "Bypassed", "NoRules",
                    "Processed", "Missing", "ServedByp", "Wired", "Status"
                },
                rows));

            lines.Add(string.Empty);
            lines.Add("  Processed counts transfer records carrying this facility's history. " +
                      "Bag Drop history is written by the standalone Bag Drop stage.");
            lines.Add("  NoRules records keep normal passenger behaviour.");

            foreach (DiagnosticsTransferServiceRow row in report.TransferServices
                .Where(r => r.Notes != null && r.Notes.Count > 0))
            {
                foreach (string note in row.Notes.Where(n => !string.IsNullOrWhiteSpace(n)))
                    lines.Add("  " + row.Service + ": " + note);
            }

            return lines;
        }

        public static List<string> CreatePeakTableLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("TRANSFER PEAKS (busiest rolling 60-minute window)");

            if (report.Peaks.Count == 0)
            {
                lines.Add("  No transfer records or peak windows were available.");
                return lines;
            }

            List<string[]> rows = report.Peaks
                .Select(row => new[]
                {
                    row.Flow,
                    row.Service,
                    row.Window,
                    row.Peak.ToString(CultureInfo.InvariantCulture),
                    row.Required.ToString(CultureInfo.InvariantCulture),
                    row.Bypassed.ToString(CultureInfo.InvariantCulture),
                    row.Unspecified.ToString(CultureInfo.InvariantCulture),
                    row.Processed.ToString(CultureInfo.InvariantCulture),
                    row.MissingRequired.ToString(CultureInfo.InvariantCulture),
                    row.Status.ToDisplayLabel()
                })
                .ToList();

            lines.AddRange(Table(
                new[]
                {
                    "Flow", "Service", "Window", "Peak", "Req", "Byp",
                    "NoRules", "Proc", "Missing", "Status"
                },
                rows));

            lines.Add(string.Empty);
            lines.Add("  Peak = required + bypassed + unspecified records in the window. " +
                      "Arrival services use the");
            lines.Add("  arrival windows; departure services use the departure windows.");

            return lines;
        }

        public static List<string> CreatePassengerIssueLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("PASSENGER ISSUES (use the ID to find the record)");

            if (report.PassengerIssues.Count == 0)
            {
                lines.Add("  No passenger-level issue was found.");
                return lines;
            }

            foreach (IGrouping<string, DiagnosticsPassengerIssue> group in report.PassengerIssues
                .GroupBy(p => p.Code)
                .OrderBy(g => g.Key))
            {
                lines.Add(group.Key + " (" + group.Count() + ")");

                foreach (DiagnosticsPassengerIssue issue in group.Take(200))
                    lines.Add("  " + issue.Describe());

                if (group.Count() > 200)
                {
                    lines.Add("  ... " + (group.Count() - 200) +
                              " more. Use the CSV output for the full list.");
                }
            }

            return lines;
        }

        public static List<string> CreateDefinitionLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("COUNT DEFINITIONS");

            if (report.Definitions.Count == 0)
            {
                lines.Add("  No definitions were recorded.");
                return lines;
            }

            List<string[]> rows = report.Definitions
                .Select(row => new[] { row.Label, row.Value, row.Note })
                .ToList();

            lines.AddRange(Table(new[] { "Term", "Value", "Meaning" }, rows));
            return lines;
        }

        public static List<string> CreateGateCheckLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("GATE CHECKS");

            if (report.GateRows.Count == 0)
            {
                lines.Add("  No gate sizing result was wired.");
                return lines;
            }

            List<string[]> rows = report.GateRows
                .Select(row => new[]
                {
                    row.Label,
                    row.Expected.ToString(CultureInfo.InvariantCulture),
                    row.Actual.ToString(CultureInfo.InvariantCulture),
                    row.Status.ToDisplayLabel(),
                    row.Note
                })
                .ToList();

            lines.AddRange(Table(
                new[] { "Check", "Expected", "Actual", "Status", "Note" },
                rows));

            return lines;
        }

        public static List<string> CreateSizingCheckLines(DiagnosticsReport report)
        {
            List<string> lines = new List<string>();

            if (report == null)
                return lines;

            lines.Add("FACILITY SIZING CHECKS");

            if (report.SizingRows.Count == 0)
            {
                lines.Add("  No facility sizing result was wired.");
                return lines;
            }

            List<string[]> rows = report.SizingRows
                .Select(row => new[]
                {
                    row.Label,
                    row.PeakDemand.ToString(CultureInfo.InvariantCulture),
                    row.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                    Format(row.NetArea),
                    Format(row.GrossArea),
                    row.Status.ToDisplayLabel(),
                    row.Note
                })
                .ToList();

            lines.AddRange(Table(
                new[] { "Facility", "Peak", "Proc", "Net m2", "Gross m2", "Status", "Note" },
                rows));

            return lines;
        }

        // -------------------------------------------------------------
        // CSV
        // -------------------------------------------------------------

        public static string CreateCsv(DiagnosticsReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(CsvHeader);

            if (report == null)
                return builder.ToString();

            AppendCsv(
                builder,
                "Summary",
                "Overall status",
                string.Empty,
                report.OverallStatus.ToDisplayLabel(),
                string.Empty,
                null,
                null,
                null,
                string.Empty,
                string.Empty,
                "Population " + report.PopulationCount + " from " + report.PopulationSource);

            AppendCsv(builder, "Summary", "Errors", string.Empty, "FAIL", "Error",
                null, report.ErrorCount, null, string.Empty, string.Empty, string.Empty);
            AppendCsv(builder, "Summary", "Warnings", string.Empty, "WARN", "Warning",
                null, report.WarningCount, null, string.Empty, string.Empty, string.Empty);
            AppendCsv(builder, "Summary", "Info", string.Empty, "INFO", "Info",
                null, report.InfoCount, null, string.Empty, string.Empty, string.Empty);

            foreach (DiagnosticFinding finding in report.Findings)
            {
                AppendCsv(
                    builder,
                    "Finding",
                    finding.Title,
                    finding.Code,
                    finding.Status.ToDisplayLabel(),
                    finding.Severity.ToString(),
                    finding.Expected,
                    finding.Actual,
                    finding.Variance,
                    finding.Unit,
                    JoinCsvIds(finding.PassengerIds),
                    finding.Message +
                    (finding.Details.Count > 0
                        ? " | " + string.Join(" | ", finding.Details)
                        : string.Empty));
            }

            foreach (DiagnosticsServiceRow row in report.Services)
            {
                AppendCsv(builder, "Service", row.Label + " / expected", "SVC-EXPECTED",
                    row.Status.ToDisplayLabel(), string.Empty, null, row.Expected, null,
                    "passengers", JoinCsvIds(row.MissingPassengerIds), row.KindLabel);

                AppendCsv(builder, "Service", row.Label + " / processed", "SVC-PROCESSED",
                    row.Status.ToDisplayLabel(), string.Empty, null, row.Processed, null,
                    "passengers", JoinCsvIds(row.ExtraPassengerIds),
                    "warm-up " + row.WarmUpProcessed + ", pool " + row.PoolPassengers);

                if (row.MissingHistory > 0)
                {
                    AppendCsv(builder, "Service", row.Label + " / history",
                        "SVC-HISTORY", "WARN", "Warning",
                        row.Processed, row.Processed - row.MissingHistory,
                        -row.MissingHistory, "records", string.Empty,
                        "Processed records without facility history.");
                }

                if (row.BagDrop != null && row.BagDrop.Active)
                {
                    AppendCsv(builder, "BagDrop", row.Label + " / bag drop expected",
                        "SVC-BAGDROP-EXPECTED", row.Status.ToDisplayLabel(), string.Empty,
                        row.BagDrop.Expected, row.BagDrop.Recorded,
                        row.BagDrop.Recorded - row.BagDrop.Expected,
                        "passengers", JoinCsvIds(row.BagDrop.MissingPassengerIds),
                        "late " + row.BagDrop.LateBypassed);

                    AppendBreakdownCsv(builder, row.Label + " / bag drop missing",
                        "SVC-BAGDROP-CHANNEL", row.BagDrop.MissingChannels);
                    AppendBreakdownCsv(builder, row.Label + " / bag drop missing mix",
                        "SVC-BAGDROP-MIX", row.BagDrop.MissingMix);
                    AppendBreakdownCsv(builder, row.Label + " / bag drop missing departure",
                        "SVC-BAGDROP-DEPT", row.BagDrop.MissingDepartureTypes);
                    AppendBreakdownCsv(builder, row.Label + " / bag drop missing journey",
                        "SVC-BAGDROP-JOURNEY", row.BagDrop.MissingJourneys);
                }

                AppendBreakdownCsv(builder, row.Label + " / channel",
                    "SVC-CHANNEL", row.Channels);
                AppendBreakdownCsv(builder, row.Label + " / passenger mix",
                    "SVC-MIX", row.PassengerMix);
                AppendBreakdownCsv(builder, row.Label + " / departure type",
                    "SVC-DEPT", row.DepartureTypes);
                AppendBreakdownCsv(builder, row.Label + " / journey",
                    "SVC-JOURNEY", row.Journeys);
                AppendBreakdownCsv(builder, row.Label + " / exception",
                    "SVC-EXCEPTION", row.Exceptions);
            }

            foreach (DiagnosticsTransferRow row in report.Transfers)
            {
                AppendCsv(builder, "Transfer", row.Scenario + " / effective", "TRF-EFFECTIVE",
                    row.Status.ToDisplayLabel(), string.Empty,
                    row.Requested, row.Effective, row.Effective - row.Requested,
                    "passengers", string.Empty, string.Empty);

                AppendCsv(builder, "Transfer", row.Scenario + " / tagged", "TRF-TAGGED",
                    row.Status.ToDisplayLabel(), string.Empty,
                    row.Effective, row.Tagged, row.Tagged - row.Effective,
                    "passengers", string.Empty, string.Empty);

                AppendCsv(builder, "Transfer", row.Scenario + " / legs", "TRF-LEGS",
                    row.Status.ToDisplayLabel(), string.Empty,
                    row.DepartureLegs, row.ArrivalLegs,
                    row.ArrivalLegs - row.DepartureLegs, "records", string.Empty,
                    "Departure and arrival records are separate passengers.");
            }

            foreach (DiagnosticsTransferServiceRow row in report.TransferServices)
            {
                AppendCsv(builder, "TransferService", row.Service + " / required",
                    "TRF-SVC-REQUIRED", row.Status.ToDisplayLabel(), string.Empty,
                    row.Required, row.Required - row.MissingRequired,
                    -row.MissingRequired, "records", string.Empty,
                    "Bypassed " + row.Bypassed + ", no rules " + row.Unspecified +
                    ", served " + row.Processed);

                if (row.ServedBypassed > 0)
                {
                    AppendCsv(builder, "TransferService", row.Service + " / served bypass",
                        "TRF-SVC-BYPASS", "FAIL", "Error", 0, row.ServedBypassed,
                        row.ServedBypassed, "records", string.Empty,
                        "History exists although the transfer rule bypasses the service.");
                }
            }

            foreach (DiagnosticsPeakRow row in report.Peaks)
            {
                AppendCsv(builder, "Peak", row.Flow + " / " + row.Service, "TRF-PEAK",
                    row.Status.ToDisplayLabel(), string.Empty,
                    row.Required, row.Required - row.MissingRequired,
                    -row.MissingRequired, "records", string.Empty,
                    row.Window + " | peak " + row.Peak + ", processed " + row.Processed);
            }

            foreach (DiagnosticsPassengerIssue issue in report.PassengerIssues)
            {
                AppendCsv(builder, "PassengerIssue", "PAX " + issue.PassengerId, issue.Code,
                    issue.Severity == DiagnosticSeverity.Error ? "FAIL" : "WARN",
                    issue.Severity.ToString(), issue.Expected, issue.Actual, null,
                    string.Empty, issue.PassengerId.ToString(CultureInfo.InvariantCulture),
                    issue.Message + " | leg " + issue.FlightLegId + " | " +
                    issue.DhpType + " | " + issue.Journey + " | " + issue.Scenario +
                    " | channel " + issue.Channel +
                    (issue.UsesOnlineCheckIn ? " | online check-in" : string.Empty) +
                    (issue.HasCheckedBag ? " | checked bag" : string.Empty));
            }

            foreach (DiagnosticsGateRow row in report.GateRows)
            {
                AppendCsv(builder, "Gate", row.Label, "GATE", row.Status.ToDisplayLabel(),
                    row.Status == DiagnosticStatus.Fail ? "Error" : string.Empty,
                    row.Expected, row.Actual, row.Actual - row.Expected, "count",
                    string.Empty, row.Note);
            }

            foreach (DiagnosticsSizingRow row in report.SizingRows)
            {
                AppendCsv(builder, "Sizing", row.Label, "SIZE", row.Status.ToDisplayLabel(),
                    row.Status == DiagnosticStatus.Fail ? "Error" : string.Empty,
                    null, row.ProcessorCount, null, "processors", string.Empty,
                    "Peak " + row.PeakDemand + ", net " + Format(row.NetArea) +
                    " m2, gross " + Format(row.GrossArea) + " m2. " + row.Note);
            }

            foreach (DiagnosticsDefinitionRow row in report.Definitions)
            {
                AppendCsv(builder, "Definition", row.Label, "DEF", "INFO", string.Empty,
                    null, null, null, string.Empty, string.Empty,
                    row.Value + " - " + row.Note);
            }

            return builder.ToString();
        }

        private static void AppendCsv(
            StringBuilder builder,
            string section,
            string item,
            string code,
            string status,
            string severity,
            double? expected,
            double? actual,
            double? variance,
            string unit,
            string passengerIds,
            string message)
        {
            builder.Append(Escape(section)).Append(',');
            builder.Append(Escape(item)).Append(',');
            builder.Append(Escape(code)).Append(',');
            builder.Append(Escape(status)).Append(',');
            builder.Append(Escape(severity)).Append(',');
            builder.Append(expected.HasValue ? Format(expected) : string.Empty).Append(',');
            builder.Append(actual.HasValue ? Format(actual) : string.Empty).Append(',');
            builder.Append(variance.HasValue ? Format(variance) : string.Empty).Append(',');
            builder.Append(Escape(unit)).Append(',');
            builder.Append(Escape(passengerIds)).Append(',');
            builder.Append(Escape(message));
            builder.AppendLine();
        }

        private static string JoinCsvIds(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return string.Empty;

            return string.Join(" ", ids);
        }

        private static void AppendBreakdownCsv(
            StringBuilder builder,
            string itemPrefix,
            string code,
            List<DiagnosticsBreakdownRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            foreach (DiagnosticsBreakdownRow row in rows)
            {
                if (row == null)
                    continue;

                bool countOnly = row.Expected == row.Count && row.Processed == 0;

                AppendCsv(
                    builder,
                    "CheckIn",
                    itemPrefix + " / " + row.Label,
                    code,
                    row.Missing > 0 ? "FAIL" : "PASS",
                    row.Missing > 0 ? "Error" : string.Empty,
                    countOnly ? row.Count : row.Expected,
                    countOnly ? null : (double?)row.Processed,
                    countOnly ? null : (double?)-row.Missing,
                    "passengers",
                    JoinCsvIds(row.PassengerIds),
                    countOnly
                        ? "Records in this slice."
                        : "Missing " + row.Missing + " of " + row.Expected + ".");
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string text = value.Replace("\r", " ").Replace("\n", " ");

            if (text.IndexOfAny(new[] { ',', '"' }) >= 0)
                text = "\"" + text.Replace("\"", "\"\"") + "\"";

            return text;
        }

        // -------------------------------------------------------------
        // Shared helpers
        // -------------------------------------------------------------

        private static string Format(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static List<string> Table(string[] headers, List<string[]> rows)
        {
            List<string> lines = new List<string>();
            int columns = headers.Length;

            int[] widths = new int[columns];
            for (int i = 0; i < columns; i++)
                widths[i] = headers[i].Length;

            foreach (string[] row in rows)
            {
                for (int i = 0; i < columns && i < row.Length; i++)
                    widths[i] = Math.Max(widths[i], (row[i] ?? string.Empty).Length);
            }

            bool[] numeric = new bool[columns];

            for (int i = 0; i < columns; i++)
            {
                bool any = false;
                bool all = true;

                foreach (string[] row in rows)
                {
                    string value = i < row.Length ? row[i] : string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    any = true;

                    double parsed;
                    if (!double.TryParse(value, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out parsed))
                    {
                        all = false;
                        break;
                    }
                }

                numeric[i] = any && all;
            }

            lines.Add(FormatRow(headers, widths, new bool[columns]));
            lines.Add(string.Join("  ", widths.Select(w => new string('-', w))));

            foreach (string[] row in rows)
                lines.Add(FormatRow(row, widths, numeric));

            return lines;
        }

        private static string FormatRow(string[] values, int[] widths, bool[] numeric)
        {
            List<string> cells = new List<string>();

            for (int i = 0; i < widths.Length; i++)
            {
                string value = i < values.Length ? values[i] ?? string.Empty : string.Empty;
                cells.Add(numeric.Length > i && numeric[i]
                    ? PadLeft(value, widths[i])
                    : Pad(value, widths[i]));
            }

            return string.Join("  ", cells).TrimEnd();
        }

        private static string Pad(string value, int width)
        {
            if (value == null)
                value = string.Empty;

            return value.Length >= width ? value : value.PadRight(width);
        }

        private static string PadLeft(string value, int width)
        {
            if (value == null)
                value = string.Empty;

            return value.Length >= width ? value : value.PadLeft(width);
        }
    }
}
