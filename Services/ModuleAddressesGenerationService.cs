using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class ModuleAddressGenerationItemReport
    {
        public string Device { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string SubModuleName { get; set; } = string.Empty;
        public string IoType { get; set; } = string.Empty;
        public int StartAddress { get; set; }
        public int Length { get; set; }
        public bool Applied { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Aplica el mapa d'adreces de maquinari (I/Q) del projecte origen als
    /// mòduls del projecte generat. TIA auto-assigna direccions en crear
    /// maquinari; quan l'origen té direccionament manual (bytes esparsos)
    /// cal re-assignar explícitament StartAddress/Length de cada item.
    /// </summary>
    public class ModuleAddressesGenerationService
    {
        private readonly Project _project;
        private readonly Dictionary<string, Device?> _deviceCache =
            new Dictionary<string, Device?>(StringComparer.OrdinalIgnoreCase);

        private int _parkCursor = 10000;

        public ModuleAddressesGenerationService(Project project)
        {
            _project = project;
        }

        public List<ModuleAddressGenerationItemReport> Generate(IList<ModuleAddressSpec> addresses)
        {
            var reports = new List<ModuleAddressGenerationItemReport>();
            if (addresses == null || addresses.Count == 0)
                return reports;

            var pending = new List<(ModuleAddressGenerationItemReport Report, Address Address, int Target)>();
            var targetSet = new HashSet<(char Area, int Start)>();

            // Pass 1: resoldre totes les adreces i recollir objectius i pendents.
            // L'espai d'adreces I/Q pot ser compartit entre dispositius, així que
            // la resolució de conflictes es fa de forma GLOBAL (no per dispositiu).
            foreach (IGrouping<string, ModuleAddressSpec> group in addresses.GroupBy(a => a.Device, StringComparer.OrdinalIgnoreCase))
            {
                Device? device = FindDevice(group.Key);

                foreach (ModuleAddressSpec spec in group)
                {
                    var report = new ModuleAddressGenerationItemReport
                    {
                        Device = spec.Device,
                        ModuleName = spec.ModuleName,
                        SubModuleName = spec.SubModuleName,
                        IoType = spec.IoType,
                        StartAddress = spec.StartAddress,
                        Length = spec.Length,
                    };

                    try
                    {
                        if (device == null)
                        {
                            report.Error = $"Dispositiu '{spec.Device}' no trobat al projecte.";
                            continue;
                        }

                        AddressResolution resolution = FindAddress(device, spec.ModuleName, spec.SubModuleName, spec.IoType);
                        if (resolution.Error != null)
                        {
                            report.Error = resolution.Error;
                            continue;
                        }

                        targetSet.Add((FirstChar(spec.IoType), spec.StartAddress));

                        // Length és read-only a l'API (el fixa el tipus de mòdul);
                        // només StartAddress és ajustable.
                        if (SafeNumber(resolution.Address!.StartAddress) == spec.StartAddress)
                        {
                            report.Applied = true;
                            report.Message = "ja coincidia";
                            continue;
                        }

                        pending.Add((report, resolution.Address!, spec.StartAddress));
                    }
                    catch (Exception ex)
                    {
                        report.Error = ex.Message;
                    }
                    finally
                    {
                        reports.Add(report);
                    }
                }
            }

            // Fase A: aparcar en una zona alta lliure (per probe, comença a 10000)
            // totes les adreces pendents per alliberar el bloc dens auto-assignat.
            foreach (var (report, address, _) in pending)
            {
                if (!TryParkAddress(address))
                    report.Error = $"No s'ha pogut alliberar la zona per a '{report.ModuleName}' (cap lloc lliure).";
            }

            // Fase A2 (global): aparcar qualsevol altra adreça del projecte (mòduls no
            // mapejats, servidor, altres dispositius que comparteixen l'espai d'adreces)
            // que ocupi un rang objectiu. S'exceptuen les adreces que ja estan en un
            // valor objectiu (les "ja coincidia"), que no s'han de tocar.
            var pendingSet = new HashSet<Address>(pending.Select(p => p.Address), new ReferenceComparer<Address>());
            foreach (Address occupied in EnumerateAllProjectAddresses())
            {
                if (pendingSet.Contains(occupied))
                    continue;

                char area = IoTypeOf(occupied.IoType);
                if (area != 'I' && area != 'Q')
                    continue;

                int occStart = SafeNumber(occupied.StartAddress);
                if (targetSet.Contains((area, occStart)))
                    continue;

                int occLen = Math.Max(1, SafeNumber(occupied.Length));
                bool collides = pending.Any(p =>
                    FirstChar(p.Report.IoType) == area &&
                    RangesOverlap(occStart, occLen, p.Target, 1));
                if (collides)
                    TryParkAddress(occupied);
            }

            // Fase B: assignar els valors finals (esparsos) ja sense conflictes.
            foreach (var (report, address, target) in pending)
            {
                if (report.Error != null)
                    continue;

                try
                {
                    if (SafeNumber(address.StartAddress) == target)
                    {
                        report.Applied = true;
                        report.Message = "ja coincidia";
                    }
                    else
                    {
                        TryParkAddress(address); // assegura zona lliure abans d'assignar
                        address.StartAddress = target;
                        report.Applied = true;
                        report.Message = "adreça ajustada";
                    }
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }
            }

            return reports;
        }

        private Device? FindDevice(string name)
        {
            if (_deviceCache.TryGetValue(name, out Device? device))
                return device;
            device = SafeFindDevice(name);
            _deviceCache[name] = device;
            return device;
        }

        private Device? SafeFindDevice(string name)
        {
            try { return _project.Devices.Find(name); }
            catch { return null; }
        }

        private static DeviceItem? FindItemByName(DeviceItem root, string name)
        {
            foreach (DeviceItem child in root.DeviceItems)
            {
                DeviceItem? found = FindItemRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static DeviceItem? FindItemByName(Device device, string name)
        {
            foreach (DeviceItem top in device.DeviceItems)
            {
                DeviceItem? found = FindItemRecursive(top, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static DeviceItem? FindItemRecursive(DeviceItem item, string name)
        {
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return item;
            foreach (DeviceItem child in item.DeviceItems)
            {
                DeviceItem? found = FindItemRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private sealed class AddressResolution
        {
            public Address? Address { get; set; }
            public string? Error { get; set; }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>
        /// Localitza l'adreça I/Q concreta (I o Q) d'un submòdul. Retorna també un
        /// missatge d'error descriptiu si el dispositiu, el mòdul o l'adreça no es troben.
        /// </summary>
        private static AddressResolution FindAddress(Device device, string moduleName, string subModuleName, string ioType)
        {
            DeviceItem? module = FindItemByName(device, moduleName);
            if (module == null)
                return new AddressResolution { Error = $"Mòdul '{moduleName}' no trobat al dispositiu '{device.Name}'." };

            DeviceItem? target = module;
            if (!string.IsNullOrWhiteSpace(subModuleName))
            {
                target = FindItemByName(module, subModuleName!);
                if (target == null)
                    return new AddressResolution { Error = $"Submòdul '{subModuleName}' no trobat sota el mòdul '{moduleName}'." };
            }

            var composition = target.Addresses;
            if (composition == null || composition.Count == 0)
                return new AddressResolution { Error = $"L'item '{target.Name}' no exposa cap adreça I/Q." };

            char area = FirstChar(ioType);
            foreach (Address address in composition)
            {
                if (IoTypeOf(address.IoType) == area)
                    return new AddressResolution { Address = address };
            }

            return new AddressResolution { Error = $"Cap adreça de tipus {ioType} a l'item '{target.Name}'." };
        }

        private static char IoTypeOf(AddressIoType ioType)
        {
            switch (ioType)
            {
                case AddressIoType.Input: return 'I';
                case AddressIoType.Output: return 'Q';
                default: return '\0';
            }
        }

        private static char FirstChar(string value)
        {
            return value.Length > 0 ? value[0] : '\0';
        }

        private static bool RangesOverlap(int s1, int l1, int s2, int l2)
        {
            return s1 < s2 + l2 && s2 < s1 + l1;
        }

        /// <summary>
        /// Mou una adreça a una zona alta lliure, provant candidats successius
        /// (des de 10000) i saltant els que TIA rebutgi per estar ja usats.
        /// El cursor només creix, així les àrees I/Q (espais separats) i els
        /// dispositius no col·lisionen entre ells.
        /// </summary>
        private bool TryParkAddress(Address address)
        {
            int len = Math.Max(2, SafeNumber(address.Length));
            if (len % 2 != 0) len++;
            for (int guard = 0; guard < 4000; guard++)
            {
                int candidate = _parkCursor;
                _parkCursor = candidate + len;
                try
                {
                    if (SafeNumber(address.StartAddress) != candidate)
                        address.StartAddress = candidate;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static List<Address> EnumerateDeviceAddresses(Device device)
        {
            var result = new List<Address>();
            foreach (DeviceItem top in device.DeviceItems)
                CollectIOAddresses(top, result);
            return result;
        }

        private List<Address> EnumerateAllProjectAddresses()
        {
            var all = new List<Address>();
            try
            {
                foreach (Device device in _project.Devices)
                {
                    try { all.AddRange(EnumerateDeviceAddresses(device)); }
                    catch { }
                }
            }
            catch { }
            return all;
        }

        private static void CollectIOAddresses(DeviceItem item, List<Address> result)
        {
            try
            {
                if (item.Addresses != null)
                {
                    foreach (Address a in item.Addresses)
                    {
                        char area = IoTypeOf(a.IoType);
                        if (area == 'I' || area == 'Q')
                            result.Add(a);
                    }
                }
            }
            catch { }

            foreach (DeviceItem child in item.DeviceItems)
                CollectIOAddresses(child, result);
        }

        private static int SafeNumber(object? value)
        {
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }
}