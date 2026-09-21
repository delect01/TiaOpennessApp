using System;
using System.Collections.Generic;
using System.Linq;
using TiaOpennessApp.Models;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class DocsExportReport
    {
        public int Devices { get; set; }
        public int Modules { get; set; }
        public int Subnets { get; set; }
        public int Interfaces { get; set; }
        public int Tags { get; set; }
        public int ModuleAddresses { get; set; }
        public List<string> TypeKeysResolved = new List<string>();
        public List<string> TypeKeysMissing = new List<string>();
        public string OutputDir { get; set; } = string.Empty;
    }

    /// <summary>
    /// Genera els documents d'entrada (CSV) a partir d'un projecte real exportat
    /// o d'un JSON de `NetworkDeviceInfo`. Els CSV es generen amb les columnes
    /// definides a <see cref="GenerationColumns"/> i llestos per a
    /// <c>--generate-project</c>.
    /// </summary>
    public class DocsExportService
    {
        private const int BusAdapterSlot = 127;
        private readonly string _docsDir;
        private readonly List<CatalogEntry> _catalog;

        public DocsExportService(string docsDir, List<CatalogEntry> catalog)
        {
            _docsDir = docsDir;
            _catalog = catalog ?? new List<CatalogEntry>();
        }

        public DocsExportReport Export(List<NetworkDeviceInfo>? devices, List<IoTagSpec>? ioTags, List<ModuleAddressSpec>? moduleAddresses)
        {
            var report = new DocsExportReport { OutputDir = _docsDir };
            if (devices == null || devices.Count == 0)
                throw new InvalidOperationException("No hi ha cap dispositiu per exportar.");

            var doc = new GenerationDocument();

            foreach (NetworkDeviceInfo device in devices)
            {
                var modules = device.Modules ?? new List<NetworkModuleInfo>();
                NetworkModuleInfo? root = CatalogService.FindRoot(device, modules);

                if (root == null)
                {
                    Console.WriteLine($"  (avís) '{device.DeviceName}': sense mòdul root detectable; es salta.");
                    continue;
                }

                string deviceTypeKey = ResolveTypeKey(root.OrderNumber, root.TypeName, report);
                doc.Devices.Add(new DeviceSpec
                {
                    DeviceName = device.DeviceName,
                    Group = DeriveGroup(device.StationType),
                    TypeKey = deviceTypeKey,
                    Firmware = ExtractFirmware(root),
                    Comment = device.StationType,
                });

                // Per als dispositius GSD, l'item estació (nom == nom del dispositiu)
                // comparteix slot amb el mòdul real de xarxa (ex. slot 0). Si el slot està
                // duplicat, descartem la fila 'nom == dispositiu': l'estació es crea amb el
                // dispositiu i la fila real és el root. Per a Siemens la fila root és única
                // al seu slot i es conserva.
                var slotCounts = modules
                    .GroupBy(m => m.PositionNumber)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (NetworkModuleInfo module in modules)
                {
                    if (CatalogService.IsWildcard(module.OrderNumber))
                        continue;

                    if (module.Name.Equals(device.DeviceName, StringComparison.OrdinalIgnoreCase)
                        && slotCounts.TryGetValue(module.PositionNumber, out int count)
                        && count > 1)
                        continue;

                    string typeKey = ResolveTypeKey(module.OrderNumber, module.TypeName, report);
                    bool isRoot = module.OrderNumber.Equals(root.OrderNumber, StringComparison.OrdinalIgnoreCase);
                    int? slot = module.PositionNumber == BusAdapterSlot ? null : module.PositionNumber;

                    doc.Modules.Add(new ModuleSpec
                    {
                        Device = device.DeviceName,
                        ModuleName = SanitizeModuleName(module.Name, typeKey),
                        Slot = slot,
                        TypeKey = typeKey,
                        Comment = isRoot ? "Root" : module.TypeName,
                    });
                }
            }

            doc.Subnets.AddRange(BuildSubnets(devices));
            BuildInterfaces(devices, doc);
            if (ioTags != null)
                doc.IoTags.AddRange(ioTags);
            if (moduleAddresses != null)
                doc.ModuleAddresses.AddRange(moduleAddresses);

            var writer = new CsvGenerationWriter(_docsDir);
            writer.Write(doc);

            report.Devices = doc.Devices.Count;
            report.Modules = doc.Modules.Count;
            report.Subnets = doc.Subnets.Count;
            report.Interfaces = doc.Interfaces.Count;
            report.Tags = doc.IoTags.Count;
            report.ModuleAddresses = doc.ModuleAddresses.Count;
            return report;
        }

        private string ResolveTypeKey(string orderNumber, string typeName, DocsExportReport report)
        {
            // Prioritat 1: derivar el TypeKey del nom del tipus (estable i inequívoc).
            // El catàleg es genera amb ToTypeKey(TypeName), així que aquesta equivalència
            // sempre és correcta. Evita l'ambigüitat quan diversos fabricants comparteixen
            // un mateix MLFB (ex. '182xxxxx' per a SEW i DFE).
            string derived = CatalogService.ToTypeKey(typeName);
            if (derived.Length > 0)
            {
                CatalogEntry? byName = _catalog.FirstOrDefault(e =>
                    e.TypeKey.Equals(derived, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    if (!report.TypeKeysResolved.Contains(byName.TypeKey))
                        report.TypeKeysResolved.Add(byName.TypeKey);
                    return byName.TypeKey;
                }
            }

            // Prioritat 2: resolució per MLFB normalitzat.
            string normalized = CatalogService.NormalizeMlfb(orderNumber);
            CatalogEntry? entry = _catalog.FirstOrDefault(e =>
                CatalogService.NormalizeMlfb(e.Mlfb).Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (entry != null && entry.TypeKey.Length > 0)
            {
                if (!report.TypeKeysResolved.Contains(entry.TypeKey))
                    report.TypeKeysResolved.Add(entry.TypeKey);
                return entry.TypeKey;
            }

            string fallback = CatalogService.ToTypeKey(typeName);
            if (fallback.Length > 0)
            {
                string missing = $"{normalized} -> {fallback}";
                if (!report.TypeKeysMissing.Contains(missing))
                    report.TypeKeysMissing.Add(missing);
                Console.WriteLine($"  (avís) '{normalized}': TypeKey no present al catàleg; s'usa '{fallback}'. Cal afegir-lo a Catalog.csv.");
            }
            return fallback;
        }

        private string ExtractFirmware(NetworkModuleInfo root)
        {
            if (string.IsNullOrWhiteSpace(root.OrderNumber))
                return string.Empty;

            string mlfb = CatalogService.NormalizeMlfb(root.OrderNumber);
            CatalogEntry? entry = _catalog.FirstOrDefault(e =>
                CatalogService.NormalizeMlfb(e.Mlfb).Equals(mlfb, StringComparison.OrdinalIgnoreCase));

            string identifier = entry?.Identifier ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identifier))
                return string.Empty;

            int slash = identifier.LastIndexOf('/');
            if (slash < 0 || slash == identifier.Length - 1)
                return string.Empty;

            string version = identifier.Substring(slash + 1).Trim();
            if (version.Length == 0 || (!version.StartsWith("V", StringComparison.OrdinalIgnoreCase) && !char.IsDigit(version[0])))
                return string.Empty;
            return version;
        }

        private static string DeriveGroup(string stationType)
        {
            string s = (stationType ?? string.Empty).ToUpperInvariant();
            if (s.Contains("CPU") || s.Contains("S7-1500") || s.Contains("S7-1200"))
                return "CPUs";
            if (s.Contains("ET200") || s.Contains("ET 200") || s.Contains("RACK"))
                return "Racks";
            if (s.Contains("DRIVE") || s.Contains("ROBOT"))
                return "Drives";
            return string.Empty;
        }

        private static string SanitizeModuleName(string? name, string typeKey)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = typeKey;

            var builder = new System.Text.StringBuilder(name!.Length);
            foreach (char c in name!)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    builder.Append(c);
            }

            string result = builder.ToString();
            if (result.Length == 0 || !char.IsLetter(result[0]))
                result = "M_" + result;
            if (result.Length == 0)
                result = typeKey;
            return result;
        }

        private static List<SubnetSpec> BuildSubnets(List<NetworkDeviceInfo> devices)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkDeviceInfo device in devices)
                foreach (NetworkInterfaceInfo iface in device.Interfaces)
                {
                    string subnet = iface.Subnet ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(subnet)
                        || subnet.Equals("Not connected", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool ethernet = IsEthernetLike(iface.InterfaceType);
                    if (map.TryGetValue(subnet, out bool current))
                        map[subnet] = current || ethernet;
                    else
                        map[subnet] = ethernet;
                }

            return map
                .Select(kvp => new SubnetSpec { Name = kvp.Key, SubnetType = kvp.Value ? "PN/IE" : "PROFIBUS" })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void BuildInterfaces(List<NetworkDeviceInfo> devices, GenerationDocument doc)
        {
            foreach (NetworkDeviceInfo device in devices)
                foreach (NetworkInterfaceInfo iface in device.Interfaces)
                {
                    string subnet = iface.Subnet ?? string.Empty;
                    if (subnet.Equals("Not connected", StringComparison.OrdinalIgnoreCase))
                        subnet = string.Empty;

                    string mask = iface.SubnetMask ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(mask) && !string.IsNullOrWhiteSpace(iface.IpAddress ?? string.Empty))
                        mask = "255.255.255.0";

                    doc.Interfaces.Add(new InterfaceSpec
                    {
                        Device = device.DeviceName,
                        InterfaceItem = iface.InterfaceName ?? string.Empty,
                        Subnet = subnet,
                        IpAddress = iface.IpAddress ?? string.Empty,
                        SubnetMask = mask,
                        PnDeviceName = SanitizePnDeviceName(iface.PnDeviceName),
                        MasterIoSystem = iface.MasterIoSystem ?? string.Empty,
                    });
                }
        }

        private static bool IsEthernetLike(string? interfaceType)
        {
            string t = (interfaceType ?? string.Empty).ToUpperInvariant();
            return t.Contains("ETHERNET") || t.Contains("PROFINET") || t.Contains(" PN ")
                || t.StartsWith("PN", StringComparison.Ordinal) || t.StartsWith("IE", StringComparison.Ordinal);
        }

        /// <summary>
        /// Converteix un nom PROFINET llegit del projecte origen a un nom vàlid:
        /// conserva lletres, dígits, '_' i '.'; passa ' ' a '-'; suprimeix '--'
        /// i els '-'/'·' inicials o finals. El nom PN de V21 és de sola-lectura a
        /// l'API (auto-naming), així que el nom generat és informatiu.
        /// </summary>
        internal static string SanitizePnDeviceName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var builder = new System.Text.StringBuilder(name!.Length);
            char prev = '\0';
            foreach (char c in name!.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    builder.Append(c);
                    prev = c;
                }
                else if (c == ' ' || c == '-')
                {
                    if (prev != '-' && prev != '.')
                    {
                        builder.Append('-');
                        prev = '-';
                    }
                }
                else if (c == '.')
                {
                    builder.Append('.');
                    prev = '.';
                }
            }

            string result = builder.ToString().Trim('-', '.');
            if (result.Length == 0)
                return string.Empty;
            if (result.IndexOf("--", StringComparison.Ordinal) >= 0)
                result = result.Replace("--", "-");
            return result;
        }
    }
}