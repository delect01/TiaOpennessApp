using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Extreu els I/O tags (taules de tags PLC) d'un projecte real i els associa
    /// al dispositiu / mòdul / submòdul de maquinari per rang d'adreces.
    ///
    /// Definició d'"I/O tag" usada aquí: tag d'una taula de tags PLC (PlcTagTable)
    /// amb adreça de maquinari d'entrada (%I...) o sortida (%Q...). Els tags de
    /// memòria (%M), blocs de dades (%DB...) i laterals es descarten.
    ///
    /// L'associació a mòdul/submòdul es calcula fent casar l'adreça del tag amb
    /// els rangs d'adreces exposats per <c>DeviceItem.Addresses</c>
    /// (Siemens.Engineering.HW.Address: IoType, StartAddress, Length) de tots els
    /// mòduls del projecte. Quan no hi ha cas, Mòdul/SubMòdul queden buits.
    /// </summary>
    public class TagsExportService
    {
        private readonly Project _project;

        public TagsExportService(Project project)
        {
            _project = project;
        }

        public TagsExportService(TiaConnection connection)
        {
            _project = connection.Project;
        }

        public List<IoTagSpec> ExportIoTags()
        {
            List<AddressRange> ranges = BuildAddressRanges();
            var tags = new List<IoTagSpec>();

            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                try
                {
                    // Patró Openness documentat: el software del dispositiu s'obté via
                    // el servei SoftwareContainer (HW.Features); GetService<T> retorna
                    // null quan el dispositiu no ofereix el servei (HMI, drives, ...).
                    // Fallback: alguns dispositius (ET200SP via IM, racks) exposen el
                    // servei en un DeviceItem concret, no al nivell de Device.
                    var container = FindSoftwareContainer(device);
                    if (container?.Software is not PlcSoftware plcSoftware)
                        continue;

                    WalkTagTableGroup(plcSoftware.TagTableGroup, device, ranges, tags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) '{device.Name}': no s'han pogut llegir els tags: {ex.Message}");
                }
            }

            return tags;
        }

        /// <summary>
        /// Adreces de maquinari (I/Q) per mòdul i submòdul, tal com estan
        /// assignades al projecte real. Es fan servir per reproduir el mapa
        /// d'adreces real al projecte generat (mòduls amb direccionament manual).
        /// </summary>
        public List<ModuleAddressSpec> ExportModuleAddresses()
        {
            var list = new List<ModuleAddressSpec>();

            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                foreach (DeviceItem item in DevicesAndNetworksService.EnumerateItems(device))
                {
                    try
                    {
                        var addresses = item.Addresses;
                        if (addresses == null || addresses.Count == 0)
                            continue;

                        DeviceItem module = FindModuleAncestor(item);
                        foreach (Address address in addresses)
                        {
                            char area;
                            switch (address.IoType)
                            {
                                case AddressIoType.Input: area = 'I'; break;
                                case AddressIoType.Output: area = 'Q'; break;
                                default: continue;
                            }

                            int start = SafeNumber(address.StartAddress);
                            int length = SafeNumber(address.Length);
                            if (length <= 0)
                                continue;

                            list.Add(new ModuleAddressSpec
                            {
                                Device = device.Name,
                                ModuleName = module.Name,
                                SubModuleName = ReferenceEquals(module, item) ? string.Empty : item.Name,
                                TypeIdentifier = SafeString(item.TypeIdentifier),
                                IoType = area.ToString(),
                                StartAddress = start,
                                Length = length,
                            });
                        }
                    }
                    catch { /* items sense adreces exposades (IM, CPU, etc.) */ }
                }
            }

            return list;
        }

        /// <summary>
        /// Ascendent més proper a <paramref name="item"/> que és un mòdul de
        /// maquinari: l'item que penja directament d'un rack o de l'item arrel
        /// del dispositiu. Si l'item mateix és el mòdul, retorna el mateix item.
        /// </summary>
        private static DeviceItem FindModuleAncestor(DeviceItem item)
        {
            DeviceItem current = item;
            try
            {
                while (current.Container is DeviceItem parent)
                {
                    if (IsRack(parent) || parent.Container is not DeviceItem)
                        break;
                    current = parent;
                }
            }
            catch { }
            return current;
        }

        /// <summary>
        /// Localitza el SoftwareContainer d'un dispositiu provant primer el patró
        /// documentat (a nivell de Device) i després els DeviceItems, perquè en
        /// alguns dispositius modul·lars el servei només s'exposa a l'item (IM/CPU).
        /// </summary>
        public static SoftwareContainer? FindSoftwareContainer(Device device)
        {
            try
            {
                SoftwareContainer? atDevice = ((IEngineeringServiceProvider)device).GetService<SoftwareContainer>();
                if (atDevice != null)
                    return atDevice;
            }
            catch { }

            foreach (DeviceItem item in DevicesAndNetworksService.EnumerateItems(device))
            {
                try
                {
                    var atItem = ((IEngineeringServiceProvider)item).GetService<SoftwareContainer>();
                    if (atItem != null)
                        return atItem;
                }
                catch { }
            }

            return null;
        }

        private static void WalkTagTableGroup(
            PlcTagTableGroup? group,
            Device device,
            List<AddressRange> ranges,
            List<IoTagSpec> tags)
        {
            if (group == null)
                return;

            foreach (PlcTagTable table in group.TagTables)
            {
                foreach (PlcTag tag in table.Tags)
                    AddTag(device, table, tag, ranges, tags);
            }

            foreach (PlcTagTableGroup child in group.Groups)
                WalkTagTableGroup(child, device, ranges, tags);
        }

        private static void AddTag(
            Device device,
            PlcTagTable table,
            PlcTag tag,
            List<AddressRange> ranges,
            List<IoTagSpec> tags)
        {
            try
            {
                string address = GetTagAddress(tag);
                if (!TryParseIoAddress(address, out char area, out int byteOffset, out int byteLength))
                    return; // No és E/S (ex. %M, %DB) o format no reconegut -> es descarta.

                var spec = new IoTagSpec
                {
                    Device = device.Name,
                    Name = tag.Name,
                    Type = SafeString(tag.DataTypeName),
                    Address = address,
                    TagTable = table.Name,
                    Comment = SafeString(tag.Comment),
                };

                ResolveModule(area, byteOffset, byteLength, ranges, spec);
                tags.Add(spec);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  (avís) tag '{tag.Name}' ({table.Name}): {ex.Message}");
            }
        }

        /// <summary>
        /// Adreça del tag tal com la mostra TIA (atribut "Path", ex. "%IW72").
        /// L'atribut "Path" és el patró establert a Openness per a la columna
        /// "Address" de les taules de tags; no es documenta a l'XML de la API,
        /// així que es fa fallback a LogicalAddress quan no està disponible.
        /// </summary>
        private static string GetTagAddress(PlcTag tag)
        {
            try
            {
                object? path = tag.GetAttribute("Path");
                if (path is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            catch { }

            try
            {
                object? logical = tag.LogicalAddress;
                return logical?.ToString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // Formats acceptats: %IW72, %IB1, %ID40, %I0.3, i variacions sense '%'
        // (estil S5). Les unitats d'accés B/W/D es converteixen a longitud en bytes.
        private static readonly Regex IoAddressPattern = new Regex(
            @"^%?(?<area>[IQ])(?<kind>[BWD])?(?<byte>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool TryParseIoAddress(string address, out char area, out int byteOffset, out int byteLength)
        {
            area = '\0';
            byteOffset = 0;
            byteLength = 1;

            Match match = IoAddressPattern.Match(address?.Trim() ?? string.Empty);
            if (!match.Success)
                return false;

            area = char.ToUpperInvariant(match.Groups["area"].Value[0]);
            byteOffset = int.Parse(match.Groups["byte"].Value, CultureInfo.InvariantCulture);

            switch (match.Groups["kind"].Value.ToUpperInvariant())
            {
                case "B": byteLength = 1; break;
                case "W": byteLength = 2; break;
                case "D": byteLength = 4; break;
                default: byteLength = 1; break; // accés per bit: pertany al byte indicat
            }
            return true;
        }

        // ── Associació a mòdul/submòdul per rang d'adreces ────────────────────────

        private sealed class AddressRange
        {
            public DeviceItem Item { get; set; } = null!;
            public string DeviceName { get; set; } = string.Empty;
            public int Depth { get; set; }
            public char Area { get; set; } // 'I' / 'Q'
            public int Start { get; set; }
            public int End { get; set; } // exclusiu
        }

        private List<AddressRange> BuildAddressRanges()
        {
            var ranges = new List<AddressRange>();

            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                foreach (DeviceItem item in DevicesAndNetworksService.EnumerateItems(device))
                {
                    try
                    {
                        var addresses = item.Addresses;
                        if (addresses == null || addresses.Count == 0)
                            continue;

                        int depth = DepthOf(item);
                        foreach (Address address in addresses)
                        {
                            char area;
                            switch (address.IoType)
                            {
                                case AddressIoType.Input: area = 'I'; break;
                                case AddressIoType.Output: area = 'Q'; break;
                                default: continue;
                            }

                            int start = SafeNumber(address.StartAddress);
                            int length = SafeNumber(address.Length);
                            if (length <= 0)
                                continue;

                            ranges.Add(new AddressRange
                            {
                                Item = item,
                                DeviceName = device.Name,
                                Depth = depth,
                                Area = area,
                                Start = start,
                                End = start + length,
                            });
                        }
                    }
                    catch { /* items sense adreces exposades (IM, CPU, etc.) */ }
                }
            }

            return ranges;
        }

        /// <summary>Fondària de l'item dins la jerarquia de DeviceItems (0 = fill directe del dispositiu).</summary>
        private static int DepthOf(DeviceItem item)
        {
            int depth = 0;
            try
            {
                HardwareObject container = item.Container;
                while (container is DeviceItem parent)
                {
                    depth++;
                    container = parent.Container;
                }
            }
            catch { }
            return depth;
        }

        private static void ResolveModule(char area, int byteOffset, int byteLength, List<AddressRange>? ranges, IoTagSpec spec)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            AddressRange? best = null;
            foreach (AddressRange range in ranges)
            {
                if (range.Area != area)
                    continue;
                if (range.Start <= byteOffset && byteOffset + byteLength <= range.End)
                {
                    if (best == null)
                    {
                        best = range;
                    }
                    else if (range.Depth > best.Depth)
                    {
                        best = range; // preferir el submòdul més profund
                    }
                    else if (range.Depth == best.Depth && (range.End - range.Start) < (best.End - best.Start))
                    {
                        best = range; // a igual fondària, el rang més petit
                    }
                }
            }

            if (best == null)
                return;

            spec.IoDevice = best.DeviceName;

            // L'item que defineix l'adreça pot ser el mòdul mateix o un submòdul fill.
            // Els "racks" (ex. Rack_0) no es consideren mòduls i se salten a l'ascens.
            try
            {
                if (best.Item.Container is DeviceItem parent && !IsRack(parent))
                {
                    spec.ModuleName = parent.Name;
                    spec.SubModuleName = best.Item.Name;
                }
                else
                {
                    spec.ModuleName = best.Item.Name;
                    spec.SubModuleName = string.Empty;
                }
            }
            catch
            {
                spec.ModuleName = best.Item.Name;
                spec.SubModuleName = string.Empty;
            }
        }

        private static bool IsRack(DeviceItem item)
        {
            try { return item.Name != null && item.Name.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return false; }
        }

        private static string SafeString(object? value)
        {
            return value as string ?? string.Empty;
        }

        private static int SafeNumber(object? value)
        {
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        // ── Diagnòstic (--dump-tags): mostra tota la cadena a runtime ─────────────

        /// <summary>
        /// Inspecciona pas a pas la cadena dispositius → software → tag tables →
        /// tags i imprimeix els atributs reals de cada objecte. Serveix per
        /// diagnosticar per què l'exportació no retorna tags (o noms de camins,
        /// adreces i atributs reals de la versió installada de TIA).
        /// </summary>
        public void DumpDiagnostics()
        {
            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                Console.WriteLine($"DEVICE: {device.Name}  TypeId={SafeTypeName(device.TypeIdentifier)}");

                // 1) Serveis d'enginyeria disponibles al dispositiu (quins existeixen?)
                try
                {
                    var serviceInfos = ((IEngineeringServiceProvider)device).GetServiceInfos();
                    Console.Write("  services: ");
                    Console.WriteLine(string.Join(", ", serviceInfos.Select(i => i.Type?.Name ?? "?")));
                }
                catch (Exception ex) { Console.WriteLine($"  services: EX {ex.Message}"); }

                // 2) SoftwareContainer -> Software: què retorna realment?
                SoftwareContainer? container = FindSoftwareContainer(device);
                if (container == null)
                    Console.WriteLine("  SoftwareContainer: null (ni al Device ni a cap DeviceItem)");
                else
                    Console.WriteLine($"  SoftwareContainer.Software: {container.Software?.GetType().FullName ?? "null"}");

                // 2b) Per-item probe: on viu el servei exactament?
                try
                {
                    var atDevice = ((IEngineeringServiceProvider)device).GetService<SoftwareContainer>();
                    Console.WriteLine($"    device-level SoftwareContainer: {(atDevice == null ? "null" : "present")}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    device-level SoftwareContainer: EX {ex.Message}");
                }
                foreach (DeviceItem item in DevicesAndNetworksService.EnumerateItems(device))
                {
                    string state;
                    try
                    {
                        var c = ((IEngineeringServiceProvider)item).GetService<SoftwareContainer>();
                        state = c == null ? "null" : (c.Software?.GetType().Name ?? "container-sin-software");
                    }
                    catch (Exception ex)
                    {
                        state = $"EX {ex.Message}";
                    }
                    Console.WriteLine($"    item '{item.Name}' SoftwareContainer: {state}");
                }

                // 3) Taules de tags del PlcSoftware
                try
                {
                    if (container?.Software is PlcSoftware plc)
                        DumpTagTableGroup(plc.TagTableGroup, 2);
                }
                catch (Exception ex) { Console.WriteLine($"  tag-tables: EX {ex.Message}"); }
                Console.WriteLine();
            }

            Console.WriteLine($"\n=== RESULTAT EXPORT IoTags: {ExportIoTags().Count} tag(s) (mateix codi que el productiu) ===");
        }

        private static void DumpTagTableGroup(PlcTagTableGroup? group, int indent)
        {
            string pad = new string(' ', indent * 2);
            if (group == null)
            {
                Console.WriteLine($"{pad}(TagTableGroup = null)");
                return;
            }

            Console.WriteLine($"{pad}GROUP: {group.Name}  tables={group.TagTables.Count}  groups={group.Groups.Count}");

            foreach (PlcTagTable table in group.TagTables)
            {
                try
                {
                    Console.WriteLine($"{pad}  TABLE: {table.Name}  (default={table.IsDefault})  tags={table.Tags.Count}");
                    bool first = true;
                    foreach (PlcTag tag in table.Tags)
                    {
                        if (first)
                        {
                            DumpTagAttributeNames(tag, pad + "      ");
                            first = false;
                        }
                        DumpTag(tag, pad + "      ");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"{pad}  TABLE: EX {ex.Message}"); }
            }

            foreach (PlcTagTableGroup child in group.Groups)
                DumpTagTableGroup(child, indent + 1);
        }

        private static void DumpTag(PlcTag tag, string pad)
        {
            string path = ReadAttributeSafe(tag, "Path");
            string name = ReadAttributeSafe(tag, "Name");
            string comment = ReadAttributeSafe(tag, "Comment");
            string tableName = ReadAttributeSafe(tag, "TableName");
            string datatype = ReadAttributeSafe(tag, "Datatype");
            object? logical = ReadObjectSafe(tag, "LogicalAddress");
            string parentType = tag.Parent?.GetType().Name ?? "?";

            Console.WriteLine($"{pad}TAG: Name={tag.Name}  Type={SafeTypeName(tag.DataTypeName)}  " +
                              $"attr.Name='{name}'  attr.Path='{path}'  attr.Datatype='{datatype}'  " +
                              $"attr.TableName='{tableName}'  attr.Comment='{comment}'  " +
                              $"LogicalAddress=[{logical?.GetType().Name ?? "-"}]'{logical?.ToString() ?? "-"}'  Parent={parentType}");
        }

        /// <summary>Llista noms + valors de TOTS els atributs d'un tag: revela el nom real de l'atribut d'adreça.</summary>
        private static void DumpTagAttributeNames(PlcTag tag, string pad)
        {
            Console.WriteLine($"{pad}ATTRS:");
            IEnumerable<EngineeringAttributeInfo> infos;
            try { infos = tag.GetAttributeInfos(); }
            catch (Exception ex) { Console.WriteLine($"{pad}  (GetAttributeInfos EX: {ex.Message})"); return; }

            foreach (EngineeringAttributeInfo info in infos)
            {
                string value = ReadAttributeSafe(tag, info.Name);
                if (value.Length > 60) value = value.Substring(0, 60) + "…";
                Console.WriteLine($"{pad}  {info.Name} = {value}");
            }
        }

        private static string ReadAttributeSafe(IEngineeringObject obj, string attributeName)
        {
            try
            {
                object? value = obj.GetAttribute(attributeName);
                return value?.ToString() ?? string.Empty;
            }
            catch (Exception ex) { return $"<EX {ex.Message}>"; }
        }

        private static object? ReadObjectSafe(IEngineeringObject obj, string attributeName)
        {
            try { return obj.GetAttribute(attributeName); }
            catch { return null; }
        }

        private static string SafeTypeName(object? value)
        {
            return value?.ToString() ?? string.Empty;
        }
    }
}