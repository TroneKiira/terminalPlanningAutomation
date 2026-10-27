using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>
    /// Canonical fields the importer knows how to bind to workbook columns.
    /// </summary>
    internal enum ScheduleField
    {
        ArrivalAirline,
        DepartureAirline,
        FlightNumber,
        RotationId,
        Seats,
        AircraftType,
        IcaoCode,
        ArrivalStation,
        ArrivalVia,
        DepartureVia,
        DepartureStation,
        ArrivalSector,
        DepartureSector,
        ArrivalTime,
        DepartureTime,
        ArrivalLoadFactor,
        DepartureLoadFactor,
        ArrivalPax,
        DeparturePax,
        ArrivalServiceType,
        DepartureServiceType,
        ArrivalTerminal,
        DepartureTerminal,
        Stand,
        Registration,
        Remarks
    }

    /// <summary>
    /// Detects a header row and binds canonical fields to physical Excel
    /// columns. Physical column letters are used throughout so a header that
    /// starts in column B is not shifted into column A.
    /// </summary>
    internal sealed class ScheduleColumnResolver
    {
        private const int MinimumRecognisedFields = 5;

        private static readonly Dictionary<ScheduleField, string[]> Aliases =
            new Dictionary<ScheduleField, string[]>
            {
                [ScheduleField.ArrivalAirline] = new[]
                {
                    "Airline (Arrival)", "Airline (Arr.)", "Arrival Airline",
                    "Arr Airline", "Airline Arrival", "Airline - Arrival"
                },
                [ScheduleField.DepartureAirline] = new[]
                {
                    "Airline (Departure)", "Airline (Dep.)", "Departure Airline",
                    "Dep Airline", "Airline Departure", "Airline - Departure"
                },
                [ScheduleField.FlightNumber] = new[]
                {
                    "Flight Number", "Flight No", "Flight No.", "Flight"
                },
                [ScheduleField.RotationId] = new[]
                {
                    "Rotation ID", "Rotation Id", "Rotation Number", "Rotation",
                    "Turn ID", "Turn Id", "Turn", "Turnaround ID", "Turnaround Id"
                },
                [ScheduleField.Seats] = new[]
                {
                    "No. of Seats", "No of Seats", "Number of Seats", "Seats",
                    "Capacity", "Seat Capacity"
                },
                [ScheduleField.AircraftType] = new[]
                {
                    "Aircraft Type", "Aircraft", "A/C Type", "AC Type", "Equipment"
                },
                [ScheduleField.IcaoCode] = new[]
                {
                    "ICAO Code", "ICAO Code Letter", "ICAO", "Code Letter",
                    "ICAO Aerodrome Reference Code"
                },
                [ScheduleField.ArrivalStation] = new[]
                {
                    "Origin", "Arrival Station", "Origin Airport", "From"
                },
                [ScheduleField.ArrivalVia] = new[]
                {
                    "Arrival (via)", "Arrival Via", "Via (Arrival)", "Via Arrival"
                },
                [ScheduleField.DepartureVia] = new[]
                {
                    "Departure (via)", "Departure Via", "Via (Departure)", "Via Departure"
                },
                [ScheduleField.DepartureStation] = new[]
                {
                    "Destination", "Departure Station", "Destination Airport", "To"
                },
                [ScheduleField.ArrivalSector] = new[]
                {
                    "Arrival Domestic/International", "Domestic/International (Arrival)",
                    "Arrival Sector", "Domestic/International", "Domestic / International",
                    "Dom/Int", "Sector"
                },
                [ScheduleField.DepartureSector] = new[]
                {
                    "Departure Domestic/International", "Domestic/International (Departure)",
                    "Departure Sector", "Domestic/International", "Domestic / International",
                    "Dom/Int", "Sector"
                },
                [ScheduleField.ArrivalTime] = new[]
                {
                    "Arrival Time", "Arrival time", "STA", "Arr Time",
                    "Scheduled Arrival Time"
                },
                [ScheduleField.DepartureTime] = new[]
                {
                    "Departure Time", "Departure time", "STD", "Dep Time",
                    "Scheduled Departure Time"
                },
                [ScheduleField.ArrivalLoadFactor] = new[]
                {
                    "Arrival PLF", "PLF (Arrival)", "Load Factor (Arrival)",
                    "Arrival Load Factor", "PLF", "Load Factor"
                },
                [ScheduleField.DepartureLoadFactor] = new[]
                {
                    "Departure PLF", "PLF (Departure)", "Load Factor (Departure)",
                    "Departure Load Factor", "PLF", "Load Factor"
                },
                [ScheduleField.ArrivalPax] = new[]
                {
                    "Arrival Pax", "Arr Pax", "Pax (Arrival)", "Passengers (Arrival)",
                    "Arrival Passengers", "Pax", "Passengers", "Passenger Count"
                },
                [ScheduleField.DeparturePax] = new[]
                {
                    "Departure Pax", "Dep Pax", "Pax (Departure)", "Passengers (Departure)",
                    "Departure Passengers", "Pax", "Passengers", "Passenger Count"
                },
                [ScheduleField.ArrivalServiceType] = new[]
                {
                    "Service Type - Arr", "Service Type - Arrival", "Service Type (Arrival)",
                    "Arrival Service Type", "Service (Arrival)"
                },
                [ScheduleField.DepartureServiceType] = new[]
                {
                    "Service Type - Dep", "Service Type - Departure", "Service Type (Departure)",
                    "Departure Service Type", "Service (Departure)"
                },
                [ScheduleField.ArrivalTerminal] = new[]
                {
                    "ARR T1/T2", "Arrival Terminal", "Terminal (Arrival)", "Arr T1/T2",
                    "Terminal - Arrival"
                },
                [ScheduleField.DepartureTerminal] = new[]
                {
                    "DEP T1/T2", "Departure Terminal", "Terminal (Departure)", "Dep T1/T2",
                    "Terminal - Departure"
                },
                [ScheduleField.Stand] = new[]
                {
                    "Stand", "Stand Code", "Gate", "Gate/Stand", "Parking", "Bay"
                },
                [ScheduleField.Registration] = new[]
                {
                    "Registration", "Reg", "Aircraft Registration"
                },
                [ScheduleField.Remarks] = new[]
                {
                    "Remarks", "Notes", "Comment", "Comments"
                }
            };

        private static readonly Dictionary<string, ScheduleField> FieldKeys =
            BuildFieldKeys();

        private static readonly HashSet<ScheduleField> ArrivalFirstFields =
            new HashSet<ScheduleField>
            {
                ScheduleField.ArrivalAirline,
                ScheduleField.ArrivalStation,
                ScheduleField.ArrivalSector,
                ScheduleField.ArrivalTime,
                ScheduleField.ArrivalLoadFactor,
                ScheduleField.ArrivalPax,
                ScheduleField.ArrivalServiceType,
                ScheduleField.ArrivalTerminal,
                ScheduleField.ArrivalVia
            };

        private readonly Dictionary<ScheduleField, string> _columns;

        private ScheduleColumnResolver(Dictionary<ScheduleField, string> columns)
        {
            _columns = columns;
        }

        public static ScheduleColumnResolver Create(
            Dictionary<string, XlsxCell> headerCells,
            ScheduleImportOptions options,
            List<LegIssue> globalIssues)
        {
            var columns = new Dictionary<ScheduleField, string>();
            var headerIndex = BuildHeaderIndex(headerCells);

            foreach (var field in (ScheduleField[])Enum.GetValues(typeof(ScheduleField)))
            {
                var column = FindFieldColumn(headerIndex, field);

                if (!string.IsNullOrWhiteSpace(column))
                    columns[field] = column;
            }

            ApplyOverrides(columns, headerIndex, headerCells, options, globalIssues);
            AddMissingFieldIssues(columns, globalIssues);

            return new ScheduleColumnResolver(columns);
        }

        public static int FindHeaderRow(XlsxSheet sheet, int maxRowsToScan)
        {
            int bestRow = 0;
            int bestScore = 0;

            foreach (var row in sheet.Rows.OrderBy(r => r.Number).Take(maxRowsToScan))
            {
                int score = CountRecognisedFields(row.Cells);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRow = row.Number;
                }
            }

            return bestScore >= MinimumRecognisedFields ? bestRow : 0;
        }

        public static int CountRecognisedFields(Dictionary<string, XlsxCell> headerCells)
        {
            var headerIndex = BuildHeaderIndex(headerCells);
            int count = 0;

            foreach (var field in (ScheduleField[])Enum.GetValues(typeof(ScheduleField)))
            {
                if (!string.IsNullOrWhiteSpace(FindFieldColumn(headerIndex, field)))
                    count++;
            }

            // A row with just an airline and a station is not a schedule header.
            bool hasTime =
                !string.IsNullOrWhiteSpace(FindFieldColumn(headerIndex, ScheduleField.ArrivalTime)) ||
                !string.IsNullOrWhiteSpace(FindFieldColumn(headerIndex, ScheduleField.DepartureTime));

            return hasTime ? count : 0;
        }

        public string this[ScheduleField field] =>
            _columns.TryGetValue(field, out var column) ? column : string.Empty;

        public Dictionary<string, string> ToDisplayMap()
        {
            return _columns.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, ScheduleField> BuildFieldKeys()
        {
            var result = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in (ScheduleField[])Enum.GetValues(typeof(ScheduleField)))
                result[NormaliseKey(field.ToString())] = field;

            // Friendly aliases accepted in the optional overrides input.
            result[NormaliseKey("Airline Arrival")] = ScheduleField.ArrivalAirline;
            result[NormaliseKey("Airline Departure")] = ScheduleField.DepartureAirline;
            result[NormaliseKey("PLF Arrival")] = ScheduleField.ArrivalLoadFactor;
            result[NormaliseKey("PLF Departure")] = ScheduleField.DepartureLoadFactor;
            result[NormaliseKey("Pax Arrival")] = ScheduleField.ArrivalPax;
            result[NormaliseKey("Pax Departure")] = ScheduleField.DeparturePax;
            result[NormaliseKey("Sector Arrival")] = ScheduleField.ArrivalSector;
            result[NormaliseKey("Sector Departure")] = ScheduleField.DepartureSector;

            return result;
        }

        private static Dictionary<string, List<HeaderBinding>> BuildHeaderIndex(
            Dictionary<string, XlsxCell> headerCells)
        {
            var result = new Dictionary<string, List<HeaderBinding>>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in headerCells)
            {
                var header = NormaliseHeader(pair.Value.Value);

                if (string.IsNullOrWhiteSpace(header))
                    continue;

                if (!result.TryGetValue(header, out var bindings))
                {
                    bindings = new List<HeaderBinding>();
                    result[header] = bindings;
                }

                bindings.Add(new HeaderBinding(pair.Key, XlsxWorkbook.ColumnIndex(pair.Key)));
            }

            foreach (var bindings in result.Values)
                bindings.Sort((a, b) => a.Index.CompareTo(b.Index));

            return result;
        }

        private static string FindFieldColumn(
            Dictionary<string, List<HeaderBinding>> headerIndex,
            ScheduleField field)
        {
            if (!Aliases.TryGetValue(field, out var aliases))
                return string.Empty;

            foreach (var alias in aliases)
            {
                if (!headerIndex.TryGetValue(NormaliseHeader(alias), out var bindings) ||
                    bindings.Count == 0)
                {
                    continue;
                }

                // Repeated headers such as Pax, PLF and Domestic/International
                // are arrival columns first and departure columns last.
                return ArrivalFirstFields.Contains(field)
                    ? bindings[0].Column
                    : bindings[bindings.Count - 1].Column;
            }

            return string.Empty;
        }

        private static void ApplyOverrides(
            Dictionary<ScheduleField, string> columns,
            Dictionary<string, List<HeaderBinding>> headerIndex,
            Dictionary<string, XlsxCell> headerCells,
            ScheduleImportOptions options,
            List<LegIssue> globalIssues)
        {
            if (options.ColumnOverrides == null)
                return;

            foreach (var pair in options.ColumnOverrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                if (!FieldKeys.TryGetValue(NormaliseKey(pair.Key), out var field))
                {
                    globalIssues.Add(new LegIssue
                    {
                        Field = "Column Override",
                        Message = $"Unknown field '{pair.Key}' in the column overrides.",
                        Severity = "Warning"
                    });
                    continue;
                }

                var column = ResolveOverrideColumn(
                    pair.Value,
                    field,
                    headerIndex,
                    headerCells);

                if (string.IsNullOrWhiteSpace(column))
                {
                    globalIssues.Add(new LegIssue
                    {
                        Field = "Column Override",
                        Message = $"Could not resolve '{pair.Value}' for field '{field}'.",
                        Severity = "Error"
                    });
                    continue;
                }

                columns[field] = column;
            }
        }

        private static string ResolveOverrideColumn(
            string overrideValue,
            ScheduleField field,
            Dictionary<string, List<HeaderBinding>> headerIndex,
            Dictionary<string, XlsxCell> headerCells)
        {
            var value = (overrideValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var header = NormaliseHeader(value);

            if (headerIndex.TryGetValue(header, out var bindings) && bindings.Count > 0)
            {
                return ArrivalFirstFields.Contains(field)
                    ? bindings[0].Column
                    : bindings[bindings.Count - 1].Column;
            }

            // An override may be a physical Excel column, for example "L" or
            // "AA". This is checked after header names so a header literally
            // called "Pax" is not mistaken for column PAX.
            if (value.Length <= 3 && value.All(char.IsLetter))
                return value.ToUpperInvariant();

            // Permit exact physical columns even when the heading is blank.
            var upper = value.ToUpperInvariant();
            return headerCells.ContainsKey(upper) ? upper : string.Empty;
        }

        private static void AddMissingFieldIssues(
            Dictionary<ScheduleField, string> columns,
            List<LegIssue> globalIssues)
        {
            bool hasArrivalTime = columns.ContainsKey(ScheduleField.ArrivalTime);
            bool hasDepartureTime = columns.ContainsKey(ScheduleField.DepartureTime);

            if (!hasArrivalTime && !hasDepartureTime)
            {
                globalIssues.Add(new LegIssue
                {
                    Field = "Arrival Time / Departure Time",
                    Message = "No arrival or departure time column was recognised. No leg can be placed on a timeline.",
                    Severity = "Error"
                });
            }
            else
            {
                if (!hasArrivalTime)
                {
                    globalIssues.Add(new LegIssue
                    {
                        Field = "Arrival Time",
                        Message = "No arrival time column was recognised.",
                        Severity = "Warning"
                    });
                }

                if (!hasDepartureTime)
                {
                    globalIssues.Add(new LegIssue
                    {
                        Field = "Departure Time",
                        Message = "No departure time column was recognised.",
                        Severity = "Warning"
                    });
                }
            }

            AddMissing(columns, globalIssues, "Airline",
                ScheduleField.ArrivalAirline, ScheduleField.DepartureAirline);
            AddMissing(columns, globalIssues, "Station",
                ScheduleField.ArrivalStation, ScheduleField.DepartureStation);
            AddMissing(columns, globalIssues, "Passenger Count",
                ScheduleField.ArrivalPax, ScheduleField.DeparturePax);
            AddMissing(columns, globalIssues, "Sector",
                ScheduleField.ArrivalSector, ScheduleField.DepartureSector);
            AddMissing(columns, globalIssues, "Service Type",
                ScheduleField.ArrivalServiceType, ScheduleField.DepartureServiceType);
            AddMissing(columns, globalIssues, "Terminal",
                ScheduleField.ArrivalTerminal, ScheduleField.DepartureTerminal);
            AddMissing(columns, globalIssues, "Seat Count",
                ScheduleField.Seats);
            AddMissing(columns, globalIssues, "Aircraft Type",
                ScheduleField.AircraftType);
        }

        private static void AddMissing(
            Dictionary<ScheduleField, string> columns,
            List<LegIssue> globalIssues,
            string label,
            params ScheduleField[] fields)
        {
            if (fields.Any(columns.ContainsKey))
                return;

            globalIssues.Add(new LegIssue
            {
                Field = label,
                Message = $"No {label.ToLowerInvariant()} column was recognised.",
                Severity = "Warning"
            });
        }

        private static string NormaliseHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return string.Empty;

            var characters = header
                .Replace('\u00A0', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .ToCharArray();

            var result = new System.Text.StringBuilder(characters.Length);
            bool previousWasSpace = false;

            foreach (var character in characters)
            {
                bool isSpace = char.IsWhiteSpace(character);

                if (isSpace)
                {
                    if (!previousWasSpace)
                        result.Append(' ');

                    previousWasSpace = true;
                }
                else
                {
                    result.Append(character);
                    previousWasSpace = false;
                }
            }

            return result.ToString().Trim();
        }

        private static string NormaliseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private sealed class HeaderBinding
        {
            public HeaderBinding(string column, int index)
            {
                Column = column;
                Index = index;
            }

            public string Column { get; }

            public int Index { get; }
        }
    }
}
