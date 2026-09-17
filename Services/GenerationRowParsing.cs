using System;
using System.Collections.Generic;
using System.Globalization;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    internal static class GenerationRowParsing
    {
        public static string[]? ValidateHeader(string[]? header, string source, ICollection<string> requiredColumns, Action<ParseIssue> addIssue)
        {
            if (header == null || header.Length == 0)
            {
                addIssue(new ParseIssue { Source = source, Row = 1, Message = "No s'ha detectat cap capçalera." });
                return null;
            }

            foreach (string column in requiredColumns)
            {
                if (Array.IndexOf(header, column) < 0)
                    addIssue(new ParseIssue { Source = source, Row = 1, Message = $"Falta la columna obligatòria '{column}'." });
            }

            return header;
        }

        public static int ColumnIndex(string[] header, string column)
        {
            return Array.IndexOf(header, column);
        }

        public static string? GetString(string[] header, Func<int, string?> getter, string column)
        {
            int index = ColumnIndex(header, column);
            if (index < 0 || index >= header.Length)
                return null;
            string? value = getter(index);
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value!.Trim();
        }

        public static int? ParseSlot(string[] header, Func<int, string?> getter, string source, int row, Action<ParseIssue> addIssue)
        {
            string? value = GetString(header, getter, GenerationColumns.Slot);
            if (value == null)
                return null;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                return slot;

            addIssue(new ParseIssue
            {
                Source = source,
                Row = row,
                Column = ColumnIndex(header, GenerationColumns.Slot) + 1,
                Message = $"El Slot '{value}' no és un enter vàlid.",
            });
            return null;
        }

        public static DeviceSpec ParseDeviceRow(string[] header, Func<int, string?> getter, string source, int row, Action<ParseIssue> addIssue)
        {
            var spec = new DeviceSpec
            {
                DeviceName = GetString(header, getter, GenerationColumns.DeviceName) ?? string.Empty,
                Group = GetString(header, getter, GenerationColumns.Group) ?? string.Empty,
                TypeKey = GetString(header, getter, GenerationColumns.TypeKey) ?? string.Empty,
                Firmware = GetString(header, getter, GenerationColumns.Firmware),
                Comment = GetString(header, getter, GenerationColumns.Comment),
            };

            Require(spec.DeviceName, GenerationColumns.DeviceName, header, source, row, addIssue);
            Require(spec.TypeKey, GenerationColumns.TypeKey, header, source, row, addIssue);
            return spec;
        }

        public static ModuleSpec ParseModuleRow(string[] header, Func<int, string?> getter, string source, int row, Action<ParseIssue> addIssue)
        {
            var spec = new ModuleSpec
            {
                Device = GetString(header, getter, GenerationColumns.Device) ?? string.Empty,
                ModuleName = GetString(header, getter, GenerationColumns.ModuleName) ?? string.Empty,
                Slot = ParseSlot(header, getter, source, row, addIssue),
                TypeKey = GetString(header, getter, GenerationColumns.TypeKey) ?? string.Empty,
                Comment = GetString(header, getter, GenerationColumns.Comment),
            };

            Require(spec.Device, GenerationColumns.Device, header, source, row, addIssue);
            Require(spec.TypeKey, GenerationColumns.TypeKey, header, source, row, addIssue);
            return spec;
        }

        public static SubnetSpec ParseSubnetRow(string[] header, Func<int, string?> getter, string source, int row, Action<ParseIssue> addIssue)
        {
            var spec = new SubnetSpec
            {
                Name = GetString(header, getter, GenerationColumns.Name) ?? string.Empty,
                SubnetType = GetString(header, getter, GenerationColumns.SubnetType) ?? string.Empty,
            };

            Require(spec.Name, GenerationColumns.Name, header, source, row, addIssue);
            Require(spec.SubnetType, GenerationColumns.SubnetType, header, source, row, addIssue);
            return spec;
        }

        public static InterfaceSpec ParseInterfaceRow(string[] header, Func<int, string?> getter, string source, int row, Action<ParseIssue> addIssue)
        {
            var spec = new InterfaceSpec
            {
                Device = GetString(header, getter, GenerationColumns.Device) ?? string.Empty,
                InterfaceItem = GetString(header, getter, GenerationColumns.InterfaceItem) ?? string.Empty,
                Subnet = GetString(header, getter, GenerationColumns.Subnet) ?? string.Empty,
                IpAddress = GetString(header, getter, GenerationColumns.IpAddress) ?? string.Empty,
                SubnetMask = GetString(header, getter, GenerationColumns.SubnetMask) ?? string.Empty,
                PnDeviceName = GetString(header, getter, GenerationColumns.PnDeviceName) ?? string.Empty,
                MasterIoSystem = GetString(header, getter, GenerationColumns.MasterIoSystem) ?? string.Empty,
            };

            Require(spec.Device, GenerationColumns.Device, header, source, row, addIssue);
            Require(spec.InterfaceItem, GenerationColumns.InterfaceItem, header, source, row, addIssue);
            return spec;
        }

        private static void Require(string value, string columnName, string[] header, string source, int row, Action<ParseIssue> addIssue)
        {
            if (value.Length == 0)
                addIssue(new ParseIssue
                {
                    Source = source,
                    Row = row,
                    Column = ColumnIndex(header, columnName) + 1,
                    Message = $"L'atribut '{columnName}' és obligatori.",
                });
        }
    }
}