using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Reads a project database (project.db) into the same <see cref="GenerationDocument"/> the CSV
    /// and Excel readers produce, so the generation services work unchanged.
    /// The database is opened read-only; an older schema must be upgraded first (<c>--db-info</c>).
    /// </summary>
    public sealed class DbGenerationReader : IGenerationReader
    {
        private readonly string _dbPath;

        public DbGenerationReader(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>Catalog rows stored in the project (types used by the project when it was imported).</summary>
        public List<CatalogEntry> ProjectCatalog { get; } = new List<CatalogEntry>();

        /// <summary>project_info values (created_utc, source, catalog_version, ...).</summary>
        public Dictionary<string, string> Info { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public GenerationParseResult Read()
        {
            var result = new GenerationParseResult();
            GenerationDocument doc = result.Document;

            using (SqliteConnection db = ProjectDatabase.OpenReadOnly(_dbPath))
            {
                int version = ProjectDatabase.GetSchemaVersion(db);
                if (version < ProjectDatabase.CurrentSchemaVersion)
                {
                    result.Issues.Add(new ParseIssue
                    {
                        Source = "project.db",
                        Message = $"Schema v{(version == 0 ? 1 : version)} is older than v{ProjectDatabase.CurrentSchemaVersion}; " +
                                  "upgrade it first with --db-info.",
                    });
                    return result;
                }

                Query(db, "SELECT key, value FROM project_info;", r => Info[S(r, 0)] = S(r, 1));

                Query(db, "SELECT type_key, mlfb, tia_version, description, role, identifier FROM catalog ORDER BY rowid;", r =>
                    ProjectCatalog.Add(new CatalogEntry
                    {
                        TypeKey = S(r, 0),
                        Mlfb = S(r, 1),
                        TiaVersion = S(r, 2),
                        Description = S(r, 3),
                        Role = string.Equals(S(r, 4), "Root", StringComparison.OrdinalIgnoreCase)
                            ? CatalogItemRole.Root
                            : CatalogItemRole.Module,
                        Identifier = S(r, 5),
                    }));

                Query(db, "SELECT device_name, group_name, type_key, firmware, comment FROM devices ORDER BY rowid;", r =>
                    doc.Devices.Add(new DeviceSpec
                    {
                        DeviceName = S(r, 0),
                        Group = S(r, 1),
                        TypeKey = S(r, 2),
                        Firmware = N(r, 3),
                        Comment = N(r, 4),
                    }));

                Query(db, "SELECT name, subnet_type FROM subnets ORDER BY rowid;", r =>
                    doc.Subnets.Add(new SubnetSpec { Name = S(r, 0), SubnetType = S(r, 1) }));

                Query(db, "SELECT device, module_name, slot, type_key, comment FROM modules ORDER BY id;", r =>
                    doc.Modules.Add(new ModuleSpec
                    {
                        Device = S(r, 0),
                        ModuleName = S(r, 1),
                        Slot = I(r, 2),
                        TypeKey = S(r, 3),
                        Comment = N(r, 4),
                    }));

                Query(db, "SELECT device, interface_item, subnet, ip_address, subnet_mask, pn_device_name, " +
                          "master_io_system, master_io_system_number FROM interfaces ORDER BY id;", r =>
                    doc.Interfaces.Add(new InterfaceSpec
                    {
                        Device = S(r, 0),
                        InterfaceItem = S(r, 1),
                        Subnet = S(r, 2),
                        IpAddress = S(r, 3),
                        SubnetMask = S(r, 4),
                        PnDeviceName = S(r, 5),
                        MasterIoSystem = S(r, 6),
                        MasterIoSystemNumber = I(r, 7),
                    }));

                Query(db, "SELECT id, device, module_name, sub_module_name, type_identifier, io_type, start_address, length " +
                          "FROM module_addresses ORDER BY id;", r =>
                {
                    int? start = I(r, 6);
                    int? length = I(r, 7);
                    if (start == null || length == null)
                    {
                        result.Issues.Add(new ParseIssue
                        {
                            Source = "module_addresses",
                            Row = (int)r.GetInt64(0),
                            Message = $"{S(r, 1)}/{S(r, 2)}: start address or length missing.",
                        });
                        return;
                    }
                    doc.ModuleAddresses.Add(new ModuleAddressSpec
                    {
                        Device = S(r, 1),
                        ModuleName = S(r, 2),
                        SubModuleName = S(r, 3),
                        TypeIdentifier = S(r, 4),
                        IoType = S(r, 5),
                        StartAddress = start.Value,
                        Length = length.Value,
                    });
                });

                Query(db, "SELECT device, io_device, module_name, sub_module_name, tag_table, name, type, address, comment " +
                          "FROM io_tags ORDER BY id;", r =>
                    doc.IoTags.Add(new IoTagSpec
                    {
                        Device = S(r, 0),
                        IoDevice = S(r, 1),
                        ModuleName = S(r, 2),
                        SubModuleName = S(r, 3),
                        TagTable = S(r, 4),
                        Name = S(r, 5),
                        Type = S(r, 6),
                        Address = S(r, 7),
                        Comment = S(r, 8),
                    }));

                Query(db, "SELECT device, group_path, kind, name, namespace, number, auto_number, block_type, " +
                          "programming_language, memory_layout, title, comment, source_file FROM blocks ORDER BY id;", r =>
                    doc.Blocks.Add(new BlockSpec
                    {
                        Device = S(r, 0),
                        GroupPath = S(r, 1),
                        Kind = S(r, 2),
                        Name = S(r, 3),
                        Namespace = S(r, 4),
                        Number = I(r, 5),
                        AutoNumber = r.IsDBNull(6) ? (bool?)null : r.GetInt64(6) != 0,
                        BlockType = S(r, 7),
                        ProgrammingLanguage = S(r, 8),
                        MemoryLayout = S(r, 9),
                        Title = S(r, 10),
                        Comment = S(r, 11),
                        SourceFile = S(r, 12),
                    }));
            }

            SqliteConnection.ClearAllPools();
            return result;
        }

        private static void Query(SqliteConnection db, string sql, Action<SqliteDataReader> row)
        {
            using (SqliteCommand command = db.CreateCommand())
            {
                command.CommandText = sql;
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        row(reader);
                }
            }
        }

        private static string S(SqliteDataReader r, int i) =>
            r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;

        private static string? N(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : S(r, i);

        private static int? I(SqliteDataReader r, int i) =>
            r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    }
}
