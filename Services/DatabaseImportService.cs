using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Database;

namespace TiaOpennessApp.Services
{
    public class DatabaseImportService
    {
        private const string CatalogFileName = "Catalog.csv";

        private static readonly string[] TableNames =
        {
            "catalog",
            "devices",
            "subnets",
            "modules",
            "interfaces",
            "module_addresses",
            "io_tags",
        };

        public string DefaultDbPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tia_topology.db"); }
        }

        public string DefaultImportsDir
        {
            get { return ResolveImportsDirectory(); }
        }

        public DbImportResult ImportFromImports(string? dbPath = null, string? importsDir = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new DbImportResult();

            string resolvedImportsDir = string.IsNullOrWhiteSpace(importsDir) ? DefaultImportsDir : importsDir!;
            resolvedImportsDir = Path.GetFullPath(resolvedImportsDir);

            string resolvedDbPath = string.IsNullOrWhiteSpace(dbPath) ? DefaultDbPath : dbPath!;
            resolvedDbPath = Path.GetFullPath(resolvedDbPath);

            result.ImportsDir = resolvedImportsDir;
            result.DbPath = resolvedDbPath;

            if (!Directory.Exists(resolvedImportsDir))
            {
                result.Errors.Add($"Imports directory not found: {resolvedImportsDir}");
                result.Elapsed = stopwatch.Elapsed;
                return result;
            }

            string? catalogPath = ResolveCatalogPath(resolvedImportsDir);
            if (catalogPath == null)
            {
                result.Errors.Add($"Catalog CSV not found (looked next to and above '{resolvedImportsDir}').");
                result.Elapsed = stopwatch.Elapsed;
                return result;
            }

            foreach (string fileName in new[] { "Devices.csv", "Subnets.csv", "Modules.csv", "Interfaces.csv", "ModuleAddresses.csv", "IoTags.csv" })
            {
                if (!File.Exists(Path.Combine(resolvedImportsDir, fileName)))
                    result.Errors.Add($"Missing CSV: {fileName}");
            }

            if (result.Errors.Count > 0)
            {
                result.Elapsed = stopwatch.Elapsed;
                return result;
            }

            try
            {
                DeleteDatabaseFiles(resolvedDbPath);

                string? dbDirectory = Path.GetDirectoryName(resolvedDbPath);
                if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
                    Directory.CreateDirectory(dbDirectory!);

                using (var connection = new SqliteConnection($"Data Source={resolvedDbPath}"))
                {
                    connection.Open();
                    ExecuteSchema(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        int errorsBefore = result.Errors.Count;

                        RunImport(connection, transaction, "catalog",
                            () => ImportCatalog(connection, transaction, catalogPath!, result.Warnings), result.Errors);

                        HashSet<string> catalogKeys = LoadStringSet(connection, transaction, "SELECT type_key FROM catalog;");

                        RunImport(connection, transaction, "devices",
                            () => ImportDevices(connection, transaction, Path.Combine(resolvedImportsDir, "Devices.csv"), catalogKeys, result.Warnings), result.Errors);

                        HashSet<string> deviceNames = LoadStringSet(connection, transaction, "SELECT device_name FROM devices;");

                        RunImport(connection, transaction, "subnets",
                            () => ImportSubnets(connection, transaction, Path.Combine(resolvedImportsDir, "Subnets.csv")), result.Errors);

                        HashSet<string> subnetNames = LoadStringSet(connection, transaction, "SELECT name FROM subnets;");

                        RunImport(connection, transaction, "modules",
                            () => ImportModules(connection, transaction, Path.Combine(resolvedImportsDir, "Modules.csv"), deviceNames, catalogKeys, result.Warnings), result.Errors);
                        RunImport(connection, transaction, "interfaces",
                            () => ImportInterfaces(connection, transaction, Path.Combine(resolvedImportsDir, "Interfaces.csv"), deviceNames, subnetNames, result.Warnings), result.Errors);
                        RunImport(connection, transaction, "module_addresses",
                            () => ImportModuleAddresses(connection, transaction, Path.Combine(resolvedImportsDir, "ModuleAddresses.csv"), deviceNames, result.Warnings), result.Errors);
                        RunImport(connection, transaction, "io_tags",
                            () => ImportIoTags(connection, transaction, Path.Combine(resolvedImportsDir, "IoTags.csv"), deviceNames, result.Warnings), result.Errors);

                        if (result.Errors.Count > errorsBefore)
                        {
                            transaction.Rollback();
                            result.Errors.Add("Transaction rolled back; no rows were committed.");
                        }
                        else
                        {
                            transaction.Commit();
                        }
                    }

                    foreach (string table in TableNames)
                        result.TableCounts.Add(new DbTableCount { TableName = table, Count = CountRows(connection, table) });
                }
            }
            catch (Exception ex)
            {
                var message = new StringBuilder(ex.Message);
                for (Exception? inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    message.Append(" | ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);

                result.Errors.Add(message.ToString());
            }

            result.Success = result.Errors.Count == 0;
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        private static string ResolveImportsDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            DirectoryInfo? current = new DirectoryInfo(baseDir);
            for (int depth = 0; depth < 5 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, "imports");
                if (File.Exists(Path.Combine(candidate, "Devices.csv")))
                    return candidate;

                current = current.Parent;
            }

            return Path.Combine(baseDir, "imports");
        }

        private static string? ResolveCatalogPath(string importsDir)
        {
            var candidates = new List<string>();

            string? parent = SafeParent(importsDir);
            if (parent != null)
                candidates.Add(Path.Combine(parent, "catalog", CatalogFileName));

            candidates.Add(Path.Combine(importsDir, CatalogFileName));

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "catalog", CatalogFileName));

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string? SafeParent(string path)
        {
            try
            {
                DirectoryInfo? parent = Directory.GetParent(path);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static void DeleteDatabaseFiles(string dbPath)
        {
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                string candidate = dbPath + suffix;
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }

        private static void ExecuteSchema(SqliteConnection connection)
        {
            const string schema = @"
PRAGMA foreign_keys = ON;

CREATE TABLE catalog (
    type_key    TEXT PRIMARY KEY,
    mlfb        TEXT,
    tia_version TEXT,
    description TEXT,
    role        TEXT,
    identifier  TEXT
);

CREATE TABLE devices (
    device_name TEXT PRIMARY KEY,
    group_name  TEXT,
    type_key    TEXT NOT NULL
                CHECK (length(trim(type_key)) > 0)
                REFERENCES catalog(type_key) ON UPDATE CASCADE ON DELETE RESTRICT,
    firmware    TEXT,
    comment     TEXT
);

CREATE TABLE subnets (
    name        TEXT PRIMARY KEY,
    subnet_type TEXT
);

CREATE TABLE modules (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    device      TEXT NOT NULL
                REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    module_name TEXT NOT NULL,
    slot        INTEGER,
    type_key    TEXT NOT NULL
                CHECK (length(trim(type_key)) > 0)
                REFERENCES catalog(type_key) ON UPDATE CASCADE ON DELETE RESTRICT,
    comment     TEXT,
    UNIQUE(device, module_name)
);

CREATE TABLE interfaces (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    device         TEXT NOT NULL
                   REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    interface_item TEXT NOT NULL,
    subnet         TEXT
                   REFERENCES subnets(name) ON UPDATE CASCADE ON DELETE RESTRICT,
    ip_address     TEXT,
    subnet_mask    TEXT,
    pn_device_name TEXT,
    master_io_system TEXT,
    master_io_system_number INTEGER,
    UNIQUE(device, interface_item)
);

CREATE TABLE module_addresses (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    device          TEXT NOT NULL
                    REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    module_name     TEXT,
    sub_module_name TEXT,
    type_identifier TEXT,
    io_type         TEXT,
    start_address   INTEGER,
    length          INTEGER
);

CREATE TABLE io_tags (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    device          TEXT NOT NULL
                    REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    io_device       TEXT
                    REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    module_name     TEXT,
    sub_module_name TEXT,
    tag_table       TEXT,
    name            TEXT,
    type            TEXT,
    address         TEXT,
    comment         TEXT
);

CREATE TRIGGER trg_devices_type_key_insert
BEFORE INSERT ON devices
BEGIN
    SELECT RAISE(ABORT, 'devices.type_key is required and must exist in catalog')
    WHERE NEW.type_key IS NULL
       OR length(trim(NEW.type_key)) = 0
       OR NOT EXISTS (SELECT 1 FROM catalog WHERE type_key = NEW.type_key);
END;

CREATE TRIGGER trg_devices_type_key_update
BEFORE UPDATE OF type_key ON devices
BEGIN
    SELECT RAISE(ABORT, 'devices.type_key is required and must exist in catalog')
    WHERE NEW.type_key IS NULL
       OR length(trim(NEW.type_key)) = 0
       OR NOT EXISTS (SELECT 1 FROM catalog WHERE type_key = NEW.type_key);
END;

CREATE TRIGGER trg_devices_delete
BEFORE DELETE ON devices
BEGIN
    SELECT RAISE(ABORT, 'device is referenced by modules, interfaces, module_addresses or io_tags')
    WHERE EXISTS (SELECT 1 FROM modules WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM interfaces WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM module_addresses WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM io_tags WHERE device = OLD.device_name OR io_device = OLD.device_name);
END;

CREATE TRIGGER trg_modules_refs_insert
BEFORE INSERT ON modules
BEGIN
    SELECT RAISE(ABORT, 'modules.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'modules.type_key is required and must exist in catalog')
    WHERE NEW.type_key IS NULL
       OR length(trim(NEW.type_key)) = 0
       OR NOT EXISTS (SELECT 1 FROM catalog WHERE type_key = NEW.type_key);
END;

CREATE TRIGGER trg_modules_refs_update
BEFORE UPDATE OF device, type_key ON modules
BEGIN
    SELECT RAISE(ABORT, 'modules.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'modules.type_key is required and must exist in catalog')
    WHERE NEW.type_key IS NULL
       OR length(trim(NEW.type_key)) = 0
       OR NOT EXISTS (SELECT 1 FROM catalog WHERE type_key = NEW.type_key);
END;

CREATE TRIGGER trg_interfaces_refs_insert
BEFORE INSERT ON interfaces
BEGIN
    SELECT RAISE(ABORT, 'interfaces.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'interfaces.subnet must exist in subnets')
    WHERE NEW.subnet IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM subnets WHERE name = NEW.subnet);
END;

CREATE TRIGGER trg_interfaces_refs_update
BEFORE UPDATE OF device, subnet ON interfaces
BEGIN
    SELECT RAISE(ABORT, 'interfaces.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'interfaces.subnet must exist in subnets')
    WHERE NEW.subnet IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM subnets WHERE name = NEW.subnet);
END;

CREATE TRIGGER trg_module_addresses_refs_insert
BEFORE INSERT ON module_addresses
BEGIN
    SELECT RAISE(ABORT, 'module_addresses.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);
END;

CREATE TRIGGER trg_module_addresses_refs_update
BEFORE UPDATE OF device ON module_addresses
BEGIN
    SELECT RAISE(ABORT, 'module_addresses.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);
END;

CREATE TRIGGER trg_io_tags_refs_insert
BEFORE INSERT ON io_tags
BEGIN
    SELECT RAISE(ABORT, 'io_tags.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'io_tags.io_device must exist in devices')
    WHERE NEW.io_device IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.io_device);
END;

CREATE TRIGGER trg_io_tags_refs_update
BEFORE UPDATE OF device, io_device ON io_tags
BEGIN
    SELECT RAISE(ABORT, 'io_tags.device must exist in devices')
    WHERE NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.device);

    SELECT RAISE(ABORT, 'io_tags.io_device must exist in devices')
    WHERE NEW.io_device IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM devices WHERE device_name = NEW.io_device);
END;

CREATE TRIGGER trg_catalog_delete
BEFORE DELETE ON catalog
BEGIN
    SELECT RAISE(ABORT, 'catalog entry is referenced by devices or modules')
    WHERE EXISTS (SELECT 1 FROM devices WHERE type_key = OLD.type_key)
       OR EXISTS (SELECT 1 FROM modules WHERE type_key = OLD.type_key);
END;

CREATE TRIGGER trg_catalog_type_key_update
AFTER UPDATE OF type_key ON catalog
WHEN OLD.type_key <> NEW.type_key
BEGIN
    UPDATE devices SET type_key = NEW.type_key WHERE type_key = OLD.type_key;
    UPDATE modules SET type_key = NEW.type_key WHERE type_key = OLD.type_key;
END;

CREATE TRIGGER trg_subnets_delete
BEFORE DELETE ON subnets
BEGIN
    SELECT RAISE(ABORT, 'subnet is referenced by interfaces')
    WHERE EXISTS (SELECT 1 FROM interfaces WHERE subnet = OLD.name);
END;

CREATE INDEX idx_modules_device ON modules(device);
CREATE INDEX idx_modules_type_key ON modules(type_key);
CREATE INDEX idx_interfaces_device ON interfaces(device);
CREATE INDEX idx_interfaces_subnet ON interfaces(subnet);
CREATE INDEX idx_module_addresses_device ON module_addresses(device);
CREATE INDEX idx_module_addresses_module ON module_addresses(module_name);
CREATE INDEX idx_io_tags_device ON io_tags(device);
CREATE INDEX idx_io_tags_io_device ON io_tags(io_device);
CREATE INDEX idx_io_tags_name ON io_tags(name);

CREATE VIEW vw_device_topology AS
SELECT d.device_name,
       d.group_name,
       d.type_key,
       c.description AS type_description,
       d.firmware,
       i.interface_item,
       i.subnet,
       i.ip_address,
       i.subnet_mask,
       i.pn_device_name,
       i.master_io_system,
       i.master_io_system_number
FROM devices d
LEFT JOIN catalog c ON c.type_key = d.type_key
LEFT JOIN interfaces i ON i.device = d.device_name;

CREATE VIEW vw_io_mapping AS
SELECT t.device,
       t.io_device,
       t.module_name,
       t.sub_module_name,
       t.tag_table,
       t.name       AS tag_name,
       t.type       AS tag_type,
       t.address,
       t.comment,
       a.type_identifier,
       a.io_type,
       a.start_address,
       a.length
FROM io_tags t
LEFT JOIN module_addresses a
       ON a.device = t.io_device
      AND a.module_name = t.module_name
      AND a.sub_module_name = t.sub_module_name
      AND a.io_type = UPPER(SUBSTR(t.address, 2, 1));
";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = schema;
                command.ExecuteNonQuery();
            }
        }

        private static void RunImport(SqliteConnection connection, SqliteTransaction transaction, string table, Action import, List<string> errors)
        {
            try
            {
                import();
            }
            catch (Exception ex)
            {
                errors.Add($"{table}: {ex.Message}");
            }
        }

        private static HashSet<string> LoadStringSet(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            values.Add(reader.GetString(0));
                    }
                }
            }

            return values;
        }

