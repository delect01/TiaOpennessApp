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
        private readonly TiaConnection _connection;
        private readonly HardwareService _hardware;

        public DevicesAndNetworksService(TiaConnection connection)
        {
            _connection = connection;
            _hardware = new HardwareService(connection);
        }

        public DevicesAndNetworksReport ExportAll(bool includeSubnets = true)
        {
            var project = _connection.Project;

            var report = new DevicesAndNetworksReport
            {
                Operation = includeSubnets
                    ? "Offline read-only extraction of Devices & Networks view using TIA Portal V21 Openness API"
                    : "Offline read-only extraction of devices using TIA Portal V21 Openness API",
                Project = new ProjectMetadata
                {
                    Name = project.Name,
                    Path = project.Path.FullName,
                    Version = project.Version,
                    CreationTime = project.CreationTime,
                    LastModified = project.LastModified,
                },
            };

            foreach (DeviceGroup group in project.DeviceGroups)
            {
                report.DeviceGroups.Add(BuildGroupInfo(group, "DeviceGroups"));
            }
            report.GroupCount = report.DeviceGroups.Count;

            foreach (Device device in project.UngroupedDevicesGroup.Devices)
            {
                report.UngroupedDevices.Add(_hardware.BuildDeviceSummary(device));
            }

            if (includeSubnets)
            {
                foreach (Subnet subnet in project.Subnets)
                {
                    report.Subnets.Add(BuildSubnetInfo(subnet));
                }
            }

            int totalDevices = report.UngroupedDevices.Count;
            foreach (var group in report.DeviceGroups)
            {
                totalDevices += CountGroupDevices(group);
            }
            report.DeviceCount = totalDevices;
            report.SubnetCount = report.Subnets.Count;

            return report;
        }

        public void PrintDevicesAndNetworks()
        {
            var project = _connection.Project;
            var report = ExportAll();

            Console.WriteLine($"  Project:  {project.Name} ({project.Version})");
            Console.WriteLine();

            Console.WriteLine("━━━ DEVICE GROUPS ━━━");
            if (report.DeviceGroups.Count == 0)
            {
                Console.WriteLine("  (No user-defined device groups)");
            }
            for (int i = 0; i < report.DeviceGroups.Count; i++)
            {
                PrintGroup(report.DeviceGroups[i], "  ", i == report.DeviceGroups.Count - 1);
            }

            Console.WriteLine();
            Console.WriteLine($"━━━ DEVICES (ungrouped) ━━━  ({report.UngroupedDevices.Count})");
            PrintDevicesList(report.UngroupedDevices, "  ");

            Console.WriteLine();
            Console.WriteLine($"━━━ SUBNETS ━━━  ({report.Subnets.Count})");
            if (report.Subnets.Count == 0)
            {
                Console.WriteLine("  (No subnets in project)");
            }
            for (int i = 0; i < report.Subnets.Count; i++)
            {
                PrintSubnet(report.Subnets[i], "  ", i == report.Subnets.Count - 1);
            }

            Console.WriteLine();
            Console.WriteLine($"  Totals: {report.GroupCount} group(s), {report.DeviceCount} device(s), {report.SubnetCount} subnet(s)");
        }

        private DeviceGroupInfo BuildGroupInfo(DeviceGroup group, string parentPath)
        {
            string groupPath = parentPath + "/" + group.Name;
            var info = new DeviceGroupInfo
            {
                Name = group.Name,
                Path = groupPath,
            };

            if (group is DeviceUserGroup userGroup)
            {
                foreach (DeviceUserGroup child in userGroup.Groups)
                {
                    info.SubGroups.Add(BuildGroupInfo(child, groupPath));
                }
            }

            foreach (Device device in group.Devices)
            {
                info.Devices.Add(_hardware.BuildDeviceSummary(device));
            }

            info.SubGroupCount = info.SubGroups.Count;
            info.DeviceCount = info.Devices.Count;
            return info;
        }

        private static SubnetInfo BuildSubnetInfo(Subnet subnet)
        {
            var info = new SubnetInfo
            {
                Name = subnet.Name,
                NetType = subnet.NetType.ToString(),
                TypeIdentifier = subnet.TypeIdentifier,
            };

            foreach (Node node in subnet.Nodes)
            {
                info.Nodes.Add(BuildNodeInfo(node, subnet.Name));
            }
            info.NodeCount = info.Nodes.Count;
            return info;
        }

        private static NodeInfo BuildNodeInfo(Node node, string subnetName)
        {
            return new NodeInfo
            {
                Name = node.Name,
                NodeType = node.NodeType.ToString(),
                NodeId = node.NodeId.ToString(),
                ConnectedSubnet = node.ConnectedSubnet?.Name ?? subnetName,
                HostPath = GetNodeHostPath(node),
                IpAddress = GetIpAddress(node),
            };
        }

        private static string GetNodeHostPath(Node node)
        {
            try
            {
                if (node.Parent is DeviceItem item)
                {
                    var parts = new List<string>();
                    HardwareObject container = item.Container;
                    parts.Insert(0, item.Name);

                    while (container is DeviceItem parentItem)
                    {
                        parts.Insert(0, parentItem.Name);
                        container = parentItem.Container;
                    }

                    if (container is Device device)
                    {
                        parts.Insert(0, device.Name);
                    }

                    return string.Join("/", parts);
                }

                return node.Parent?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetIpAddress(Node node)
        {
            try
            {
                if (node.Parent is IEngineeringObject engObj)
                {
                    return (engObj.GetAttribute("IpAddress") as string) ?? string.Empty;
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int CountGroupDevices(DeviceGroupInfo info)
        {
            int count = info.Devices.Count;
            foreach (var child in info.SubGroups)
            {
                count += CountGroupDevices(child);
            }
            return count;
        }

        private static void PrintGroup(DeviceGroupInfo group, string indent, bool isLast)
        {
            string connector = isLast ? "└── " : "├── ";
            Console.WriteLine($"{indent}{connector}[G] {group.Name}  ({group.DeviceCount} dev, {group.SubGroupCount} sub-groups)");
            string childIndent = indent + (isLast ? "    " : "│   ");

            PrintDevicesList(group.Devices, childIndent);

            for (int i = 0; i < group.SubGroups.Count; i++)
            {
                PrintGroup(group.SubGroups[i], childIndent, i == group.SubGroups.Count - 1);
            }
        }

        private static void PrintDevicesList(List<DeviceSummary> devices, string indent)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                bool isLast = i == devices.Count - 1;
                string connector = isLast ? "└── " : "├── ";
                Console.WriteLine($"{indent}{connector}[D] {device.Name}  ({device.TypeName ?? "unknown"})");
            }
        }

        private static void PrintSubnet(SubnetInfo subnet, string indent, bool isLast)
        {
            string connector = isLast ? "└── " : "├── ";
            Console.WriteLine($"{indent}{connector}[S] {subnet.Name}  ({subnet.NetType})  nodes: {subnet.NodeCount}");
            string childIndent = indent + (isLast ? "    " : "│   ");

            for (int i = 0; i < subnet.Nodes.Count; i++)
            {
                var node = subnet.Nodes[i];
                string v = subnet.Nodes.Count == 1 ? "└── " : (i == subnet.Nodes.Count - 1 ? "└── " : "├── ");
                string ip = string.IsNullOrEmpty(node.IpAddress) ? string.Empty : $"  IP: {node.IpAddress}";
                Console.WriteLine($"{childIndent}{v}N: {node.Name} ({node.NodeType})  @ {node.HostPath}{ip}");
            }
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
                        });
                    }
                    else if (IsInterfaceItem(item))
                    {
                        info.Interfaces.Add(new NetworkInterfaceInfo
                        {
                            InterfaceName = item.Name,
                            InterfaceType = GetInterfaceTypeName(item),
                            Subnet = "Not connected",
                        });
                    }

                    if (!string.IsNullOrEmpty(orderNumber))
                    {
                        info.Modules.Add(new NetworkModuleInfo
                        {
                            Name = item.Name,
                            TypeName = GetTypeName(item) ?? string.Empty,
                            OrderNumber = orderNumber ?? string.Empty,
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
            var project = _connection.Project;
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

        private IEnumerable<Device> EnumerateAllDevices()
        {
            var seen = new HashSet<string>();
            var project = _connection.Project;

            foreach (Device device in project.Devices)
                if (seen.Add(device.Name)) yield return device;

            foreach (Device device in project.UngroupedDevicesGroup.Devices)
                if (seen.Add(device.Name)) yield return device;

            foreach (DeviceGroup group in project.DeviceGroups)
                foreach (Device device in EnumerateGroupDevices(group))
                    if (seen.Add(device.Name)) yield return device;
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

        private static IEnumerable<DeviceItem> EnumerateItems(Device device)
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

        private static IEnumerable<DeviceItem> EnumerateItemChildren(DeviceItem item, HashSet<string> seen)
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