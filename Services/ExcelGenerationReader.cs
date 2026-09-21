using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class ExcelGenerationReader : IGenerationReader
    {
        private readonly string _path;

        public ExcelGenerationReader(string path)
        {
            _path = path;
        }

        public GenerationParseResult Read()
        {
            var result = new GenerationParseResult();

            if (!File.Exists(_path))
            {
                result.Issues.Add(new ParseIssue { Source = Path.GetFileName(_path), Message = "Fitxer no trobat." });
                return result;
            }

            using (var workbook = new XLWorkbook(_path))
            {
                result.Document.Devices = ReadDevices(workbook, result.Issues);
                result.Document.Modules = ReadModules(workbook, result.Issues);
                result.Document.Subnets = ReadSubnets(workbook, result.Issues);
                result.Document.Interfaces = ReadInterfaces(workbook, result.Issues);
                result.Document.IoTags = ReadIoTags(workbook, result.Issues);
                result.Document.ModuleAddresses = ReadModuleAddresses(workbook, result.Issues);
            }

            return result;
        }

        private static List<DeviceSpec> ReadDevices(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<DeviceSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.Devices, issues);
            if (ws == null) return list;

            string source = GenerationColumns.Devices;
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, GenerationColumns.DevicesColumns, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseDeviceRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static List<ModuleSpec> ReadModules(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<ModuleSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.Modules, issues);
            if (ws == null) return list;

            string source = GenerationColumns.Modules;
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, GenerationColumns.ModulesColumns, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseModuleRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static List<SubnetSpec> ReadSubnets(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<SubnetSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.Subnets, issues);
            if (ws == null) return list;

            string source = GenerationColumns.Subnets;
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, GenerationColumns.SubnetsColumns, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseSubnetRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static List<InterfaceSpec> ReadInterfaces(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<InterfaceSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.Interfaces, issues);
            if (ws == null) return list;

            string source = GenerationColumns.Interfaces;
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, GenerationColumns.InterfacesColumns, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseInterfaceRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static List<IoTagSpec> ReadIoTags(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<IoTagSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.IoTags, issues);
            if (ws == null) return list;

            string source = GenerationColumns.IoTags;
            var required = new[] { GenerationColumns.Device, GenerationColumns.Name, GenerationColumns.Type, GenerationColumns.Address };
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, required, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseTagRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static List<ModuleAddressSpec> ReadModuleAddresses(XLWorkbook workbook, List<ParseIssue> issues)
        {
            var list = new List<ModuleAddressSpec>();
            var ws = GetWorksheet(workbook, GenerationColumns.ModuleAddresses, issues);
            if (ws == null) return list;

            string source = GenerationColumns.ModuleAddresses;
            var required = new[] { GenerationColumns.Device, GenerationColumns.ModuleName, GenerationColumns.IoType };
            var header = GenerationRowParsing.ValidateHeader(ReadHeader(ws), source, required, issues.Add);
            if (header == null) return list;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (RowIsEmpty(ws, row, header.Length))
                    continue;
                list.Add(GenerationRowParsing.ParseModuleAddressRow(header, Getter(ws, row), source, row, issues.Add));
            }
            return list;
        }

        private static IXLWorksheet? GetWorksheet(XLWorkbook workbook, string name, List<ParseIssue> issues)
        {
            if (workbook.Worksheets.TryGetWorksheet(name, out var worksheet))
                return worksheet;
            issues.Add(new ParseIssue { Source = name, Message = "Full de càlcul no trobat." });
            return null;
        }

        private static string[]? ReadHeader(IXLWorksheet ws)
        {
            var lastCell = ws.Row(1).LastCellUsed();
            if (lastCell == null)
                return null;

            int columnCount = lastCell.Address.ColumnNumber;
            var header = new string[columnCount];
            for (int c = 1; c <= columnCount; c++)
                header[c - 1] = ws.Cell(1, c).GetString();
            return header;
        }

        private static bool RowIsEmpty(IXLWorksheet ws, int row, int columns)
        {
            for (int c = 1; c <= columns; c++)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cell(row, c).GetString()))
                    return false;
            }
            return true;
        }

        private static Func<int, string?> Getter(IXLWorksheet ws, int row)
        {
            return index => ws.Cell(row, index + 1).GetString();
        }
    }
}