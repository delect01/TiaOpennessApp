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
            string? kitName = null, bool includeTags = true)
        {
            DeviceSpec? device = doc.Devices.FirstOrDefault(d => Same(d.DeviceName, deviceName))
                ?? throw new ArgumentException($"Device '{deviceName}' is not in the project.");

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

            if (kit.Interfaces.Count == 0)
                result.Warnings.Add("The device has no network interface in the project.");
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
