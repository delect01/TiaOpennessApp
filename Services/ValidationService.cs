using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class ValidationService
    {
        private readonly List<CatalogEntry> _catalog;
        private readonly ValidationReport _report = new ValidationReport();

        public ValidationService(List<CatalogEntry> catalog)
        {
            _catalog = catalog;
        }

        public ValidationReport Validate(GenerationDocument doc)
        {
            ValidateNames(doc);
            ValidateUniqueness(doc);
            ValidateReferences(doc);
            ValidateIpv4(doc);
            ValidateSlots(doc);
            ValidateCatalog(doc);
            BuildPlannedOperations(doc);
            return _report;
        }

        public ValidationReport ValidateDestinationFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _report.Issues.Add(new ValidationIssue
                {
                    Section = "Projecte",
                    Message = "No s'ha indicat carpeta de destí per al projecte nou.",
                });
                return _report;
            }

            if (!Directory.Exists(path))
                return _report;

            string[] entries = Directory.GetFileSystemEntries(path);
            if (entries.Length > 0)
            {
                _report.Issues.Add(new ValidationIssue
                {
                    Section = "Projecte",
                    Message = "La carpeta de destí no està buida (" + entries.Length + " elements).",
                });
            }
            return _report;
        }

        private void ValidateNames(GenerationDocument doc)
        {
            foreach (var device in doc.Devices)
            {
                if (device.DeviceName.Length == 0)
                    continue;
                if (!IsValidVisibleDeviceName(device.DeviceName))
                    Error("Devices", device.DeviceName,
                        "Nom visible de dispositiu TIA invàlid: no pot contenir salts de línia ni caràcters de control i ha de tenir entre 1 i 128 caràcters.");
            }

            foreach (var module in doc.Modules)
            {
                if (module.ModuleName.Length == 0)
                    continue;
                if (!IsValidTiaName(module.ModuleName))
                    Error("Modules", module.Device + " / " + module.ModuleName,
                        "Nom TIA invàlid: ha de començar per lletra i contenir només lletres, dígits o guions baixos.");
            }

            foreach (var subnet in doc.Subnets)
            {
                if (subnet.Name.Length == 0)
                    continue;
                if (!IsValidSubnetName(subnet.Name))
                    Error("Subnets", subnet.Name,
                        "Nom de subxarxa invàlid: caràcters permesos lletres, dígits, espais, '_', '-', '.'.");
                if (subnet.SubnetType != "PN/IE" && subnet.SubnetType != "PROFIBUS")
                    Error("Subnets", subnet.Name,
                        $"SubnetType '{subnet.SubnetType}' no reconegut (esperat 'PN/IE' o 'PROFIBUS').");
            }

            foreach (var iface in doc.Interfaces)
            {
                if (iface.PnDeviceName.Length > 0 && !IsValidProfinetName(iface.PnDeviceName))
                    Error("Interfaces", iface.Device + " / " + iface.PnDeviceName,
                        "Nom PROFINET invàlid: 1-127 caràcters lletres/dígits/'_'/'-'; les etiquetes han de començar i acabar amb lletra o dígit, sense '--'.");
            }
        }

        private void ValidateUniqueness(GenerationDocument doc)
        {
            var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in doc.Devices)
            {
                if (device.DeviceName.Length == 0)
                    continue;
                if (!deviceNames.Add(device.DeviceName))
                    Error("Devices", device.DeviceName, "Nom de dispositiu duplicat.");
            }

            var subnetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subnet in doc.Subnets)
            {
                if (subnet.Name.Length == 0)
                    continue;
                if (!subnetNames.Add(subnet.Name))
                    Error("Subnets", subnet.Name, "Nom de subxarxa duplicat.");
            }

            var interfaceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var iface in doc.Interfaces)
            {
                string key = iface.Device + "|" + iface.InterfaceItem;
                if (!interfaceKeys.Add(key))
                    Error("Interfaces", key, "Interfície duplicada (mateix dispositiu i interfície).");
            }
        }

        private void ValidateReferences(GenerationDocument doc)
        {
            var deviceNames = new HashSet<string>(doc.Devices.Select(d => d.DeviceName), StringComparer.OrdinalIgnoreCase);
            var subnetNames = new HashSet<string>(doc.Subnets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var module in doc.Modules)
            {
                if (module.Device.Length == 0)
                    continue;
                if (!deviceNames.Contains(module.Device))
                    Error("Modules", module.Device + " / " + module.ModuleName,
                        $"Mòdul referenciat a un dispositiu inexistent: '{module.Device}'.");
            }

            foreach (var iface in doc.Interfaces)
            {
                if (!deviceNames.Contains(iface.Device))
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"Interfície referenciada a un dispositiu inexistent: '{iface.Device}'.");

                if (iface.Subnet.Length > 0 && !subnetNames.Contains(iface.Subnet))
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"Subxarxa inexistent referenciada: '{iface.Subnet}'.");
            }

            foreach (var tag in doc.IoTags)
            {
                if (tag.Device.Length == 0)
                    continue;
                if (!deviceNames.Contains(tag.Device))
                    Error("IoTags", tag.Device + " / " + tag.Name,
                        $"Tag referenciat a un dispositiu inexistent: '{tag.Device}'.");
            }

            var moduleKeys = new HashSet<string>(
                doc.Modules.Select(m => m.Device + "\u0001" + m.ModuleName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var addr in doc.ModuleAddresses)
            {
                if (addr.Device.Length == 0 || addr.ModuleName.Length == 0)
                    continue;
                string addrDeviceTypeKey = doc.Devices
                    .FirstOrDefault(d => d.DeviceName.Equals(addr.Device, StringComparison.OrdinalIgnoreCase))
                    ?.TypeKey ?? string.Empty;

                if (!deviceNames.Contains(addr.Device))
                {
                    Warning("ModuleAddresses", addr.Device + " / " + addr.ModuleName,
                        $"Dispositiu no present als CSV ('{addr.Device}').");
                    continue;
                }
                if (!moduleKeys.Contains(addr.Device + "\u0001" + addr.ModuleName)
                    && !IsGsdDevice(addrDeviceTypeKey))
                    Warning("ModuleAddresses", addr.Device + " / " + addr.ModuleName,
                        $"Mòdul no present a Modules.csv ('{addr.ModuleName}').");
                if (addr.IoType != "I" && addr.IoType != "Q")
                    Error("ModuleAddresses", addr.Device + " / " + addr.ModuleName,
                        $"IoType invàlid: '{addr.IoType}' (esperat 'I' o 'Q').");
            }
        }

        private void ValidateIpv4(GenerationDocument doc)
        {
            var ipsBySubnet = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);

            foreach (var iface in doc.Interfaces)
            {
                bool connected = iface.Subnet.Length > 0;
                bool hasIp = iface.IpAddress.Length > 0;
                bool hasMask = iface.SubnetMask.Length > 0;

                if (connected && !hasIp)
                {
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        "Interfície connectada a una subxarxa ha de tenir IP i màscara.");
                    continue;
                }
                if (hasIp && !hasMask)
                {
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        "Falta la SubnetMask de la IP.");
                    continue;
                }
                if (!hasIp)
                    continue;

                if (!TryParseIpv4(iface.IpAddress, out uint ip))
                {
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"IP invàlida: '{iface.IpAddress}'.");
                    continue;
                }
                if (!TryParseIpv4(iface.SubnetMask, out uint mask) || !IsValidSubnetMask(mask))
                {
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"Màscara invàlida (ha de ser contínua 1..0): '{iface.SubnetMask}'.");
                    continue;
                }

                if (!ipsBySubnet.TryGetValue(iface.Subnet, out var ipSet))
                {
                    ipSet = new HashSet<uint>();
                    ipsBySubnet[iface.Subnet] = ipSet;
                }
                if (!ipSet.Add(ip))
                {
                    Error("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"IP duplicada a la subxarxa '{iface.Subnet}': '{iface.IpAddress}'.");
                }

                uint host = ip & ~mask;
                if (host == 0)
                    Warning("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"La IP '{iface.IpAddress}' és la direcció de xarxa de la subxarxa.");
                if (host == (~mask))
                    Warning("Interfaces", iface.Device + " / " + iface.InterfaceItem,
                        $"La IP '{iface.IpAddress}' és la direcció de difusió de la subxarxa.");
            }
        }

        private void ValidateSlots(GenerationDocument doc)
        {
            var deviceTypeKeys = doc.Devices
                .GroupBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().TypeKey, StringComparer.OrdinalIgnoreCase);

            foreach (var group in doc.Modules.GroupBy(m => m.Device, StringComparer.OrdinalIgnoreCase))
            {
                // Els mòduls de dispositius GSD són informatius: l'estructura real ve de
                // l'import golden i tots els specs es salten quan el dispositiu ja existeix;
                // la col·lisió de slot (p. ex. item estació + root auto a slot 0) no ha de
                // bloquejar la generació.
                deviceTypeKeys.TryGetValue(group.Key, out string? deviceTypeKey);
                bool isGsd = IsGsdDevice(deviceTypeKey ?? string.Empty);

                var slots = new HashSet<int>();
                foreach (var module in group.Where(m => m.Slot.HasValue))
                {
                    int slot = module.Slot!.Value;
                    if (slot < 0)
                    {
                        Error("Modules", group.Key + " / " + module.ModuleName,
                            $"Slot negatiu: {slot}.");
                        continue;
                    }
                    if (isGsd)
                        continue;
                    if (!slots.Add(slot))
                        Error("Modules", group.Key + " / " + module.ModuleName,
                            $"Col·lisió de slot {slot} (ja ocupat per un altre mòdul).");
                }
            }
        }

        private void ValidateCatalog(GenerationDocument doc)
        {
            foreach (var device in doc.Devices)
            {
                if (device.TypeKey.Length == 0)
                    continue;
                var entry = Find(device.TypeKey);
                if (entry == null)
                {
                    Error("Devices", device.DeviceName,
                        $"TypeKey '{device.TypeKey}' no present al catàleg per a TIA {CatalogService.DefaultTiaVersion}.");
                }
                else if (entry.Role != CatalogItemRole.Root)
                {
                    Warning("Devices", device.DeviceName,
                        $"TypeKey '{device.TypeKey}' és un mòdul del catàleg; en dispositius s'espera un Root.");
                }
            }

            foreach (var module in doc.Modules)
            {
                if (module.TypeKey.Length == 0)
                    continue;
                var entry = Find(module.TypeKey);
                if (entry == null)
                {
                    Error("Modules", module.Device + " / " + module.ModuleName,
                        $"TypeKey '{module.TypeKey}' no present al catàleg per a TIA {CatalogService.DefaultTiaVersion}.");
                    continue;
                }

                if (entry.Role == CatalogItemRole.Root)
                {
                    bool isDeviceRoot = doc.Devices.Any(d =>
                        d.DeviceName.Equals(module.Device, StringComparison.OrdinalIgnoreCase)
                        && d.TypeKey.Equals(module.TypeKey, StringComparison.OrdinalIgnoreCase));
                    string moduleDeviceTypeKey = doc.Devices
                        .FirstOrDefault(d => d.DeviceName.Equals(module.Device, StringComparison.OrdinalIgnoreCase))
                        ?.TypeKey ?? string.Empty;

                    // Els dispositius GSD tenen el TypeKey del root que no coincideix
                    // amb el TypeKey de l'estació (p. ex. UM6A_PR = UFRXXBV17 vs root
                    // MDX61BDFE32BV14); es creen per import gsdml, no per CreateWithItem.
                    if (!isDeviceRoot && !IsGsdDevice(moduleDeviceTypeKey))
                        Warning("Modules", module.Device + " / " + module.ModuleName,
                            $"TypeKey '{module.TypeKey}' és un Root del catàleg; en mòduls s'espera un mòdul.");
                }
            }
        }

        private CatalogEntry? Find(string typeKey)
        {
            return _catalog.FirstOrDefault(e =>
                e.TypeKey.Equals(typeKey, StringComparison.OrdinalIgnoreCase)
                && e.TiaVersion == CatalogService.DefaultTiaVersion);
        }

        private bool IsGsdDevice(string deviceTypeKey)
        {
            if (string.IsNullOrEmpty(deviceTypeKey))
                return false;
            return _catalog.Any(e =>
                e.TypeKey.Equals(deviceTypeKey, StringComparison.OrdinalIgnoreCase)
                && e.Identifier?.StartsWith("GSD:", StringComparison.OrdinalIgnoreCase) == true);
        }

        private void BuildPlannedOperations(GenerationDocument doc)
        {
            foreach (var subnet in doc.Subnets)
                _report.PlannedOperations.Add($"Crear subxarxa '{subnet.Name}' ({subnet.SubnetType}).");

            foreach (var device in doc.Devices)
                _report.PlannedOperations.Add($"Crear dispositiu '{device.DeviceName}' (TypeKey {device.TypeKey}).");

            foreach (var module in doc.Modules)
            {
                string slot = module.Slot.HasValue ? $"slot {module.Slot.Value}" : "sense slot explícit";
                _report.PlannedOperations.Add($"Afegir mòdul '{module.ModuleName}' ({module.TypeKey}) a '{module.Device}' ({slot}).");
            }

            foreach (var iface in doc.Interfaces)
            {
                string target = iface.Subnet.Length > 0
                    ? $"→ subnet '{iface.Subnet}'; IP {iface.IpAddress}/{iface.SubnetMask}" +
                      (iface.PnDeviceName.Length > 0 ? $"; nom PN '{iface.PnDeviceName}'" : "")
                    : "sense connexió";
                _report.PlannedOperations.Add($"Configurar '{iface.InterfaceItem}' de '{iface.Device}' {target}.");
            }
        }

        private void Error(string section, string item, string message)
        {
            _report.Issues.Add(new ValidationIssue { Level = ParseIssueLevel.Error, Section = section, Item = item, Message = message });
        }

        private void Warning(string section, string item, string message)
        {
            _report.Issues.Add(new ValidationIssue { Level = ParseIssueLevel.Warning, Section = section, Item = item, Message = message });
        }

        internal static bool IsValidTiaName(string name)
        {
            if (name.Length == 0 || name.Length > 128)
                return false;
            if (!char.IsLetter(name[0]))
                return false;
            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Validacio per al <b>nom visible</b> d'un dispositiu. Es mes permesiva que
        /// <see cref="IsValidTiaName"/> perque TIA assigna per defecte noms amb espais,
        /// barres i guions (p. ex. "ET 200SP station_1", "S7-1500/ET200MP station_1").
        /// Només es rebutgen caracters de control i longituds fora de 1..128.
        /// </summary>
        internal static bool IsValidVisibleDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                return false;

            foreach (char c in name)
            {
                if (char.IsControl(c))
                    return false;
            }

            return true;
        }

        internal static bool IsValidSubnetName(string name)
        {
            if (name.Length == 0 || name.Length > 128)
                return false;
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-' && c != '.')
                    return false;
            }
            return !string.IsNullOrWhiteSpace(name);
        }

        internal static bool IsValidProfinetName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 127)
                return false;
            if (name[0] == '-' || name[name.Length - 1] == '-')
                return false;
            if (name.IndexOf("--", StringComparison.Ordinal) >= 0)
                return false;

            bool profinetValid = true;
            foreach (string label in name.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63)
                    profinetValid = false;
                foreach (char c in label)
                {
                    if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                        profinetValid = false;
                }
            }
            return profinetValid;
        }

        internal static bool TryParseIpv4(string text, out uint value)
        {
            value = 0;
            string[] parts = text.Split('.');
            if (parts.Length != 4)
                return false;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3)
                    return false;
                if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out byte octet))
                    return false;
                value = (value << 8) | octet;
            }
            return true;
        }

        internal static bool IsValidSubnetMask(uint mask)
        {
            if (mask == 0)
                return false;
            if (mask == uint.MaxValue)
                return true;
            uint inv = ~mask;
            return (inv & (inv + 1)) == 0;
        }
    }
}