using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>
    /// Options for parsing a flight-schedule workbook.
    /// </summary>
    public class ScheduleImportOptions
    {
        public string SourceFile { get; set; } = string.Empty;

        public string ScheduleName { get; set; } = string.Empty;

        /// <summary>
        /// Worksheet to read. When empty, the first worksheet containing a
        /// recognisable schedule header is used.
        /// </summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>
        /// Operating date used when the workbook contains only time fractions.
        /// It is recorded on each leg and used to resolve dated serial values.
        /// </summary>
        public string BaseDate { get; set; } = string.Empty;

        /// <summary>
        /// 1-based header row. Zero auto-detects the header in the selected sheet.
        /// </summary>
        public int HeaderRowIndex { get; set; }

        /// <summary>
        /// 1-based first data row. Zero uses the first row below the header.
        /// </summary>
        public int FirstDataRowIndex { get; set; }

        /// <summary>
        /// True when one row contains both the arrival and departure of the same
        /// aircraft turn.
        /// </summary>
        public bool LinkedRows { get; set; } = true;

        /// <summary>
        /// Terminal applied to non-freight legs whose terminal is blank or
        /// cannot be resolved from the workbook. Freight legs use "Cargo".
        /// Leave empty to leave unresolved terminals blank and report them
        /// individually.
        /// </summary>
        public string FallbackTerminal { get; set; } = string.Empty;

        /// <summary>
        /// Optional field-to-column overrides. Keys are canonical field names,
        /// for example "ArrivalPax" or "DepartureTime". Values may be an Excel
        /// column letter ("L") or a header name ("Pax").
        /// </summary>
        public Dictionary<string, string> ColumnOverrides { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
