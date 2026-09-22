using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.HW;

namespace TiaOpennessApp.Services
{
    public class GsdCaxImportReport
    {
        public bool ImportOk { get; set; }
        public int ImportedDeviceCount { get; set; }
        public List<string> KeptDevices { get; set; } = new List<string>();
        public List<string> RemovedDevices { get; set; } = new List<string>();
        public List<string> MissingExpectedDevices { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
    }

    /// <summary>
    /// Importa un fitxer CAx/AML "golden" (export del projecte sencer) dins del projecte
    /// de treball amb <see cref="CaxImportOptions.RetainTiaDevice"/> i conserva únicament
    /// els dispositius que el catàleg espera (els GSD de tercers), esborrant la resta.
    /// Així els dispositius GSD arriben al pipeline ja creats i els serveis de generació
    /// els troben (i els salten) sense necessitat de la llicència gsdml.
    /// </summary>
    public class GsdCaxImportService
    {
        private readonly Project _project;

        public GsdCaxImportService(Project project)
        {
            _project = project;
        }

        public bool HasGsdDevices()
        {
            return DevicesAndNetworksService.EnumerateAllDevices(_project)
                .Any(d => d.IsGsd);
        }

        public GsdCaxImportReport ImportGoldenAndKeep(string amlPath, IReadOnlyCollection<string> keepDeviceNames)
        {
            var report = new GsdCaxImportReport();

            if (!File.Exists(amlPath))
            {
                report.Errors.Add($"Fitxer golden AML no trobat: {amlPath}");
                return report;
            }

            try
            {
                var aml = new FileInfo(amlPath);
                var expected = new HashSet<string>(keepDeviceNames.Where(n => !string.IsNullOrWhiteSpace(n)),
                    StringComparer.OrdinalIgnoreCase);

                TransferResult result = CaxTransferService.ImportAml(_project, aml, CaxImportOptions.RetainTiaDevice);

                report.ErrorCount = result.ErrorCount;
                report.WarningCount = result.WarningCount;
                report.ImportOk = result.State == TransferResultState.Success || result.State == TransferResultState.Warning;

                foreach (TransferResultMessage message in result.Messages)
                {
                    if (message.State == TransferResultState.Error)
                        report.Errors.Add(message.Message);
                    else if (message.State == TransferResultState.Warning)
                        report.Warnings.Add(message.Message);
                }

                if (!report.ImportOk)
                    return report;

                var imported = DevicesAndNetworksService.EnumerateAllDevices(_project).ToList();
                report.ImportedDeviceCount = imported.Count;
                report.KeptDevices.AddRange(imported.Where(d => expected.Contains(d.Name)).Select(d => d.Name));
                report.MissingExpectedDevices.AddRange(expected.Where(n => imported.All(d => !string.Equals(d.Name, n, StringComparison.OrdinalIgnoreCase))));

                // Capturar els noms ANTES de cap esborrat: les instancies Device del projecte
                // queden invalidades ("disposed") quan TIA reordena la composicio despres d'un Delete.
                List<string> namesToRemove = imported
                    .Where(d => !expected.Contains(d.Name))
                    .Select(d => d.Name)
                    .ToList();

                foreach (string name in namesToRemove)
                {
                    try
                    {
                        Device? device = DevicesAndNetworksService.FindDeviceByName(_project, name);
                        if (device == null)
                            continue;
                        device.Delete();
                        report.RemovedDevices.Add(name);
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"No s'ha pogut esborrar '{name}': {ex.Message}");
                    }
                }

                return report;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Import CAx fallit: {ex.Message}");
                report.ImportOk = false;
                return report;
            }
        }
    }
}