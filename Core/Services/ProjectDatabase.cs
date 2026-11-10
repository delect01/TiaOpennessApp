using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Project database (SQLite) schema and versioning.
    /// The schema version is stored in <c>PRAGMA user_version</c>; <see cref="Open"/>
    /// upgrades older databases in place, one version step at a time.
    /// </summary>
    /// <remarks>
    /// v1: catalog, devices, subnets, modules, interfaces, module_addresses, io_tags (+ triggers/views).
    /// v2: project_info (key/value), blocks (Blocks.csv manifest), unique IP and PROFINET name per
    ///     subnet, unique I/O tag name per PLC.
    /// module_addresses keeps a text link to the module: GSD submodules (e.g. MC120/UM1_PD) are not
    /// rows of the modules table, so a foreign key is not possible; the validator checks it instead.
    /// </remarks>
    public static class ProjectDatabase
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>Tables in dependency order (parents first).</summary>
        public static readonly string[] TableNames =
        {
            "project_info",
            "catalog",
            "devices",
            "subnets",
            "modules",
            "interfaces",
            "module_addresses",
            "io_tags",
            "blocks",
        };

        /// <summary>Well-known keys of the project_info table.</summary>
        public static class InfoKeys
        {
            public const string CreatedUtc = "created_utc";
            public const string Source = "source";
            public const string CatalogVersion = "catalog_version";
        }

        /// <summary>
        /// Opens (or creates) a project database with foreign keys enabled and the schema
        /// upgraded to <see cref="CurrentSchemaVersion"/>.
        /// </summary>
        public static SqliteConnection Open(string path, bool create = false)
        {
            string fullPath = Path.GetFullPath(path);
            if (!create && !File.Exists(fullPath))
                throw new FileNotFoundException($"Project database not found: {fullPath}", fullPath);

            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory!);

            var connection = new SqliteConnection($"Data Source={fullPath}");
            try
            {
                connection.Open();
                Execute(connection, null, "PRAGMA foreign_keys = ON;");
                EnsureSchema(connection);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>Opens an existing database read-only, without migrating it.</summary>
        public static SqliteConnection OpenReadOnly(string path)
        {
            var connection = new SqliteConnection($"Data Source={Path.GetFullPath(path)};Mode=ReadOnly");
            connection.Open();
            return connection;
        }

        public static int GetSchemaVersion(SqliteConnection connection) =>
            Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;") ?? 0, CultureInfo.InvariantCulture);

        public static bool TableExists(SqliteConnection connection, string table) =>
            Convert.ToInt64(Scalar(connection, null,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;",
                ("@name", table)) ?? 0L, CultureInfo.InvariantCulture) > 0;

        /// <summary>
        /// Creates the schema on an empty database or upgrades an older one.
        /// Each step runs in its own transaction; a failing step leaves the database at the previous version.
        /// </summary>
        public static void EnsureSchema(SqliteConnection connection)
        {
            int version = GetSchemaVersion(connection);
            if (version > CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Project database schema v{version} is newer than this application (v{CurrentSchemaVersion}).");

            // v1 databases were created before versioning existed: user_version = 0 but tables present.
            if (version == 0 && TableExists(connection, "devices"))
                version = 1;

            if (version < 1)
                Step(connection, 1, SchemaV1);
            if (version < 2)
                Step(connection, 2, MigrateV1ToV2);
        }

        public static string? GetInfo(SqliteConnection connection, string key) =>
            Scalar(connection, null, "SELECT value FROM project_info WHERE key = @key;", ("@key", key)) as string;

        public static void SetInfo(SqliteConnection connection, SqliteTransaction? transaction, string key, string? value) =>
            Execute(connection, transaction,
                "INSERT INTO project_info (key, value) VALUES (@key, @value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                ("@key", key), ("@value", value));

        private static void Step(SqliteConnection connection, int targetVersion, string sql)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    Execute(connection, transaction, sql);
                    Execute(connection, transaction, $"PRAGMA user_version = {targetVersion};");
                    transaction.Commit();
                }
                catch (SqliteException ex)
                {
                    transaction.Rollback();
                    throw new InvalidOperationException(
                        $"Cannot upgrade the project database to schema v{targetVersion}: {ex.Message}", ex);
                }
            }
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
            params (string Name, object? Value)[] parameters)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql,
            params (string Name, object? Value)[] parameters)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                object? result = command.ExecuteScalar();
                return result == DBNull.Value ? null : result;
            }
        }

        // ----------------------------------------------------------------------------------
        // v1 - original schema (kept verbatim so upgraded and new databases end up identical).
        // ----------------------------------------------------------------------------------
        private const string SchemaV1 = @"

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

        // ----------------------------------------------------------------------------------
        // v2
        // ----------------------------------------------------------------------------------
        private const string MigrateV1ToV2 = @"
CREATE TABLE project_info (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE blocks (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    device               TEXT NOT NULL
                         REFERENCES devices(device_name) ON UPDATE CASCADE ON DELETE RESTRICT,
    group_path           TEXT NOT NULL DEFAULT '',
    kind                 TEXT NOT NULL DEFAULT 'Block',
    name                 TEXT NOT NULL CHECK (length(trim(name)) > 0),
    namespace            TEXT,
    number               INTEGER,
    auto_number          INTEGER CHECK (auto_number IN (0, 1)),
    block_type           TEXT,
    programming_language TEXT,
    memory_layout        TEXT,
    title                TEXT,
    comment              TEXT,
    source_file          TEXT,
    UNIQUE(device, kind, name)
);

CREATE INDEX idx_blocks_device ON blocks(device);

-- Devices referenced by blocks cannot be deleted either.
DROP TRIGGER trg_devices_delete;
CREATE TRIGGER trg_devices_delete
BEFORE DELETE ON devices
BEGIN
    SELECT RAISE(ABORT, 'device is referenced by modules, interfaces, module_addresses, io_tags or blocks')
    WHERE EXISTS (SELECT 1 FROM modules WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM interfaces WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM module_addresses WHERE device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM io_tags WHERE device = OLD.device_name OR io_device = OLD.device_name)
       OR EXISTS (SELECT 1 FROM blocks WHERE device = OLD.device_name);
END;

-- One IP address and one PROFINET device name per subnet.
CREATE UNIQUE INDEX ux_interfaces_subnet_ip
    ON interfaces(subnet, ip_address)
    WHERE subnet IS NOT NULL AND ip_address IS NOT NULL AND ip_address <> '';
CREATE UNIQUE INDEX ux_interfaces_subnet_pn_name
    ON interfaces(subnet, pn_device_name)
    WHERE subnet IS NOT NULL AND pn_device_name IS NOT NULL AND pn_device_name <> '';

-- TIA tag names are unique per PLC across all tag tables.
CREATE UNIQUE INDEX ux_io_tags_device_name
    ON io_tags(device, name)
    WHERE name IS NOT NULL AND name <> '';
";
    }
}
