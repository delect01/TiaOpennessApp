using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class TagGenerationItemReport
    {
        public string Device { get; set; } = string.Empty;
        public string TagTable { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public bool Created { get; set; }
        public bool Skipped { get; set; }
        public string? Error { get; set; }
    }

    public class TagsGenerationService
    {
        private readonly Project _project;

        public TagsGenerationService(Project project)
        {
            _project = project;
        }

        public List<TagGenerationItemReport> Generate(IList<IoTagSpec> ioTags)
        {
            var reports = new List<TagGenerationItemReport>();
            if (ioTags == null || ioTags.Count == 0)
                return reports;

            var deviceCache = new Dictionary<string, Device?>(StringComparer.OrdinalIgnoreCase);
            var plcCache = new Dictionary<string, PlcSoftware?>(StringComparer.OrdinalIgnoreCase);
            var tableCache = new Dictionary<string, Dictionary<string, PlcTagTable?>>(StringComparer.OrdinalIgnoreCase);

            foreach (IoTagSpec tag in ioTags)
            {
                var report = new TagGenerationItemReport
                {
                    Device = tag.Device,
                    TagTable = tag.TagTable,
                    TagName = tag.Name,
                };

                try
                {
                    string deviceKey = tag.Device;

                    if (!deviceCache.TryGetValue(deviceKey, out Device? device))
                    {
                        device = SafeFindDevice(deviceKey);
                        deviceCache[deviceKey] = device;
                    }

                    if (device == null)
                    {
                        report.Error = $"Dispositiu PLC '{deviceKey}' no trobat al projecte.";
                        reports.Add(report);
                        continue;
                    }

                    if (!plcCache.TryGetValue(deviceKey, out PlcSoftware? plc))
                    {
                        plc = GetPlcSoftware(device);
                        plcCache[deviceKey] = plc;
                    }

                    if (plc == null)
                    {
                        report.Error = $"No s'ha pogut obtenir el PlcSoftware del dispositiu '{deviceKey}'.";
                        reports.Add(report);
                        continue;
                    }

                    var root = plc.TagTableGroup;
                    string tableName = string.IsNullOrWhiteSpace(tag.TagTable) ? string.Empty : tag.TagTable!;

                    if (!tableCache.TryGetValue(deviceKey, out var perDeviceTables))
                    {
                        perDeviceTables = new Dictionary<string, PlcTagTable?>(StringComparer.OrdinalIgnoreCase);
                        tableCache[deviceKey] = perDeviceTables;
                    }

                    if (!perDeviceTables.TryGetValue(tableName, out PlcTagTable? table))
                    {
                        table = FindOrCreateTable(root, tableName);
                        perDeviceTables[tableName] = table;
                    }

                    if (table == null)
                    {
                        report.Error = $"No s'ha pogut crear/obtenir la taula de tags '{tableName}'.";
                        reports.Add(report);
                        continue;
                    }

                    PlcTag? existing = SafeFindTag(table, tag.Name);
                    if (existing != null)
                    {
                        report.Skipped = true;
                        reports.Add(report);
                        continue;
                    }

                    PlcTag plcTag = CreateTag(table, tag);
                    if (!string.IsNullOrWhiteSpace(tag.Comment))
                    {
                        try { ((IEngineeringObject)plcTag).SetAttribute("Comment", tag.Comment); }
                        catch { }
                    }

                    report.Created = true;
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }

                reports.Add(report);
            }

            return reports;
        }

        private Device? SafeFindDevice(string name)
        {
            try { return _project.Devices.Find(name); }
            catch { return null; }
        }

        private PlcSoftware? GetPlcSoftware(Device device)
        {
            SoftwareContainer? container = TagsExportService.FindSoftwareContainer(device);
            return container?.Software as PlcSoftware;
        }

        private static PlcTagTable? FindOrCreateTable(PlcTagTableGroup root, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                foreach (PlcTagTable t in root.TagTables)
                    if (t.IsDefault) return t;
                return null;
            }

            PlcTagTable? existing = root.TagTables.Find(tableName);
            if (existing != null) return existing;

            PlcTagTable created = root.TagTables.Create(tableName);
            created.Name = tableName;
            return created;
        }

        private static PlcTag CreateTag(PlcTagTable table, IoTagSpec spec)
        {
            string name = spec.Name;
            string type = spec.Type;
            string address = spec.Address;

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("El nom del tag és buit.");
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException($"El tipus de dada del tag '{name}' és buit.");
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException($"L'adreça del tag '{name}' és buida.");

            return table.Tags.Create(name, type, address);
        }

        private static PlcTag? SafeFindTag(PlcTagTable table, string name)
        {
            try { return table.Tags.Find(name); }
            catch { return null; }
        }
    }
}
