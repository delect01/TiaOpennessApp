using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaOpennessApp.Models;
using TiaOpennessApp.Models.Generation;
using TiaOpennessApp.Services;

namespace TiaOpennessApp
{
    class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static readonly JsonSerializerOptions ConfigJsonOptions = new()
        {
            WriteIndented = true,
        };

        private static IConfigurationRoot? _appConfig;

        private static IConfigurationRoot AppConfig => _appConfig ??= BuildConfiguration();

        private static IConfigurationRoot BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                RunCliMode(args);
                return;
            }

            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║      EQUIPCERAMIC S.A. - Engineering Tool      ║");
            Console.WriteLine("║       TIA Portal Openness V21 / .NET 4.8       ║");
            Console.WriteLine("║                                                ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            using var connection = new TiaConnection();

            try
            {
                string mode = (AppConfig["TiaPortal:Mode"] ?? "Auto").ToLowerInvariant();
                bool forceHeadless = mode.Contains("without") || mode.Contains("headless") || mode.Contains("no");
                bool forceWithUi = mode.Contains("with") || mode.Contains("ui");

                if (forceHeadless)
                {
                    Console.WriteLine("Connecting to TIA Portal headless (no GUI)...");
                    connection.ConnectHeadless();
                }
                else if (forceWithUi)
                {
                    Console.WriteLine("Connecting to TIA Portal with user interface...");
                    connection.ConnectWithUi();
                }
                else
                {
                    Console.WriteLine("Connecting to TIA Portal (auto)...");
                    connection.ConnectAuto();
                }

                if (connection.AttachedToExisting)
                    Console.WriteLine("TIA Portal was already open -> attached. It will stay open after the tasks.");
                else
                    Console.WriteLine("TIA Portal was not open -> started headless (no GUI).");

                Console.WriteLine("Connected.\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nERROR connecting to TIA Portal: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                Console.ResetColor();
                Console.WriteLine("\nDone. Press any key to exit.");
                Console.ReadKey(true);
                return;
            }

            bool exitRequested = false;
            while (!exitRequested)
            {
                try
                {
                    Console.WriteLine("┌──────────────────────────────────────┐");
                    Console.WriteLine("│               MAIN MENU              │");
                    Console.WriteLine("├──────────────────────────────────────┤");
                    Console.WriteLine("│  1. Open an existing project         │");
                    Console.WriteLine("│  2. Create a new project             │");
                    Console.WriteLine("│  3. Generate project from documents  │");
                    Console.WriteLine("│  0. Exit                             │");
                    Console.WriteLine("└──────────────────────────────────────┘");
                    Console.Write("Select: ");
                    string? choice = Console.ReadLine()?.Trim();
                    switch (choice)
                    {
                        case "1":
                            exitRequested = OpenExistingProjectFlow(connection);
                            break;
                        case "2":
                            exitRequested = CreateNewProjectFlow(connection);
                            break;
                        case "3":
                            GenerateProjectFromDocumentsFlow(connection);
                            break;
                        case "0":
                            Console.WriteLine("Exiting...");
                            exitRequested = true;
                            break;
                        default:
                            Console.WriteLine("Invalid option. Select 1, 2, 3 or 0: ");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nERROR: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                    Console.ResetColor();
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey(true);
                    Console.Clear();
                }
            }

            if (connection.AttachedToExisting)
                Console.WriteLine("\nAttached TIA Portal left open (same state as found).");
            else
                Console.WriteLine("\nClosing headless TIA Portal instance...");

            Console.WriteLine("Done. Press any key to exit.");
            Console.ReadKey(true);
        }

        private static void RunCliMode(string[] args)
        {
            string command = args[0].ToLowerInvariant();

            if (command == "--check-allowlist")
            {
                CheckAllowlist();
                return;
            }

            if (command == "--gen-catalog-from-export")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --gen-catalog-from-export <exportJson> [catalogCsv]");
                    return;
                }

                string jsonPath = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();

                List<NetworkDeviceInfo>? devices =
                    JsonSerializer.Deserialize<List<NetworkDeviceInfo>>(File.ReadAllText(jsonPath), JsonOptions);

                List<CatalogEntry> candidates = CatalogService.GenerateCandidates(devices ?? new List<NetworkDeviceInfo>());
                List<CatalogEntry> existing = CatalogService.Load(catalogPath);
                List<CatalogEntry> merged = CatalogService.Merge(existing, candidates);
                CatalogService.Save(catalogPath, merged);

                Console.WriteLine($"Catalog: {existing.Count} existing, {candidates.Count} candidates, " +
                                  $"{merged.Count - existing.Count} added.");
                Console.WriteLine($"Saved: {catalogPath}");
                return;
            }

            if (command == "--generate-template")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --generate-template <outXlsx> [catalogCsv]");
                    return;
                }

                string outPath = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();

                new ExcelTemplateGenerator(catalogPath).SaveTemplate(outPath);
                Console.WriteLine($"Template saved: {outPath}");
                return;
            }

            if (command == "--csv-to-xlsx")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --csv-to-xlsx <docsDir> <outXlsx> [catalogCsv]");
                    return;
                }

                string docsDir = args[1];
                string outPath = args[2];
                string catalogPath = args.Length >= 4
                    ? args[3]
                    : ResolveCatalogPath();

                GenerationParseResult csvResult = ReadCsvDocs(docsDir);
                PrintIssues(csvResult.Issues);

                if (csvResult.HasErrors)
                {
                    Console.WriteLine("Cannot convert: CSV has errors.");
                    return;
                }

                new ExcelTemplateGenerator(catalogPath).SaveDocument(outPath, csvResult.Document);
                Console.WriteLine($"XLSX saved: {outPath} " +
                                  $"(devices={csvResult.Document.Devices.Count}, modules={csvResult.Document.Modules.Count}, " +
                                  $"subnets={csvResult.Document.Subnets.Count}, interfaces={csvResult.Document.Interfaces.Count})");
                return;
            }

            if (command == "--xlsx-to-csv")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --xlsx-to-csv <inXlsx> <outDocsDir>");
                    return;
                }

                string xlsxPath = args[1];
                string docsDir = args[2];

                GenerationParseResult xlsxResult = new ExcelGenerationReader(xlsxPath).Read();
                PrintIssues(xlsxResult.Issues);

                if (xlsxResult.HasErrors)
                {
                    Console.WriteLine("Cannot convert: XLSX has errors.");
                    return;
                }

                new CsvGenerationWriter(docsDir).Write(xlsxResult.Document);
                Console.WriteLine($"CSVs saved to: {docsDir} " +
                                  $"(devices={xlsxResult.Document.Devices.Count}, modules={xlsxResult.Document.Modules.Count}, " +
                                  $"subnets={xlsxResult.Document.Subnets.Count}, interfaces={xlsxResult.Document.Interfaces.Count})");
                return;
            }

            if (command == "--verify-readers")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --verify-readers <docsDir> <xlsx>");
                    return;
                }

                string docsDir = args[1];
                string xlsxPath = args[2];

                GenerationParseResult csvResult = ReadCsvDocs(docsDir);
                GenerationParseResult xlsxResult = new ExcelGenerationReader(xlsxPath).Read();

                PrintIssues(csvResult.Issues);
                PrintIssues(xlsxResult.Issues);

                bool csvOk = !csvResult.HasErrors;
                bool xlsxOk = !xlsxResult.HasErrors;

                string csvJson = JsonSerializer.Serialize(csvResult.Document, JsonOptions);
                string xlsxJson = JsonSerializer.Serialize(xlsxResult.Document, JsonOptions);
                bool identical = string.Equals(csvJson, xlsxJson, StringComparison.Ordinal);

                Console.WriteLine($"CSV  parse: {(csvOk ? "OK" : "ERRORS")} " +
                                  $"(devices={csvResult.Document.Devices.Count}, modules={csvResult.Document.Modules.Count}, " +
                                  $"subnets={csvResult.Document.Subnets.Count}, interfaces={csvResult.Document.Interfaces.Count})");
                Console.WriteLine($"XLSX parse: {(xlsxOk ? "OK" : "ERRORS")} " +
                                  $"(devices={xlsxResult.Document.Devices.Count}, modules={xlsxResult.Document.Modules.Count}, " +
                                  $"subnets={xlsxResult.Document.Subnets.Count}, interfaces={xlsxResult.Document.Interfaces.Count})");
                Console.WriteLine($"CSV == XLSX: {(csvOk && xlsxOk && identical ? "identical" : "DIFFERENT")}");
                return;
            }

            if (command == "--validate-docs")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --validate-docs <docsDir> [catalogCsv] [projectFolder]");
                    return;
                }

                string docsDir = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();
                string? projectFolder = args.Length >= 4 ? args[3] : null;

                GenerationParseResult parseResult = ReadCsvDocs(docsDir);
                PrintIssues(parseResult.Issues);

                if (parseResult.HasErrors)
                {
                    Console.WriteLine("Cannot validate: document has parse errors.");
                    return;
                }

                var catalog = CatalogService.Load(catalogPath);
                var validator = new ValidationService(catalog);
                ValidationReport report = validator.Validate(parseResult.Document);
                if (projectFolder != null)
                    validator.ValidateDestinationFolder(projectFolder);

                Console.WriteLine("\n═══ PLANNED OPERATIONS ═══");
                foreach (string op in report.PlannedOperations)
                    Console.WriteLine("  - " + op);

                Console.WriteLine("\n═══ VALIDATION REPORT ═══");
                foreach (var issue in report.Issues)
                {
                    string level = issue.Level == ParseIssueLevel.Error ? "ERROR" : "WARN ";
                    string item = issue.Item.Length > 0 ? issue.Section + " · " + issue.Item : issue.Section;
                    Console.WriteLine($"  {level} {item}: {issue.Message}");
                }

                Console.WriteLine($"\nResult: {report.ErrorCount} error(s), {report.WarningCount} warning(s). " +
                                  $"{(report.HasErrors ? "NOT READY" : "OK")}");
                return;
            }

            if (command == "--gen-devices-sandbox")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --gen-devices-sandbox <docsDir> [catalogCsv] [outJson]");
                    return;
                }

                string docsDir = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();
                string? outJson = args.Length >= 4 ? args[3] : null;

                GenerationParseResult parseResult = ReadCsvDocs(docsDir);
                PrintIssues(parseResult.Issues);
                if (parseResult.HasErrors)
                {
                    Console.WriteLine("Cannot generate: document has parse errors.");
                    return;
                }

                var catalog = CatalogService.Load(catalogPath);
                var validator = new ValidationService(catalog);
                ValidationReport validation = validator.Validate(parseResult.Document);

                foreach (var issue in validation.Issues)
                {
                    string level = issue.Level == ParseIssueLevel.Error ? "ERROR" : "WARN ";
                    string item = issue.Item.Length > 0 ? issue.Section + " · " + issue.Item : issue.Section;
                    Console.WriteLine($"  {level} {item}: {issue.Message}");
                }

                if (validation.HasErrors)
                {
                    Console.WriteLine("Cannot generate: validation errors.");
                    return;
                }

                Console.WriteLine($"Planned operations: {validation.PlannedOperations.Count}");

                string sandboxPath = ResolveSandboxProjectPath();
                Console.WriteLine($"Sandbox project: {sandboxPath}");

                using var connection = new TiaConnection();
                connection.ConnectHeadless();
                Console.WriteLine("Connected to TIA Portal (headless).");

                Project project = EnsureSandboxProject(connection, sandboxPath);
                Console.WriteLine($"Project opened/created: {project.Path.FullName}");

                Dictionary<string, string> rootItemNames = BuildRootItemNames(parseResult.Document);
                var svc = new DevicesGenerationService(project, catalog);
                List<DeviceGenerationItemReport> reports = svc.Generate(parseResult.Document.Devices, rootItemNames);

                Console.WriteLine("\n═══ DEVICE GENERATION REPORT ═══");
                PrintDeviceReports(reports);

                var modulesSvc = new ModulesGenerationService(project, catalog);
                List<ModuleGenerationItemReport> moduleReports =
                    modulesSvc.Generate(parseResult.Document, rootItemNames);

                Console.WriteLine("\n═══ MODULE GENERATION REPORT ═══");
                PrintModuleReports(moduleReports);

                var subnetsSvc = new SubnetsGenerationService(project);
                List<SubnetGenerationItemReport> subnetReports =
                    subnetsSvc.Generate(parseResult.Document.Subnets);

                Console.WriteLine("\n═══ SUBNET GENERATION REPORT ═══");
                PrintSubnetReports(subnetReports);

                var interfacesSvc = new InterfacesGenerationService(project);
                List<InterfaceGenerationItemReport> interfaceReports =
                    interfacesSvc.Generate(parseResult.Document);

                Console.WriteLine("\n═══ INTERFACE GENERATION REPORT ═══");
                PrintInterfaceReports(interfaceReports);

                project.Save();
                Console.WriteLine("\nProject saved.");

                if (outJson != null)
                {
                    var exportSvc = new DevicesAndNetworksService(project);
                    var devices = exportSvc.ExportNetworkDevices();
                    string json = JsonSerializer.Serialize(devices, JsonOptions);
                    WriteFile(outJson, json);
                    Console.WriteLine($"Exported network devices ({devices.Count}) to: {outJson}");
                }
                return;
            }

            if (command == "--generate-project")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --generate-project <docsDir> [catalogCsv] [targetDirectory] [projectName]");
                    return;
                }

                string docsDir = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();
                string? targetDirectory = args.Length >= 4 ? args[3] : null;
                string? projectName = args.Length >= 5 ? args[4] : null;

                using var genConnection = new TiaConnection();
                genConnection.ConnectHeadless();
                Console.WriteLine("Connected to TIA Portal (headless).");

                RunFullGenerationPipeline(genConnection, docsDir, catalogPath, targetDirectory, projectName);
                return;
            }

            if (command == "--roundtrip-test")
            {
                if (args.Length < 5)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --roundtrip-test <srcProject> <workDir> <targetDir> <projectName> [catalogSeed]");
                    return;
                }

                using var rtConnection = new TiaConnection();
                RunRoundTripTest(rtConnection, args);
                return;
            }

            if (command == "--diff-topology")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --diff-topology <expectedJson> <actualJson>");
                    return;
                }

                List<NetworkDeviceInfo> expected =
                    JsonSerializer.Deserialize<List<NetworkDeviceInfo>>(File.ReadAllText(args[1]), JsonOptions)
                    ?? new List<NetworkDeviceInfo>();
                List<NetworkDeviceInfo> actual =
                    JsonSerializer.Deserialize<List<NetworkDeviceInfo>>(File.ReadAllText(args[2]), JsonOptions)
                    ?? new List<NetworkDeviceInfo>();

                IReadOnlyCollection<string>? knownGap = null;
                if (args.Length >= 4 && File.Exists(args[3]))
                {
                    knownGap = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(args[3]), JsonOptions)
                               ?? new List<string>();
                }

                var topologyDiff = new TopologyDiffService().Compare(expected, actual, knownGap);
                PrintTopologyDiff(topologyDiff);
                return;
            }

            if (command == "--dump-type-id")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-type-id <projectPath>");
                    return;
                }

                using var dumpConnection = new TiaConnection();
                dumpConnection.ConnectHeadless();
                dumpConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {dumpConnection.Project.Name}");
                new DevicesAndNetworksService(dumpConnection).DumpTypeIdentifiers();
                return;
            }

            if (command == "--plug-diagnose")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --plug-diagnose <projectPath> [catalogCsv]");
                    return;
                }

                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();

                var catalog = CatalogService.Load(catalogPath);
                var identifiers = catalog
                    .Where(e => e.Identifier.Length > 0)
                    .Select(e => e.Identifier)
                    .ToList();

                using var diagConnection = new TiaConnection();
                diagConnection.ConnectHeadless();
                diagConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {diagConnection.Project.Name}\n");

                var service = new ModulesGenerationService(diagConnection.Project, catalog);
                service.PrintPlugDiagnostics(diagConnection.Project, "ET200SP_01", "PN03", identifiers);
                return;
            }

            if (command == "--export-docs")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --export-docs <projectPath|exportJson> [outDocsDir] [catalogCsv]");
                    return;
                }

                string sourcePath = args[1];
                string docsDir = args.Length >= 3
                    ? args[2]
                    : (AppConfig["Export:DocsPath"] ?? Path.Combine("exports"));
                string catalogPath = args.Length >= 4
                    ? args[3]
                    : ResolveCatalogPath();

                List<NetworkDeviceInfo> exportDevices;
                List<IoTagSpec>? exportTags = null;
                List<ModuleAddressSpec>? exportModuleAddresses = null;
                if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(sourcePath))
                {
                    exportDevices = JsonSerializer.Deserialize<List<NetworkDeviceInfo>>(File.ReadAllText(sourcePath), JsonOptions)
                                    ?? new List<NetworkDeviceInfo>();
                    Console.WriteLine($"Font: exportació JSON ({exportDevices.Count} dispositiu(s))");
                    Console.WriteLine("  (avís) els I/O tags només es poden extreure d'un projecte obert, no d'un JSON.");
                }
                else
                {
                    using var docsConnection = new TiaConnection();
                    docsConnection.ConnectHeadless();
                    docsConnection.OpenProject(sourcePath);
                    Console.WriteLine($"Font: projecte {docsConnection.Project.Name}");
                    exportDevices = new DevicesAndNetworksService(docsConnection.Project).ExportNetworkDevices();
                    exportTags = new TagsExportService(docsConnection.Project).ExportIoTags();
                    exportModuleAddresses = new TagsExportService(docsConnection.Project).ExportModuleAddresses();
                }

                var catalog = CatalogService.Load(catalogPath);
                Console.WriteLine($"Catàleg: {catalog.Count} entrada(s)");

                var docsReport = new DocsExportService(docsDir, catalog).Export(exportDevices, exportTags, exportModuleAddresses);
                Console.WriteLine();
                Console.WriteLine($"Docs generats a: {docsReport.OutputDir}");
                Console.WriteLine($"  Devices:    {docsReport.Devices}");
                Console.WriteLine($"  Modules:    {docsReport.Modules}");
                Console.WriteLine($"  Subnets:    {docsReport.Subnets}");
                Console.WriteLine($"  Interfaces: {docsReport.Interfaces}");
                Console.WriteLine($"  IoTags:     {docsReport.Tags}");
                Console.WriteLine($"  HwAddr:     {docsReport.ModuleAddresses}");
                return;
            }

            if (command == "--dump-subnets")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-subnets <projectPath>");
                    return;
                }

                using var dumpSubnetsConnection = new TiaConnection();
                dumpSubnetsConnection.ConnectHeadless();
                dumpSubnetsConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {dumpSubnetsConnection.Project.Name}");
                new SubnetsGenerationService(dumpSubnetsConnection.Project).DumpSubnets();
                return;
            }

            if (command == "--dump-interfaces")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-interfaces <projectPath>");
                    return;
                }

                using var dumpIfConnection = new TiaConnection();
                dumpIfConnection.ConnectHeadless();
                dumpIfConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {dumpIfConnection.Project.Name}");
                new InterfacesGenerationService(dumpIfConnection.Project).DumpInterfaces();
                return;
            }

            if (command == "--dump-tags")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-tags <projectPath>");
                    return;
                }

                using var dumpTagsConnection = new TiaConnection();
                dumpTagsConnection.ConnectHeadless();
                dumpTagsConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {dumpTagsConnection.Project.Name}");
                Console.WriteLine("═══ I/O TAGS DIAGNOSTICS ═══");
                new TagsExportService(dumpTagsConnection).DumpDiagnostics();
                return;
            }

            if (command == "--dump-hw-items")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-hw-items <projectPath> [deviceName]");
                    return;
                }

                using var dumpHwConnection = new TiaConnection();
                dumpHwConnection.ConnectHeadless();
                dumpHwConnection.OpenProject(args[1]);
                string? filter = args.Length >= 3 ? args[2] : null;
                DumpHwItems(dumpHwConnection.Project, filter);
                return;
            }

            Console.WriteLine($"Unknown command: {args[0]}");
            Console.WriteLine("Supported: --gen-catalog-from-export | --generate-template | --csv-to-xlsx | " +
                              "--xlsx-to-csv | --verify-readers | --validate-docs | --gen-devices-sandbox | " +
                              "--generate-project | --roundtrip-test | --diff-topology | --dump-type-id | " +
                              "--plug-diagnose | --export-docs | --dump-subnets | --dump-interfaces | --dump-tags");
        }

        private static GenerationParseResult ReadCsvDocs(string docsDir)
        {
            string GetPath(string fileName) => Path.Combine(docsDir, fileName);
            var reader = new CsvGenerationReader(
                GetPath("Devices.csv"),
                GetPath("Modules.csv"),
                GetPath("Subnets.csv"),
                GetPath("Interfaces.csv"),
                GetPath("IoTags.csv"),
                GetPath("ModuleAddresses.csv"));
            return reader.Read();
        }

        private static void PrintIssues(List<ParseIssue> issues)
        {
            foreach (var issue in issues)
            {
                string where = issue.Source;
                if (issue.Row > 0)
                    where += ":" + issue.Row;
                if (issue.Column > 0)
                    where += ":" + issue.Column;

                Console.WriteLine($"  {(issue.Level == ParseIssueLevel.Error ? "ERROR" : "WARN ")} {where}: {issue.Message}");
            }
        }

        private static string ResolveSandboxProjectPath()
        {
            string dir = AppConfig["TiaPortal:NewProjectPath"] ?? string.Empty;
            string name = AppConfig["TiaPortal:NewProjectName"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("TiaPortal:NewProjectPath and TiaPortal:NewProjectName must be configured.");
            return Path.Combine(dir, name, name + ".ap21");
        }

        private static Project EnsureSandboxProject(TiaConnection connection, string projectPath, string? createRoot = null, string? createName = null)
        {
            if (File.Exists(projectPath))
            {
                connection.OpenProject(projectPath);
                return connection.Project;
            }

            string root = createRoot ?? AppConfig["TiaPortal:NewProjectPath"] ?? string.Empty;
            string name = createName ?? AppConfig["TiaPortal:NewProjectName"] ?? string.Empty;
            var svc = new ProjectCreationService(connection);
            return svc.CreateNewProject(root, name);
        }

        private static Dictionary<string, string> BuildRootItemNames(GenerationDocument doc)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in doc.Modules)
            {
                if (module.TypeKey.Length == 0 || module.Device.Length == 0)
                    continue;
                DeviceSpec? device = doc.Devices.FirstOrDefault(d =>
                    d.DeviceName.Equals(module.Device, StringComparison.OrdinalIgnoreCase)
                    && d.TypeKey.Equals(module.TypeKey, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                    map[device.DeviceName] = module.ModuleName;
            }
            return map;
        }

        private static void GenerateProjectFromDocumentsFlow(TiaConnection connection)
        {
            Console.WriteLine("\n═══ GENERATE PROJECT FROM DOCUMENTS ═══");

            string docsDir = ResolveDocsDirectory();
            string catalogPath = ResolveCatalogPath();

            string targetDirectory = ReadAndRememberValue("Destination directory", "TiaPortal:NewProjectPath");
            string projectName = ReadAndRememberValue("Project name", "TiaPortal:NewProjectName");

            RunFullGenerationPipeline(connection, docsDir, catalogPath, targetDirectory, projectName);
        }

        private sealed class GenerationRunResult
        {
            public Project? Project { get; set; }
            public List<DeviceGenerationItemReport> Devices { get; set; } = new();
            public List<ModuleGenerationItemReport> Modules { get; set; } = new();
            public List<SubnetGenerationItemReport> Subnets { get; set; } = new();
            public List<InterfaceGenerationItemReport> Interfaces { get; set; } = new();
            public List<TagGenerationItemReport> Tags { get; set; } = new();
            public List<ModuleAddressGenerationItemReport> HwAddresses { get; set; } = new();
        }

        private static GenerationRunResult? RunFullGenerationPipeline(
            TiaConnection connection,
            string docsDir,
            string catalogPath,
            string? targetDirectory = null,
            string? projectName = null)
        {
            var result = new GenerationRunResult();

            GenerationParseResult parseResult = ReadCsvDocs(docsDir);
            PrintIssues(parseResult.Issues);
            if (parseResult.HasErrors)
            {
                Console.WriteLine("\nAbortat: el document té errors de parseig.");
                return null;
            }

            var catalog = CatalogService.Load(catalogPath);
            var validator = new ValidationService(catalog);
            ValidationReport validation = validator.Validate(parseResult.Document);

            Console.WriteLine("\n═══ VALIDATION ═══");
            foreach (var issue in validation.Issues)
            {
                string level = issue.Level == ParseIssueLevel.Error ? "ERROR" : "WARN ";
                string item = issue.Item.Length > 0 ? issue.Section + " · " + issue.Item : issue.Section;
                Console.WriteLine($"  {level} {item}: {issue.Message}");
            }
            Console.WriteLine($"Result: {validation.ErrorCount} error(s), {validation.WarningCount} warning(s).");

            if (validation.HasErrors)
            {
                Console.WriteLine("Abortat: hi ha errors de validació.");
                return null;
            }

            Console.WriteLine($"Planned operations: {validation.PlannedOperations.Count}");

            string sandboxPath;
            Project project;
            if (string.IsNullOrWhiteSpace(targetDirectory) || string.IsNullOrWhiteSpace(projectName))
            {
                sandboxPath = ResolveSandboxProjectPath();
                project = EnsureSandboxProject(connection, sandboxPath);
            }
            else
            {
                sandboxPath = Path.Combine(targetDirectory, projectName, projectName + ".ap21");
                project = EnsureSandboxProject(connection, sandboxPath, targetDirectory, projectName);
            }
            Console.WriteLine($"Projecte sandbox: {project.Path.FullName}");

            Dictionary<string, string> rootItemNames = BuildRootItemNames(parseResult.Document);

            Console.WriteLine("\n═══ DEVICE GENERATION ═══");
            var deviceReports = new DevicesGenerationService(project, catalog)
                .Generate(parseResult.Document.Devices, rootItemNames);
            PrintDeviceReports(deviceReports);

            Console.WriteLine("\n═══ MODULE GENERATION ═══");
            var moduleReports = new ModulesGenerationService(project, catalog)
                .Generate(parseResult.Document, rootItemNames);
            PrintModuleReports(moduleReports);

            Console.WriteLine("\n═══ SUBNET GENERATION ═══");
            var subnetReports = new SubnetsGenerationService(project)
                .Generate(parseResult.Document.Subnets);
            PrintSubnetReports(subnetReports);

            Console.WriteLine("\n═══ INTERFACE GENERATION ═══");
            var interfaceReports = new InterfacesGenerationService(project)
                .Generate(parseResult.Document);
            PrintInterfaceReports(interfaceReports);

            Console.WriteLine("\nPausa 1: desant i reobrint perquè TIA materialitzi les adreces I/Q dels mòduls...");
            project.Save();
            string savedPath = project.Path!.FullName;
            Console.WriteLine($"   (guardat a {savedPath})");
            bool reopened = false;
            for (int attempt = 1; attempt <= 6 && !reopened; attempt++)
            {
                try
                {
                    connection.ForceCloseProject();
                    connection.OpenProject(savedPath);
                    project = connection.Project;
                    reopened = true;
                }
                catch (Exception closeEx)
                {
                    Console.WriteLine($"   (intent {attempt}/6 de reobertura fallit: {closeEx.Message}; es reintenta en 5s)");
                    System.Threading.Thread.Sleep(5000);
                }
            }

            if (reopened)
            {
                Console.WriteLine("   reobert OK: les adreces dels mòduls ja estan materialitzades.");
            }
            else
            {
                Console.WriteLine("   (avís) no s'ha pogut reobrir; es continua amb el projecte en memòria. " +
                                  "Si el pas HW no veu adreces, ho reportarà com abans.");
            }

            Console.WriteLine("\n═══ HW ADDRESS GENERATION ═══");
            var hwAddressReports = new ModuleAddressesGenerationService(project)
                .Generate(parseResult.Document.ModuleAddresses);
            PrintHwAddressReports(hwAddressReports);

            Console.WriteLine("\n═══ I/O TAG GENERATION ═══");
            var tagReports = new TagsGenerationService(project)
                .Generate(parseResult.Document.IoTags);
            PrintTagReports(tagReports);

            project.Save();
            Console.WriteLine("\nProjecte desat.");

            result.Project = project;
            result.Devices = deviceReports;
            result.Modules = moduleReports;
            result.Subnets = subnetReports;
            result.Interfaces = interfaceReports;
            result.Tags = tagReports;
            result.HwAddresses = hwAddressReports;

            Console.WriteLine("\nPipeline acabat.");
            return result;
        }

        private static void RunRoundTripTest(TiaConnection connection, string[] args)
        {
            // --roundtrip-test <srcProject> <workDir> <targetDir> <projectName> [catalogSeed]
            string srcProject = args[1];
            string workDir = args[2];
            string targetDir = args[3];
            string projectName = args[4];
            string seedCatalog = args.Length >= 6 ? args[5] : ResolveCatalogPath();

            Console.WriteLine($"Font:          {srcProject}");
            Console.WriteLine($"WorkDir:       {workDir}");
            Console.WriteLine($"Resultat:      {Path.Combine(targetDir, projectName, projectName + ".ap21")}");
            Console.WriteLine($"Catàleg seed:  {seedCatalog}");
            Console.WriteLine();

            connection.ConnectHeadless();
            Console.WriteLine("Connected to TIA Portal (headless).");

            // PAS 1+2+3: obrir font, generar catàleg i els 4 CSV
            connection.OpenProject(srcProject);
            Console.WriteLine($"Projecte origen: {connection.Project.Name}");

            List<NetworkDeviceInfo> sourceDevices = new DevicesAndNetworksService(connection.Project).ExportNetworkDevices();
            Console.WriteLine($"Topologia font: {sourceDevices.Count} dispositiu(s).");

            Directory.CreateDirectory(workDir);
            string catalogPath = Path.Combine(workDir, "Catalog.csv");
            string sourceJson = Path.Combine(workDir, "source.json");
            File.WriteAllText(sourceJson, JsonSerializer.Serialize(sourceDevices, JsonOptions));
            Console.WriteLine($"Topologia font exportada a: {sourceJson}");
            List<CatalogEntry> seed = CatalogService.Load(seedCatalog);
            List<CatalogEntry> candidates = CatalogService.GenerateCandidates(sourceDevices);
            List<CatalogEntry> catalog = CatalogService.Merge(seed, candidates);
            CatalogService.Save(catalogPath, catalog);
            Console.WriteLine($"Catàleg: {seed.Count} seed + {candidates.Count} candidats -> {catalog.Count} entrades a {catalogPath}");

            List<IoTagSpec> sourceTags = new TagsExportService(connection.Project).ExportIoTags();
            List<ModuleAddressSpec> sourceModuleAddresses =
                new TagsExportService(connection.Project).ExportModuleAddresses();
            var docsReport = new DocsExportService(workDir, catalog).Export(sourceDevices, sourceTags, sourceModuleAddresses);
            Console.WriteLine($"Docs a {workDir}: {docsReport.Devices} devices, {docsReport.Modules} modules, " +
                              $"{docsReport.Subnets} subnets, {docsReport.Interfaces} interfaces, " +
                              $"{docsReport.Tags} I/O tags, {docsReport.ModuleAddresses} hw adreces.");
            Console.WriteLine("  (info) els I/O tags i les adreces de maquinari s'exporten i es regeneraran al pas 4.");
            if (docsReport.TypeKeysMissing.Count > 0)
                Console.WriteLine($"  (avís) TypeKeys no resolts: {string.Join(", ", docsReport.TypeKeysMissing)}");

            connection.CloseProject();

            // PAS 4: neteja del resultat anterior i generació
            string resultFolder = Path.Combine(targetDir, projectName);
            if (Directory.Exists(resultFolder))
            {
                Console.WriteLine($"\nNetejant resultat anterior: {resultFolder}");
                try { Directory.Delete(resultFolder, true); }
                catch (Exception ex) { Console.WriteLine($"  (avís) no s'ha pogut netejar: {ex.Message}"); }
            }

            GenerationRunResult? gen = RunFullGenerationPipeline(connection, workDir, catalogPath, targetDir, projectName);
            if (gen?.Project == null)
            {
                Console.WriteLine("\nROUNDTRIP FAILED: la generació no ha completat.");
                Environment.ExitCode = 1;
                return;
            }

            // PAS 5: re-exportar el generat i comparar amb la font
            Console.WriteLine("\n═══ RE-EXPORT I COMPARACIÓ ═══");
            List<NetworkDeviceInfo> actualDevices = new DevicesAndNetworksService(gen.Project).ExportNetworkDevices();
            File.WriteAllText(Path.Combine(workDir, "actual.json"),
                JsonSerializer.Serialize(actualDevices, JsonOptions));

            var gsdGapNames = gen.Devices
                .Where(r => r.Error != null && IsGsdError(r.Error))
                .Select(r => r.DeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var diff = new TopologyDiffService().Compare(sourceDevices, actualDevices, gsdGapNames);
            PrintTopologyDiff(diff);

            // Classificació: errors durs (Siemens) vs gaps GSD coneguts
            int gsdDeviceErrors = gen.Devices.Count(r => r.Error != null && IsGsdError(r.Error));
            int hardDeviceErrors = gen.Devices.Count(r => r.Error != null && !IsGsdError(r.Error));
            int moduleErrors = gen.Modules.Count(r => r.Error != null);
            int interfaceErrors = gen.Interfaces.Count(r => r.Error != null);
            int subnetErrors = gen.Subnets.Count(r => r.Error != null);

            int tagErrors = gen.Tags.Count(r => r.Error != null);
            int tagCreated = gen.Tags.Count(r => r.Created);
            int tagSkipped = gen.Tags.Count(r => r.Skipped);

            // Re-export dels tags generats per verificar-los amb la font.
            List<IoTagSpec> actualTags = new TagsExportService(gen.Project).ExportIoTags();
            int tagMismatch = 0;
            if (sourceTags.Count > 0 && actualTags.Count != sourceTags.Count)
                tagMismatch = sourceTags.Count - actualTags.Count;

            // Re-export de les adreces de maquinari per verificar-les amb la font.
            List<ModuleAddressSpec> actualModuleAddresses =
                new TagsExportService(gen.Project).ExportModuleAddresses();
            int hwAddressErrors = gen.HwAddresses.Count(r => r.Error != null);
            int hwAddressMismatch = 0;
            if (sourceModuleAddresses.Count > 0)
                hwAddressMismatch = CompareHwAddresses(sourceModuleAddresses, actualModuleAddresses);

            Console.WriteLine("\n═══ ROUNDTRIP SUMMARY ═══");
            Console.WriteLine($"  Devices  : {gen.Devices.Count} total | {hardDeviceErrors} errors durs | {gsdDeviceErrors} GSD coneguts (licència)");
            Console.WriteLine($"  Modules  : {moduleErrors} errors (derivats de dispositius GSD no creats)");
            Console.WriteLine($"  Subnets  : {subnetErrors} errors");
            Console.WriteLine($"  Interf.  : {interfaceErrors} errors");
            Console.WriteLine($"  Tags     : {tagCreated} creats | {tagSkipped} ja existien | {tagErrors} errors | " +
                              $"re-exportats {actualTags.Count} (font: {sourceTags.Count})" +
                              (tagMismatch > 0 ? $" | DIFERÈNCIA +{tagMismatch}" : string.Empty));
            Console.WriteLine($"  HwAddr   : {hwAddressErrors} errors | re-exportades {actualModuleAddresses.Count} " +
                              $"(font: {sourceModuleAddresses.Count})" +
                              (hwAddressMismatch > 0 ? $" | {hwAddressMismatch} discrepàncies" : string.Empty));
            Console.WriteLine($"  Topology : {diff.Differences} diferències | {diff.Explained} explicades " +
                              $"({diff.KnownGaps} GSD esperats) | {diff.DevicesCompared} dispositius comparats");

            bool pass = hardDeviceErrors == 0 && subnetErrors == 0;
            Console.WriteLine($"\n  RESULTAT: {(pass ? "PASS" : "FAIL")}  " +
                              $"({(pass ? "cap error dur al camí Siemens; els GSD coneguts no compten" : "hi ha errors durs a resoldre")})");
            Environment.ExitCode = pass ? 0 : 1;
        }

        private static void CheckAllowlist()
        {
            const string exeName = "TiaOpennessApp.exe";

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
            string currentHash = ComputeSha1(exePath);
            bool isAdmin = false;
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            Console.WriteLine($"Exe        : {exePath}");
            Console.WriteLine($"Existeix   : {File.Exists(exePath)}");
            Console.WriteLine($"SHA1 actual: {currentHash}");
            Console.WriteLine($"Consola    : {(isAdmin ? "PROVAT AMB ADMIN (elevada)" : "NO elevada")}");
            Console.WriteLine();

            const string keyPath = @"SOFTWARE\Siemens\Automation\Openness\AllowList\TiaOpennessApp.exe\Entry";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null)
                {
                    Console.WriteLine("AllowList   : NO EXISTEIX -> TIA rebutjarà l'app.");
                    return;
                }

                string registeredPath = (key.GetValue("Path") as string) ?? string.Empty;
                string registeredDate = (key.GetValue("DateModified") as string) ?? string.Empty;
                string registeredHash = (key.GetValue("FileHash") as string) ?? string.Empty;

                Console.WriteLine($"AllowList OK: {registeredPath}");
                Console.WriteLine($"  DateModified: {registeredDate}");
                Console.WriteLine($"  FileHash    : {registeredHash}");
                Console.WriteLine($"  Coincideix  : {string.Equals(registeredHash, currentHash, StringComparison.OrdinalIgnoreCase)}");
                Console.WriteLine();
                Console.WriteLine("Conclusió: " +
                    (string.Equals(registeredHash, currentHash, StringComparison.OrdinalIgnoreCase)
                        ? "hash OK -> si falla TIA, el motiu és un altre (elevació o diàleg Openness)."
                        : "hash DESACTUALITZAT -> reexecuta run_app.bat / run_roundtrip.bat (consola ADMIN)."));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error llegint el registre (cal ADMIN?): {ex.Message}");
            }
        }

        private static string ComputeSha1(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            return BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static bool IsGsdError(string message)
        {
            string m = message.ToUpperInvariant();
            return m.Contains("LICENSE") || m.Contains("GSDML") || m.Contains("GSD");
        }

        /// <summary>
        /// Compta les files d'adreces de maquinari de l'origen que no tenen
        /// contrapartida exacta (dispositiu/mòdul/submòdul/tipus/inici/llargada)
        /// a la re-exportació del projecte generat. 0 = mapa d'adreces reproduït.
        /// </summary>
        private static int CompareHwAddresses(List<ModuleAddressSpec> expected, List<ModuleAddressSpec> actual)
        {
            static string Key(ModuleAddressSpec a) =>
                $"{a.Device}|{a.ModuleName}|{a.SubModuleName}|{a.IoType}|{a.StartAddress}|{a.Length}";

            var actualSet = new HashSet<string>(actual.Select(Key), StringComparer.OrdinalIgnoreCase);
            return expected.Count(e => !actualSet.Contains(Key(e)));
        }

        private static void DumpHwItems(Siemens.Engineering.Project project, string? filter)
        {
            Console.WriteLine("═══ HW ITEMS DIAGNOSTICS ═══");
            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(project))
            {
                if (!string.IsNullOrWhiteSpace(filter) &&
                    !device.Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                Console.WriteLine($"# {device.Name}");
                try
                {
                    foreach (DeviceItem top in device.DeviceItems)
                        DumpItem(top, 1);
                }
                catch (Exception ex) { Console.WriteLine($"  (avís) device items: {ex.Message}"); }
            }
        }

        private static void DumpItem(Siemens.Engineering.HW.DeviceItem item, int depth)
        {
            string indent = new string(' ', depth * 2);
            try
            {
                Console.WriteLine($"{indent}- {item.Name}  [{SafeString(item.TypeIdentifier)}]");
            }
            catch
            {
                Console.WriteLine($"{indent}- {item.Name}  [typeid?]");
            }

            try
            {
                var addresses = item.Addresses;
                if (addresses != null && addresses.Count > 0)
                {
                    foreach (Address address in addresses)
                    {
                        Console.WriteLine($"{indent}    ADDR {IsIq(address.IoType)} start={SafeInt(address.StartAddress)} " +
                                          $"len={SafeInt(address.Length)}");
                    }
                }
                else
                {
                    Console.WriteLine($"{indent}    (sense adreces)");
                }
            }
            catch (Exception ex) { Console.WriteLine($"{indent}    (adreces: {ex.Message})"); }

            DumpInterfaceInfo(item, indent);

            try
            {
                foreach (DeviceItem child in item.DeviceItems)
                    DumpItem(child, depth + 1);
            }
            catch { }
        }

        private static void DumpInterfaceInfo(Siemens.Engineering.HW.DeviceItem item, string indent)
        {
            try
            {
                NetworkInterface? netIf = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
                if (netIf == null)
                    return;

                string ioSys = "?";
                try
                {
                    var connector = netIf.IoConnectors.Cast<IoConnector>().FirstOrDefault();
                    ioSys = connector?.ConnectedToIoSystem?.Name ?? "(no IO system)";
                }
                catch { }

                string subnet = "-";
                try
                {
                    var node = netIf.Nodes.Cast<Node>().FirstOrDefault();
                    subnet = node?.ConnectedSubnet?.Name ?? "-";
                }
                catch { }

                Console.WriteLine($"{indent}    IF mode={netIf.InterfaceOperatingMode} iosystem={ioSys} subnet={subnet}");
            }
            catch { }
        }

        private static string IsIq(Siemens.Engineering.HW.AddressIoType ioType)
        {
            switch (ioType)
            {
                case Siemens.Engineering.HW.AddressIoType.Input: return "I";
                case Siemens.Engineering.HW.AddressIoType.Output: return "Q";
                default: return ioType.ToString();
            }
        }

        private static int SafeInt(object? value)
        {
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static string SafeString(object? value)
        {
            try { return value?.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static void PrintTopologyDiff(TopologyDiffReport diff)
        {

            foreach (var item in diff.Items)
            {
                string level = item.Level == TopologyDiffLevel.Ok ? "OK  " :
                               item.Level == TopologyDiffLevel.Difference ? "DIFF" : "EXPL";
                Console.WriteLine($"  {level} [{item.Category}] {item.Scope}: {item.Message}");
                if (item.Expected.Length > 0 || item.Actual.Length > 0)
                    Console.WriteLine($"          esperat='{item.Expected}'  actual='{item.Actual}'");
            }

            Console.WriteLine($"\n  RESUM: {diff.Differences} diferència(s) dura(s), {diff.Explained} explicada(s). " +
                              (diff.Passed
                                  ? "PASSED." + (diff.Explained > 0 ? " Totes les faltes són explicades (GSD coneguts o noms interns de TIA)." : "")
                                  : "NOT PASSED."));
        }

        private static string ResolveDocsDirectory()
        {
            string? configured = AppConfig["Import:DocsPath"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                return configured!;
            return Path.Combine("imports");
        }

        private static string ResolveCatalogPath()
        {
            string? configured = AppConfig["Import:CatalogPath"];
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured!;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog", "Catalog.csv");
        }

        private static void PrintDeviceReports(List<DeviceGenerationItemReport> reports)
        {
            foreach (var r in reports)
            {
                if (r.Created)
                    Console.WriteLine($"  [CREATE] {r.DeviceName} ({r.TypeKey})");
                else if (r.Skipped)
                    Console.WriteLine($"  [EXISTS] {r.DeviceName} ({r.TypeKey})");
                else
                    Console.WriteLine($"  [ERROR ] {r.DeviceName} ({r.TypeKey}): {r.Error}");
            }

            int created = reports.Count(r => r.Created);
            int skipped = reports.Count(r => r.Skipped);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {created} created, {skipped} already existed, {errors} errors.");
        }

        private static void PrintModuleReports(List<ModuleGenerationItemReport> reports)
        {
            foreach (var r in reports)
            {
                if (r.Created)
                    Console.WriteLine($"  [PLUG ] {r.DeviceName}·{r.ModuleName} → slot {r.Slot} ({r.TypeKey})");
                else if (r.Skipped)
                    Console.WriteLine($"  [SKIP ] {r.DeviceName}·{r.ModuleName} slot {r.Slot}: {r.Message ?? "no acció"}");
                else
                    Console.WriteLine($"  [ERROR] {r.DeviceName}·{r.ModuleName} slot {r.Slot}: {r.Error}");
            }

            int created = reports.Count(r => r.Created);
            int skipped = reports.Count(r => r.Skipped);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {created} plugged, {skipped} skipped, {errors} errors.");
        }

        private static void PrintSubnetReports(List<SubnetGenerationItemReport> reports)
        {
            foreach (var r in reports)
            {
                if (r.Created)
                    Console.WriteLine($"  [CREATE] {r.Name} ({r.SubnetType})");
                else if (r.Skipped)
                    Console.WriteLine($"  [EXISTS] {r.Name} ({r.SubnetType})");
                else
                    Console.WriteLine($"  [ERROR ] {r.Name} ({r.SubnetType}): {r.Error}");
            }

            int created = reports.Count(r => r.Created);
            int skipped = reports.Count(r => r.Skipped);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {created} created, {skipped} already existed, {errors} errors.");
        }

        private static void PrintInterfaceReports(List<InterfaceGenerationItemReport> reports)
        {
            foreach (var r in reports)
            {
                string state = r.Connected ? "[CONN]" : (r.Skipped ? "[SKIP ]" : "[SET  ]");
                string detail = r.Message ?? string.Empty;
                Console.WriteLine($"  {state} {r.DeviceName}·{r.InterfaceName}: {detail}" +
                                  (r.Error != null ? $" ERROR: {r.Error}" : string.Empty));
            }

            int connected = reports.Count(r => r.Connected);
            int changed = reports.Count(r => !r.Skipped && !r.Connected);
            int skipped = reports.Count(r => r.Skipped);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {connected} connected, {changed} configured, {skipped} skipped, {errors} errors.");
        }

        private static void PrintTagReports(List<TagGenerationItemReport> reports)
        {
            if (reports.Count == 0)
            {
                Console.WriteLine("  (cap) no hi ha I/O tags per generar.");
                return;
            }

            foreach (var r in reports)
            {
                if (r.Created)
                    Console.WriteLine($"  [CREATE] {r.Device}·{r.TagTable}·{r.TagName}");
                else if (r.Skipped)
                    Console.WriteLine($"  [EXISTS] {r.Device}·{r.TagTable}·{r.TagName}");
                else
                    Console.WriteLine($"  [ERROR ] {r.Device}·{r.TagTable}·{r.TagName}: {r.Error}");
            }

            int created = reports.Count(r => r.Created);
            int skipped = reports.Count(r => r.Skipped);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {created} tags creats, {skipped} ja existien, {errors} errors.");
        }

        private static void PrintHwAddressReports(List<ModuleAddressGenerationItemReport> reports)
        {
            if (reports.Count == 0)
            {
                Console.WriteLine("  (cap) no hi ha adreces de maquinari per aplicar.");
                return;
            }

            foreach (var r in reports)
            {
                if (r.Applied)
                    Console.WriteLine($"  [SET  ] {r.Device}·{r.ModuleName}·{r.SubModuleName} " +
                                      $"{r.IoType}{r.StartAddress}..+{r.Length} ({r.Message})");
                else
                    Console.WriteLine($"  [ERROR] {r.Device}·{r.ModuleName}·{r.SubModuleName} " +
                                      $"{r.IoType}({r.StartAddress})+: {r.Error}");
            }

            int applied = reports.Count(r => r.Applied);
            int errors = reports.Count(r => r.Error != null);
            Console.WriteLine($"\nSummary: {applied} adreces aplicades, {errors} errors.");
        }

        private static void PrintMenu()
        {
            Console.WriteLine("┌────────────────────────────────────┐");
            Console.WriteLine("│            PROJECT MENU            │");
            Console.WriteLine("├────────────────────────────────────┤");
            Console.WriteLine("│  1. Project Info                   │");
            Console.WriteLine("│  2. Network Devices Tree           │");
            Console.WriteLine("│  3. Export Docs → 5 CSV files       │");
            Console.WriteLine("│  4. Generate Catalog → Catalog.csv │");
            Console.WriteLine("│  0. Exit                           │");
            Console.WriteLine("└────────────────────────────────────┘");
            Console.Write("Select: ");
        }

        private static void ShowProjectInfo(TiaConnection connection)
        {
            Console.WriteLine("\n═══ PROJECT INFO ═══");
            var svc = new ProjectService(connection);
            svc.PrintProjectInfo();
        }

        private static void ShowNetworkTree(TiaConnection connection)
        {
            Console.WriteLine("\n═══ NETWORK DEVICES TREE ═══\n");
            var svc = new DevicesAndNetworksService(connection);
            svc.PrintNetworkTree();
        }

        private static void ExportDocsFromOpenProject(TiaConnection connection)
        {
            string defaultDocsDir = AppConfig["Export:DocsPath"]
                ?? Path.Combine("exports");
            Console.Write($"Docs directory [{defaultDocsDir}]: ");
            string? input = Console.ReadLine()?.Trim();
            string docsDir = string.IsNullOrEmpty(input) ? defaultDocsDir : input!;

            string catalogPath = ResolveCatalogPath();
            var catalog = CatalogService.Load(catalogPath);
            Console.WriteLine($"Catalog: {catalog.Count} entry(s).");

            Console.WriteLine("Extracting network devices from the open project...");
            var devices = new DevicesAndNetworksService(connection).ExportNetworkDevices();
            Console.WriteLine("Extracting I/O tags from the open project...");
            var tags = new TagsExportService(connection).ExportIoTags();
            Console.WriteLine($"  {tags.Count} I/O tag(s) trobats.");

            var report = new DocsExportService(docsDir, catalog).Export(devices, tags);
            Console.WriteLine();
            Console.WriteLine($"Docs generats a: {report.OutputDir}");
            Console.WriteLine($"  Devices:    {report.Devices}");
            Console.WriteLine($"  Modules:    {report.Modules}");
            Console.WriteLine($"  Subnets:    {report.Subnets}");
            Console.WriteLine($"  Interfaces: {report.Interfaces}");
            Console.WriteLine($"  IoTags:     {report.Tags}");

            if (report.TypeKeysMissing.Count > 0)
            {
                Console.WriteLine("\nTypeKeys no resolts al catàleg (afegiu-los a Catalog.csv):");
                foreach (string missing in report.TypeKeysMissing)
                    Console.WriteLine($"  - {missing}");
            }
        }

        private static void GenerateCatalogFromProject(TiaConnection connection)
        {
            string catalogPath = ResolveCatalogPath();

            Console.WriteLine("Scanning devices and modules of the open project...");
            var devices = new DevicesAndNetworksService(connection).ExportNetworkDevices();
            var candidates = CatalogService.GenerateCandidates(devices);
            var existing = CatalogService.Load(catalogPath);
            var merged = CatalogService.Merge(existing, candidates);
            CatalogService.Save(catalogPath, merged);

            Console.WriteLine($"Catalog: {existing.Count} existing, {candidates.Count} candidates, " +
                              $"{merged.Count - existing.Count} added.");
            Console.WriteLine($"Saved: {catalogPath}");
        }

        private static bool OpenExistingProjectFlow(TiaConnection connection)
        {
            Console.WriteLine("\n═══ OPEN EXISTING PROJECT ═══");

            string directory = ReadAndRememberValue("Project path", "TiaPortal:ProjectPath");
            string projectName = ReadAndRememberValue("Project name", "TiaPortal:ProjectName");

            string projectPath = ResolveProjectPath(directory, projectName);

            Console.WriteLine("Opening project (read-only)...");
            connection.OpenProject(projectPath);
            Console.WriteLine("Project opened.\n");

            return RunOpenProjectMenu(connection);
        }

        private static bool CreateNewProjectFlow(TiaConnection connection)
        {
            Console.WriteLine("\n═══ CREATE NEW PROJECT ═══");

            string targetDirectory = ReadAndRememberValue("Destination directory", "TiaPortal:NewProjectPath");
            string projectName = ReadAndRememberValue("Project name", "TiaPortal:NewProjectName");

            Console.WriteLine("Creating new project (ProjectComposition.Create)...");
            var svc = new ProjectCreationService(connection);
            var created = svc.CreateNewProject(targetDirectory, projectName);

            Console.WriteLine($"Created project: {created.Path.FullName}");
            Console.WriteLine($"  Name:       {created.Name}");
            Console.WriteLine($"  Version:    {created.Version}");

            string createdPath = created.Path.FullName;
            created.Close();
            Console.WriteLine("Project created and closed.");

            Console.Write("Open the new project to work with it? (y/N): ");
            string? answer = Console.ReadLine()?.Trim();
            if (answer != null && answer.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                connection.OpenProject(createdPath);
                Console.WriteLine("Project opened.\n");
                return RunOpenProjectMenu(connection);
            }
            return false;
        }

        private static bool RunOpenProjectMenu(TiaConnection connection)
        {
            bool running = true;
            while (running)
            {
                PrintMenu();
                string? choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        ShowProjectInfo(connection);
                        break;
                    case "2":
                        ShowNetworkTree(connection);
                        break;
                    case "3":
                        ExportDocsFromOpenProject(connection);
                        break;
                    case "4":
                        GenerateCatalogFromProject(connection);
                        break;
                    case "0":
                        Console.WriteLine("Back to main menu.\n");
                        return false;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey(true);
                Console.Clear();
            }
            return false;
        }

        private static string ResolveProjectPath(string directory, string projectName)
        {
            string dir = directory?.Trim() ?? string.Empty;
            string name = projectName?.Trim() ?? string.Empty;

            if (name.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(name);
            if (dir.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(dir);

            if (string.IsNullOrWhiteSpace(dir) && string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Project path/name not configured and neither provided.");

            string full = Path.Combine(dir, name, name + ".ap21");
            Console.WriteLine($"Using project: {full}");
            return full;
        }

        private static string ReadAndRememberValue(string label, string settingsKey)
        {
            string configured = AppConfig[settingsKey] ?? string.Empty;
            string value = ReadRequiredValue(label, configured);

            if (!string.Equals(value, configured, StringComparison.Ordinal))
                PersistAppSettings((settingsKey, value));

            return value;
        }

        private static void PersistAppSettings(params (string Key, string Value)[] updates)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
                return;

            JsonNode? root = JsonNode.Parse(File.ReadAllText(configPath));
            if (root is not JsonObject settings)
                return;

            foreach ((string key, string value) in updates)
            {
                string[] segments = key.Split(':');
                JsonObject current = settings;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (current[segments[i]] is not JsonObject nested)
                    {
                        nested = new JsonObject();
                        current[segments[i]] = nested;
                    }
                    current = nested;
                }
                current[segments[segments.Length - 1]] = value;
            }

            WriteFile(configPath, JsonSerializer.Serialize(settings, ConfigJsonOptions));
            _appConfig = null;
        }

        private static string ReadRequiredValue(string label, string configuredDefault)
        {
            string prefix = string.IsNullOrWhiteSpace(configuredDefault)
                ? $"{label}: "
                : $"{label} [{configuredDefault}]: ";

            while (true)
            {
                Console.Write(prefix);
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (input.Length > 0)
                    return input;
                if (!string.IsNullOrWhiteSpace(configuredDefault))
                    return configuredDefault;
                Console.WriteLine("A value is required.");
            }
        }

        private static void WriteFile(string path, string content)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content);
        }
    }
}
