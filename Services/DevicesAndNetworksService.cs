using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    public class DevicesAndNetworksService
    {
        private readonly Project _project;

        public DevicesAndNetworksService(TiaConnection connection)
        {
            _project = connection.Project;
        }

        public DevicesAndNetworksService(Project project)
        {
            _project = project;
        }

        public List<NetworkDeviceInfo> ExportNetworkDevices()
        {
            var devices = new List<NetworkDeviceInfo>();

            foreach (Device device in EnumerateAllDevices())
            {
                var info = new NetworkDeviceInfo
                {
                    DeviceName = device.Name,
                    StationType = GetTypeName(device) ?? string.Empty,
                };

                foreach (DeviceItem item in EnumerateItems(device))
                {
                    string? orderNumber = GetAttr(item, "OrderNumber");
                    var netIf = GetNetworkInterface(item);

                    if (netIf != null)
                    {
                        Node? node = null;
                        foreach (Node n in netIf.Nodes)
                        {
                            node = n;
                            if (n.ConnectedSubnet != null)
                                break;
                        }

                        info.Interfaces.Add(new NetworkInterfaceInfo
                        {
                            InterfaceName = item.Name,
                            InterfaceType = node?.NodeType.ToString() ?? GetInterfaceTypeName(item),
                            IpAddress = GetItemIp(item, netIf, node),
                            Subnet = node?.ConnectedSubnet?.Name ?? "Not connected",
                            MasterIoSystem = GetMasterIoSystem(item, node) ?? string.Empty,
                            SubnetMask = GetItemSubnetMask(item, netIf, node),
                            PnDeviceName = GetPnDeviceName(item, netIf, node),
                        });
                    }
                    else if (IsInterfaceItem(item))
                    {
                        info.Interfaces.Add(new NetworkInterfaceInfo
                        {
                            InterfaceName = item.Name,
                            InterfaceType = GetInterfaceTypeName(item),
                            Subnet = "Not connected",
                            SubnetMask = GetItemSubnetMask(item, null, null),
                            PnDeviceName = GetPnDeviceName(item, null, null),
                        });
                    }

                    if (!string.IsNullOrEmpty(orderNumber))
                    {
                        info.Modules.Add(new NetworkModuleInfo
                        {
                            Name = item.Name,
                            TypeName = GetTypeName(item) ?? string.Empty,
                            OrderNumber = orderNumber ?? string.Empty,
                            TypeIdentifier = SafeTypeIdentifier(item),
                            PositionNumber = item.PositionNumber,
                        });
                    }
                }

                info.DeviceOrderNumber = GetDeviceOrderNumber(device, info.Modules);
                devices.Add(info);
            }

            return devices;
        }

        public void PrintNetworkTree()
        {
            var project = _project;
            var devices = ExportNetworkDevices();

            Console.WriteLine($"  Project:  {project.Name} ({project.Version})");
            Console.WriteLine();

            Console.WriteLine($"━━━ NETWORK DEVICES ━━━  ({devices.Count} device(s))");
            if (devices.Count == 0)
            {
                Console.WriteLine("  (No devices in project)");
                return;
            }

            int totalInterfaces = devices.Sum(d => d.Interfaces.Count);
            for (int d = 0; d < devices.Count; d++)
            {
                var dev = devices[d];
                bool lastDevice = d == devices.Count - 1;
                string dConn = lastDevice ? "└── " : "├── ";

                Console.WriteLine($"{dConn}[D] {dev.StationType}  ({dev.DeviceName})  Ref: {dev.DeviceOrderNumber}");
                string dIndent = "  " + (lastDevice ? "    " : "│   ");

                int iCount = dev.Interfaces.Count;
                int mCount = dev.Modules.Count;
                int totalLines = iCount + mCount;
                int lineIndex = 0;

                for (int i = 0; i < iCount; i++)
                {
                    var iface = dev.Interfaces[i];
                    lineIndex++;
                    bool lastLine = lineIndex == totalLines;
                    string conn = lastLine ? "└── " : "├── ";
                    string master = string.IsNullOrEmpty(iface.MasterIoSystem)
                        ? string.Empty
                        : $"  Master: {iface.MasterIoSystem}";
                    Console.WriteLine($"{dIndent}{conn}{iface.InterfaceName} ({iface.InterfaceType})  {iface.IpAddress}  @ {iface.Subnet}{master}");
                }

                if (mCount > 0)
                {
                    string mIndent = dIndent + "│   ";

                    if (iCount == 0)
                    {
                        lineIndex++;
                        Console.WriteLine($"{dIndent}├── Modules:");
                    }

                    for (int m = 0; m < mCount; m++)
                    {
                        var mod = dev.Modules[m];
                        lineIndex++;
                        bool lastLine = lineIndex == totalLines;
                        string conn = lastLine ? "└── " : "├── ";
                        Console.WriteLine($"{mIndent}{conn}{mod.Name}  ({mod.TypeName})  {mod.OrderNumber}");
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"  Totals: {devices.Count} device(s), {totalInterfaces} interface(s)");
        }

        public void DumpTypeIdentifiers()
        {
            foreach (Device device in EnumerateAllDevices())
            {
                Console.WriteLine($"DEVICE: {device.Name}  TypeIdent={device.TypeIdentifier}");
                foreach (DeviceItem item in EnumerateItems(device))
                {
                    string order = GetAttr(item, "OrderNumber") ?? string.Empty;
                    string? fw = GetAttr(item, "FirmwareVersion");
                    if (fw == null)
                        fw = GetAttr(item, "Firmware");
                    Console.WriteLine($"  ITEM: {item.Name}  TypeIdent={item.TypeIdentifier}  Order={order}  FW={fw ?? string.Empty}");
                }
            }
        }

        internal static IEnumerable<Device> EnumerateAllDevices(Project project)
        {
            var seen = new HashSet<string>();

            foreach (Device device in project.Devices)
                if (seen.Add(device.Name)) yield return device;

            foreach (Device device in project.UngroupedDevicesGroup.Devices)
                if (seen.Add(device.Name)) yield return device;

            foreach (DeviceGroup group in project.DeviceGroups)
                foreach (Device device in EnumerateGroupDevices(group))
                    if (seen.Add(device.Name)) yield return device;
        }

        internal static Device? FindDeviceByName(Project project, string name)
        {
            foreach (Device device in EnumerateAllDevices(project))
            {
                if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            return null;
        }

        private IEnumerable<Device> EnumerateAllDevices()
        {
            return EnumerateAllDevices(_project);
        }

        private static IEnumerable<Device> EnumerateGroupDevices(DeviceGroup group)
        {
            foreach (Device device in group.Devices)
                yield return device;

            if (group is DeviceUserGroup userGroup)
            {
                foreach (DeviceUserGroup child in userGroup.Groups)
                {
                    foreach (Device device in EnumerateGroupDevices(child))
                        yield return device;
                }
            }
        }

        internal static IEnumerable<DeviceItem> EnumerateItems(Device device)
        {
            var seen = new HashSet<string>();

            foreach (DeviceItem item in device.DeviceItems)
            {
                if (!seen.Add(GetItemKey(item)))
                    continue;
                yield return item;
                foreach (DeviceItem child in EnumerateItemChildren(item, seen))
                    yield return child;
            }
        }

        internal static IEnumerable<DeviceItem> EnumerateItemChildren(DeviceItem item, HashSet<string> seen)
        {
            IEnumerable<DeviceItem> source;
            if (item.DeviceItems != null && item.DeviceItems.Count > 0)
                source = item.DeviceItems.ToList();
            else if (item.Items != null && item.Items.Count > 0)
                source = item.Items.ToList();
            else
                yield break;

            foreach (DeviceItem child in source)
            {
                if (!seen.Add(GetItemKey(child)))
                    continue;
                yield return child;
                foreach (DeviceItem gc in EnumerateItemChildren(child, seen))
                    yield return gc;
            }
        }

        private static string GetItemKey(DeviceItem item)
        {
            try
            {
                if (item.HwIdentifiers != null && item.HwIdentifiers.Count > 0)
                {
                    var ids = item.HwIdentifiers
                        .Select(h => h.Identifier.ToString())
                        .Where(id => id != "0")
                        .ToList();
                    if (ids.Count > 0)
                        return "HW:" + string.Join("+", ids);
                }
            }
            catch { }

            string parentPath = string.Empty;
            try
            {
                var parts = new List<string>();
                HardwareObject container = item.Container;
                while (container is DeviceItem parentItem)
                {
                    parts.Insert(0, parentItem.Name);
                    container = parentItem.Container;
                }
                if (container is Device device)
                    parts.Insert(0, device.Name);
                parentPath = string.Join("/", parts);
            }
            catch { }

            return $"{parentPath}/{item.Name}|pos:{item.PositionNumber}|{item.TypeIdentifier}";
        }

        private static NetworkInterface? GetNetworkInterface(object obj)
        {
            try { return ((IEngineeringServiceProvider)obj).GetService<NetworkInterface>(); }
            catch { return null; }
        }

        private static bool IsInterfaceItem(DeviceItem item)
        {
            return item.Name.IndexOf("interface", StringComparison.OrdinalIgnoreCase) >= 0
                || GetInterfaceTypeName(item).IndexOf("PROFINET", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetInterfaceTypeName(DeviceItem item)
        {
            string? value = GetAttr(item, "InterfaceType");
            if (!string.IsNullOrEmpty(value))
                return value ?? string.Empty;
            return item.Name;
        }

        private static string GetItemIp(DeviceItem item, NetworkInterface? netIf, Node? node)
        {
            string[] names = { "IpAddress", "Ipv4Address", "Address" };
            foreach (string name in names)
            {
                string? value;
                if ((value = GetAttr(item, name)) != null) return value;
                if (netIf != null && (value = GetAttr(netIf, name)) != null) return value;
                if (node != null && (value = GetAttr(node, name)) != null) return value;
                if (node?.Parent != null && (value = GetAttr(node.Parent, name)) != null) return value;
            }
            return string.Empty;
        }

        private static string GetItemSubnetMask(DeviceItem item, NetworkInterface? netIf, Node? node)
        {
            string[] names = { "SubnetMask", "IpSubnetMask", "Mask", "NetworkMask" };
            foreach (string name in names)
            {
                string? value;
                if ((value = GetAttr(item, name)) != null) return value;
                if (netIf != null && (value = GetAttr(netIf, name)) != null) return value;
                if (node != null && (value = GetAttr(node, name)) != null) return value;
                if (node?.Parent != null && (value = GetAttr(node.Parent, name)) != null) return value;
            }
            return string.Empty;
        }

        private static string GetPnDeviceName(DeviceItem item, NetworkInterface? netIf, Node? node)
        {
            string[] names = { "PnDeviceName", "PnName", "IoDeviceName", "InterfaceDeviceName" };
            foreach (string name in names)
            {
                string? value;
                if ((value = GetAttr(item, name)) != null) return value;
                if (netIf != null && (value = GetAttr(netIf, name)) != null) return value;
                if (node != null && (value = GetAttr(node, name)) != null) return value;
                if (node?.Parent != null && (value = GetAttr(node.Parent, name)) != null) return value;
            }
            return string.Empty;
        }

        private static string? GetMasterIoSystem(DeviceItem item, Node? node)
        {
            try
            {
                object? ioSystem = (item as IEngineeringObject)?.GetAttribute("IOSystem");
                if (ioSystem is IEngineeringObject io)
                    return io.GetAttribute("Name") as string;
            }
            catch { }
            return node?.ConnectedSubnet?.Name;
        }

        private static string? GetTypeName(object obj)
        {
            try { return (obj as IEngineeringObject)?.GetAttribute("TypeName") as string; }
            catch { return null; }
        }

        private static string SafeTypeIdentifier(DeviceItem item)
        {
            try { return item.TypeIdentifier ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string? GetAttr(object obj, string name)
        {
            try { return (obj as IEngineeringObject)?.GetAttribute(name) as string; }
            catch { return null; }
        }

        private static string GetDeviceOrderNumber(Device device, List<NetworkModuleInfo> modules)
        {
            string? orderNumber = GetAttr(device, "OrderNumber");
            if (!string.IsNullOrEmpty(orderNumber))
                return orderNumber ?? string.Empty;

            string? typeId = device.TypeIdentifier;
            if (!string.IsNullOrEmpty(typeId) && typeId.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase))
                return typeId.Substring("OrderNumber:".Length);

            return modules.Count > 0 ? modules[0].OrderNumber : string.Empty;
        }
    }
}