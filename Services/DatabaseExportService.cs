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
    public class DatabaseExportService
    {
        private const string CatalogFileName = "Catalog.csv";

        public string DefaultDbPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tia_topology.db"); }
        }

        public string DefaultOutputDir
        {
            get { return ResolveOutputDirectory(); }
        }

        public string DefaultCatalogPath
        {
            get { return ResolveCatalogPath(DefaultOutputDir); }
        }

        public DbExportResult ExportToCsv(string? dbPath = null, string? outputDir = null, string? catalogPath = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new DbExportResult();

            string resolvedDbPath = string.IsNullOrWhiteSpace(dbPath) ? DefaultDbPath : dbPath!;
            resolvedDbPath = Path.GetFullPath(resolvedDbPath);

            string resolvedOutputDir = string.IsNullOrWhiteSpace(outputDir) ? DefaultOutputDir : outputDir!;
            resolvedOutputDir = Path.GetFullPath(resolvedOutputDir);

            string resolvedCatalogPath = string.IsNullOrWhiteSpace(catalogPath) ? ResolveCatalogPath(resolvedOutputDir) : catalogPath!;
            resolvedCatalogPath = Path.GetFullPath(resolvedCatalogPath);

            result.DbPath = resolvedDbPath;
            result.OutputDir = resolvedOutputDir;
            result.CatalogPath = resolvedCatalogPath;

            if (!File.Exists(resolvedDbPath))
            {
                result.Errors.Add($"Database not found: {resolvedDbPath}");
                result.Elapsed = stopwatch.Elapsed;
                return result;
            }

            try
            {
                Directory.CreateDirectory(resolvedOutputDir);

                string? catalogDirectory = Path.GetDirectoryName(resolvedCatalogPath);
                if (!string.IsNullOrEmpty(catalogDirectory))
                    Directory.CreateDirectory(catalogDirectory!);

                using (var connection = new SqliteConnection($"Data Source={resolvedDbPath};Mode=ReadOnly"))
                {
                    connection.Open();

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "Devices.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "Devices.csv"),
                            withBom: true,
                            header: new[] { "DeviceName", "Group", "TypeKey", "Firmware", "Comment" },
                            sql: "SELECT device_name, group_name, type_key, firmware, comment FROM devices ORDER BY rowid;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "Modules.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "Modules.csv"),
                            withBom: true,
                            header: new[] { "Device", "ModuleName", "Slot", "TypeKey", "Comment" },
                            sql: "SELECT device, module_name, slot, type_key, comment FROM modules ORDER BY id;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Number(r, 2), Text(r, 3), Text(r, 4) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "Subnets.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "Subnets.csv"),
                            withBom: true,
                            header: new[] { "Name", "SubnetType" },
                            sql: "SELECT name, subnet_type FROM subnets ORDER BY rowid;",
                            map: r => new[] { Text(r, 0), Text(r, 1) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "Interfaces.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "Interfaces.csv"),
                            withBom: true,
                            header: new[] { "Device", "InterfaceItem", "Subnet", "IPAddress", "SubnetMask", "PNDeviceName", "MasterIOSystem" },
                            sql: "SELECT device, interface_item, subnet, ip_address, subnet_mask, pn_device_name, master_io_system FROM interfaces ORDER BY id;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "ModuleAddresses.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "ModuleAddresses.csv"),
                            withBom: true,
                            header: new[] { "Device", "ModuleName", "SubModuleName", "TypeIdentifier", "IoType", "StartAddress", "Length" },
                            sql: "SELECT device, module_name, sub_module_name, type_identifier, io_type, start_address, length FROM module_addresses ORDER BY id;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Number(r, 5), Number(r, 6) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = Path.Combine(resolvedOutputDir, "IoTags.csv"),
                        Count = WriteFile(connection,
                            Path.Combine(resolvedOutputDir, "IoTags.csv"),
                            withBom: true,
                            header: new[] { "Device", "IoDevice", "ModuleName", "SubModuleName", "TagTable", "Name", "Type", "Address", "Comment" },
                            sql: "SELECT device, io_device, module_name, sub_module_name, tag_table, name, type, address, comment FROM io_tags ORDER BY id;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6), Text(r, 7), Text(r, 8) }),
                    });

                    result.FileCounts.Add(new DbFileCount
                    {
                        FileName = resolvedCatalogPath,
                        Count = WriteFile(connection,
                            resolvedCatalogPath,
                            withBom: false,
                            header: new[] { "TypeKey", "MLFB", "TiaVersion", "Description", "Role", "Identifier" },
                            sql: "SELECT type_key, mlfb, tia_version, description, role, identifier FROM catalog ORDER BY rowid;",
                            map: r => new[] { Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5) }),
                    });
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

        private static int WriteFile(
            SqliteConnection connection,
            string path,
            bool withBom,
            string[] header,
            string sql,
            Func<SqliteDataReader, string?[]> map)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;

                using (var reader = command.ExecuteReader())
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(withBom)))
                using (var csv = new CsvWriter(writer, CreateConfig()))
                {
                    foreach (string column in header)
                        csv.WriteField(column);
                    csv.NextRecord();

                    int count = 0;
                    while (reader.Read())
                    {
                        foreach (string? value in map(reader))
                            csv.WriteField(value);
                        csv.NextRecord();
                        count++;
                    }

                    return count;
                }
            }
        }

        private static string? Text(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? null : reader.GetString(index);
        }

        private static string? Number(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index)
                ? null
                : reader.GetInt64(index).ToString(CultureInfo.InvariantCulture);
        }

        private static string ResolveOutputDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            DirectoryInfo? current = new DirectoryInfo(baseDir);
            for (int depth = 0; depth < 5 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, "exports");
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return Path.Combine(baseDir, "exports");
        }

        private static string ResolveCatalogPath(string outputDir)
        {
            string? parent = SafeParent(outputDir);
            if (parent != null)
                return Path.Combine(parent, "catalog", CatalogFileName);

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog", CatalogFileName);
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
    }
}
