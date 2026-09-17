using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class CsvGenerationReader : IGenerationReader
    {
        private readonly string _devicesPath;
        private readonly string _modulesPath;
        private readonly string _subnetsPath;
        private readonly string _interfacesPath;

        public CsvGenerationReader(string devicesPath, string modulesPath, string subnetsPath, string interfacesPath)
        {
            _devicesPath = devicesPath;
            _modulesPath = modulesPath;
            _subnetsPath = subnetsPath;
            _interfacesPath = interfacesPath;
        }

        public GenerationParseResult Read()
        {
            var result = new GenerationParseResult();

            result.Document.Devices = ReadDevices(_devicesPath, result.Issues);
            result.Document.Modules = ReadModules(_modulesPath, result.Issues);
            result.Document.Subnets = ReadSubnets(_subnetsPath, result.Issues);
            result.Document.Interfaces = ReadInterfaces(_interfacesPath, result.Issues);

            return result;
        }

        private static List<DeviceSpec> ReadDevices(string path, List<ParseIssue> issues)
        {
            var list = new List<DeviceSpec>();
            using (var csv = Open(path, issues))
            {
                if (csv == null) return list;
                string source = Path.GetFileName(path);
                var header = StartSection(csv, source, GenerationColumns.DevicesColumns, issues);
                if (header == null) return list;

                while (csv.Read())
                {
                    int row = csv.Parser.Row;
                    list.Add(GenerationRowParsing.ParseDeviceRow(header, SafeGetter(csv), source, row, issues.Add));
                }
            }
            return list;
        }

        private static List<ModuleSpec> ReadModules(string path, List<ParseIssue> issues)
        {
            var list = new List<ModuleSpec>();
            using (var csv = Open(path, issues))
            {
                if (csv == null) return list;
                string source = Path.GetFileName(path);
                var header = StartSection(csv, source, GenerationColumns.ModulesColumns, issues);
                if (header == null) return list;

                while (csv.Read())
                {
                    int row = csv.Parser.Row;
                    list.Add(GenerationRowParsing.ParseModuleRow(header, SafeGetter(csv), source, row, issues.Add));
                }
            }
            return list;
        }

        private static List<SubnetSpec> ReadSubnets(string path, List<ParseIssue> issues)
        {
            var list = new List<SubnetSpec>();
            using (var csv = Open(path, issues))
            {
                if (csv == null) return list;
                string source = Path.GetFileName(path);
                var header = StartSection(csv, source, GenerationColumns.SubnetsColumns, issues);
                if (header == null) return list;

                while (csv.Read())
                {
                    int row = csv.Parser.Row;
                    list.Add(GenerationRowParsing.ParseSubnetRow(header, SafeGetter(csv), source, row, issues.Add));
                }
            }
            return list;
        }

        private static List<InterfaceSpec> ReadInterfaces(string path, List<ParseIssue> issues)
        {
            var list = new List<InterfaceSpec>();
            using (var csv = Open(path, issues))
            {
                if (csv == null) return list;
                string source = Path.GetFileName(path);
                var header = StartSection(csv, source, GenerationColumns.InterfacesColumns, issues);
                if (header == null) return list;

                while (csv.Read())
                {
                    int row = csv.Parser.Row;
                    list.Add(GenerationRowParsing.ParseInterfaceRow(header, SafeGetter(csv), source, row, issues.Add));
                }
            }
            return list;
        }

        private static CsvReader? Open(string path, List<ParseIssue> issues)
        {
            if (!File.Exists(path))
            {
                issues.Add(new ParseIssue { Source = Path.GetFileName(path), Message = "Fitxer no trobat." });
                return null;
            }

            var streamReader = new StreamReader(path, Encoding.UTF8, true);
            return new CsvReader(streamReader, CreateConfig());
        }

        private static string[]? StartSection(CsvReader csv, string source, ICollection<string> requiredColumns, List<ParseIssue> issues)
        {
            if (!csv.Read() || !csv.ReadHeader())
                return GenerationRowParsing.ValidateHeader(null, source, requiredColumns, issues.Add);

            return GenerationRowParsing.ValidateHeader(csv.HeaderRecord, source, requiredColumns, issues.Add);
        }

        private static Func<int, string?> SafeGetter(CsvReader csv)
        {
            return index =>
            {
                try { return csv.GetField(index); }
                catch { return null; }
            };
        }

        private static CsvConfiguration CreateConfig()
        {
            return new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim,
            };
        }
    }
}