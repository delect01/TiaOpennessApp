using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Catalog;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Approved catalog database (<c>catalog.db</c>). The administrator edits a "catalog source"
    /// copy; publishing signs a copy of it into a package. Version in <c>PRAGMA user_version</c>:
    /// v1 = catalog_info + types; v2 = + kits (one row per kit, definition stored as JSON).
    /// </summary>
    public static class CatalogDatabase
    {
        public const int CurrentSchemaVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        public static SqliteConnection Open(string path, bool create = false)
        {
            string full = Path.GetFullPath(path);
            if (!create && !File.Exists(full))
                throw new FileNotFoundException($"Catalog database not found: {full}", full);
            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory!);

            var connection = new SqliteConnection($"Data Source={full};Pooling=False");
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

        public static SqliteConnection OpenReadOnly(string path)
        {
            var connection = new SqliteConnection($"Data Source={Path.GetFullPath(path)};Mode=ReadOnly;Pooling=False");
            connection.Open();
            return connection;
        }

        public static int GetSchemaVersion(SqliteConnection connection) =>
            Convert.ToInt32(Scalar(connection, "PRAGMA user_version;") ?? 0, CultureInfo.InvariantCulture);

        private static void EnsureSchema(SqliteConnection connection)
        {
            int version = GetSchemaVersion(connection);
            if (version > CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Catalog database schema v{version} is newer than this application (v{CurrentSchemaVersion}).");
            if (version < 1)
                Step(connection, 1, SchemaV1);
            if (version < 2)
                Step(connection, 2, MigrateV1ToV2);
        }

        // ------------------------------------------------------------------ info

        public static string? GetInfo(SqliteConnection connection, string key) =>
            Scalar(connection, "SELECT value FROM catalog_info WHERE key = @k;", ("@k", key)) as string;

        public static void SetInfo(SqliteConnection connection, string key, string? value) =>
            Execute(connection, null,
                "INSERT INTO catalog_info (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                ("@k", key), ("@v", value));

        // ------------------------------------------------------------------ types

        /// <summary>Inserts or updates types (by type key). Returns (added, updated).</summary>
        public static (int Added, int Updated) UpsertTypes(SqliteConnection connection, IEnumerable<CatalogEntry> types)
        {
            int added = 0, updated = 0;
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                foreach (CatalogEntry t in types)
                {
                    bool exists = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM types WHERE type_key = @k;",
                        ("@k", t.TypeKey)) ?? 0L, CultureInfo.InvariantCulture) > 0;
                    Execute(connection, transaction,
                        "INSERT INTO types (type_key, mlfb, tia_version, description, role, identifier) " +
                        "VALUES (@k, @m, @v, @d, @r, @i) ON CONFLICT(type_key) DO UPDATE SET " +
                        "mlfb = excluded.mlfb, tia_version = excluded.tia_version, description = excluded.description, " +
                        "role = excluded.role, identifier = excluded.identifier;",
                        ("@k", t.TypeKey), ("@m", t.Mlfb), ("@v", t.TiaVersion), ("@d", t.Description),
                        ("@r", t.Role.ToString()), ("@i", t.Identifier));
                    if (exists) updated++; else added++;
                }
                transaction.Commit();
            }
            return (added, updated);
        }

        public static List<CatalogEntry> LoadTypes(SqliteConnection connection)
        {
            var types = new List<CatalogEntry>();
            Query(connection, "SELECT type_key, mlfb, tia_version, description, role, identifier FROM types ORDER BY type_key;", r =>
                types.Add(new CatalogEntry
                {
                    TypeKey = r.GetString(0),
                    Mlfb = Text(r, 1),
                    TiaVersion = Text(r, 2),
                    Description = Text(r, 3),
                    Role = string.Equals(Text(r, 4), "Root", StringComparison.OrdinalIgnoreCase) ? CatalogItemRole.Root : CatalogItemRole.Module,
                    Identifier = Text(r, 5),
                }));
            return types;
        }

        // ------------------------------------------------------------------ kits

        /// <summary>Validates and stores a kit (replaces an existing kit with the same id).</summary>
        public static void SaveKit(SqliteConnection connection, KitDefinition kit)
        {
            List<string> errors = ValidateKit(kit, LoadTypes(connection));
            if (errors.Count > 0)
                throw new InvalidDataException($"Kit '{kit.Id}' is not valid:{Environment.NewLine}  " +
                                               string.Join(Environment.NewLine + "  ", errors));

            Execute(connection, null,
                "INSERT INTO kits (kit_id, name, device_type_key, definition_json, modified_utc) " +
                "VALUES (@id, @n, @t, @j, @m) ON CONFLICT(kit_id) DO UPDATE SET name = excluded.name, " +
                "device_type_key = excluded.device_type_key, definition_json = excluded.definition_json, modified_utc = excluded.modified_utc;",
                ("@id", kit.Id), ("@n", kit.Name), ("@t", kit.DeviceTypeKey),
                ("@j", JsonSerializer.Serialize(kit, JsonOptions)),
                ("@m", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
        }

        public static bool DeleteKit(SqliteConnection connection, string kitId) =>
            Convert.ToInt32(ExecuteCount(connection, "DELETE FROM kits WHERE kit_id = @id;", ("@id", kitId))) > 0;

        public static List<KitDefinition> LoadKits(SqliteConnection connection)
        {
            var kits = new List<KitDefinition>();
            if (GetSchemaVersion(connection) < 2)
                return kits;
            Query(connection, "SELECT definition_json FROM kits ORDER BY kit_id;", r =>
            {
                KitDefinition? kit = JsonSerializer.Deserialize<KitDefinition>(r.GetString(0), JsonOptions);
                if (kit != null)
                    kits.Add(kit);
            });
            return kits;
        }

        /// <summary>Rules every kit must meet (types approved, roles, I/Q, lengths, tags inside ranges).</summary>
        public static List<string> ValidateKit(KitDefinition kit, IReadOnlyList<CatalogEntry> types)
        {
            var errors = new List<string>();
            CatalogEntry? Type(string key) => types.FirstOrDefault(t => string.Equals(t.TypeKey, key, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(kit.Id) || !kit.Id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
                errors.Add("Id must be letters, digits, '_', '-' or '.'.");
            if (string.IsNullOrWhiteSpace(kit.Name))
                errors.Add("Name is required.");

            CatalogEntry? deviceType = Type(kit.DeviceTypeKey);
            if (deviceType == null)
                errors.Add($"Device type '{kit.DeviceTypeKey}' is not in the catalog types.");
            else if (deviceType.Role != CatalogItemRole.Root)
                errors.Add($"Device type '{kit.DeviceTypeKey}' is a module type, not a station type.");

            foreach (KitModule m in kit.Modules)
            {
                if (string.IsNullOrWhiteSpace(m.ModuleName))
                    errors.Add("A module has no name.");
                if (Type(m.TypeKey) == null)
                    errors.Add($"Module '{m.ModuleName}': type '{m.TypeKey}' is not in the catalog types.");
            }
            foreach (var dup in kit.Modules.GroupBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                errors.Add($"Module name '{dup.Key}' is repeated.");

            foreach (KitInterface i in kit.Interfaces)
            {
                if (string.IsNullOrWhiteSpace(i.InterfaceItem))
                    errors.Add("An interface has no name.");
            }

            foreach (KitAddress a in kit.Addresses)
            {
                if (a.IoType != "I" && a.IoType != "Q")
                    errors.Add($"Address '{a.ModuleName}/{a.SubModuleName}': IoType must be I or Q.");
                if (a.LengthBits <= 0)
                    errors.Add($"Address '{a.ModuleName}/{a.SubModuleName}': length must be > 0 bits.");
            }

            foreach (KitModuleOption o in kit.AllowedModules)
            {
                if (string.IsNullOrWhiteSpace(o.Name))
                    errors.Add("An allowed module has no name.");
                if (!string.IsNullOrWhiteSpace(o.TypeKey) && Type(o.TypeKey) == null)
                    errors.Add($"Allowed module '{o.Name}': type '{o.TypeKey}' is not in the catalog types.");
                if (string.IsNullOrWhiteSpace(o.TypeKey) && o.Addresses.Count == 0)
                    errors.Add($"Allowed module '{o.Name}': needs a catalog type or at least one I/Q range.");
                if (o.MinSlot != null && o.MaxSlot != null && o.MinSlot > o.MaxSlot)
                    errors.Add($"Allowed module '{o.Name}': slot range {o.MinSlot}..{o.MaxSlot} is empty.");
                foreach (KitAddress a in o.Addresses.Where(a => (a.IoType != "I" && a.IoType != "Q") || a.LengthBits <= 0))
                    errors.Add($"Allowed module '{o.Name}': range '{a.SubModuleName}' needs I/Q and a length > 0.");
            }

            foreach (KitTag t in kit.Tags)
            {
                KitAddress? range = kit.Addresses.FirstOrDefault(a =>
                    string.Equals(a.ModuleName, t.ModuleName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.SubModuleName, t.SubModuleName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.IoType, t.IoType, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.DataType))
                    errors.Add("A tag template has no name or data type.");
                else if (range == null)
                    errors.Add($"Tag '{t.Name}': no {t.IoType} range for '{t.ModuleName}/{t.SubModuleName}' in the kit.");
                else if (t.BitOffset < 0 || t.BitOffset + DataTypeBits(t.DataType) > range.LengthBits)
                    errors.Add($"Tag '{t.Name}': offset {t.BitOffset} + {DataTypeBits(t.DataType)} bits is outside the {range.LengthBits}-bit range.");
            }

            return errors;
        }

        /// <summary>Size in bits of the elementary types used for I/O tags (unknown types count as 1 bit).</summary>
        public static int DataTypeBits(string dataType)
        {
            switch ((dataType ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "BYTE": case "SINT": case "USINT": case "CHAR": return 8;
                case "WORD": case "INT": case "UINT": return 16;
                case "DWORD": case "DINT": case "UDINT": case "REAL": case "TIME": return 32;
                case "LWORD": case "LINT": case "ULINT": case "LREAL": return 64;
                default: return 1;
            }
        }

        // ------------------------------------------------------------------ schema

        private const string SchemaV1 = @"
CREATE TABLE catalog_info (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE types (
    type_key    TEXT PRIMARY KEY COLLATE NOCASE,
    mlfb        TEXT,
    tia_version TEXT,
    description TEXT,
    role        TEXT NOT NULL CHECK (role IN ('Root', 'Module')),
    identifier  TEXT
);";

        private const string MigrateV1ToV2 = @"
CREATE TABLE kits (
    kit_id          TEXT PRIMARY KEY COLLATE NOCASE,
    name            TEXT NOT NULL,
    device_type_key TEXT NOT NULL REFERENCES types(type_key) ON UPDATE CASCADE ON DELETE RESTRICT,
    definition_json TEXT NOT NULL,
    modified_utc    TEXT
);
CREATE INDEX idx_kits_device_type ON kits(device_type_key);";

        // ------------------------------------------------------------------ helpers

        private static void Step(SqliteConnection connection, int targetVersion, string sql)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                Execute(connection, transaction, sql);
                Execute(connection, transaction, $"PRAGMA user_version = {targetVersion};");
                transaction.Commit();
            }
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
            params (string Name, object? Value)[] parameters) =>
            ExecuteCount(connection, sql, transaction, parameters);

        private static int ExecuteCount(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
            ExecuteCount(connection, sql, null, parameters);

        private static int ExecuteCount(SqliteConnection connection, string sql, SqliteTransaction? transaction,
            params (string Name, object? Value)[] parameters)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                return command.ExecuteNonQuery();
            }
        }

        private static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                object? result = command.ExecuteScalar();
                return result == DBNull.Value ? null : result;
            }
        }

        private static void Query(SqliteConnection connection, string sql, Action<SqliteDataReader> row)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (SqliteDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                        row(reader);
            }
        }

        private static string Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
    }
}
