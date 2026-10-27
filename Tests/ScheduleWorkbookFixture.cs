using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AirportPlanning.Tests
{
    /// <summary>
    /// Creates a minimal but valid XLSX workbook for schedule-import tests.
    /// Tests must not depend on a network drive or a user-specific workbook.
    /// </summary>
    internal sealed class ScheduleFixtureBuilder
    {
        private static readonly XNamespace MainNs =
            XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        private static readonly XNamespace PackageNs =
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        private static readonly XNamespace RelNs =
            XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        private static readonly string[] DefaultHeaders =
        {
            "Airline  (Arrival)",
            "Airline  (Departure)",
            "No. of Seats",
            "Aircraft Type",
            "ICAO Code",
            "Origin",
            "Arrival (via)",
            "Domestic/International",
            "Arrival time",
            "PLF",
            "Pax",
            "Departure (via)",
            "Destination",
            "Domestic/International",
            "Departure time",
            "PLF",
            "Pax",
            "Service Type - Arr",
            "Service Type - Dep",
            "ARR T1/T2",
            "DEP T1/T2",
            "Remarks"
        };

        private readonly string _sheetName;
        private readonly int _headerRow;
        private readonly int _firstHeaderColumn;
        private readonly string[] _headers;
        private readonly List<FixtureRow> _rows = new List<FixtureRow>();
        private readonly List<string> _sharedStrings = new List<string>();
        private readonly Dictionary<string, int> _sharedStringIndices =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public ScheduleFixtureBuilder(
            string sheetName = "Schedule",
            int headerRow = 7,
            int firstHeaderColumn = 2,
            IEnumerable<string> headers = null)
        {
            _sheetName = sheetName;
            _headerRow = headerRow;
            _firstHeaderColumn = firstHeaderColumn;
            _headers = headers?.ToArray() ?? DefaultHeaders;

            var headerCells = new Dictionary<string, FixtureCell>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _headers.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_headers[i]))
                    continue;

                headerCells[ColumnName(_firstHeaderColumn + i)] = FixtureCell.TextCell(_headers[i]);
            }

            AddRow(headerRow, headerCells);
        }

        public void AddRow(int rowNumber, IDictionary<string, FixtureCell> cells)
        {
            _rows.Add(new FixtureRow(
                rowNumber,
                new Dictionary<string, FixtureCell>(cells, StringComparer.OrdinalIgnoreCase)));
        }

        public void AddCell(int rowNumber, string column, FixtureCell cell)
        {
            var row = _rows.FirstOrDefault(r => r.RowNumber == rowNumber);

            if (row == null)
            {
                row = new FixtureRow(
                    rowNumber,
                    new Dictionary<string, FixtureCell>(StringComparer.OrdinalIgnoreCase));
                _rows.Add(row);
            }

            row.Cells[column] = cell;
        }

        public byte[] Build()
        {
            using var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Build the worksheet first: it populates the shared-string
                // table used by xl/sharedStrings.xml.
                var worksheet = BuildWorksheet();
                var sharedStrings = BuildSharedStrings();

                WriteXml(archive, "[Content_Types].xml", BuildContentTypes());
                WriteXml(archive, "_rels/.rels", BuildPackageRelationships());
                WriteXml(archive, "xl/workbook.xml", BuildWorkbook());
                WriteXml(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships());
                WriteXml(archive, "xl/sharedStrings.xml", sharedStrings);
                WriteXml(archive, "xl/worksheets/sheet1.xml", worksheet);
            }

            return stream.ToArray();
        }

        private XDocument BuildContentTypes()
        {
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";

            return new XDocument(
                new XElement(
                    ns + "Types",
                    new XElement(
                        ns + "Default",
                        new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(
                        ns + "Default",
                        new XAttribute("Extension", "xml"),
                        new XAttribute("ContentType", "application/xml")),
                    new XElement(
                        ns + "Override",
                        new XAttribute("PartName", "/xl/workbook.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(
                        ns + "Override",
                        new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                    new XElement(
                        ns + "Override",
                        new XAttribute("PartName", "/xl/sharedStrings.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"))));
        }

        private XDocument BuildPackageRelationships()
        {
            return new XDocument(
                new XElement(
                    PackageNs + "Relationships",
                    new XElement(
                        PackageNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml"))));
        }

        private XDocument BuildWorkbook()
        {
            return new XDocument(
                new XElement(
                    MainNs + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(
                        MainNs + "sheets",
                        new XElement(
                            MainNs + "sheet",
                            new XAttribute("name", _sheetName),
                            new XAttribute("sheetId", "1"),
                            new XAttribute(RelNs + "id", "rId1")))));
        }

        private XDocument BuildWorkbookRelationships()
        {
            return new XDocument(
                new XElement(
                    PackageNs + "Relationships",
                    new XElement(
                        PackageNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml"))));
        }

        private XDocument BuildSharedStrings()
        {
            return new XDocument(
                new XElement(
                    MainNs + "sst",
                    new XAttribute("count", _sharedStrings.Count.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("uniqueCount", _sharedStrings.Count.ToString(CultureInfo.InvariantCulture)),
                    _sharedStrings.Select(value =>
                        new XElement(
                            MainNs + "si",
                            new XElement(
                                MainNs + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"),
                                value)))));
        }

        private XDocument BuildWorksheet()
        {
            var sheetData = new XElement(MainNs + "sheetData");

            foreach (var row in _rows.OrderBy(r => r.RowNumber))
            {
                var rowElement = new XElement(
                    MainNs + "row",
                    new XAttribute("r", row.RowNumber.ToString(CultureInfo.InvariantCulture)));

                foreach (var pair in row.Cells
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .OrderBy(pair => ColumnIndex(pair.Key)))
                {
                    var cell = pair.Value;

                    if (cell.Kind == FixtureCellKind.Blank)
                        continue;

                    var cellElement = new XElement(
                        MainNs + "c",
                        new XAttribute("r", pair.Key.ToUpperInvariant() + row.RowNumber));

                    switch (cell.Kind)
                    {
                        case FixtureCellKind.Text:
                            cellElement.SetAttributeValue("t", "s");
                            cellElement.Add(new XElement(
                                MainNs + "v",
                                SharedStringIndex(cell.Text).ToString(CultureInfo.InvariantCulture)));
                            break;

                        case FixtureCellKind.Number:
                            cellElement.Add(new XElement(
                                MainNs + "v",
                                cell.Number.ToString("R", CultureInfo.InvariantCulture)));
                            break;

                        case FixtureCellKind.Formula:
                            cellElement.SetAttributeValue("t", "str");
                            cellElement.Add(new XElement(MainNs + "f", cell.Formula));

                            if (cell.HasCachedValue)
                            {
                                cellElement.Add(new XElement(MainNs + "v", cell.CachedValue ?? string.Empty));
                            }
                            else if (cell.EmptyCachedValue)
                            {
                                cellElement.Add(new XElement(MainNs + "v"));
                            }

                            break;

                        case FixtureCellKind.Error:
                            cellElement.SetAttributeValue("t", "e");
                            cellElement.Add(new XElement(MainNs + "v", cell.Error ?? "#REF!"));
                            break;
                    }

                    rowElement.Add(cellElement);
                }

                sheetData.Add(rowElement);
            }

            return new XDocument(
                new XElement(
                    MainNs + "worksheet",
                    new XElement(MainNs + "dimension", new XAttribute("ref", "A1")),
                    sheetData));
        }

        private int SharedStringIndex(string value)
        {
            value = value ?? string.Empty;

            if (_sharedStringIndices.TryGetValue(value, out var index))
                return index;

            index = _sharedStrings.Count;
            _sharedStrings.Add(value);
            _sharedStringIndices[value] = index;
            return index;
        }

        private static void WriteXml(ZipArchive archive, string path, XDocument document)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);

            using var stream = entry.Open();
            using var writer = XmlWriter.Create(
                stream,
                new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = false,
                    CloseOutput = false
                });

            document.Save(writer);
        }

        private static int ColumnIndex(string column)
        {
            var index = 0;

            foreach (var character in column.ToUpperInvariant())
                index = index * 26 + (character - 'A' + 1);

            return index;
        }

        private static string ColumnName(int oneBasedIndex)
        {
            var result = string.Empty;
            int value = oneBasedIndex;

            while (value > 0)
            {
                value--;
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }

            return result;
        }

        private sealed class FixtureRow
        {
            public FixtureRow(int rowNumber, Dictionary<string, FixtureCell> cells)
            {
                RowNumber = rowNumber;
                Cells = cells;
            }

            public int RowNumber { get; }

            public Dictionary<string, FixtureCell> Cells { get; }
        }
    }

    internal enum FixtureCellKind
    {
        Blank,
        Text,
        Number,
        Formula,
        Error
    }

    internal readonly struct FixtureCell
    {
        private FixtureCell(
            FixtureCellKind kind,
            string text,
            double number,
            string formula,
            string cachedValue,
            bool hasCachedValue,
            bool emptyCachedValue,
            string error)
        {
            Kind = kind;
            Text = text;
            Number = number;
            Formula = formula;
            CachedValue = cachedValue;
            HasCachedValue = hasCachedValue;
            EmptyCachedValue = emptyCachedValue;
            Error = error;
        }

        public FixtureCellKind Kind { get; }

        public string Text { get; }

        public double Number { get; }

        public string Formula { get; }

        public string CachedValue { get; }

        public bool HasCachedValue { get; }

        public bool EmptyCachedValue { get; }

        public string Error { get; }

        public static FixtureCell Blank => default;

        public static FixtureCell TextCell(string value) =>
            new FixtureCell(FixtureCellKind.Text, value, 0, null, null, false, false, null);

        public static FixtureCell NumberCell(double value) =>
            new FixtureCell(FixtureCellKind.Number, null, value, null, null, false, false, null);

        public static FixtureCell FormulaCell(
            string formula,
            string cachedValue = null,
            bool emptyCachedValue = false) =>
            new FixtureCell(
                FixtureCellKind.Formula,
                null,
                0,
                formula,
                cachedValue,
                cachedValue != null,
                emptyCachedValue,
                null);

        public static FixtureCell ErrorCell(string error) =>
            new FixtureCell(FixtureCellKind.Error, null, 0, null, null, false, false, error);
    }
}
