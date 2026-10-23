using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class DeviceGenerationItemReport
    {
        public string DeviceName { get; set; } = string.Empty;
        public string TypeKey { get; set; } = string.Empty;
        public bool Created { get; set; }
        public bool Skipped { get; set; }
        public string? Error { get; set; }
    }

    public class DevicesGenerationService
    {
        private readonly Project _project;
        private readonly Dictionary<string, CatalogEntry> _catalog;

        public DevicesGenerationService(Project project, List<CatalogEntry> catalog)
        {
            _project = project;
            _catalog = catalog.ToDictionary(e => e.TypeKey, StringComparer.OrdinalIgnoreCase);
        }

        public List<DeviceGenerationItemReport> Generate(
            IEnumerable<DeviceSpec> deviceSpecs,
            IReadOnlyDictionary<string, string>? rootItemNames = null)
        {
            var results = new List<DeviceGenerationItemReport>();
            DeviceComposition devices = _project.Devices;

            foreach (var spec in deviceSpecs)
            {
                var report = new DeviceGenerationItemReport { DeviceName = spec.DeviceName, TypeKey = spec.TypeKey };

                try
                {
                    if (DevicesAndNetworksService.FindDeviceByName(_project, spec.DeviceName) != null)
                    {
                        report.Skipped = true;
                        results.Add(report);
                        continue;
                    }

                    if (!_catalog.TryGetValue(spec.TypeKey, out var entry))
                    {
                        report.Error = $"TypeKey '{spec.TypeKey}' no present al catàleg.";
                        results.Add(report);
                        continue;
                    }

                    if (CatalogService.IsGsdIdentifier(entry.Identifier))
                    {
                        report.Error = $"Dispositiu GSD '{spec.DeviceName}' no importat: cal un golden AML " +
                                       $"que el contingui o inserir-lo manualment a TIA.";
                        results.Add(report);
                        continue;
                    }

                    string itemName = DeviceNameToItemName(spec, rootItemNames);
                    Device? device = null;
                    var errors = new List<string>();

                    foreach (string identifier in BuildIdentifiers(entry, spec.Firmware))
                    {
                        try
                        {
                            device = devices.CreateWithItem(identifier, itemName, spec.DeviceName);
                            break;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex.Message);
                        }
                    }

                    if (device == null)
                        report.Error = "No s'ha pogut crear: " + string.Join(" | ", errors);
                    else
                        report.Created = true;
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }

                results.Add(report);
            }
            return results;
        }

        private static string DeviceNameToItemName(DeviceSpec spec, IReadOnlyDictionary<string, string>? rootItemNames)
        {
            if (rootItemNames != null
                && rootItemNames.TryGetValue(spec.DeviceName, out string? name)
                && name?.Length > 0)
                return name;
            return spec.DeviceName;
        }

        private static List<string> BuildIdentifiers(CatalogEntry entry, string? firmware)
        {
            var list = new List<string>();

            if (entry.Identifier?.Length > 0)
                list.Add(entry.Identifier);

            string mlfb = entry.Mlfb;
            string compact = "OrderNumber:" + mlfb;
            string styled = "OrderNumber:" + InsertStandardSpacing(mlfb);

            if (!list.Contains(compact, StringComparer.OrdinalIgnoreCase))
                list.Add(compact);

            if (!string.Equals(compact, styled, StringComparison.OrdinalIgnoreCase)
                && !list.Contains(styled, StringComparer.OrdinalIgnoreCase))
                list.Add(styled);

            if (!string.IsNullOrWhiteSpace(firmware))
            {
                string fw = firmware!;
                list.Add(compact + "/" + fw);
                list.Add(styled + "/" + fw);
            }

            return list;
        }

        private static string InsertStandardSpacing(string mlfb)
        {
            if (mlfb.Length > 4)
                return mlfb.Substring(0, 4) + " " + mlfb.Substring(4);
            return mlfb;
        }
    }
}