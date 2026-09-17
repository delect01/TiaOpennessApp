using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Compares two network-device exports (expected = source/reference, actual = generated)
    /// and produces a topology diff report covering devices, modules, interfaces and subnets.
    /// Naming-only differences are reported as "Explained"; structural mismatches (types,
    /// order numbers, slots, IPs, subnet connections) are reported as "Difference".
    /// </summary>
    public class TopologyDiffService
    {
        public TopologyDiffReport Compare(
            IReadOnlyList<NetworkDeviceInfo> expected,
            IReadOnlyList<NetworkDeviceInfo> actual,
            IReadOnlyCollection<string>? knownGapDevices = null)
        {
            var report = new TopologyDiffReport();
            var actualList = actual.ToList();
            var usedActual = new HashSet<int>();

            foreach (NetworkDeviceInfo exp in expected)
            {
                int? actIndex = FindUnusedDeviceMatch(actualList, exp, usedActual);

                if (actIndex == null)
                {
                    if (knownGapDevices != null &&
                        knownGapDevices.Contains(exp.DeviceName, StringComparer.OrdinalIgnoreCase))
                    {
                        Add(report, "Devices", TopologyDiffLevel.Explained, exp.DeviceName,
                            exp.DeviceName, "<missing>",
                            "Dispositiu GSD sense llicència al catàleg; inserció manual prevista (gap conegut).");
                        report.Explained++;
                        report.KnownGaps++;
                        continue;
                    }

                    Add(report, "Devices", TopologyDiffLevel.Difference, exp.DeviceName,
                        exp.DeviceName, "<missing>",
                        "Dispositiu present a l'origen i absent al projecte generat.");
                    report.Differences++;
                    continue;
                }

                NetworkDeviceInfo act = actualList[actIndex.Value];
                usedActual.Add(actIndex.Value);
                report.DevicesCompared++;

                CompareDevice(report, exp, act);
                report.ModulesCompared += CompareModules(report, exp, act);
                report.InterfacesCompared += CompareInterfaces(report, exp, act);
            }

            for (int i = 0; i < actualList.Count; i++)
            {
                if (usedActual.Contains(i))
                    continue;
                NetworkDeviceInfo act = actualList[i];
                Add(report, "Devices", TopologyDiffLevel.Difference, act.DeviceName,
                    "<none>", act.DeviceName,
                    "Dispositiu present al projecte generat i absent a l'origen.");
                report.Differences++;
            }

            CompareSubnets(report, expected, actual);
            return report;
        }

        private static void CompareDevice(TopologyDiffReport report, NetworkDeviceInfo exp, NetworkDeviceInfo act)
        {
            if (!Same(exp.StationType, act.StationType))
            {
                Add(report, "Devices", TopologyDiffLevel.Difference, exp.DeviceName,
                    exp.StationType, act.StationType, "StationType del dispositiu diferent.");
                report.Differences++;
            }

            if (!Same(exp.DeviceOrderNumber, act.DeviceOrderNumber))
            {
                Add(report, "Devices", TopologyDiffLevel.Difference, exp.DeviceName,
                    exp.DeviceOrderNumber, act.DeviceOrderNumber, "OrderNumber del dispositiu diferent.");
                report.Differences++;
            }

            if (!Same(exp.DeviceName, act.DeviceName))
            {
                Add(report, "Devices", TopologyDiffLevel.Explained, exp.DeviceName,
                    exp.DeviceName, act.DeviceName,
                    "Nom del dispositiu diferent (el nom s'agafa dels documents de generació).");
                report.Explained++;
            }
        }

        private static int CompareModules(TopologyDiffReport report, NetworkDeviceInfo exp, NetworkDeviceInfo act)
        {
            var slotted = new Dictionary<int, NetworkModuleInfo>();
            foreach (NetworkModuleInfo m in act.Modules)
                slotted[m.PositionNumber] = m;

            var usedSlots = new HashSet<int>();
            int compared = 0;

            foreach (NetworkModuleInfo em in exp.Modules)
            {
                NetworkModuleInfo? am = slotted.TryGetValue(em.PositionNumber, out NetworkModuleInfo? value) ? value : null;
                if (am == null)
                {
                    if (LooksLikeBusAdapter(em))
                    {
                        Add(report, "Modules", TopologyDiffLevel.Explained, ModuleScope(exp.DeviceName, em.Name),
                            em.Name, "<missing>",
                            "BA/bus adapter sense slot als documents; no es genera (explicat).");
                        report.Explained++;
                    }
                    else
                    {
                        Add(report, "Modules", TopologyDiffLevel.Difference, ModuleScope(exp.DeviceName, em.Name),
                            em.Name, "<missing>",
                            $"Mòdul present a l'origen i absent al generat (slot {em.PositionNumber}).");
                        report.Differences++;
                    }
                    continue;
                }

                usedSlots.Add(em.PositionNumber);
                compared++;

                if (!Same(em.OrderNumber, am.OrderNumber))
                {
                    Add(report, "Modules", TopologyDiffLevel.Difference, ModuleScope(exp.DeviceName, em.Name),
                        em.OrderNumber, am.OrderNumber, "OrderNumber del mòdul diferent.");
                    report.Differences++;
                }

                if (!Same(em.TypeName, am.TypeName))
                {
                    Add(report, "Modules", TopologyDiffLevel.Difference, ModuleScope(exp.DeviceName, em.Name),
                        em.TypeName, am.TypeName, "Tipus del mòdul diferent.");
                    report.Differences++;
                }

                if (!Same(em.Name, am.Name))
                {
                    Add(report, "Modules", TopologyDiffLevel.Explained, ModuleScope(exp.DeviceName, em.Name),
                        em.Name, am.Name, "Nom del mòdul diferent (naming dels documents).");
                    report.Explained++;
                }
            }

            foreach (NetworkModuleInfo am in act.Modules)
            {
                if (usedSlots.Contains(am.PositionNumber))
                    continue;
                Add(report, "Modules", TopologyDiffLevel.Difference, ModuleScope(act.DeviceName, am.Name),
                    "<none>", am.Name,
                    $"Mòdul present al generat i absent a l'origen (slot {am.PositionNumber}).");
                report.Differences++;
            }

            return compared;
        }

        private static int CompareInterfaces(TopologyDiffReport report, NetworkDeviceInfo exp, NetworkDeviceInfo act)
        {
            int compared = 0;
            var used = new HashSet<int>();

            for (int i = 0; i < exp.Interfaces.Count; i++)
            {
                NetworkInterfaceInfo ei = exp.Interfaces[i];
                NetworkInterfaceInfo? ai = FindInterfaceMatch(act.Interfaces, ei, used, i);

                if (ai == null)
                {
                    Add(report, "Interfaces", TopologyDiffLevel.Difference, InterfaceScope(exp.DeviceName, ei.InterfaceName),
                        ei.IpAddress, "<missing>",
                        "Interfície present a l'origen i absent al generat.");
                    report.Differences++;
                    continue;
                }

                used.Add(act.Interfaces.IndexOf(ai));
                compared++;

                if (!Same(ei.IpAddress, ai.IpAddress))
                {
                    Add(report, "Interfaces", TopologyDiffLevel.Difference, InterfaceScope(exp.DeviceName, ei.InterfaceName),
                        ei.IpAddress, ai.IpAddress, "IP diferent.");
                    report.Differences++;
                }

                if (!Same(NormalizeSubnet(ei.Subnet), NormalizeSubnet(ai.Subnet)))
                {
                    Add(report, "Interfaces", TopologyDiffLevel.Difference, InterfaceScope(exp.DeviceName, ei.InterfaceName),
                        ei.Subnet, ai.Subnet, "Connexió de subxarxa diferent.");
                    report.Differences++;
                }

                if (!Same(ei.InterfaceType, ai.InterfaceType))
                {
                    Add(report, "Interfaces", TopologyDiffLevel.Difference, InterfaceScope(exp.DeviceName, ei.InterfaceName),
                        ei.InterfaceType, ai.InterfaceType, "Tipus d'interfície diferent.");
                    report.Differences++;
                }

                if (!Same(ei.InterfaceName, ai.InterfaceName))
                {
                    Add(report, "Interfaces", TopologyDiffLevel.Explained, InterfaceScope(exp.DeviceName, ei.InterfaceName),
                        ei.InterfaceName, ai.InterfaceName, "Nom de la interfície diferent.");
                    report.Explained++;
                }
            }

            for (int i = 0; i < act.Interfaces.Count; i++)
            {
                if (used.Contains(i))
                    continue;
                NetworkInterfaceInfo ai = act.Interfaces[i];
                Add(report, "Interfaces", TopologyDiffLevel.Difference, InterfaceScope(act.DeviceName, ai.InterfaceName),
                    "<none>", ai.IpAddress,
                    "Interfície present al generat i absent a l'origen.");
                report.Differences++;
            }

            return compared;
        }

        private static void CompareSubnets(
            TopologyDiffReport report,
            IReadOnlyList<NetworkDeviceInfo> expected,
            IReadOnlyList<NetworkDeviceInfo> actual)
        {
            var expSubnets = CollectSubnets(expected);
            var actSubnets = CollectSubnets(actual);

            foreach (string subnet in expSubnets)
            {
                if (actSubnets.Contains(subnet))
                {
                    report.SubnetsCompared++;
                    Add(report, "Subnets", TopologyDiffLevel.Ok, subnet, subnet, subnet, "Subxarxa coincident.");
                }
                else
                {
                    Add(report, "Subnets", TopologyDiffLevel.Difference, subnet, subnet, "<missing>",
                        "Subxarxa present a l'origen i absent al generat.");
                    report.Differences++;
                }
            }

            foreach (string subnet in actSubnets)
            {
                if (expSubnets.Contains(subnet))
                    continue;
                Add(report, "Subnets", TopologyDiffLevel.Difference, subnet, "<none>", subnet,
                    "Subxarxa present al generat i absent a l'origen.");
                report.Differences++;
            }
        }

        private static HashSet<string> CollectSubnets(IReadOnlyList<NetworkDeviceInfo> devices)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkDeviceInfo device in devices)
                foreach (NetworkInterfaceInfo iface in device.Interfaces)
                {
                    string subnet = NormalizeSubnet(iface.Subnet);
                    if (subnet != "Not connected")
                        set.Add(subnet);
                }
            return set;
        }

        private static int? FindUnusedDeviceMatch(
            IReadOnlyList<NetworkDeviceInfo> actual,
            NetworkDeviceInfo exp,
            ISet<int> used)
        {
            for (int i = 0; i < actual.Count; i++)
            {
                if (used.Contains(i))
                    continue;
                if (Same(exp.DeviceName, actual[i].DeviceName))
                    return i;
            }

            string expKey = DeviceIdentityKey(exp);
            if (expKey.Length == 0)
                return null;

            for (int i = 0; i < actual.Count; i++)
            {
                if (used.Contains(i))
                    continue;
                string key = DeviceIdentityKey(actual[i]);
                if (key.Length > 0 && Same(expKey, key))
                    return i;
            }

            return null;
        }

        private static NetworkInterfaceInfo? FindInterfaceMatch(
            IReadOnlyList<NetworkInterfaceInfo> list,
            NetworkInterfaceInfo target,
            ISet<int> used,
            int index)
        {
            if (!string.IsNullOrWhiteSpace(target.IpAddress))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (used.Contains(i))
                        continue;
                    if (!string.IsNullOrWhiteSpace(list[i].IpAddress) && Same(list[i].IpAddress, target.IpAddress))
                        return list[i];
                }
                return null;
            }

            return index >= 0 && index < list.Count && !used.Contains(index) ? list[index] : null;
        }

        private static string DeviceIdentityKey(NetworkDeviceInfo device)
        {
            string station = Normalize(device.StationType);
            string order = Normalize(device.DeviceOrderNumber);
            return station.Length == 0 && order.Length == 0 ? string.Empty : station + "|" + order;
        }

        private static bool LooksLikeBusAdapter(NetworkModuleInfo module)
        {
            string text = ((module.Name ?? string.Empty) + " " + (module.TypeName ?? string.Empty)).ToUpperInvariant();
            return text.Contains("2XRJ45")
                || text.Contains("BUS ADAPTER")
                || text.Contains(" BA ")
                || text.StartsWith("BA ", StringComparison.Ordinal)
                || text.StartsWith("BA_", StringComparison.Ordinal);
        }

        private static string NormalizeSubnet(string? subnet)
        {
            if (string.IsNullOrWhiteSpace(subnet) || string.Equals(subnet, "Not connected", StringComparison.OrdinalIgnoreCase))
                return "Not connected";
            return Normalize(subnet) ?? string.Empty;
        }

        private static bool Same(string? a, string? b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            string trimmed = text!.Trim();
            return Regex.Replace(trimmed, @"\s+", " ");
        }

        private static string ModuleScope(string deviceName, string moduleName)
        {
            return deviceName + " / " + moduleName;
        }

        private static string InterfaceScope(string deviceName, string interfaceName)
        {
            return deviceName + " / " + interfaceName;
        }

        private static void Add(
            TopologyDiffReport report,
            string category,
            TopologyDiffLevel level,
            string scope,
            string expected,
            string actual,
            string message)
        {
            report.Items.Add(new TopologyDiffItem
            {
                Category = category,
                Level = level,
                Scope = scope,
                Expected = expected,
                Actual = actual,
                Message = message,
            });
        }
    }
}