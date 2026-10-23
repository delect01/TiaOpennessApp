using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using TiaOpennessApp.Models;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class CatalogService
    {
        public const string DefaultTiaVersion = "21";

        public static List<CatalogEntry> Load(string path)
        {
            if (!File.Exists(path))
                return new List<CatalogEntry>();

            var config = CreateConfig();
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, config);
            csv.Context.TypeConverterCache.AddConverter(typeof(CatalogItemRole), new CatalogItemRoleConverter());
            csv.Context.RegisterClassMap<CatalogEntryMap>();
            return csv.GetRecords<CatalogEntry>().ToList();
        }

        public static void Save(string path, List<CatalogEntry> entries)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var config = CreateConfig();
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            using var csv = new CsvWriter(writer, config);
            csv.Context.TypeConverterCache.AddConverter(typeof(CatalogItemRole), new CatalogItemRoleConverter());
            csv.Context.RegisterClassMap<CatalogEntryMap>();
            csv.WriteRecords(entries);
        }

        public static List<CatalogEntry> Merge(List<CatalogEntry> existing, List<CatalogEntry> candidates)
        {
            var merged = new List<CatalogEntry>(existing);
            var keys = new HashSet<string>(existing.Select(Key));
            foreach (var candidate in candidates)
            {
                if (keys.Add(Key(candidate)))
                    merged.Add(candidate);
            }
            return merged;
        }

        public static List<CatalogEntry> GenerateCandidates(List<NetworkDeviceInfo> devices)
        {
            var result = new List<CatalogEntry>();
            var seen = new HashSet<string>();

            foreach (var device in devices)
            {
                var modules = device.Modules ?? new List<NetworkModuleInfo>();
                var root = FindRoot(device, modules);

                if (root != null)
                    AddCandidate(result, seen, root.OrderNumber, root.TypeName, CatalogItemRole.Root, root.TypeIdentifier);

                foreach (var module in modules)
                    AddCandidate(result, seen, module.OrderNumber, module.TypeName, CatalogItemRole.Module, module.TypeIdentifier);
            }

            return result;
        }

        internal static NetworkModuleInfo? FindRoot(NetworkDeviceInfo device, List<NetworkModuleInfo> modules)
        {
            NetworkModuleInfo? root = modules.FirstOrDefault(m =>
                (m.TypeName ?? string.Empty).StartsWith("CPU", StringComparison.OrdinalIgnoreCase));

            if (root != null)
                return root;

            root = modules.FirstOrDefault(m =>
                (m.TypeName ?? string.Empty).StartsWith("IM ", StringComparison.OrdinalIgnoreCase)
                || (m.TypeName ?? string.Empty).IndexOf("Interface Module", StringComparison.OrdinalIgnoreCase) >= 0);

            if (root != null)
                return root;

            // Third-party GSD stations: the device order number is usually a wildcard
            // placeholder (182xxxxx) while the real identity lives in TypeIdentifier.
            root = modules.FirstOrDefault(m =>
                IsGsdIdentifier(m.TypeIdentifier)
                && m.TypeIdentifier.EndsWith("/D", StringComparison.OrdinalIgnoreCase));

            if (root != null)
                return root;

            root = modules.FirstOrDefault(m => IsGsdIdentifier(m.TypeIdentifier));
            if (root != null)
                return root;

            string deviceOrder = device.DeviceOrderNumber ?? string.Empty;
            if (!IsWildcard(deviceOrder))
                return new NetworkModuleInfo { OrderNumber = deviceOrder, TypeName = device.StationType };

            return modules.FirstOrDefault(m => !IsWildcard(m.OrderNumber));
        }

        private static void AddCandidate(
            List<CatalogEntry> result,
            HashSet<string> seen,
            string orderNumber,
            string typeName,
            CatalogItemRole role,
            string? typeIdentifier)
        {
            string identifier = (typeIdentifier ?? string.Empty).Trim();
            if (IsWildcard(orderNumber) && identifier.Length == 0)
                return;

            string mlfb = NormalizeMlfb(orderNumber);
            if (mlfb.Length == 0 && identifier.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase))
            {
                string fromIdentifier = identifier.Substring("OrderNumber:".Length);
                int versionIndex = fromIdentifier.IndexOf('/');
                if (versionIndex >= 0)
                    fromIdentifier = fromIdentifier.Substring(0, versionIndex);
                mlfb = NormalizeMlfb(fromIdentifier);
            }

            string typeKey = ToTypeKey(typeName);
            if (typeKey.Length == 0)
                return;

            var entry = new CatalogEntry
            {
                TypeKey = typeKey,
                Mlfb = mlfb,
                TiaVersion = DefaultTiaVersion,
                Role = role,
                Identifier = identifier,
            };

            if (seen.Add(Key(entry)))
                result.Add(entry);
        }

        private static string Key(CatalogEntry entry)
        {
            return entry.TypeKey + "|" + entry.TiaVersion;
        }

        internal static bool IsWildcard(string? orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return true;
            string value = orderNumber!;
            if (value.IndexOf('*') >= 0)
                return true;
            return value.IndexOf('x') >= 0 || value.IndexOf('X') >= 0;
        }

        internal static bool IsGsdIdentifier(string? typeIdentifier)
        {
            return !string.IsNullOrWhiteSpace(typeIdentifier)
                && typeIdentifier!.StartsWith("GSD:", StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeMlfb(string orderNumber)
        {
            return orderNumber.Replace(" ", string.Empty).Trim();
        }

        internal static string ToTypeKey(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return string.Empty;

            string raw = typeName!;
            string normalized = raw.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(c) || c == '-')
                    builder.Append(char.ToUpperInvariant(c));
            }
            return builder.ToString();
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

        private sealed class CatalogEntryMap : ClassMap<CatalogEntry>
        {
            public CatalogEntryMap()
            {
                Map(m => m.TypeKey).Name("TypeKey");
                Map(m => m.Mlfb).Name("MLFB");
                Map(m => m.TiaVersion).Name("TiaVersion");
                Map(m => m.Description).Name("Description");
                Map(m => m.Role).Name("Role");
                Map(m => m.Identifier).Name("Identifier");
            }
        }

        private sealed class CatalogItemRoleConverter : DefaultTypeConverter
        {
            public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return CatalogItemRole.Module;
                if (Enum.TryParse<CatalogItemRole>(text, ignoreCase: true, out var role))
                    return role;
                return CatalogItemRole.Module;
            }

            public override string? ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
            {
                return value?.ToString();
            }
        }
    }
}