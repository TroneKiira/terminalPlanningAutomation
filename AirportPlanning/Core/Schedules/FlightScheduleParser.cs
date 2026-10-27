using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>
    /// Reads a flight-schedule workbook into normalized legs.
    ///
    /// The expected workbook has one row per aircraft turnaround: the arrival
    /// and the departure share one row, with arrival columns on the left and
    /// departure columns on the right. The same row produces two legs with a
    /// shared link ID. A row that only carries one side produces one leg. When
    /// a Rotation ID is present it links one-sided rows that belong to the
    /// same turn, including a departure that lands on the next day.
    ///
    /// The parser only reads, normalizes and validates the file. It does not
    /// generate passengers or run queues.
    /// </summary>
    public static class FlightScheduleParser
    {
        private const int HeaderScanLimit = 100;

        public static FlightScheduleResult Parse(byte[] fileBytes, ScheduleImportOptions options)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                throw new ArgumentException("Flight schedule file is empty.", nameof(fileBytes));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var workbook = XlsxWorkbook.Load(fileBytes);
            var globalIssues = new List<LegIssue>();

            var sheet = SelectSheet(workbook, options, out var headerRow);
            var headerRowObject = sheet.Rows.FirstOrDefault(r => r.Number == headerRow) ??
                throw new InvalidDataException(
                    $"Header row {headerRow} does not exist in worksheet '{sheet.Name}'.");

            ValidateBaseDate(options.BaseDate, globalIssues);

            var resolver = ScheduleColumnResolver.Create(
                headerRowObject.Cells,
                options,
                globalIssues);

            var result = new FlightScheduleResult
            {
                ScheduleName = ResolveScheduleName(options),
                SheetName = sheet.Name,
                SourceFile = options.SourceFile ?? string.Empty,
                BaseDate = options.BaseDate ?? string.Empty,
                FallbackTerminal = (options.FallbackTerminal ?? string.Empty).Trim(),
                LinkedRows = options.LinkedRows,
                HeaderRow = headerRow,
                GlobalIssues = globalIssues,
                Columns = resolver.ToDisplayMap()
            };

            string scheduleKey = ResolveScheduleKey(options);
            int firstDataRow = options.FirstDataRowIndex > 0
                ? options.FirstDataRowIndex
                : headerRow + 1;

            foreach (var row in sheet.Rows
                .Where(r => r.Number >= firstDataRow)
                .OrderBy(r => r.Number))
            {
                if (row.IsEmpty)
                    continue;

                string rowKey = $"{scheduleKey}#R{row.Number}";

                var arrival = ParseLeg(
                    row,
                    LegDirection.Arrival,
                    $"{rowKey}#A",
                    options.LinkedRows ? rowKey : $"{rowKey}#A",
                    resolver,
                    options);

                var departure = ParseLeg(
                    row,
                    LegDirection.Departure,
                    $"{rowKey}#D",
                    options.LinkedRows ? rowKey : $"{rowKey}#D",
                    resolver,
                    options);

                if (arrival == null && departure == null)
                    continue;

                result.SourceRowCount++;

                if (options.LinkedRows)
                {
                    ApplyRotationIdLink(arrival, scheduleKey);
                    ApplyRotationIdLink(departure, scheduleKey);
                }

                if (arrival != null)
                    result.Legs.Add(arrival);

                if (departure != null)
                    result.Legs.Add(departure);
            }

            if (options.LinkedRows)
                ApplyOvernightRolls(result.Legs);

            if (result.Legs.Count == 0)
            {
                result.GlobalIssues.Add(new LegIssue
                {
                    Field = "Schedule",
                    Message = "No arrival or departure legs were found below the header row.",
                    Severity = "Error"
                });
            }

            if (result.SourceRowCount > 0 && result.ArrivalCount == 0 && result.DepartureCount > 0)
            {
                result.GlobalIssues.Add(new LegIssue
                {
                    Field = "Arrival Time",
                    Message = "Only departure legs were found; no arrival columns produced data.",
                    Severity = "Warning"
                });
            }

            int fallbackCount = result.Legs.Count(l => l.TerminalSource == TerminalSource.Fallback);

            if (fallbackCount > 0)
            {
                result.GlobalIssues.Add(new LegIssue
                {
                    Field = "Terminal",
                    Message = $"{fallbackCount} leg(s) had no usable terminal in the workbook; " +
                              $"fallback terminal '{result.FallbackTerminal}' was applied. " +
                              "Fallback terminals are an explicit assumption, not workbook values. " +
                              "Freight legs use 'Cargo'.",
                    Severity = "Warning"
                });
            }

            return result;
        }

        private static void ApplyRotationIdLink(FlightLeg leg, string scheduleKey)
        {
            if (leg == null || string.IsNullOrWhiteSpace(leg.RotationId))
                return;

            leg.LinkId = $"{scheduleKey}#T{leg.RotationId.Trim()}";
        }

        private static void ApplyOvernightRolls(List<FlightLeg> legs)
        {
            foreach (var group in legs.GroupBy(l =>
                string.IsNullOrWhiteSpace(l.LinkId) ? l.Id : l.LinkId))
            {
                var arrival = group.FirstOrDefault(l => l.Direction == LegDirection.Arrival);
                var departure = group.FirstOrDefault(l => l.Direction == LegDirection.Departure);

                if (arrival == null ||
                    departure == null ||
                    !arrival.HasTime ||
                    !departure.HasTime ||
                    departure.MinutesFromMidnight >= arrival.MinutesFromMidnight ||
                    departure.DayOffset > arrival.DayOffset)
                {
                    continue;
                }

                // A linked overnight turn has a departure clock time below the
                // arrival clock time. Store that as a next-day departure rather
                // than a negative turn.
                departure.DayOffset = arrival.DayOffset + 1;
            }
        }

        private static XlsxSheet SelectSheet(
            XlsxWorkbook workbook,
            ScheduleImportOptions options,
            out int headerRow)
        {
            var requested = (options.SheetName ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(requested))
            {
                var named = workbook.Sheets.FirstOrDefault(s =>
                    string.Equals(s.Name, requested, StringComparison.OrdinalIgnoreCase));

                if (named == null)
                {
                    var available = string.Join(", ", workbook.Sheets.Select(s => $"'{s.Name}'"));
                    throw new InvalidDataException(
                        $"Worksheet '{requested}' was not found. Available worksheets: {available}.");
                }

                headerRow = ResolveHeaderRow(named, options);

                if (headerRow <= 0)
                {
                    throw new InvalidDataException(
                        $"Could not find a schedule header row in worksheet '{named.Name}'.");
                }

                return named;
            }

            if (options.HeaderRowIndex > 0)
            {
                var sheetWithRow = workbook.Sheets.FirstOrDefault(s =>
                    s.Rows.Any(r => r.Number == options.HeaderRowIndex));

                if (sheetWithRow == null)
                {
                    throw new InvalidDataException(
                        $"No worksheet has the requested header row {options.HeaderRowIndex}.");
                }

                headerRow = options.HeaderRowIndex;
                return sheetWithRow;
            }

            foreach (var sheet in workbook.Sheets)
            {
                headerRow = ScheduleColumnResolver.FindHeaderRow(sheet, HeaderScanLimit);

                if (headerRow > 0)
                    return sheet;
            }

            throw new InvalidDataException(
                "No worksheet contains a recognisable flight-schedule header. " +
                "Check that the workbook has arrival/departure time and passenger columns.");
        }

        private static int ResolveHeaderRow(XlsxSheet sheet, ScheduleImportOptions options)
        {
            if (options.HeaderRowIndex > 0)
                return options.HeaderRowIndex;

            return ScheduleColumnResolver.FindHeaderRow(sheet, HeaderScanLimit);
        }

        private static FlightLeg ParseLeg(
            XlsxRow row,
            LegDirection direction,
            string id,
            string linkId,
            ScheduleColumnResolver resolver,
            ScheduleImportOptions options)
        {
            bool arrival = direction == LegDirection.Arrival;
            var cells = row.Cells;

            var airline = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalAirline
                : ScheduleField.DepartureAirline);

            var station = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalStation
                : ScheduleField.DepartureStation);

            var via = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalVia
                : ScheduleField.DepartureVia);

            var sector = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalSector
                : ScheduleField.DepartureSector);

            var time = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalTime
                : ScheduleField.DepartureTime);

            var loadFactor = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalLoadFactor
                : ScheduleField.DepartureLoadFactor);

            var pax = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalPax
                : ScheduleField.DeparturePax);

            var service = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalServiceType
                : ScheduleField.DepartureServiceType);

            var terminal = GetCell(cells, resolver, arrival
                ? ScheduleField.ArrivalTerminal
                : ScheduleField.DepartureTerminal);

            // Shared aircraft and operational fields.
            var flightNumber = GetCell(cells, resolver, ScheduleField.FlightNumber);
            var rotationId = GetCell(cells, resolver, ScheduleField.RotationId);
            var seats = GetCell(cells, resolver, ScheduleField.Seats);
            var aircraftType = GetCell(cells, resolver, ScheduleField.AircraftType);
            var icaoCode = GetCell(cells, resolver, ScheduleField.IcaoCode);
            var stand = GetCell(cells, resolver, ScheduleField.Stand);
            var registration = GetCell(cells, resolver, ScheduleField.Registration);
            var remarks = GetCell(cells, resolver, ScheduleField.Remarks);

            // A formula with no cached value is not evidence of a flight. Some
            // workbooks carry terminal formulas on both sides of a row even
            // when only one side has a movement. Passenger, PLF and generic
            // sector cells are also weak evidence because one header may be
            // shared between directions. A side needs operational identity:
            // airline, station, time, service or terminal value.
            bool hasSideData =
                HasValue(airline) ||
                HasValue(station) ||
                HasValue(via) ||
                HasValue(time) ||
                HasValue(service) ||
                HasValue(terminal);

            if (!hasSideData)
                return null;

            var leg = new FlightLeg
            {
                Id = id,
                LinkId = linkId,
                Direction = direction,
                SourceRow = row.Number,
                Date = (options.BaseDate ?? string.Empty).Trim()
            };

            leg.Airline = CellText(airline);
            leg.Station = CellText(station);
            leg.Via = CellText(via);
            leg.FlightNumber = CellText(flightNumber);
            leg.RotationId = CellText(rotationId);
            leg.AircraftTypeRaw = CellText(aircraftType);
            leg.IcaoCodeRaw = CellText(icaoCode);
            leg.Stand = CellText(stand);
            leg.Registration = CellText(registration);
            leg.Remarks = CellText(remarks);
            leg.TimeRaw = CellText(time);

            // Time, terminal and numeric fields report their own formula
            // problems. The remaining text fields are checked here so an
            // uncached formula is never mistaken for an intentional blank.
            AddTextCellIssue(leg, "Airline", airline);
            AddTextCellIssue(leg, "Station", station);
            AddTextCellIssue(leg, "Via", via);
            AddTextCellIssue(leg, "Flight Number", flightNumber);
            AddTextCellIssue(leg, "Rotation ID", rotationId);
            AddTextCellIssue(leg, "Aircraft Type", aircraftType);
            AddTextCellIssue(leg, "ICAO Code", icaoCode);
            AddTextCellIssue(leg, "Sector", sector);
            AddTextCellIssue(leg, "Service Type", service);
            AddTextCellIssue(leg, "Stand", stand);
            AddTextCellIssue(leg, "Registration", registration);
            AddTextCellIssue(leg, "Remarks", remarks);

            leg.SectorRaw = ParseSector(CellText(sector));
            leg.ServiceType = ParseServiceType(CellText(service));

            var decodedTime = ScheduleTimeDecoder.Decode(time, options.BaseDate);

            if (decodedTime.IsValid)
            {
                leg.HasTime = true;
                leg.MinutesFromMidnight = decodedTime.Minutes;
                leg.DayOffset = decodedTime.DayOffset;

                if (!string.IsNullOrWhiteSpace(decodedTime.Date))
                    leg.Date = decodedTime.Date;
            }
            else
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Time",
                    Message = decodedTime.Error,
                    Severity = "Error"
                });
            }

            leg.SeatsRaw = ParseIntCell(seats, "Seat count", leg);
            leg.LoadFactor = ParseDoubleCell(loadFactor, "Load factor", leg);
            leg.PaxRaw = ParseIntCell(pax, "Passenger count", leg);

            // A configured fallback, or the freight service type, means a
            // missing terminal will be filled deliberately. In that case the
            // per-leg warning is suppressed; one workbook-level warning records
            // the assumption instead.
            string fallbackTerminal = (options.FallbackTerminal ?? string.Empty).Trim();
            bool terminalPolicyResolves =
                leg.ServiceType == LegServiceType.Freight ||
                !string.IsNullOrWhiteSpace(fallbackTerminal);

            if (HasContent(terminal))
            {
                if (terminal.IsUncachedFormula)
                {
                    leg.TerminalUnresolved = true;

                    if (!terminalPolicyResolves)
                    {
                        leg.Issues.Add(new LegIssue
                        {
                            Field = "Terminal",
                            Message = "Terminal formula has no usable cached result (empty or unresolved); " +
                                      "it cannot be resolved from the file. Repair the formula references or " +
                                      "enter the terminal as a literal value.",
                            Severity = "Warning"
                        });
                    }
                }
                else if (terminal.IsError)
                {
                    leg.TerminalUnresolved = true;

                    if (!terminalPolicyResolves)
                    {
                        leg.Issues.Add(new LegIssue
                        {
                            Field = "Terminal",
                            Message = $"Terminal contains an Excel error ({CellText(terminal)}).",
                            Severity = "Warning"
                        });
                    }
                }
                else
                {
                    leg.Terminal = CellText(terminal);
                }
            }

            ApplyTerminalPolicy(leg, fallbackTerminal);

            AddValidationIssues(leg, resolver, arrival);
            return leg;
        }

        /// <summary>
        /// Resolves the terminal after the workbook value has been read.
        /// Freight legs are always "Cargo" when no terminal was supplied.
        /// Non-freight legs use the configured fallback when no terminal was
        /// supplied. Every filled value records its source, so a fallback is
        /// never mistaken for a workbook value.
        /// </summary>
        private static void ApplyTerminalPolicy(FlightLeg leg, string fallbackTerminal)
        {
            if (leg == null)
                return;

            if (leg.ServiceType == LegServiceType.Freight)
            {
                if (string.IsNullOrWhiteSpace(leg.Terminal))
                {
                    leg.Terminal = "Cargo";
                    leg.TerminalSource = TerminalSource.Cargo;
                }
                else
                {
                    leg.TerminalSource = TerminalSource.Workbook;
                }

                leg.TerminalUnresolved = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(leg.Terminal))
            {
                leg.TerminalSource = TerminalSource.Workbook;
                leg.TerminalUnresolved = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(fallbackTerminal))
            {
                leg.Terminal = fallbackTerminal;
                leg.TerminalSource = TerminalSource.Fallback;
                leg.TerminalUnresolved = false;
            }
        }

        private static void AddValidationIssues(
            FlightLeg leg,
            ScheduleColumnResolver resolver,
            bool arrival)
        {
            if (string.IsNullOrWhiteSpace(leg.Airline))
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Airline",
                    Message = "Airline is blank.",
                    Severity = "Warning"
                });
            }

            if (leg.SectorRaw == LegSector.Unknown)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Sector",
                    Message = "Domestic/International could not be read for this leg.",
                    Severity = "Warning"
                });
            }

            var serviceColumn = resolver[arrival
                ? ScheduleField.ArrivalServiceType
                : ScheduleField.DepartureServiceType];

            if (!string.IsNullOrWhiteSpace(serviceColumn) &&
                leg.ServiceType == LegServiceType.Other)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Service Type",
                    Message = "Service type is blank or not recognised; the leg is retained as 'Other'.",
                    Severity = "Warning"
                });
            }

            if (leg.ServiceType != LegServiceType.Freight && leg.PaxRaw == null)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Passenger Count",
                    Message = leg.SeatsRaw.HasValue
                        ? "Passenger count is missing; seat count is available for estimation."
                        : "Passenger count and seat count are both missing.",
                    Severity = "Warning"
                });
            }

            if (leg.PaxRaw.HasValue && leg.PaxRaw.Value < 0)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Passenger Count",
                    Message = $"Passenger count is negative ({leg.PaxRaw.Value}).",
                    Severity = "Error"
                });
            }

            if (leg.SeatsRaw.HasValue &&
                leg.SeatsRaw.Value <= 0 &&
                leg.ServiceType == LegServiceType.Passenger)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Seat Count",
                    Message = $"Seat count is {leg.SeatsRaw.Value} for a passenger leg.",
                    Severity = "Warning"
                });
            }

            if (leg.LoadFactor.HasValue &&
                (leg.LoadFactor.Value < 0 || leg.LoadFactor.Value > 1))
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = "Load Factor",
                    Message = $"Load factor {leg.LoadFactor.Value} is outside the 0 to 1 range.",
                    Severity = "Warning"
                });
            }
        }

        private static XlsxCell GetCell(
            Dictionary<string, XlsxCell> cells,
            ScheduleColumnResolver resolver,
            ScheduleField field)
        {
            var column = resolver[field];

            if (string.IsNullOrWhiteSpace(column))
                return default;

            return cells.TryGetValue(column, out var cell) ? cell : default;
        }

        private static bool HasValue(XlsxCell cell)
        {
            return !string.IsNullOrWhiteSpace(cell.Column) && !cell.IsBlank;
        }

        private static bool HasContent(XlsxCell cell)
        {
            return !string.IsNullOrWhiteSpace(cell.Column) &&
                (!cell.IsBlank || cell.IsFormula || cell.IsError);
        }

        private static string CellText(XlsxCell cell)
        {
            return string.IsNullOrWhiteSpace(cell.Value) ? string.Empty : cell.Value.Trim();
        }

        private static void AddTextCellIssue(FlightLeg leg, string label, XlsxCell cell)
        {
            if (cell.IsUncachedFormula)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = label,
                    Message = $"{label} is a formula with no cached value; it was left blank.",
                    Severity = "Warning"
                });
            }
            else if (cell.IsError)
            {
                leg.Issues.Add(new LegIssue
                {
                    Field = label,
                    Message = $"{label} contains an Excel error ({CellText(cell)}).",
                    Severity = "Warning"
                });
            }
        }

        private static int? ParseIntCell(XlsxCell cell, string label, FlightLeg leg)
        {
            if (string.IsNullOrWhiteSpace(cell.Column))
                return null;

            if (cell.IsUncachedFormula)
            {
                AddCellIssue(leg, label, "cell is a formula with no cached value.");
                return null;
            }

            if (cell.IsError)
            {
                AddCellIssue(leg, label, $"cell contains an Excel error ({CellText(cell)}).");
                return null;
            }

            var raw = CellText(cell);

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (int.TryParse(
                raw,
                NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var integer))
            {
                return integer;
            }

            if (double.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var number) &&
                !double.IsNaN(number) &&
                !double.IsInfinity(number) &&
                Math.Abs(number - Math.Round(number)) < 1e-9)
            {
                return (int)Math.Round(number);
            }

            AddCellIssue(leg, label, $"value '{raw}' is not a whole number.");
            return null;
        }

        private static double? ParseDoubleCell(XlsxCell cell, string label, FlightLeg leg)
        {
            if (string.IsNullOrWhiteSpace(cell.Column))
                return null;

            if (cell.IsUncachedFormula)
            {
                AddCellIssue(leg, label, "cell is a formula with no cached value.");
                return null;
            }

            if (cell.IsError)
            {
                AddCellIssue(leg, label, $"cell contains an Excel error ({CellText(cell)}).");
                return null;
            }

            var raw = CellText(cell);

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            bool percent = raw.EndsWith("%", StringComparison.Ordinal);

            if (percent)
                raw = raw.Substring(0, raw.Length - 1).Trim();

            if (!double.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var number) ||
                double.IsNaN(number) ||
                double.IsInfinity(number))
            {
                AddCellIssue(leg, label, $"value '{CellText(cell)}' is not numeric.");
                return null;
            }

            if (percent)
                return number / 100.0;

            // A hand-entered percentage such as 87.5 is common in schedules.
            if (number > 2.0 && number <= 100.0)
                return number / 100.0;

            return number;
        }

        private static void AddCellIssue(FlightLeg leg, string label, string message)
        {
            leg.Issues.Add(new LegIssue
            {
                Field = label,
                Message = $"{label} {message}",
                Severity = "Warning"
            });
        }

        private static LegSector ParseSector(string raw)
        {
            var value = (raw ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "D" ||
                value.StartsWith("DOM", StringComparison.Ordinal))
            {
                return LegSector.Domestic;
            }

            if (value == "I" ||
                value.StartsWith("INT", StringComparison.Ordinal))
            {
                return LegSector.International;
            }

            return LegSector.Unknown;
        }

        private static LegServiceType ParseServiceType(string raw)
        {
            var value = (raw ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "J" ||
                value.StartsWith("PAX", StringComparison.Ordinal) ||
                value.StartsWith("PASS", StringComparison.Ordinal))
            {
                return LegServiceType.Passenger;
            }

            if (value == "F" ||
                value == "C" ||
                value.StartsWith("FRE", StringComparison.Ordinal) ||
                value.StartsWith("CAR", StringComparison.Ordinal))
            {
                return LegServiceType.Freight;
            }

            return LegServiceType.Other;
        }

        private static string ResolveScheduleName(ScheduleImportOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ScheduleName))
                return options.ScheduleName.Trim();

            var fileName = string.IsNullOrWhiteSpace(options.SourceFile)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(options.SourceFile);

            return string.IsNullOrWhiteSpace(fileName) ? "Imported schedule" : fileName;
        }

        private static string ResolveScheduleKey(ScheduleImportOptions options)
        {
            var name = ResolveScheduleName(options);
            return name.Replace(" ", "_");
        }

        private static void ValidateBaseDate(string baseDate, List<LegIssue> globalIssues)
        {
            if (string.IsNullOrWhiteSpace(baseDate))
                return;

            if (!DateTime.TryParse(
                baseDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out _))
            {
                globalIssues.Add(new LegIssue
                {
                    Field = "Base Date",
                    Message = $"Base Date '{baseDate}' is not a recognised date.",
                    Severity = "Warning"
                });
            }
        }
    }
}
