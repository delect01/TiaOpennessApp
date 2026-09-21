using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class ExcelTemplateGenerator
    {
        private const int DataRows = 500;
        private const string TypeKeyRangeName = "_TypeKeys";
        private const string SubnetTypeRangeName = "_SubnetTypes";
        private readonly List<string> _typeKeys;

        public ExcelTemplateGenerator(string catalogPath)
        {
            _typeKeys = CatalogService.Load(catalogPath)
                .Select(e => e.TypeKey)
                .Distinct()
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void SaveTemplate(string path)
        {
            Save(path, new GenerationDocument());
        }

        public void SaveDocument(string path, GenerationDocument document)
        {
            Save(path, document);
        }

        private void Save(string path, GenerationDocument document)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var workbook = new XLWorkbook())
            {
                var helper = workbook.AddWorksheet("_Lists");
                helper.Visibility = XLWorksheetVisibility.Hidden;
                WriteHelperList(helper, 1, _typeKeys, TypeKeyRangeName);
                WriteHelperList(helper, 3, new List<string> { "PN/IE", "PROFIBUS" }, SubnetTypeRangeName);

                WriteDevices(workbook.AddWorksheet(GenerationColumns.Devices), document.Devices);
                WriteModules(workbook.AddWorksheet(GenerationColumns.Modules), document.Modules);
                WriteSubnets(workbook.AddWorksheet(GenerationColumns.Subnets), document.Subnets);
                WriteInterfaces(workbook.AddWorksheet(GenerationColumns.Interfaces), document.Interfaces);
                WriteIoTags(workbook.AddWorksheet(GenerationColumns.IoTags), document.IoTags);
                WriteModuleAddresses(workbook.AddWorksheet(GenerationColumns.ModuleAddresses), document.ModuleAddresses);
                workbook.SaveAs(path);
            }
        }

        private static void WriteHelperList(IXLWorksheet ws, int column, List<string> values, string rangeName)
        {
            for (int i = 0; i < values.Count; i++)
                ws.Cell(i + 1, column).Value = values[i];

            var range = ws.Range(1, column, values.Count, column);
            ws.Workbook.DefinedNames.Add(rangeName, range);
        }

        private void WriteDevices(IXLWorksheet ws, List<DeviceSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.DevicesColumns);
            SetWidths(ws, new[] { 18, 12, 18, 10, 24 });
            AddListValidation(ws, Column(GenerationColumns.DevicesColumns, GenerationColumns.TypeKey), TypeKeyRangeName);

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.DevicesColumns, GenerationColumns.DeviceName)).Value = rows[i].DeviceName;
                ws.Cell(r, Column(GenerationColumns.DevicesColumns, GenerationColumns.Group)).Value = rows[i].Group;
                ws.Cell(r, Column(GenerationColumns.DevicesColumns, GenerationColumns.TypeKey)).Value = rows[i].TypeKey;
                ws.Cell(r, Column(GenerationColumns.DevicesColumns, GenerationColumns.Firmware)).Value = rows[i].Firmware ?? string.Empty;
                ws.Cell(r, Column(GenerationColumns.DevicesColumns, GenerationColumns.Comment)).Value = rows[i].Comment ?? string.Empty;
            }
        }

        private void WriteModules(IXLWorksheet ws, List<ModuleSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.ModulesColumns);
            SetWidths(ws, new[] { 12, 20, 8, 18, 24 });
            AddListValidation(ws, Column(GenerationColumns.ModulesColumns, GenerationColumns.TypeKey), TypeKeyRangeName);

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.ModulesColumns, GenerationColumns.Device)).Value = rows[i].Device;
                ws.Cell(r, Column(GenerationColumns.ModulesColumns, GenerationColumns.ModuleName)).Value = rows[i].ModuleName;
                int? slot = rows[i].Slot;
                if (slot.HasValue)
                    ws.Cell(r, Column(GenerationColumns.ModulesColumns, GenerationColumns.Slot)).Value = slot.Value;
                ws.Cell(r, Column(GenerationColumns.ModulesColumns, GenerationColumns.TypeKey)).Value = rows[i].TypeKey;
                ws.Cell(r, Column(GenerationColumns.ModulesColumns, GenerationColumns.Comment)).Value = rows[i].Comment ?? string.Empty;
            }
        }

        private void WriteSubnets(IXLWorksheet ws, List<SubnetSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.SubnetsColumns);
            SetWidths(ws, new[] { 20, 14 });
            AddListValidation(ws, Column(GenerationColumns.SubnetsColumns, GenerationColumns.SubnetType), SubnetTypeRangeName);

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.SubnetsColumns, GenerationColumns.Name)).Value = rows[i].Name;
                ws.Cell(r, Column(GenerationColumns.SubnetsColumns, GenerationColumns.SubnetType)).Value = rows[i].SubnetType;
            }
        }

        private void WriteInterfaces(IXLWorksheet ws, List<InterfaceSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.InterfacesColumns);
            SetWidths(ws, new[] { 12, 22, 14, 16, 16, 20, 16 });

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.Device)).Value = rows[i].Device;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.InterfaceItem)).Value = rows[i].InterfaceItem;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.Subnet)).Value = rows[i].Subnet;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.IpAddress)).Value = rows[i].IpAddress;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.SubnetMask)).Value = rows[i].SubnetMask;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.PnDeviceName)).Value = rows[i].PnDeviceName;
                ws.Cell(r, Column(GenerationColumns.InterfacesColumns, GenerationColumns.MasterIoSystem)).Value = rows[i].MasterIoSystem;
            }
        }

        private void WriteIoTags(IXLWorksheet ws, List<IoTagSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.IoTagsColumns);
            SetWidths(ws, new[] { 12, 12, 22, 22, 16, 16, 10, 10, 24 });

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.Device)).Value = rows[i].Device;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.IoDevice)).Value = rows[i].IoDevice;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.ModuleName)).Value = rows[i].ModuleName;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.SubModuleName)).Value = rows[i].SubModuleName;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.TagTable)).Value = rows[i].TagTable;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.Name)).Value = rows[i].Name;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.Type)).Value = rows[i].Type;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.Address)).Value = rows[i].Address;
                ws.Cell(r, Column(GenerationColumns.IoTagsColumns, GenerationColumns.Comment)).Value = rows[i].Comment;
            }
        }

        private void WriteModuleAddresses(IXLWorksheet ws, List<ModuleAddressSpec> rows)
        {
            WriteHeader(ws, GenerationColumns.ModuleAddressesColumns);
            SetWidths(ws, new[] { 12, 22, 22, 24, 8, 14, 8 });

            for (int i = 0; i < rows.Count; i++)
            {
                int r = 2 + i;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.Device)).Value = rows[i].Device;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.ModuleName)).Value = rows[i].ModuleName;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.SubModuleName)).Value = rows[i].SubModuleName;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.TypeIdentifier)).Value = rows[i].TypeIdentifier;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.IoType)).Value = rows[i].IoType;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.StartAddress)).Value = rows[i].StartAddress;
                ws.Cell(r, Column(GenerationColumns.ModuleAddressesColumns, GenerationColumns.Length)).Value = rows[i].Length;
            }
        }

        private static int Column(string[] columns, string name)
        {
            return Array.IndexOf(columns, name) + 1;
        }

        private static void WriteHeader(IXLWorksheet ws, string[] columns)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = columns[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
            }
            ws.SheetView.FreezeRows(1);
        }

        private static void SetWidths(IXLWorksheet ws, int[] widths)
        {
            for (int i = 0; i < widths.Length; i++)
                ws.Column(i + 1).Width = widths[i];
        }

        private static void AddListValidation(IXLWorksheet ws, int column, string rangeName)
        {
            var validation = ws.Range(2, column, DataRows, column).CreateDataValidation();
            validation.InCellDropdown = true;
            validation.List(rangeName);
        }
    }
}