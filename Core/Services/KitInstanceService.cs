using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Catalog;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    /// <summary>A module chosen for a copy of a modular kit.</summary>
    public sealed class KitModuleChoice
    {
        /// <summary>Name of the allowed module option in the kit (e.g. "DI16X24VDCST").</summary>
        public string Option { get; set; } = string.Empty;

        /// <summary>Module name in the project; "{DEVICE}" allowed (e.g. "{DEVICE}_6").</summary>
        public string ModuleName { get; set; } = string.Empty;

        public int? Slot { get; set; }
    }

    public sealed class KitCopyRequest
    {
        public string KitId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>Default: device name in lower case.</summary>
        public string? PnDeviceName { get; set; }

        public string? Comment { get; set; }
        public string? Group { get; set; }

        /// <summary>PLC that owns the tags. Default: the only PLC that already has I/O tags.</summary>
        public string? PlcDevice { get; set; }

        public List<KitModuleChoice> Modules { get; set; } = new List<KitModuleChoice>();
    }

    public sealed class KitCopyResult
    {
        public bool Success => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Created { get; } = new List<string>();
    }

    /// <summary>
    /// Adds a copy of an approved kit to a project database: device, modules, interface, I/Q ranges
    /// at the first free address of the IO system, and tags from the kit templates. Only the free
    /// fields come from the user; everything else comes from the kit. All-or-nothing transaction.
    /// </summary>
    public static class KitInstanceService
    {
        public static KitCopyResult AddCopy(string projectDb, ApprovedCatalog catalog, KitCopyRequest request)
        {
            var result = new KitCopyResult();
            KitDefinition? kit = catalog.FindKit(request.KitId);
            if (kit == null)
            {
                result.Errors.Add($"Kit '{request.KitId}' is not in the approved catalog v{catalog.Version}.");
                return result;
            }

            GenerationDocument doc = new DbGenerationReader(projectDb).Read().Document;
            string name = request.DeviceName.Trim();
            CheckRequest(doc, kit, request, name, result);
            if (!result.Success)
                return result;

            // ---- build the rows of the copy (in memory) ----
            var device = new DeviceSpec
            {
                DeviceName = name,
                Group = request.Group ?? string.Empty,
                TypeKey = kit.DeviceTypeKey,
                Firmware = kit.Firmware,
                Comment = request.Comment ?? kit.DeviceComment,
                KitId = kit.Id,
            };

            var modules = kit.Modules.Select(m => new ModuleSpec
            {
                Device = name,
                ModuleName = KitService.Expand(m.ModuleName, name),
                Slot = m.Slot,
                TypeKey = m.TypeKey,
                Comment = m.Comment,
            }).ToList();

            // (module, submodule) → its ranges, so I and Q of one submodule get the same start.
            var rangeGroups = kit.Addresses
                .GroupBy(a => (KitService.Expand(a.ModuleName, name), KitService.Expand(a.SubModuleName, name)))
                .Select(g => (Module: g.Key.Item1, SubModule: g.Key.Item2, Ranges: g.ToList()))
                .ToList();

            foreach (KitModuleChoice choice in request.Modules)
            {
                KitModuleOption option = kit.AllowedModules.First(o => Same(o.Name, choice.Option));
                string moduleName = KitService.Expand(choice.ModuleName, name);
                if (!string.IsNullOrWhiteSpace(option.TypeKey))
                    modules.Add(new ModuleSpec { Device = name, ModuleName = moduleName, Slot = choice.Slot, TypeKey = option.TypeKey });
                foreach (var g in option.Addresses.GroupBy(a => a.SubModuleName))
                    rangeGroups.Add((moduleName,
                        KitService.Expand(g.Key, name).Replace(KitDefinition.ModulePlaceholder, moduleName),
                        g.ToList()));
            }

            // Interface(s): subnet / IO system from the kit, IP / PROFINET name from the user.
            var interfaces = new List<InterfaceSpec>();
            foreach (KitInterface ki in kit.Interfaces)
            {
                InterfaceSpec? sameSystem = doc.Interfaces.FirstOrDefault(i =>
                    Same(i.Subnet, ki.Subnet) && Same(i.MasterIoSystem, ki.MasterIoSystem) && i.MasterIoSystemNumber != null);
                interfaces.Add(new InterfaceSpec
                {
                    Device = name,
                    InterfaceItem = ki.InterfaceItem,
                    Subnet = ki.Subnet,
                    IpAddress = interfaces.Count == 0 ? request.IpAddress.Trim() : string.Empty,
                    SubnetMask = ki.SubnetMask,
                    PnDeviceName = interfaces.Count == 0 ? (request.PnDeviceName ?? name.ToLowerInvariant()) : string.Empty,
                    MasterIoSystem = ki.MasterIoSystem,
                    MasterIoSystemNumber = sameSystem?.MasterIoSystemNumber,
                });
            }

            // I/Q: first free start in the IO system's address space (common start for I and Q of a submodule).
            InterfaceSpec? ioInterface = interfaces.FirstOrDefault(i => i.MasterIoSystem.Length > 0);
            var used = UsedRanges(doc, ioInterface);
            var addresses = new List<ModuleAddressSpec>();
            foreach (var group in rangeGroups)
            {
                int start = FirstFreeCommonStart(used, group.Ranges);
                foreach (KitAddress range in group.Ranges)
                {
                    int bytes = (range.LengthBits + 7) / 8;
                    used[range.IoType].Add((start, start + bytes - 1));
                    addresses.Add(new ModuleAddressSpec
                    {
                        Device = name,
                        ModuleName = group.Module,
                        SubModuleName = group.SubModule,
                        TypeIdentifier = range.TypeIdentifier,
                        IoType = range.IoType,
                        StartAddress = start,
                        Length = range.LengthBits,
                    });
                }
            }

            // Tags from templates, on the PLC that owns the I/O tags.
            var tags = new List<IoTagSpec>();
            if (kit.Tags.Count > 0)
            {
                string? plc = request.PlcDevice;
                if (string.IsNullOrWhiteSpace(plc))
                {
                    List<string> plcs = doc.IoTags.Select(t => t.Device).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    plc = plcs.Count == 1 ? plcs[0] : null;
                }
                if (string.IsNullOrWhiteSpace(plc))
                {
                    result.Errors.Add("The kit has tag templates: give the PLC that owns the tags (--plc=...).");
                    return result;
                }

                foreach (KitTag t in kit.Tags)
                {
                    string module = KitService.Expand(t.ModuleName, name);
                    string subModule = KitService.Expand(t.SubModuleName, name);
                    ModuleAddressSpec range = addresses.First(a => Same(a.ModuleName, module) && Same(a.SubModuleName, subModule) && Same(a.IoType, t.IoType));
                    tags.Add(new IoTagSpec
                    {
                        Device = plc!,
                        IoDevice = name,
                        ModuleName = module,
                        SubModuleName = subModule,
                        TagTable = KitService.Expand(t.TagTable, name),
                        Name = KitService.Expand(t.Name, name),
                        Type = t.DataType,
                        Address = FormatAddress(t.IoType, range.StartAddress * 8 + t.BitOffset, CatalogDatabase.DataTypeBits(t.DataType)),
                        Comment = KitService.Expand(t.Comment, name),
                    });
                }
            }

            // Sanity: the copy must match its own kit before anything is written.
            var check = new GenerationDocument();
            check.Devices.Add(device);
            check.Modules.AddRange(modules);
            check.Interfaces.AddRange(interfaces);
            check.ModuleAddresses.AddRange(addresses);
            foreach (string diff in KitMatcher.Compare(check, device, kit))
                result.Errors.Add($"Internal check failed: {diff}");
            if (!result.Success)
                return result;

            Write(projectDb, catalog, kit, device, modules, interfaces, addresses, tags, result);
            return result;
        }

        private static void CheckRequest(GenerationDocument doc, KitDefinition kit, KitCopyRequest request, string name, KitCopyResult result)
        {
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9_\-\.]+$"))
                result.Errors.Add($"Device name '{name}' must use letters, digits, '_', '-' or '.'.");
            if (doc.Devices.Any(d => Same(d.DeviceName, name)))
                result.Errors.Add($"A device named '{name}' already exists in the project.");
            if (kit.Interfaces.Count > 0 && !Regex.IsMatch(request.IpAddress ?? string.Empty, @"^\d{1,3}(\.\d{1,3}){3}$"))
                result.Errors.Add($"IP address '{request.IpAddress}' is not valid.");

            foreach (KitInterface ki in kit.Interfaces.Where(i => i.Subnet.Length > 0))
                if (!doc.Subnets.Any(s => Same(s.Name, ki.Subnet)))
                    result.Errors.Add($"Subnet '{ki.Subnet}' required by the kit does not exist in the project.");

            KitInterface? first = kit.Interfaces.FirstOrDefault();
            if (first != null)
            {
                string pn = request.PnDeviceName ?? name.ToLowerInvariant();
                InterfaceSpec? ipOwner = doc.Interfaces.FirstOrDefault(i => Same(i.Subnet, first.Subnet) && Same(i.IpAddress, request.IpAddress));
                if (ipOwner != null)
                    result.Errors.Add($"IP {request.IpAddress} is already used by '{ipOwner.Device}' in subnet {first.Subnet}.");
                InterfaceSpec? pnOwner = doc.Interfaces.FirstOrDefault(i => Same(i.Subnet, first.Subnet) && Same(i.PnDeviceName, pn));
                if (pnOwner != null)
                    result.Errors.Add($"PROFINET name '{pn}' is already used by '{pnOwner.Device}' in subnet {first.Subnet}.");
            }

            if (!kit.IsModular && request.Modules.Count > 0)
                result.Errors.Add($"Kit '{kit.Id}' has a fixed layout; modules cannot be chosen.");

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var moduleNames = new HashSet<string>(kit.Modules.Select(m => KitService.Expand(m.ModuleName, name)), StringComparer.OrdinalIgnoreCase);
            var slots = new HashSet<int>(kit.Modules.Where(m => m.Slot != null).Select(m => m.Slot!.Value));
            foreach (KitModuleChoice choice in request.Modules)
            {
                KitModuleOption? option = kit.AllowedModules.FirstOrDefault(o => Same(o.Name, choice.Option));
                string moduleName = KitService.Expand(choice.ModuleName, name);
                if (option == null)
                {
                    result.Errors.Add($"Module '{choice.Option}' is not allowed in kit '{kit.Id}' " +
                                      $"(allowed: {string.Join(", ", kit.AllowedModules.Select(o => o.Name))}).");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(moduleName) || !moduleNames.Add(moduleName))
                    result.Errors.Add($"Module name '{moduleName}' is empty or repeated.");
                if (choice.Slot != null)
                {
                    if (!slots.Add(choice.Slot.Value))
                        result.Errors.Add($"Slot {choice.Slot} is used twice.");
                    if ((option.MinSlot != null && choice.Slot < option.MinSlot) || (option.MaxSlot != null && choice.Slot > option.MaxSlot))
                        result.Errors.Add($"Module '{moduleName}': slot {choice.Slot} outside {option.MinSlot}..{option.MaxSlot}.");
                }
                else if (!string.IsNullOrWhiteSpace(option.TypeKey))
                {
                    result.Errors.Add($"Module '{moduleName}' ({option.TypeKey}) needs a slot.");
                }
                counts[option.Name] = counts.TryGetValue(option.Name, out int n) ? n + 1 : 1;
                if (option.MaxCount != null && counts[option.Name] > option.MaxCount)
                    result.Errors.Add($"At most {option.MaxCount} module(s) '{option.Name}' allowed.");
            }
        }

        /// <summary>Byte ranges already used per I / Q in the IO system of <paramref name="io"/>.</summary>
        private static Dictionary<string, List<(int First, int Last)>> UsedRanges(GenerationDocument doc, InterfaceSpec? io)
        {
            var used = new Dictionary<string, List<(int, int)>> { ["I"] = new List<(int, int)>(), ["Q"] = new List<(int, int)>() };
            var sameSpace = new HashSet<string>(doc.Interfaces
                .Where(i => io != null && Same(i.Subnet, io.Subnet) && Same(i.MasterIoSystem, io.MasterIoSystem)
                            && (io.MasterIoSystemNumber == null || i.MasterIoSystemNumber == io.MasterIoSystemNumber))
                .Select(i => i.Device), StringComparer.OrdinalIgnoreCase);

            foreach (ModuleAddressSpec a in doc.ModuleAddresses)
            {
                if (io != null && !sameSpace.Contains(a.Device))
                    continue;
                if (used.TryGetValue(a.IoType, out var list) && a.Length > 0)
                    list.Add((a.StartAddress, a.StartAddress + (a.Length + 7) / 8 - 1));
            }
            return used;
        }

        /// <summary>Lowest start where every range of the group (I and/or Q) fits without overlapping.</summary>
        private static int FirstFreeCommonStart(Dictionary<string, List<(int First, int Last)>> used, List<KitAddress> ranges)
        {
            int start = 0;
            while (true)
            {
                int? next = null;
                foreach (KitAddress r in ranges)
                {
                    int last = start + (r.LengthBits + 7) / 8 - 1;
                    foreach (var u in used[r.IoType])
                        if (u.First <= last && start <= u.Last)
                            next = Math.Max(next ?? 0, u.Last + 1);
                }
                if (next == null)
                    return start;
                start = next.Value;
            }
        }

        public static string FormatAddress(string ioType, int bit, int sizeBits)
        {
            int b = bit / 8;
            switch (sizeBits)
            {
                case 8: return $"%{ioType}B{b}";
                case 16: return $"%{ioType}W{b}";
                case 32: return $"%{ioType}D{b}";
                case 64: return $"%{ioType}L{b}";
                default: return $"%{ioType}{b}.{bit % 8}";
            }
        }

        private static void Write(string projectDb, ApprovedCatalog catalog, KitDefinition kit, DeviceSpec device,
            List<ModuleSpec> modules, List<InterfaceSpec> interfaces, List<ModuleAddressSpec> addresses,
            List<IoTagSpec> tags, KitCopyResult result)
        {
            using (SqliteConnection db = ProjectDatabase.Open(projectDb))
            using (SqliteTransaction tx = db.BeginTransaction())
            {
                try
                {
                    // Project catalog rows for the types used (copied from the approved catalog).
                    foreach (string typeKey in new[] { kit.DeviceTypeKey }.Concat(modules.Select(m => m.TypeKey)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        CatalogEntry? t = catalog.FindType(typeKey);
                        if (t != null)
                            Exec(db, tx, "INSERT OR IGNORE INTO catalog (type_key, mlfb, tia_version, description, role, identifier) VALUES (@a, @b, @c, @d, @e, @f);",
                                t.TypeKey, t.Mlfb, t.TiaVersion, t.Description, t.Role.ToString(), t.Identifier);
                    }

                    Exec(db, tx, "INSERT INTO devices (device_name, group_name, type_key, firmware, comment, kit_id) VALUES (@a, @b, @c, @d, @e, @f);",
                        device.DeviceName, device.Group, device.TypeKey, device.Firmware, device.Comment, device.KitId);
                    result.Created.Add($"device {device.DeviceName} ({device.TypeKey}, kit {device.KitId})");

                    foreach (ModuleSpec m in modules)
                    {
                        Exec(db, tx, "INSERT INTO modules (device, module_name, slot, type_key, comment) VALUES (@a, @b, @c, @d, @e);",
                            m.Device, m.ModuleName, m.Slot, m.TypeKey, m.Comment);
                        result.Created.Add($"module {m.ModuleName} slot {m.Slot?.ToString() ?? "-"} {m.TypeKey}");
                    }

                    foreach (InterfaceSpec i in interfaces)
                    {
                        Exec(db, tx, "INSERT INTO interfaces (device, interface_item, subnet, ip_address, subnet_mask, pn_device_name, master_io_system, master_io_system_number) " +
                                     "VALUES (@a, @b, @c, @d, @e, @f, @g, @h);",
                            i.Device, i.InterfaceItem, Null(i.Subnet), Null(i.IpAddress), Null(i.SubnetMask), Null(i.PnDeviceName), Null(i.MasterIoSystem), i.MasterIoSystemNumber);
                        result.Created.Add($"interface {i.InterfaceItem} {i.Subnet} {i.IpAddress} '{i.PnDeviceName}' {i.MasterIoSystem} ({i.MasterIoSystemNumber})");
                    }

                    foreach (ModuleAddressSpec a in addresses)
                    {
                        Exec(db, tx, "INSERT INTO module_addresses (device, module_name, sub_module_name, type_identifier, io_type, start_address, length) VALUES (@a, @b, @c, @d, @e, @f, @g);",
                            a.Device, a.ModuleName, a.SubModuleName, a.TypeIdentifier, a.IoType, a.StartAddress, a.Length);
                        result.Created.Add($"address {a.ModuleName}/{a.SubModuleName} {a.IoType}{a.StartAddress} ({a.Length} bits)");
                    }

                    foreach (IoTagSpec t in tags)
                    {
                        Exec(db, tx, "INSERT INTO io_tags (device, io_device, module_name, sub_module_name, tag_table, name, type, address, comment) VALUES (@a, @b, @c, @d, @e, @f, @g, @h, @i);",
                            t.Device, t.IoDevice, t.ModuleName, t.SubModuleName, t.TagTable, t.Name, t.Type, t.Address, t.Comment);
                        result.Created.Add($"tag {t.TagTable}/{t.Name} {t.Type} {t.Address}");
                    }

                    tx.Commit();
                }
                catch (SqliteException ex)
                {
                    tx.Rollback();
                    result.Created.Clear();
                    result.Errors.Add($"Nothing was added: {ex.Message.Replace("SQLite Error 19: ", string.Empty)}");
                }
            }
            SqliteConnection.ClearAllPools();
        }

        private static object? Null(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

        private static void Exec(SqliteConnection db, SqliteTransaction tx, string sql, params object?[] values)
        {
            using (SqliteCommand command = db.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = sql;
                for (int i = 0; i < values.Length; i++)
                    command.Parameters.AddWithValue("@" + (char)('a' + i), values[i] ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        private static bool Same(string? a, string? b) =>
            string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
