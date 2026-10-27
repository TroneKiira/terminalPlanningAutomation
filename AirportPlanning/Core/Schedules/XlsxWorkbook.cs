using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>
    /// Dependency-free reader for the cached values in an XLSX workbook.
    ///
    /// Only values stored in the file are read. Formulas are never evaluated;
    /// callers can see whether a formula cell actually has a cached result.
    /// </summary>
    internal sealed class XlsxWorkbook
    {
        private static readonly XNamespace MainNs =
            XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        private static readonly XNamespace RelNs =
            XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        private static readonly XNamespace PackageRelNs =
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        private XlsxWorkbook()
        {
            Sheets = new List<XlsxSheet>();
            SharedStrings = new List<string>();
        }

        public List<XlsxSheet> Sheets { get; }

        public List<string> SharedStrings { get; }

        public static XlsxWorkbook Load(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                throw new ArgumentException("Flight schedule file is empty.", nameof(fileBytes));

            using (var archive = new ZipArchive(
                new MemoryStream(fileBytes),
                ZipArchiveMode.Read,
                leaveOpen: false))
            {
                var result = new XlsxWorkbook();
                result.SharedStrings.AddRange(LoadSharedStrings(archive));

                var workbookEntry = archive.GetEntry("xl/workbook.xml") ??
                    throw new InvalidDataException(
                        "Not a valid XLSX workbook: xl/workbook.xml is missing.");

                XDocument workbook;

                using (var stream = workbookEntry.Open())
                    workbook = XDocument.Load(stream);

                var relationships = LoadRelationships(archive);

                foreach (var sheetElement in workbook.Descendants(MainNs + "sheet"))
                {
                    var name = sheetElement.Attribute("name")?.Value ?? string.Empty;
                    var relationshipId = sheetElement.Attribute(RelNs + "id")?.Value;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relationshipId))
                        continue;

                    if (!relationships.TryGetValue(relationshipId, out var target))
                        continue;

                    var sheetEntry = archive.GetEntry(target);
                    if (sheetEntry == null)
                        continue;

                    XDocument sheetDocument;

                    using (var stream = sheetEntry.Open())
                        sheetDocument = XDocument.Load(stream);

                    var rows = ReadRows(sheetDocument, result.SharedStrings);

                    result.Sheets.Add(new XlsxSheet(name, rows));
                }

                if (result.Sheets.Count == 0)
                    throw new InvalidDataException("No worksheets were found in the workbook.");

                return result;
            }
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return new List<string>();

            using (var stream = entry.Open())
            {
                var document = XDocument.Load(stream);

                return document
                    .Descendants(MainNs + "si")
                    .Select(si => string.Concat(
                        si.Descendants(MainNs + "t").Select(t => t.Value)))
                    .ToList();
            }
        }

        private static Dictionary<string, string> LoadRelationships(ZipArchive archive)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var entry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (entry == null)
                return result;

            XDocument document;

            using (var stream = entry.Open())
                document = XDocument.Load(stream);

            foreach (var relationship in document.Descendants(PackageRelNs + "Relationship"))
            {
                var id = relationship.Attribute("Id")?.Value;
                var target = relationship.Attribute("Target")?.Value;

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target))
                    continue;

                var type = relationship.Attribute("Type")?.Value ?? string.Empty;
                if (!type.EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase))
                    continue;

                result[id] = ResolvePartPath(target);
            }

            return result;
        }

        private static string ResolvePartPath(string target)
        {
            var normalized = target.Replace('\\', '/').Trim();

            if (normalized.StartsWith("/", StringComparison.Ordinal))
                normalized = normalized.TrimStart('/');
            else if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                normalized = "xl/" + normalized;

            var parts = new List<string>();

            foreach (var part in normalized.Split('/'))
            {
                if (string.IsNullOrEmpty(part) || part == ".")
                    continue;

                if (part == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);

                    continue;
                }

                parts.Add(part);
            }

            return string.Join("/", parts);
        }

        private static List<XlsxRow> ReadRows(XDocument sheetDocument, List<string> sharedStrings)
        {
            var root = sheetDocument.Root;
            if (root == null)
                return new List<XlsxRow>();

            var rows = new List<XlsxRow>();
            int previousRowNumber = 0;

            foreach (var rowElement in root.Descendants(MainNs + "row"))
            {
                var rowNumber = ParseRowNumber(rowElement.Attribute("r")?.Value);

                if (rowNumber <= 0)
                    rowNumber = previousRowNumber + 1;

                previousRowNumber = rowNumber;

                var cells = new Dictionary<string, XlsxCell>(StringComparer.OrdinalIgnoreCase);
                int previousColumn = 0;

                foreach (var cellElement in rowElement.Elements(MainNs + "c"))
                {
                    var reference = cellElement.Attribute("r")?.Value ?? string.Empty;
                    var column = CellReferenceColumn(reference);

                    if (string.IsNullOrWhiteSpace(column))
                        column = ColumnName(previousColumn + 1);

                    previousColumn = ColumnIndex(column);

                    var cell = ReadCell(cellElement, column, sharedStrings);
                    cells[column] = cell;
                }

                rows.Add(new XlsxRow(rowNumber, cells));
            }

            return rows;
        }

        private static XlsxCell ReadCell(
            XElement cellElement,
            string column,
            List<string> sharedStrings)
        {
            var cellType = cellElement.Attribute("t")?.Value ?? string.Empty;
            var formulaElement = cellElement.Element(MainNs + "f");
            var valueElement = cellElement.Element(MainNs + "v");
            var rawValue = valueElement?.Value ?? string.Empty;
            var isFormula = formulaElement != null;

            // A formula cell is only resolved when it carries an actual cached
            // result. Excel writes <v/> for a cached empty string, which is not
            // useful for a terminal or a scheduled time.
            var hasCachedValue = valueElement != null && !string.IsNullOrWhiteSpace(rawValue);
            var value = rawValue;
            var isError = string.Equals(cellType, "e", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < sharedStrings.Count)
                {
                    value = sharedStrings[index];
                }
            }
            else if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                value = string.Concat(
                    cellElement
                        .Descendants(MainNs + "t")
                        .Select(t => t.Value));
            }
            else if (string.Equals(cellType, "b", StringComparison.OrdinalIgnoreCase))
            {
                value = rawValue == "1" ? "TRUE" : "FALSE";
            }
            else if (!isError && value.StartsWith("#", StringComparison.Ordinal))
            {
                isError = true;
            }

            return new XlsxCell(
                column,
                value,
                rawValue,
                isFormula,
                hasCachedValue,
                isError);
        }

        public static string CellReferenceColumn(string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return string.Empty;

            return new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        }

        public static int ParseRowNumber(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return 0;

            var letters = reference.TakeWhile(char.IsLetter).Count();
            var numberPart = reference.Substring(letters);
            return int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
        }

        public static int ColumnIndex(string column)
        {
            if (string.IsNullOrWhiteSpace(column))
                return 0;

            var result = 0;

            foreach (var character in column.ToUpperInvariant())
            {
                if (character < 'A' || character > 'Z')
                    return 0;

                result = result * 26 + (character - 'A' + 1);
            }

            return result;
        }

        public static string ColumnName(int oneBasedIndex)
        {
            if (oneBasedIndex <= 0)
                return string.Empty;

            var result = string.Empty;
            var value = oneBasedIndex;

            while (value > 0)
            {
                value--;
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }

            return result;
        }
    }

    internal sealed class XlsxSheet
    {
        public XlsxSheet(string name, List<XlsxRow> rows)
        {
            Name = name;
            Rows = rows;
        }

        public string Name { get; }

        public List<XlsxRow> Rows { get; }
    }

    internal sealed class XlsxRow
    {
        public XlsxRow(int number, Dictionary<string, XlsxCell> cells)
        {
            Number = number;
            Cells = cells;
        }

        public int Number { get; }

        public Dictionary<string, XlsxCell> Cells { get; }

        public bool IsEmpty => Cells.Values.All(cell => cell.IsBlank && !cell.IsFormula);
    }

    internal readonly struct XlsxCell
    {
        public XlsxCell(
            string column,
            string value,
            string rawValue,
            bool isFormula,
            bool hasCachedValue,
            bool isError)
        {
            Column = column;
            Value = value ?? string.Empty;
            RawValue = rawValue ?? string.Empty;
            IsFormula = isFormula;
            HasCachedValue = hasCachedValue;
            IsError = isError;
        }

        public string Column { get; }

        public string Value { get; }

        public string RawValue { get; }

        public bool IsFormula { get; }

        public bool HasCachedValue { get; }

        public bool IsError { get; }

        public bool IsBlank => string.IsNullOrWhiteSpace(Value);

        /// <summary>
        /// A formula with no cached value has no usable result, even though the
        /// cell itself is not empty.
        /// </summary>
        public bool IsUncachedFormula => IsFormula && !HasCachedValue;
    }
}
