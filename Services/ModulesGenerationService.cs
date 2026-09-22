using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Extensions;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class ModuleGenerationItemReport
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string TypeKey { get; set; } = string.Empty;
        public int Slot { get; set; }
        public bool Created { get; set; }
        public bool Skipped { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }

    public class ModulesGenerationService
    {
        private readonly Project _project;
        private readonly Dictionary<string, CatalogEntry> _catalog;

        public ModulesGenerationService(Project project, List<CatalogEntry> catalog)
        {
            _project = project;
            _catalog = catalog.ToDictionary(e => e.TypeKey, StringComparer.OrdinalIgnoreCase);
        }

        public List<ModuleGenerationItemReport> Generate(
            GenerationDocument doc,
            IReadOnlyDictionary<string, string> rootItemNames)
        {
            var results = new List<ModuleGenerationItemReport>();

            foreach (var module in doc.Modules)
            {
                var report = new ModuleGenerationItemReport
                {
                    DeviceName = module.Device,
                    ModuleName = module.ModuleName,
                    TypeKey = module.TypeKey,
                    Slot = module.Slot ?? 0,
                };

                try
                {
                    int slot = module.Slot ?? 0;

                    Device? device = DevicesAndNetworksService.FindDeviceByName(_project, module.Device);
                    if (device == null)
                    {
                        report.Error = "Dispositiu no trobat al projecte.";
                        results.Add(report);
                        continue;
                    }

                    DeviceSpec? deviceSpec = doc.Devices.FirstOrDefault(d =>
                        d.DeviceName.Equals(module.Device, StringComparison.OrdinalIgnoreCase));
                    if (deviceSpec != null
                        && deviceSpec.TypeKey.Equals(module.TypeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        report.Skipped = true;
                        report.Message = "item root (ja creat amb el dispositiu)";
                        results.Add(report);
                        continue;
                    }

                    if (slot <= 0)
                    {
                        report.Skipped = true;
                        report.Message = "sense slot assignat (no es requereix inserció)";
                        results.Add(report);
                        continue;
                    }

                    if (!_catalog.TryGetValue(module.TypeKey, out CatalogEntry? entry))
                    {
                        report.Error = $"TypeKey '{module.TypeKey}' no present al catàleg.";
                        results.Add(report);
                        continue;
                    }

                    string identifier = entry.Identifier.Length > 0
                        ? entry.Identifier
                        : "OrderNumber:" + entry.Mlfb;

                    string rootName = rootItemNames.TryGetValue(module.Device, out string? rn)
                        && !string.IsNullOrWhiteSpace(rn)
                        ? rn
                        : module.Device;

                    DeviceItem? rootItem = FindItemByName(device, rootName);
                    if (rootItem == null)
                    {
                        report.Error = $"Item root '{rootName}' no trobat al dispositiu.";
                        results.Add(report);
                        continue;
                    }

                    HardwareObject target = rootItem.Container
                        ?? throw new InvalidOperationException($"L'item root '{rootName}' no té contenidor.");

                    DeviceItem? existing = target.Items.Find(slot);
                    if (existing != null)
                    {
                        report.Skipped = true;
                        report.Message = $"el slot {slot} ja està ocupat per '{existing.Name}'";
                        results.Add(report);
                        continue;
                    }

                    if (target.CanPlugNew(identifier, module.ModuleName, slot))
                    {
                        DeviceItem created = target.PlugNew(identifier, module.ModuleName, slot);
                        report.Created = created != null;
                        if (created == null)
                            report.Message = "PlugNew no va retornar cap ítem.";
                    }
                    else
                    {
                        report.Skipped = true;
                        report.Message = $"CanPlugNew=false (el slot {slot} no admet el mòdul o el nom no està disponible)";
                    }
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }

                results.Add(report);
            }
            return results;
        }

        public void PrintPlugDiagnostics(Project project, string deviceName, string rootItemName, IReadOnlyList<string> identifiers)
        {
            Device? device = project.Devices.Find(deviceName)
                ?? project.UngroupedDevicesGroup.Devices.Find(deviceName);
            if (device == null)
            {
                Console.WriteLine($"Device '{deviceName}' not found.");
                return;
            }

            DeviceItem? rootItem = FindItemByName(device, rootItemName);
            if (rootItem?.Container == null)
            {
                Console.WriteLine($"Root item '{rootItemName}' (with container) not found.");
                return;
            }

            HardwareObject target = rootItem.Container;
            Console.WriteLine($"Container: '{target.Name}'");
            Console.WriteLine("Free slots:");
            foreach (PlugLocation location in target.GetPlugLocations())
                Console.WriteLine($"  pos={location.PositionNumber}  label='{location.Label}'");

            foreach (string identifier in identifiers)
            {
                Console.WriteLine($"\nIdentifier: {identifier}");
                foreach (PlugLocation location in target.GetPlugLocations())
                {
                    if (location.PositionNumber <= 0)
                        continue;
                    Console.WriteLine($"  CanPlugNew at {location.PositionNumber}: " +
                                      target.CanPlugNew(identifier, "probe", location.PositionNumber));
                }
            }
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
            if (item.Name == name)
                return item;
            foreach (DeviceItem child in item.DeviceItems)
            {
                DeviceItem? found = FindItemRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}