        private static void ImportCatalog(SqliteConnection connection, SqliteTransaction transaction, string path, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT OR REPLACE INTO catalog (type_key, mlfb, tia_version, description, role, identifier) VALUES (@type_key, @mlfb, @tia_version, @description, @role, @identifier);"))
            {
                AddParameter(command, "@type_key", "@mlfb", "@tia_version", "@description", "@role", "@identifier");

                foreach (Row row in ReadRows(path))
                {
                    string? typeKey = row.Get("TypeKey");
                    if (typeKey == null)
                    {
                        warnings.Add($"{file} row {row.Number}: missing 'TypeKey'; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@type_key", typeKey);
                    SetParameter(command, "@mlfb", row.Get("MLFB"));
                    SetParameter(command, "@tia_version", row.Get("TiaVersion"));
                    SetParameter(command, "@description", row.Get("Description"));
                    SetParameter(command, "@role", row.Get("Role"));
                    SetParameter(command, "@identifier", row.Get("Identifier"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportDevices(SqliteConnection connection, SqliteTransaction transaction, string path, ISet<string> catalogKeys, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT OR REPLACE INTO devices (device_name, group_name, type_key, firmware, comment) VALUES (@device_name, @group_name, @type_key, @firmware, @comment);"))
            {
                AddParameter(command, "@device_name", "@group_name", "@type_key", "@firmware", "@comment");

                foreach (Row row in ReadRows(path))
                {
                    if (!TryGetRequired(row, "DeviceName", file, warnings, out string deviceName))
                        continue;

                    string? typeKey = row.Get("TypeKey");
                    if (typeKey == null)
                    {
                        warnings.Add($"{file} row {row.Number}: missing 'TypeKey' (must exist in {CatalogFileName}); row skipped.");
                        continue;
                    }

                    if (!catalogKeys.Contains(typeKey))
                    {
                        warnings.Add($"{file} row {row.Number}: TypeKey '{typeKey}' not found in {CatalogFileName}; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@device_name", deviceName);
                    SetParameter(command, "@group_name", row.Get("Group"));
                    SetParameter(command, "@type_key", typeKey);
                    SetParameter(command, "@firmware", row.Get("Firmware"));
                    SetParameter(command, "@comment", row.Get("Comment"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportSubnets(SqliteConnection connection, SqliteTransaction transaction, string path)
        {
            using (var command = CreateCommand(connection, transaction,
                "INSERT OR REPLACE INTO subnets (name, subnet_type) VALUES (@name, @subnet_type);"))
            {
                AddParameter(command, "@name", "@subnet_type");

                foreach (Row row in ReadRows(path))
                {
                    string? name = row.Get("Name");
                    if (name == null)
                        continue;

                    SetParameter(command, "@name", name);
                    SetParameter(command, "@subnet_type", row.Get("SubnetType"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportModules(SqliteConnection connection, SqliteTransaction transaction, string path, ISet<string> deviceNames, ISet<string> catalogKeys, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT INTO modules (device, module_name, slot, type_key, comment) VALUES (@device, @module_name, @slot, @type_key, @comment);"))
            {
                AddParameter(command, "@device", "@module_name", "@slot", "@type_key", "@comment");

                foreach (Row row in ReadRows(path))
                {
                    if (!TryGetRequired(row, "Device", file, warnings, out string device))
                        continue;
                    if (!TryGetRequired(row, "ModuleName", file, warnings, out string moduleName))
                        continue;

                    if (!deviceNames.Contains(device))
                    {
                        warnings.Add($"{file} row {row.Number}: device '{device}' not found in Devices.csv; row skipped.");
                        continue;
                    }

                    string? typeKey = row.Get("TypeKey");
                    if (typeKey == null)
                    {
                        warnings.Add($"{file} row {row.Number}: missing 'TypeKey' (must exist in {CatalogFileName}); row skipped.");
                        continue;
                    }

                    if (!catalogKeys.Contains(typeKey))
                    {
                        warnings.Add($"{file} row {row.Number}: TypeKey '{typeKey}' not found in {CatalogFileName}; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@device", device);
                    SetParameter(command, "@module_name", moduleName);
                    SetParameter(command, "@slot", row.GetInt("Slot"));
                    SetParameter(command, "@type_key", typeKey);
                    SetParameter(command, "@comment", row.Get("Comment"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportInterfaces(SqliteConnection connection, SqliteTransaction transaction, string path, ISet<string> deviceNames, ISet<string> subnetNames, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT INTO interfaces (device, interface_item, subnet, ip_address, subnet_mask, pn_device_name, master_io_system, master_io_system_number) VALUES (@device, @interface_item, @subnet, @ip_address, @subnet_mask, @pn_device_name, @master_io_system, @master_io_system_number);"))
            {
                AddParameter(command, "@device", "@interface_item", "@subnet", "@ip_address", "@subnet_mask", "@pn_device_name", "@master_io_system", "@master_io_system_number");

                foreach (Row row in ReadRows(path))
                {
                    if (!TryGetRequired(row, "Device", file, warnings, out string device))
                        continue;
                    if (!TryGetRequired(row, "InterfaceItem", file, warnings, out string interfaceItem))
                        continue;

                    if (!deviceNames.Contains(device))
                    {
                        warnings.Add($"{file} row {row.Number}: device '{device}' not found in Devices.csv; row skipped.");
                        continue;
                    }

                    string? subnet = row.Get("Subnet");
                    if (subnet != null && !subnetNames.Contains(subnet))
                    {
                        warnings.Add($"{file} row {row.Number}: subnet '{subnet}' not found in Subnets.csv; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@device", device);
                    SetParameter(command, "@interface_item", interfaceItem);
                    SetParameter(command, "@subnet", subnet);
                    SetParameter(command, "@ip_address", row.Get("IPAddress"));
                    SetParameter(command, "@subnet_mask", row.Get("SubnetMask"));
                    SetParameter(command, "@pn_device_name", row.Get("PNDeviceName"));
                    SetParameter(command, "@master_io_system", row.Get("MasterIOSystem"));
                    SetParameter(command, "@master_io_system_number", row.Get("MasterIOSystemNumber"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportModuleAddresses(SqliteConnection connection, SqliteTransaction transaction, string path, ISet<string> deviceNames, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT INTO module_addresses (device, module_name, sub_module_name, type_identifier, io_type, start_address, length) VALUES (@device, @module_name, @sub_module_name, @type_identifier, @io_type, @start_address, @length);"))
            {
                AddParameter(command, "@device", "@module_name", "@sub_module_name", "@type_identifier", "@io_type", "@start_address", "@length");

                foreach (Row row in ReadRows(path))
                {
                    if (!TryGetRequired(row, "Device", file, warnings, out string device))
                        continue;

                    if (!deviceNames.Contains(device))
                    {
                        warnings.Add($"{file} row {row.Number}: device '{device}' not found in Devices.csv; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@device", device);
                    SetParameter(command, "@module_name", row.Get("ModuleName"));
                    SetParameter(command, "@sub_module_name", row.Get("SubModuleName"));
                    SetParameter(command, "@type_identifier", row.Get("TypeIdentifier"));
                    SetParameter(command, "@io_type", row.Get("IoType"));
                    SetParameter(command, "@start_address", row.GetInt("StartAddress"));
                    SetParameter(command, "@length", row.GetInt("Length"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ImportIoTags(SqliteConnection connection, SqliteTransaction transaction, string path, ISet<string> deviceNames, List<string> warnings)
        {
            string file = Path.GetFileName(path);

            using (var command = CreateCommand(connection, transaction,
                "INSERT INTO io_tags (device, io_device, module_name, sub_module_name, tag_table, name, type, address, comment) VALUES (@device, @io_device, @module_name, @sub_module_name, @tag_table, @name, @type, @address, @comment);"))
            {
                AddParameter(command, "@device", "@io_device", "@module_name", "@sub_module_name", "@tag_table", "@name", "@type", "@address", "@comment");

                foreach (Row row in ReadRows(path))
                {
                    if (!TryGetRequired(row, "Device", file, warnings, out string device))
                        continue;

                    if (!deviceNames.Contains(device))
                    {
                        warnings.Add($"{file} row {row.Number}: device '{device}' not found in Devices.csv; row skipped.");
                        continue;
                    }

                    string? ioDevice = row.Get("IoDevice");
                    if (ioDevice != null && !deviceNames.Contains(ioDevice))
                    {
                        warnings.Add($"{file} row {row.Number}: io_device '{ioDevice}' not found in Devices.csv; row skipped.");
                        continue;
                    }

                    SetParameter(command, "@device", device);
                    SetParameter(command, "@io_device", ioDevice);
                    SetParameter(command, "@module_name", row.Get("ModuleName"));
                    SetParameter(command, "@sub_module_name", row.Get("SubModuleName"));
                    SetParameter(command, "@tag_table", row.Get("TagTable"));
                    SetParameter(command, "@name", row.Get("Name"));
                    SetParameter(command, "@type", row.Get("Type"));
                    SetParameter(command, "@address", row.Get("Address"));
                    SetParameter(command, "@comment", row.Get("Comment"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private static bool TryGetRequired(Row row, string column, string file, List<string> warnings, out string value)
        {
            string? raw = row.Get(column);
            if (string.IsNullOrWhiteSpace(raw))
            {
                warnings.Add($"{file} row {row.Number}: missing '{column}'; row skipped.");
                value = string.Empty;
                return false;
            }

            value = raw!;
            return true;
        }

        private static long CountRows(SqliteConnection connection, string table)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT COUNT(*) FROM {table};";
                object? value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private static void AddParameter(SqliteCommand command, params string[] names)
        {
            foreach (string name in names)
                command.Parameters.Add(new SqliteParameter(name, DBNull.Value));
        }

        private static void SetParameter(SqliteCommand command, string name, object? value)
        {
            command.Parameters[name].Value = value ?? (object)DBNull.Value;
        }

        private static IEnumerable<Row> ReadRows(string path)
        {
            var rows = new List<Row>();
            if (!File.Exists(path))
                return rows;

            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, CreateConfig()))
            {
                if (!csv.Read() || !csv.ReadHeader())
                    return rows;

                string[] headers = csv.HeaderRecord ?? Array.Empty<string>();
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Length; i++)
                    index[headers[i]] = i;

                while (csv.Read())
                {
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, int> column in index)
                    {
                        string? value = null;
                        try
                        {
                            value = csv.GetField(column.Value);
                        }
                        catch
                        {
                            value = null;
                        }

                        if (value != null)
                        {
                            value = value.Trim();
                            if (value.Length == 0)
                                value = null;
                        }

                        values[column.Key] = value;
                    }

                    rows.Add(new Row(values, csv.Parser.Row));
                }
            }

            return rows;
        }

        private static CsvConfiguration CreateConfig()
        {
            return new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim,
            };
        }

        private sealed class Row
        {
            private readonly IDictionary<string, string?> _values;

            public Row(IDictionary<string, string?> values, int number)
            {
                _values = values;
                Number = number;
            }

            public int Number { get; }

            public string? Get(string name)
            {
                return _values.TryGetValue(name, out string? value) ? value : null;
            }

            public int? GetInt(string name)
            {
                string? value = Get(name);
                if (value == null)
                    return null;

                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    ? number
                    : (int?)null;
            }
        }
    }
}
