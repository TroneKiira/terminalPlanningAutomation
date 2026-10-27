using System;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Core.Schedules
{
    internal readonly struct ParsedScheduleTime
    {
        public ParsedScheduleTime(bool isValid, int minutes, int dayOffset, string date, string error)
        {
            IsValid = isValid;
            Minutes = minutes;
            DayOffset = dayOffset;
            Date = date ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool IsValid { get; }

        public int Minutes { get; }

        public int DayOffset { get; }

        public string Date { get; }

        public string Error { get; }

        public static ParsedScheduleTime Invalid(string error)
        {
            return new ParsedScheduleTime(false, 0, 0, string.Empty, error);
        }
    }

    /// <summary>
    /// Decodes the time formats that appear in planning schedules without
    /// treating an empty or unreadable cell as midnight.
    /// </summary>
    internal static class ScheduleTimeDecoder
    {
        public static ParsedScheduleTime Decode(XlsxCell cell, string baseDate)
        {
            if (string.IsNullOrWhiteSpace(cell.Column))
                return ParsedScheduleTime.Invalid("The time column is not mapped.");

            if (cell.IsUncachedFormula)
                return ParsedScheduleTime.Invalid("The time cell is a formula with no cached value.");

            if (cell.IsError)
                return ParsedScheduleTime.Invalid(
                    $"The time cell contains an Excel error ({cell.Value.Trim()}).");

            var raw = cell.Value.Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return ParsedScheduleTime.Invalid("The time cell is blank.");

            bool digitsOnly = raw.All(char.IsDigit);

            // A three- or four-digit all-digit cell is read as HHmm. This also
            // catches numeric cells that Excel has stored as 930 rather than
            // as a fraction of a day.
            if (digitsOnly && raw.Length >= 3 && raw.Length <= 4)
            {
                if (TryDecodeClock(raw, out var compactClock))
                    return compactClock;

                return ParsedScheduleTime.Invalid($"'{raw}' is not a valid 24-hour time.");
            }

            if (double.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var numeric))
            {
                return DecodeNumeric(numeric, baseDate);
            }

            if (TryDecodeClock(raw, out var clock))
                return clock;

            // An all-digit value that is not a valid HHmm clock (for example
            // 2360) must not fall through to a loose date parser.
            if (digitsOnly)
                return ParsedScheduleTime.Invalid($"'{raw}' is not a valid 24-hour time.");

            if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dateTime))
            {
                return FromDateTime(dateTime, baseDate);
            }

            return ParsedScheduleTime.Invalid($"'{raw}' is not a recognised time.");
        }

        private static ParsedScheduleTime DecodeNumeric(double numeric, string baseDate)
        {
            if (double.IsNaN(numeric) || double.IsInfinity(numeric) || numeric < 0)
                return ParsedScheduleTime.Invalid("The numeric time is negative or not finite.");

            // Excel stores a pure time as a fraction of a day.
            if (numeric < 1.0)
            {
                int totalMinutes = (int)Math.Round(numeric * 1440.0, MidpointRounding.AwayFromZero);
                return FromMinutes(totalMinutes, baseDate);
            }

            // A value between one and 24 is treated as decimal hours. This is
            // common in hand-built schedules (9.5 means 09:30), but it is not
            // an Excel time fraction.
            if (numeric < 24.0)
            {
                int totalMinutes = (int)Math.Round(numeric * 60.0, MidpointRounding.AwayFromZero);
                return FromMinutes(totalMinutes, baseDate);
            }

            // A real Excel datetime serial. Values below 1000 are far too small
            // to be dates in a modern schedule and are rejected instead of
            // silently becoming 1900 dates.
            if (numeric < 1000.0)
                return ParsedScheduleTime.Invalid("The numeric time is neither a time fraction nor an Excel date.");

            try
            {
                var dateTime = DateTime.FromOADate(numeric);
                return FromDateTime(dateTime, baseDate);
            }
            catch (ArgumentException)
            {
                return ParsedScheduleTime.Invalid("The Excel date serial is out of range.");
            }
        }

        private static bool TryDecodeClock(
            string raw,
            out ParsedScheduleTime result)
        {
            result = default;

            var value = raw.Trim();

            if (value.IndexOf(':') < 0)
            {
                // HHmm / Hmm, for example 0930, 930, 2400.
                if (value.Length >= 3 && value.Length <= 4 && value.All(char.IsDigit))
                {
                    int compact = int.Parse(value, CultureInfo.InvariantCulture);
                    int hours = compact / 100;
                    int minutes = compact % 100;

                    if (minutes < 60 && hours <= 47)
                    {
                        result = FromMinutes(hours * 60 + minutes, string.Empty);
                        return true;
                    }
                }

                return false;
            }

            var parts = value.Split(':');

            if (parts.Length < 2 || parts.Length > 3)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
            {
                return false;
            }

            if (parts.Length == 3 &&
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ignoredSeconds))
            {
                return false;
            }

            if (hour > 47 || minute > 59)
                return false;

            result = FromMinutes(hour * 60 + minute, string.Empty);
            return true;
        }

        private static ParsedScheduleTime FromDateTime(
            DateTime dateTime,
            string baseDate)
        {
            int minutes = dateTime.Hour * 60 + dateTime.Minute;
            int dayOffset = 0;

            if (!string.IsNullOrWhiteSpace(baseDate) &&
                DateTime.TryParse(
                    baseDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var baseDateTime))
            {
                dayOffset = (dateTime.Date - baseDateTime.Date).Days;
            }

            return new ParsedScheduleTime(
                true,
                minutes,
                dayOffset,
                dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                string.Empty);
        }

        private static ParsedScheduleTime FromMinutes(
            int totalMinutes,
            string baseDate)
        {
            if (totalMinutes < 0)
                return ParsedScheduleTime.Invalid("The time is before midnight.");

            int dayOffset = totalMinutes / 1440;
            int minutes = totalMinutes % 1440;

            return new ParsedScheduleTime(
                true,
                minutes,
                dayOffset,
                string.IsNullOrWhiteSpace(baseDate) ? string.Empty : baseDate.Trim(),
                string.Empty);
        }
    }
}
