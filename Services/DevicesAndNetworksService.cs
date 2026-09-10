using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
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
    }
}