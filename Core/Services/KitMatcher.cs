using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Catalog;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public sealed class KitAssignmentResult
    {
        /// <summary>device → kit id, for devices linked now.</summary>
        public Dictionary<string, string> Assigned { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> AlreadyLinked { get; } = new List<string>();

        /// <summary>device → reason (no kit of its type, or the differences with the closest kit).</summary>
        public Dictionary<string, string> NotMatched { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares a device of a project with an approved kit on the LOCKED fields only:
    /// type, firmware, modules (name, slot, type), interfaces (subnet, IO system) and submodule
    /// I/Q ranges (type identifier, I/Q, length). IP, PROFINET name, start addresses and tags are free.
    /// </summary>
    public static class KitMatcher
    {
        /// <summary>Differences between the device and the kit; empty = the device is a copy of the kit.</summary>
        public static List<string> Compare(GenerationDocument doc, DeviceSpec device, KitDefinition kit)
        {
            var diffs = new List<string>();
            string name = device.DeviceName;

            if (!Same(device.TypeKey, kit.DeviceTypeKey))
                diffs.Add($"type '{device.TypeKey}' instead of '{kit.DeviceTypeKey}'");
            if (!string.IsNullOrWhiteSpace(kit.Firmware) && !Same(device.Firmware, kit.Firmware))
                diffs.Add($"firmware '{device.Firmware}' instead of '{kit.Firmware}'");

            CompareSets(diffs, "interface",
                kit.Interfaces.Select(i => $"{i.InterfaceItem} → {Show(i.Subnet)} / {Show(i.MasterIoSystem)}"),
                doc.Interfaces.Where(i => Same(i.Device, name)).Select(i => $"{i.InterfaceItem} → {Show(i.Subnet)} / {Show(i.MasterIoSystem)}"));

            List<ModuleSpec> modules = doc.Modules.Where(m => Same(m.Device, name)).ToList();
            List<ModuleAddressSpec> addresses = doc.ModuleAddresses.Where(a => Same(a.Device, name)).ToList();

            // Fixed part: every fixed module and range must be present (removed from the lists).
            foreach (KitModule fixedModule in kit.Modules)
            {
                string moduleName = KitService.Expand(fixedModule.ModuleName, name);
                int index = modules.FindIndex(m => Same(m.ModuleName, moduleName) && m.Slot == fixedModule.Slot && Same(m.TypeKey, fixedModule.TypeKey));
                if (index >= 0)
                    modules.RemoveAt(index);
                else
                    diffs.Add($"missing module [{moduleName} slot {Slot(fixedModule.Slot)} {fixedModule.TypeKey}]");
            }
            foreach (KitAddress fixedRange in kit.Addresses)
            {
                string key = RangeKey(KitService.Expand(fixedRange.ModuleName, name), KitService.Expand(fixedRange.SubModuleName, name),
                    fixedRange.IoType, fixedRange.LengthBits, fixedRange.TypeIdentifier);
                int index = addresses.FindIndex(a => Same(RangeKey(a), key));
                if (index >= 0)
                    addresses.RemoveAt(index);
                else
                    diffs.Add($"missing I/Q range [{key}]");
            }

            if (!kit.IsModular)
            {
                diffs.AddRange(modules.Select(m => $"extra module [{m.ModuleName} slot {Slot(m.Slot)} {m.TypeKey}]"));
                diffs.AddRange(addresses.Select(a => $"extra I/Q range [{RangeKey(a)}]"));
                return diffs;
            }

            // Modular part: every other module must be one of the allowed options, with exactly its ranges.
            var perOption = new Dictionary<KitModuleOption, int>();
            var moduleNames = modules.Select(m => m.ModuleName)
                .Concat(addresses.Select(a => a.ModuleName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string moduleName in moduleNames)
            {
                ModuleSpec? row = modules.FirstOrDefault(m => Same(m.ModuleName, moduleName));
                List<string> actualRanges = addresses.Where(a => Same(a.ModuleName, moduleName)).Select(RangeKey).OrderBy(k => k).ToList();
                string label = row == null ? moduleName : $"{moduleName} ({row.TypeKey}, slot {Slot(row.Slot)})";

                List<KitModuleOption> sameKind = kit.AllowedModules
                    .Where(o => row != null ? Same(o.TypeKey, row.TypeKey) : string.IsNullOrWhiteSpace(o.TypeKey))
                    .ToList();
                KitModuleOption? option = sameKind.FirstOrDefault(o =>
                    o.Addresses.Select(a => RangeKey(Expand(a.ModuleName, name, moduleName), Expand(a.SubModuleName, name, moduleName),
                        a.IoType, a.LengthBits, a.TypeIdentifier)).OrderBy(k => k)
                        .SequenceEqual(actualRanges, StringComparer.OrdinalIgnoreCase));

                if (option == null)
                {
                    diffs.Add(sameKind.Count == 0
                        ? $"module [{label}] is not allowed in this kit"
                        : $"module [{label}] has I/Q ranges different from the allowed '{sameKind[0].Name}'");
                    continue;
                }
                if (row?.Slot != null && ((option.MinSlot != null && row.Slot < option.MinSlot) || (option.MaxSlot != null && row.Slot > option.MaxSlot)))
                    diffs.Add($"module [{label}] slot outside {option.MinSlot}..{option.MaxSlot}");
                perOption[option] = perOption.TryGetValue(option, out int count) ? count + 1 : 1;
            }
            foreach (var pair in perOption.Where(p => p.Key.MaxCount != null && p.Value > p.Key.MaxCount))
                diffs.Add($"{pair.Value} modules '{pair.Key.Name}' (max {pair.Key.MaxCount})");

            return diffs;
        }

        private static string Expand(string pattern, string device, string module) =>
            KitService.Expand(pattern, device).Replace(KitDefinition.ModulePlaceholder, module);

        private static string RangeKey(ModuleAddressSpec a) => RangeKey(a.ModuleName, a.SubModuleName, a.IoType, a.Length, a.TypeIdentifier);

        private static string RangeKey(string module, string subModule, string ioType, int lengthBits, string? typeIdentifier) =>
            $"{module}/{subModule} {ioType} {lengthBits} bits {typeIdentifier}".Trim();

        /// <summary>Kits whose locked fields the device matches (normally 0 or 1).</summary>
        public static List<KitDefinition> Matching(GenerationDocument doc, DeviceSpec device, IEnumerable<KitDefinition> kits) =>
            kits.Where(k => Same(k.DeviceTypeKey, device.TypeKey) && Compare(doc, device, k).Count == 0).ToList();

        /// <summary>
        /// Links every device of a project database that has no kit yet to the approved kit it matches.
        /// Devices already linked are left alone. Ambiguous or unmatched devices are reported, not linked.
        /// </summary>
        public static KitAssignmentResult AssignKits(string projectDb, IReadOnlyList<KitDefinition> kits)
        {
            var result = new KitAssignmentResult();
            GenerationDocument doc = new DbGenerationReader(projectDb).Read().Document;

            foreach (DeviceSpec device in doc.Devices)
            {
                if (!string.IsNullOrWhiteSpace(device.KitId))
                {
                    result.AlreadyLinked.Add(device.DeviceName);
                    continue;
                }

                List<KitDefinition> matches = Matching(doc, device, kits);
                if (matches.Count == 1)
                {
                    result.Assigned[device.DeviceName] = matches[0].Id;
                    continue;
                }
                if (matches.Count > 1)
                {
                    result.NotMatched[device.DeviceName] = $"matches several kits ({string.Join(", ", matches.Select(k => k.Id))})";
                    continue;
                }

                List<KitDefinition> sameType = kits.Where(k => Same(k.DeviceTypeKey, device.TypeKey)).ToList();
                if (sameType.Count == 0)
                {
                    result.NotMatched[device.DeviceName] = $"no approved kit for type '{device.TypeKey}'";
                    continue;
                }
                var closest = sameType
                    .Select(k => (Kit: k, Diffs: Compare(doc, device, k)))
                    .OrderBy(x => x.Diffs.Count)
                    .First();
                result.NotMatched[device.DeviceName] = $"differs from kit '{closest.Kit.Id}' in {closest.Diffs.Count} point(s): " +
                                                       string.Join("; ", closest.Diffs.Take(4)) +
                                                       (closest.Diffs.Count > 4 ? "; ..." : string.Empty);
            }

            if (result.Assigned.Count > 0)
            {
                using (SqliteConnection db = ProjectDatabase.Open(projectDb))
                using (SqliteTransaction transaction = db.BeginTransaction())
                using (SqliteCommand command = db.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE devices SET kit_id = @kit WHERE device_name = @device;";
                    command.Parameters.Add(new SqliteParameter("@kit", string.Empty));
                    command.Parameters.Add(new SqliteParameter("@device", string.Empty));
                    foreach (var pair in result.Assigned)
                    {
                        command.Parameters["@kit"].Value = pair.Value;
                        command.Parameters["@device"].Value = pair.Key;
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
                SqliteConnection.ClearAllPools();
            }

            return result;
        }

        /// <summary>
        /// Multiset comparison: every expected item must appear once in the actual list and vice versa.
        /// </summary>
        private static void CompareSets(List<string> diffs, string what, IEnumerable<string> expected, IEnumerable<string> actual)
        {
            List<string> remaining = actual.ToList();
            foreach (string item in expected)
            {
                int index = remaining.FindIndex(a => string.Equals(a, item, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    remaining.RemoveAt(index);
                else
                    diffs.Add($"missing {what} [{item}]");
            }
            foreach (string extra in remaining)
                diffs.Add($"extra {what} [{extra}]");
        }

        private static string Slot(int? slot) => slot?.ToString() ?? "-";
        private static string Show(string? text) => string.IsNullOrWhiteSpace(text) ? "(none)" : text!;
        private static bool Same(string? a, string? b) =>
            string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
