using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TiaOpennessApp.Models.Catalog;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public sealed class KitFromDeviceResult
    {
        public KitDefinition Kit { get; set; } = new KitDefinition();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Kit helpers that do not need TIA (derive a kit from an example device, placeholders).</summary>
    public static class KitService
    {
        /// <summary>
        /// Builds a kit from one device of a project document: its type and firmware, modules,
        /// interfaces (subnet + IO system, without IP / PROFINET name), submodule I/Q lengths and,
        /// optionally, its I/O tags as templates (address → offset inside the submodule range).
        /// Every occurrence of the device name becomes <see cref="KitDefinition.DevicePlaceholder"/>.
        /// </summary>
        public static KitFromDeviceResult FromDevice(GenerationDocument doc, string deviceName, string kitId,
            string? kitName = null, bool includeTags = true) =>
            FromDevices(doc, new[] { deviceName }, kitId, kitName, includeTags, modular: false);

        /// <summary>
        /// Like <see cref="FromDevice"/>; with <paramref name="modular"/> the kit keeps fixed only the head
        /// (modules without slot or in slot 0, and the device type itself) and turns every other module of
        /// ALL the example devices into an allowed module option (one per distinct type + I/Q layout).
        /// </summary>
        public static KitFromDeviceResult FromDevices(GenerationDocument doc, IReadOnlyList<string> deviceNames, string kitId,
            string? kitName = null, bool includeTags = true, bool modular = false)
        {
            if (deviceNames.Count == 0)
                throw new ArgumentException("No example device given.");
            foreach (string n in deviceNames)
                if (!doc.Devices.Any(d => Same(d.DeviceName, n)))
                    throw new ArgumentException($"Device '{n}' is not in the project.");

            DeviceSpec first = doc.Devices.First(d => Same(d.DeviceName, deviceNames[0]));
            foreach (string n in deviceNames.Skip(1))
            {
                DeviceSpec other = doc.Devices.First(d => Same(d.DeviceName, n));
                if (!Same(other.TypeKey, first.TypeKey))
                    throw new ArgumentException($"Device '{n}' is a '{other.TypeKey}', not a '{first.TypeKey}' like '{first.DeviceName}'.");
            }

            KitFromDeviceResult result = modular
                ? BuildModular(doc, first, deviceNames, kitId, kitName, includeTags)
                : BuildFixed(doc, first, kitId, kitName, includeTags);
            if (result.Kit.Interfaces.Count == 0)
                result.Warnings.Add("The device has no network interface in the project.");
            return result;
        }

        private static KitFromDeviceResult BuildModular(GenerationDocument doc, DeviceSpec first, IReadOnlyList<string> deviceNames,
            string kitId, string? kitName, bool includeTags)
        {
            // The fixed part of the first device: head, interfaces, ranges of the fixed modules.
            KitFromDeviceResult result = BuildFixed(doc, first, kitId, kitName, includeTags: false);
            KitDefinition kit = result.Kit;
            kit.Description = $"Modular kit derived from {string.Join(", ", deviceNames)}.";

            bool IsFixed(ModuleSpec m, DeviceSpec device) =>
                m.Slot == null || m.Slot == 0 || Same(m.TypeKey, device.TypeKey);

            List<string> fixedNames = doc.Modules.Where(m => Same(m.Device, first.DeviceName) && IsFixed(m, first))
                .Select(m => ToPattern(m.ModuleName, first.DeviceName)).ToList();
            kit.Modules.RemoveAll(m => !fixedNames.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase));
            kit.Addresses.RemoveAll(a => !fixedNames.Contains(a.ModuleName, StringComparer.OrdinalIgnoreCase));

            var options = new Dictionary<string, KitModuleOption>(StringComparer.OrdinalIgnoreCase);
            foreach (string deviceName in deviceNames)
            {
                DeviceSpec device = doc.Devices.First(d => Same(d.DeviceName, deviceName));
                List<ModuleSpec> modules = doc.Modules.Where(m => Same(m.Device, deviceName)).ToList();
                List<ModuleAddressSpec> addresses = doc.ModuleAddresses.Where(a => Same(a.Device, deviceName)).ToList();
                var fixedModuleNames = new HashSet<string>(modules.Where(m => IsFixed(m, device)).Select(m => m.ModuleName), StringComparer.OrdinalIgnoreCase);

                IEnumerable<string> variableNames = modules.Where(m => !IsFixed(m, device)).Select(m => m.ModuleName)
                    .Concat(addresses.Select(a => a.ModuleName).Where(n => !fixedModuleNames.Contains(n)))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (string moduleName in variableNames)
                {
                    ModuleSpec? row = modules.FirstOrDefault(m => Same(m.ModuleName, moduleName));
                    List<KitAddress> ranges = addresses.Where(a => Same(a.ModuleName, moduleName))
                        .Select(a => new KitAddress
                        {
                            ModuleName = KitDefinition.ModulePlaceholder,
                            SubModuleName = ToPattern(Regex.Replace(a.SubModuleName, Regex.Escape(moduleName), KitDefinition.ModulePlaceholder, RegexOptions.IgnoreCase), deviceName),
                            TypeIdentifier = a.TypeIdentifier,
                            IoType = a.IoType,
                            LengthBits = a.Length,
                        })
                        .OrderBy(a => a.SubModuleName).ThenBy(a => a.IoType)
                        .ToList();

                    string typeKey = row?.TypeKey ?? string.Empty;
                    string key = typeKey + "|" + string.Join(";", ranges.Select(r => $"{r.SubModuleName} {r.IoType} {r.LengthBits} {r.TypeIdentifier}"));
                    if (options.ContainsKey(key))
                        continue;

                    string optionName = typeKey.Length > 0
                        ? typeKey
                        : ranges.Select(r => r.SubModuleName).FirstOrDefault() ?? moduleName;
                    if (options.Values.Any(o => Same(o.Name, optionName)))
                        optionName += $" ({string.Join("/", ranges.Select(r => r.IoType + r.LengthBits))})";
                    options[key] = new KitModuleOption { Name = optionName, TypeKey = typeKey, Addresses = ranges };
                }
            }

            kit.AllowedModules = options.Values.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (includeTags)
                result.Warnings.Add("Tag templates are not derived for modular kits (module names differ per copy).");
            return result;
        }

        private static KitFromDeviceResult BuildFixed(GenerationDocument doc, DeviceSpec device, string kitId,
            string? kitName, bool includeTags)
        {
            var result = new KitFromDeviceResult();
            string name = device.DeviceName;
            KitDefinition kit = result.Kit;
            kit.Id = kitId;
            kit.Name = string.IsNullOrWhiteSpace(kitName) ? $"{device.TypeKey} (from {name})" : kitName!;
            kit.Description = $"Derived from device '{name}'.";
            kit.DeviceTypeKey = device.TypeKey;
            kit.Firmware = device.Firmware ?? string.Empty;
            kit.DeviceComment = device.Comment ?? string.Empty;

            foreach (ModuleSpec m in doc.Modules.Where(m => Same(m.Device, name)))
                kit.Modules.Add(new KitModule
                {
                    ModuleName = ToPattern(m.ModuleName, name),
                    Slot = m.Slot,
                    TypeKey = m.TypeKey,
                    Comment = m.Comment ?? string.Empty,
                });

            foreach (InterfaceSpec i in doc.Interfaces.Where(i => Same(i.Device, name)))
                kit.Interfaces.Add(new KitInterface
                {
                    InterfaceItem = i.InterfaceItem,
                    Subnet = i.Subnet,
                    SubnetMask = i.SubnetMask,
                    MasterIoSystem = i.MasterIoSystem,
                });

            List<ModuleAddressSpec> addresses = doc.ModuleAddresses.Where(a => Same(a.Device, name)).ToList();
            foreach (ModuleAddressSpec a in addresses)
                kit.Addresses.Add(new KitAddress
                {
                    ModuleName = ToPattern(a.ModuleName, name),
                    SubModuleName = ToPattern(a.SubModuleName, name),
                    TypeIdentifier = a.TypeIdentifier,
                    IoType = a.IoType,
                    LengthBits = a.Length,
                });

            if (includeTags)
            {
                foreach (IoTagSpec tag in doc.IoTags.Where(t => Same(t.IoDevice, name)))
                {
                    ModuleAddressSpec? range = addresses.FirstOrDefault(a =>
                        Same(a.ModuleName, tag.ModuleName) && Same(a.SubModuleName, tag.SubModuleName)
                        && TryParseAddress(tag.Address, out string io, out _) && Same(a.IoType, io));
                    if (range == null || !TryParseAddress(tag.Address, out string ioType, out int bit))
                    {
                        result.Warnings.Add($"Tag '{tag.Name}' ({tag.Address}) skipped: not inside an I/Q range of the device.");
                        continue;
                    }

                    kit.Tags.Add(new KitTag
                    {
                        TagTable = ToPattern(tag.TagTable, name),
                        Name = ToPattern(tag.Name, name),
                        DataType = tag.Type,
                        ModuleName = ToPattern(range.ModuleName, name),
                        SubModuleName = ToPattern(range.SubModuleName, name),
                        IoType = ioType,
                        BitOffset = bit - range.StartAddress * 8,
                        Comment = tag.Comment,
                    });
                }

                int fixedNames = kit.Tags.Count(t => !t.Name.Contains(KitDefinition.DevicePlaceholder));
                if (fixedNames > 0)
                    result.Warnings.Add($"{fixedNames} tag name(s) do not contain the device name: every copy of the kit " +
                                        "would create tags with the same name (tag names must be unique per PLC). " +
                                        "Rename them with {DEVICE} in the kit, or derive the kit without tags.");
            }

            return result;
        }

        /// <summary>Replaces the device name inside a text with the placeholder (case-insensitive).</summary>
        public static string ToPattern(string? text, string deviceName) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(deviceName)
                ? text ?? string.Empty
                : Regex.Replace(text, Regex.Escape(deviceName), KitDefinition.DevicePlaceholder, RegexOptions.IgnoreCase);

        /// <summary>Fills the placeholder with a device name.</summary>
        public static string Expand(string? pattern, string deviceName) =>
            (pattern ?? string.Empty).Replace(KitDefinition.DevicePlaceholder, deviceName);

        /// <summary>
        /// Parses an I/O tag address (%I12.3, %IW20, %QD8, %IB4 ...) into "I"/"Q" and an absolute bit number.
        /// </summary>
        public static bool TryParseAddress(string? address, out string ioType, out int bit)
        {
            ioType = string.Empty;
            bit = 0;
            Match m = Regex.Match(address ?? string.Empty, @"^%?([IQ])([BWDX]?)(\d+)(?:\.(\d))?$", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;
            ioType = m.Groups[1].Value.ToUpperInvariant();
            int b = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int sub = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
            bit = b * 8 + sub;
            return true;
        }

        private static bool Same(string? a, string? b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
