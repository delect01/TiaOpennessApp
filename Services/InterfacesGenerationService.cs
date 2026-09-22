using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class InterfaceGenerationItemReport
    {
        public string DeviceName { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string Subnet { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string PnDeviceName { get; set; } = string.Empty;
        public bool Connected { get; set; }
        public bool Skipped { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }

    public class InterfacesGenerationService
    {
        private readonly Project _project;

        public InterfacesGenerationService(Project project)
        {
            _project = project;
        }

        public List<InterfaceGenerationItemReport> Generate(GenerationDocument doc)
        {
            var results = new List<InterfaceGenerationItemReport>();
            var usedItems = new Dictionary<string, List<DeviceItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var spec in doc.Interfaces)
            {
                var report = new InterfaceGenerationItemReport
                {
                    DeviceName = spec.Device,
                    InterfaceName = spec.InterfaceItem,
                    Subnet = spec.Subnet,
                    IpAddress = spec.IpAddress,
                    SubnetMask = spec.SubnetMask,
                    PnDeviceName = spec.PnDeviceName,
                };

                try
                {
                    Device? device = FindDevice(spec.Device);
                    if (device == null)
                    {
                        report.Error = "Dispositiu no trobat al projecte.";
                        results.Add(report);
                        continue;
                    }

                    DeviceItem? item = FindItemByNameOrFreeInterface(device, spec.InterfaceItem, usedItems);
                    if (item == null)
                    {
                        report.Error = $"Item d'interfície '{spec.InterfaceItem}' no trobat al dispositiu.";
                        results.Add(report);
                        continue;
                    }

                    NetworkInterface? netIf = GetNetworkInterface(item);
                    if (netIf == null)
                    {
                        report.Error = "'" + spec.InterfaceItem + "' no ofereix servei NetworkInterface.";
                        results.Add(report);
                        continue;
                    }

                    Node? node = netIf.Nodes.Cast<Node?>().FirstOrDefault();

                    int actions = 0;
                    var steps = new List<string>();

                    if (!string.IsNullOrWhiteSpace(spec.Subnet))
                    {
                        if (node != null && node.ConnectedSubnet != null)
                        {
                            steps.Add($"ja connectada a '{node.ConnectedSubnet.Name}'");
                        }
                        else if (node == null)
                        {
                            report.Error = "La interfície no té cap node.";
                            results.Add(report);
                            continue;
                        }
                        else
                        {
                            Subnet? subnet = _project.Subnets.Find(spec.Subnet);
                            if (subnet == null)
                            {
                                report.Error = $"Subxarxa '{spec.Subnet}' no trobada al projecte.";
                                results.Add(report);
                                continue;
                            }

                            node.ConnectToSubnet(subnet);
                            report.Connected = true;
                            actions++;
                            steps.Add($"connectada a '{spec.Subnet}'");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(spec.IpAddress))
                    {
                        bool changed = WriteAddressAttribute(item, node, spec.IpAddress, "IP", new[] { "IpAddress", "Ipv4Address", "Address" });
                        if (changed)
                            actions++;
                        steps.Add(changed ? $"IP={spec.IpAddress}" : "IP ja configurada");
                    }

                    if (!string.IsNullOrWhiteSpace(spec.SubnetMask))
                    {
                        bool changed = WriteAddressAttribute(item, node, spec.SubnetMask, "màscara", new[] { "SubnetMask", "Mask", "NetworkMask" });
                        if (changed)
                            actions++;
                        steps.Add(changed ? $"màscara={spec.SubnetMask}" : "màscara ja configurada");
                    }

                    if (!string.IsNullOrWhiteSpace(spec.PnDeviceName))
                    {
                        try
                        {
                            bool changed = WritePnDeviceName(item, node, spec.PnDeviceName, out string? nameUsed);
                            if (changed)
                                actions++;
                            steps.Add(changed
                                ? $"PN name configurat ({nameUsed ?? "atribut"})"
                                : "PN name ja configurat");
                        }
                        catch (Exception ex)
                        {
                            steps.Add($"PN name NO aplicable: {ex.Message}");
                        }
                    }

                    report.Skipped = actions == 0;
                    report.Message = string.Join("; ", steps);
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }

                results.Add(report);
            }

            AssignIoSystems(doc, results);
            return results;
        }

        private void AssignIoSystems(GenerationDocument doc, List<InterfaceGenerationItemReport> results)
        {
            var ioSystemsByName = new Dictionary<string, IoSystem>(StringComparer.OrdinalIgnoreCase);
            var usedForIo = new Dictionary<string, List<DeviceItem>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < doc.Interfaces.Count; i++)
            {
                InterfaceSpec spec = doc.Interfaces[i];
                InterfaceGenerationItemReport report = results[i];

                Device? device = FindDevice(spec.Device);
                if (device == null)
                    continue;
                DeviceItem? item = FindItemByNameOrFreeInterface(device, spec.InterfaceItem, usedForIo);
                if (item == null)
                    continue;

                NetworkInterface? netIf = GetNetworkInterface(item);
                if (netIf == null)
                    continue;

                if ((netIf.InterfaceOperatingMode & InterfaceOperatingModes.IoController) != 0
                    && !string.IsNullOrWhiteSpace(spec.MasterIoSystem))
                {
                    try
                    {
                        IoController? controller = netIf.IoControllers.Cast<IoController>().FirstOrDefault();
                        if (controller != null)
                        {
                            IoSystem ioSystem = controller.IoSystem;
                            if (ioSystem == null)
                                ioSystem = controller.CreateIoSystem(spec.MasterIoSystem.Trim());
                            else if (!string.Equals(ioSystem.Name, spec.MasterIoSystem, StringComparison.OrdinalIgnoreCase))
                                ioSystem.Name = spec.MasterIoSystem.Trim();

                            ioSystemsByName[spec.MasterIoSystem.Trim()] = ioSystem;
                            report.Message = AppendIoStep(report.Message, $"IO system '{ioSystem.Name}' garantit al controlador");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Error = "Error creant IO system: " + ex.Message;
                    }
                }

                IoConnector? connector = netIf.IoConnectors.Cast<IoConnector>().FirstOrDefault();
                if (connector == null)
                    continue;

                IoSystem? target = null;
                if (!string.IsNullOrWhiteSpace(spec.MasterIoSystem))
                    ioSystemsByName.TryGetValue(spec.MasterIoSystem.Trim(), out target);
                else
                {
                    IoSystem? sameSubnet = ioSystemsByName.Values
                        .Where(s => string.Equals(s.Subnet?.Name, spec.Subnet, StringComparison.OrdinalIgnoreCase))
                        .SingleOrDefault();
                    target = sameSubnet;
                }

                if (target == null)
                {
                    report.Message = AppendIoStep(report.Message,
                        "IO: cap IO system assignable (sense MasterIOSystem o sense IO system al projecte)");
                    continue;
                }

                try
                {
                    if (ReferenceEquals(connector.ConnectedToIoSystem, target))
                    {
                        report.Message = AppendIoStep(report.Message, $"IO: ja assignada al '{target.Name}'");
                    }
                    else
                    {
                        connector.ConnectToIoSystem(target);
                        report.Message = AppendIoStep(report.Message, $"assignada al IO system '{target.Name}'");
                        report.Connected = true;
                    }
                }
                catch (Exception ex)
                {
                    report.Error = "Error assignant IO device: " + ex.Message;
                }
            }
        }

        private static string AppendIoStep(string? message, string step)
        {
            return string.IsNullOrEmpty(message) ? step : message + "; " + step;
        }

        public void DumpInterfaces()
        {
            foreach (Device device in _project.Devices)
            {
                Console.WriteLine($"DEVICE: {device.Name}");
                foreach (DeviceItem top in device.DeviceItems)
                    DumpInterfaceItems(top);
            }
        }

        private void DumpInterfaceItems(DeviceItem item)
        {
            NetworkInterface? netIf = GetNetworkInterface(item);
            if (netIf != null)
            {
                Console.WriteLine($"  INTERFACE: {item.Name}  TypeIdent={item.TypeIdentifier}");
                foreach (Node node in netIf.Nodes)
                {
                    Console.WriteLine($"    NODE: {node.Name ?? "(unnamed)"}  NodeType={node.NodeType}  Connected={node.ConnectedSubnet?.Name ?? "not connected"}");
                    DumpMatchingAttributes("item", item, new[] { "Address", "Mask", "Name", "Pn" });
                    DumpMatchingAttributes("node", node, new[] { "Address", "Mask", "Name", "Pn" });
                    if (node.Parent is DeviceItem parent)
                        DumpMatchingAttributes("node.Parent", parent, new[] { "Address", "Mask", "Name", "Pn" });
                }
            }

            foreach (DeviceItem child in item.DeviceItems)
                DumpInterfaceItems(child);
        }

        private static void DumpMatchingAttributes(string label, object obj, string[] filters)
        {
            var eng = obj as IEngineeringObject;
            if (eng == null)
                return;

            try
            {
                foreach (string name in eng.GetAttributeInfos().Select(a => a.Name))
                {
                    string upper = name.ToUpperInvariant();
                    bool match = filters.Any(f => upper.Contains(f.ToUpperInvariant()));
                    if (!match)
                        continue;

                    object? value;
                    try { value = eng.GetAttribute(name); }
                    catch { value = "<unreadable>"; }
                    Console.WriteLine($"      [{label}] {name} = {value ?? "<null>"}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [{label}] <GetAttributeInfos failed: {ex.Message}>");
            }
        }

        private static bool WriteAddressAttribute(DeviceItem item, Node? node, string value, string what, string[] candidates)
        {
            foreach (object candidate in CandidateStores(item, node))
            {
                var eng = candidate as IEngineeringObject;
                if (eng == null)
                    continue;

                string? name = FindAttribute(eng, candidates);
                if (name == null)
                    continue;

                string? current = GetStr(eng, name);
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return false;

                eng.SetAttribute(name, value);
                return true;
            }

            return false;
        }

        private static bool WritePnDeviceName(DeviceItem item, Node? node, string value, out string? nameUsed)
        {
            nameUsed = null;
            var stores = CandidateStores(item, node).ToList();

            foreach (IEngineeringObject eng in stores.OfType<IEngineeringObject>())
            {
                string? name = FindAttribute(eng, new[] { "PnDeviceName", "DeviceName", "PnName", "IoDeviceName", "InterfaceDeviceName" });
                if (string.IsNullOrEmpty(name))
                    name = FindPnLikeName(eng);

                if (string.IsNullOrEmpty(name))
                    continue;

                string? current = GetStr(eng, name!);
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return false;

                eng.SetAttribute(name, value);
                nameUsed = name;
                return true;
            }

            return false;
        }

        private static IEnumerable<object> CandidateStores(DeviceItem item, Node? node)
        {
            yield return item;
            if (node != null)
            {
                if (node.Parent != null)
                    yield return node.Parent;
                yield return node;
            }
        }

        private static string? FindPnLikeName(IEngineeringObject eng)
        {
            try
            {
                foreach (string name in eng.GetAttributeInfos().Select(a => a.Name))
                {
                    if (name.Equals("Name", StringComparison.Ordinal))
                        continue;
                    string upper = name.ToUpperInvariant();
                    if (upper.Contains("DEVICENAME") || upper.Contains("PNNAME"))
                    {
                        object? value;
                        try { value = eng.GetAttribute(name); }
                        catch { value = null; }
                        if (value is string current && !string.IsNullOrEmpty(current))
                            return name;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string? FindAttribute(IEngineeringObject eng, string[] candidates)
        {
            try
            {
                var infos = eng.GetAttributeInfos().Select(a => a.Name).ToList();
                foreach (string candidate in candidates)
                    if (infos.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        return infos.First(n => n.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            return null;
        }

        private static string? GetStr(IEngineeringObject eng, string name)
        {
            try { return eng.GetAttribute(name) as string; }
            catch { return null; }
        }

        private Device? FindDevice(string name)
        {
            Device? device = DevicesAndNetworksService.FindDeviceByName(_project, name);
            if (device != null)
                return device;
            device = _project.UngroupedDevicesGroup.Devices.Find(name);
            return device;
        }

        /// <summary>
        /// Busca l'item d'interfície pel nom exacte i, si no es troba, fa fallback
        /// al primer item del dispositiu que ofereixi el servei <see cref="NetworkInterface"/>
        /// i que encara no s'hagi fet servir. Això permet suportar projectes on els noms
        /// dels items canvien (noms per defecte de TIA vs. noms personalitzats de l'origen).
        /// </summary>
        private static DeviceItem? FindItemByNameOrFreeInterface(Device device, string name, Dictionary<string, List<DeviceItem>> used)
        {
            DeviceItem? exact = FindItemByName(device, name);
            if (exact != null)
            {
                GetUsedList(used, device.Name).Add(exact);
                return exact;
            }

            List<DeviceItem> usedList = GetUsedList(used, device.Name);
            foreach (DeviceItem item in EnumerateAllItems(device))
            {
                if (usedList.Contains(item))
                    continue;
                if (GetNetworkInterface(item) != null)
                {
                    usedList.Add(item);
                    return item;
                }
            }
            return null;
        }

        private static List<DeviceItem> GetUsedList(Dictionary<string, List<DeviceItem>> used, string deviceName)
        {
            if (!used.TryGetValue(deviceName, out List<DeviceItem>? list))
            {
                list = new List<DeviceItem>();
                used[deviceName] = list;
            }
            return list;
        }

        private static IEnumerable<DeviceItem> EnumerateAllItems(Device device)
        {
            foreach (DeviceItem top in device.DeviceItems)
            {
                yield return top;
                foreach (DeviceItem child in EnumerateItemTree(top))
                    yield return child;
            }
        }

        private static IEnumerable<DeviceItem> EnumerateItemTree(DeviceItem item)
        {
            foreach (DeviceItem child in item.DeviceItems)
            {
                yield return child;
                foreach (DeviceItem grand in EnumerateItemTree(child))
                    yield return grand;
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

        private static NetworkInterface? GetNetworkInterface(object obj)
        {
            try { return ((IEngineeringServiceProvider)obj).GetService<NetworkInterface>(); }
            catch { return null; }
        }
    }
}