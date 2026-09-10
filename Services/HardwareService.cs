using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    public class HardwareService
    {
        private readonly TiaConnection _connection;

        public HardwareService(TiaConnection connection)
        {
            _connection = connection;
        }

        public HardwareExportReport ExportFullHardware()
        {
            var project = _connection.Project;
            var devices = new List<DeviceSummary>();

            foreach (Device device in project.Devices)
            {
                devices.Add(BuildDeviceSummary(device));
            }

            return new HardwareExportReport
            {
                Operation = "Offline read-only extraction using TIA Portal V21 Openness API",
                Project = new ProjectMetadata
                {
                    Name = project.Name,
                    Path = project.Path.FullName,
                    Version = project.Version,
                    CreationTime = project.CreationTime,
                    LastModified = project.LastModified,
                },
                DeviceCount = devices.Count,
                Devices = devices,
            };
        }

        public List<DeviceSummary> GetHardwareSummary()
        {
            return ExportFullHardware().Devices;
        }

        public DeviceSummary BuildDeviceSummary(Device device)
        {
            var deviceSummary = new DeviceSummary
            {
                Name = device.Name,
                TypeIdentifier = device.TypeIdentifier,
                TypeName = GetTypeName(device) ?? string.Empty,
                IsGsd = device.IsGsd,
            };

            foreach (DeviceItem item in device.DeviceItems)
            {
                var hwItem = BuildHardwareTree(item, device.Name);
                deviceSummary.HardwareTree.Add(hwItem);
                deviceSummary.TotalItems += CountItems(hwItem);
            }

            return deviceSummary;
        }

        public void PrintDeviceTree()
        {
            var project = _connection.Project;
            int deviceIndex = 0;

            foreach (Device device in project.Devices)
            {
                deviceIndex++;
                string typeName = GetTypeName(device) ?? "N/A";
                Console.WriteLine($"[{deviceIndex}] {device.Name} ({typeName})");
                Console.WriteLine($"    TypeIdentifier: {device.TypeIdentifier}");
                Console.WriteLine($"    IsGSD: {device.IsGsd}");

                var hwIds = device.HwIdentifiers.Select(h => h.Identifier.ToString()).ToList();
                if (hwIds.Any())
                    Console.WriteLine($"    HW IDs: {string.Join(", ", hwIds)}");

                int itemIndex = 0;
                foreach (DeviceItem item in device.DeviceItems)
                {
                    itemIndex++;
                    PrintDeviceItem(item, "    ", itemIndex == device.DeviceItems.Count);
                }

                Console.WriteLine();
            }

            if (deviceIndex == 0)
            {
                Console.WriteLine("  (No devices found in project)");
            }
        }

        private void PrintDeviceItem(DeviceItem item, string indent, bool isLast)
        {
            string connector = isLast ? "└── " : "├── ";
            string orderNum = GetOrderNumber(item) ?? "";
            string suffix = !string.IsNullOrEmpty(orderNum) ? $" [{orderNum}]" : "";

            Console.WriteLine($"{indent}{connector}{item.Name}{suffix}");
            Console.WriteLine($"{indent}{(isLast ? "    " : "│   ")}TypeName: {GetTypeName(item) ?? "N/A"}");
            Console.WriteLine($"{indent}{(isLast ? "    " : "│   ")}Position: {item.PositionNumber} | Class: {item.Classification} | Plugged: {item.IsPlugged}");

            var addresses = item.Addresses;
            if (addresses != null && addresses.Count > 0)
            {
                foreach (var addr in addresses)
                {
                    Console.WriteLine($"{indent}{(isLast ? "    " : "│   ")}Address: {addr.IoType} Start={addr.StartAddress} Len={addr.Length}");
                }
            }

            var children = GetChildren(item).ToList();
            for (int i = 0; i < children.Count; i++)
            {
                PrintDeviceItem(children[i], indent + (isLast ? "    " : "│   "), i == children.Count - 1);
            }
        }

        private HardwareInfo BuildHardwareTree(DeviceItem item, string parentPath)
        {
            string path = parentPath + "/" + item.Name;
            var gsdItem = GetGsdDeviceItem(item);

            var info = new HardwareInfo
            {
                Name = item.Name,
                TypeName = GetTypeName(item) ?? string.Empty,
                TypeIdentifier = item.TypeIdentifier,
                OrderNumber = GetOrderNumber(item) ?? string.Empty,
                GsdName = gsdItem?.GsdName ?? string.Empty,
                PositionNumber = item.PositionNumber,
                Classification = item.Classification.ToString(),
                IsBuiltIn = item.IsBuiltIn,
                IsGsd = gsdItem != null,
                IsPlugged = item.IsPlugged,
                HierarchyPath = path,
            };

            if (item.HwIdentifiers != null)
            {
                info.HardwareIdentifiers = item.HwIdentifiers
                    .Select(h => h.Identifier.ToString())
                    .ToList();
            }

            if (item.Addresses != null)
            {
                info.Addresses = item.Addresses.Select(a => new IoAddressInfo
                {
                    IoType = a.IoType.ToString(),
                    StartAddress = a.StartAddress,
                    Length = a.Length,
                    EndAddress = a.StartAddress + a.Length - 1,
                }).ToList();
            }

            foreach (var child in GetChildren(item))
            {
                info.Children.Add(BuildHardwareTree(child, path));
            }

            return info;
        }

        private static GsdDeviceItem? GetGsdDeviceItem(DeviceItem item)
        {
            try
            {
                return ((IEngineeringServiceProvider)item).GetService<GsdDeviceItem>();
            }
            catch
            {
                return null;
            }
        }

        private static string? GetTypeName(object obj)
        {
            try
            {
                if (obj is DeviceItem di)
                    return GetAttribute<string>(di, "TypeName");
                return GetAttribute<string>(obj, "TypeName");
            }
            catch
            {
                return null;
            }
        }

        private static string? GetOrderNumber(DeviceItem item)
        {
            return GetAttribute<string>(item, "OrderNumber");
        }

        private static IEnumerable<DeviceItem> GetChildren(DeviceItem item)
        {
            if (item.DeviceItems != null && item.DeviceItems.Count > 0)
            {
                return item.DeviceItems;
            }

            if (item.Items != null && item.Items.Count > 0)
            {
                return item.Items.ToList();
            }

            return Enumerable.Empty<DeviceItem>();
        }

        private static int CountItems(HardwareInfo item)
        {
            int count = 1;
            foreach (var child in item.Children)
                count += CountItems(child);
            return count;
        }

        private static T? GetAttribute<T>(object obj, string name)
        {
            var iface = obj.GetType().GetInterface("Siemens.Engineering.IEngineeringObject");
            if (iface != null && obj is IEngineeringObject engObj)
            {
                try { return (T?)engObj.GetAttribute(name); } catch { }
            }

            var prop = obj.GetType().GetProperty(name);
            if (prop != null)
            {
                try { return (T?)prop.GetValue(obj, null); } catch { }
            }

            return default;
        }
    }
}