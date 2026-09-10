using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    public class IoChannelService
    {
        private readonly TiaConnection _connection;

        public IoChannelService(TiaConnection connection)
        {
            _connection = connection;
        }

        public IoChannelExportReport ExportAll()
        {
            var project = _connection.Project;

            var report = new IoChannelExportReport
            {
                Operation = "Offline read-only extraction of I/O channels (stations, racks, modules) using TIA Portal V21 Openness API",
                Project = new ProjectMetadata
                {
                    Name = project.Name,
                    Path = project.Path.FullName,
                    Version = project.Version,
                    CreationTime = project.CreationTime,
                    LastModified = project.LastModified,
                },
            };

            int moduleCount = 0;
            int channelCount = 0;

            foreach (Device device in project.Devices)
            {
                var station = new IoStationInfo { Name = device.Name };
                var modules = new List<(string Rack, IoModuleInfo Module)>();

                foreach (DeviceItem root in device.DeviceItems)
                {
                    CollectModules(root, device.Name, modules);
                }

                if (modules.Count == 0)
                    continue;

                var racks = new Dictionary<string, IoRackInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var (rackName, module) in modules)
                {
                    if (!racks.TryGetValue(rackName, out IoRackInfo? rack))
                    {
                        rack = new IoRackInfo { Name = rackName };
                        racks.Add(rackName, rack);
                    }
                    rack.Modules.Add(module);
                    moduleCount++;
                    channelCount += module.Channels.Count;
                }

                station.Racks.AddRange(racks.Values);
                report.Stations.Add(station);
            }

            report.StationCount = report.Stations.Count;
            report.ModuleCount = moduleCount;
            report.ChannelCount = channelCount;
            return report;
        }

        public void PrintIoChannels()
        {
            var report = ExportAll();

            Console.WriteLine("━━━ I/O CHANNELS (stations / racks / modules) ━━━");
            if (report.Stations.Count == 0)
            {
                Console.WriteLine("  (No I/O modules with channels found)");
                return;
            }

            foreach (IoStationInfo station in report.Stations)
            {
                Console.WriteLine($"[Station] {station.Name}");
                foreach (IoRackInfo rack in station.Racks)
                {
                    Console.WriteLine($"  └── [Rack] {rack.Name}");
                    foreach (IoModuleInfo module in rack.Modules)
                    {
                        string range = GetAddressRange(module);
                        Console.WriteLine($"      ├── {module.Name}  ({module.Channels.Count} ch)  {range}");
                        foreach (IoChannelInfo channel in module.Channels)
                        {
                            string tag = string.IsNullOrEmpty(channel.Tag) ? "" : $"  tag: {channel.Tag}";
                            Console.WriteLine($"      │    [{channel.Number:d2}] {channel.Address}{tag}");
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"  Totals: {report.StationCount} station(s), {report.ModuleCount} module(s), {report.ChannelCount} channel(s)");
        }

        public void ExportToXml(string outputPath)
        {
            var report = ExportAll();

            var root = new XElement("Stations");
            foreach (IoStationInfo station in report.Stations)
            {
                var stationElem = new XElement("Station", new XAttribute("Name", station.Name));
                foreach (IoRackInfo rack in station.Racks)
                {
                    var rackElem = new XElement("Rack", new XAttribute("Name", rack.Name));
                    foreach (IoModuleInfo module in rack.Modules)
                    {
                        var moduleElem = new XElement("Module", new XAttribute("Name", module.Name));
                        foreach (IoChannelInfo channel in module.Channels)
                        {
                            moduleElem.Add(new XElement("IOChannel",
                                new XAttribute("Number", channel.Number),
                                new XElement("Address", channel.Address),
                                new XElement("Tag", channel.Tag)));
                        }
                        rackElem.Add(moduleElem);
                    }
                    stationElem.Add(rackElem);
                }
                root.Add(stationElem);
            }

            var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            document.Save(outputPath);

            Console.WriteLine($"Exported {report.StationCount} stations, {report.ModuleCount} modules, {report.ChannelCount} channels to: {outputPath}");
        }

        private static void CollectModules(DeviceItem item, string deviceName, List<(string Rack, IoModuleInfo Module)> modules, HashSet<DeviceItem>? visited = null, string currentRack = "Rack_0")
        {
            visited ??= new HashSet<DeviceItem>();
            if (!visited.Add(item))
                return;

            if (item.Name.IndexOf("Rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                currentRack = item.Name;
            }

            if (item.Channels != null && item.Channels.Count > 0)
            {
                modules.Add((currentRack, BuildModuleInfo(item)));
            }

            foreach (DeviceItem child in GetChildren(item))
            {
                CollectModules(child, deviceName, modules, visited, currentRack);
            }
        }

        private static IoModuleInfo BuildModuleInfo(DeviceItem item)
        {
            var module = new IoModuleInfo { Name = item.Name };

            foreach (Channel channel in item.Channels)
            {
                module.Channels.Add(new IoChannelInfo
                {
                    Number = channel.Number,
                    Address = FormatChannelAddress(channel, item),
                    Tag = GetChannelTag(channel),
                });
            }

            return module;
        }

        private static string FormatChannelAddress(Channel channel, DeviceItem module)
        {
            string prefix = channel.IoType == ChannelIoType.Input ? "%I"
                          : channel.IoType == ChannelIoType.Output ? "%Q"
                          : "%";

            string? attr = ReadAttribute(channel, "Address");
            if (!string.IsNullOrWhiteSpace(attr))
            {
                string addr = attr!.Trim();
                return addr.StartsWith("%") ? addr : prefix + addr;
            }

            if (module.Addresses != null)
            {
                foreach (var address in module.Addresses)
                {
                    if (string.Equals(address.IoType.ToString(), channel.IoType.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        int byteAddr = address.StartAddress + channel.Number / 8;
                        return $"{prefix}{byteAddr}.{channel.Number % 8}";
                    }
                }
            }

            return prefix + channel.Number;
        }

        private static string GetChannelTag(Channel channel)
        {
            foreach (string name in new[] { "Tag", "AssignedTag", "AssignedIoTag" })
            {
                string? value = ReadAttribute(channel, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value!.Trim();
            }
            return string.Empty;
        }

        private static string? ReadAttribute(Channel channel, string name)
        {
            try
            {
                return channel.GetAttribute(name)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string GetAddressRange(IoModuleInfo module)
        {
            if (module.Channels.Count == 0)
                return string.Empty;
            string first = module.Channels[0].Address;
            string last = module.Channels[module.Channels.Count - 1].Address;
            return $"( {first} .. {last} )";
        }

        private static IEnumerable<DeviceItem> GetChildren(DeviceItem item)
        {
            if (item.DeviceItems != null && item.DeviceItems.Count > 0)
                return item.DeviceItems;

            if (item.Items != null && item.Items.Count > 0)
                return item.Items.ToList();

            return Enumerable.Empty<DeviceItem>();
        }
    }
}