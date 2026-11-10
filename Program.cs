using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaOpennessApp.Models;
using TiaOpennessApp.Models.Database;
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
            // Prioritat (de menys a més): valors per defecte d'AppPaths ->
            // appsettings.json al costat de l'exe -> appsettings.json d'usuari (Documents\TIAO).
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddInMemoryCollection(AppPaths.ConfigDefaults())
                .AddJsonFile("appsettings.json", optional: true);

            string? userSettings = AppPaths.UserSettingsPath;
            if (userSettings != null)
                builder.AddJsonFile(userSettings, optional: true);

            return builder.Build();
        }

        static void Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            AppPaths.EnsureUserData();

            if (args.Length > 0)
            {
                RunCliMode(args);
                return;
            }

            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║      EQUIPCERAMIC S.A. - Engineering Tool      ║");
            Console.WriteLine("║       TIA Portal Openness V21 / .NET 4.8       ║");
            Console.WriteLine("║             VERSION 1.0.0 (delect01)           ║");
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
                Console.WriteLine($"  Type: {ex.GetType().FullName}");
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    Console.WriteLine($"  Inner: {inner.GetType().FullName}: {inner.Message}");
                if (ex.Message.IndexOf("not permitted", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("The executable is not in the TIA Portal Openness V21 AllowList.");
                    Console.WriteLine("Run fix_allowlist.bat as Administrator, then start the app again.");
                    Console.WriteLine("Check the current entry with: TiaOpennessApp.exe --check-allowlist");
                }
                else if (ex.Message.IndexOf("elevation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Starting TIA Portal needs an elevated process on this machine.");
                    Console.WriteLine("Launch the tool from an elevated console (or run run_app.bat elevated).");
                }
                if (Environment.GetEnvironmentVariable("TIA_OPENNESS_DEBUG") == "1")
                    Console.WriteLine(ex.ToString());
                Console.ResetColor();
                Console.WriteLine("\nDone. Press any key to exit.");
                WaitForKey();
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
                    Console.WriteLine("│  4. Import CSVs to SQLite database   │");
                    Console.WriteLine("│  5. Export SQLite database to CSVs   │");
                    Console.WriteLine("│  6. Generate blocks into template    │");
                    Console.WriteLine("│  0. Exit                             │");
                    Console.WriteLine("└──────────────────────────────────────┘");
                    Console.Write("Select: ");
                    string? choice = Console.ReadLine()?.Trim();
                    if (choice == null)
                    {
                        // stdin closed/redirected (debugger, CI): leave the menu instead of spinning.
                        return;
                    }
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
                        case "4":
                            ImportDatabaseFlow();
                            break;
                        case "5":
                            ExportDatabaseFlow();
                            break;
                        case "6":
                            GenerateBlocksFromDocumentsFlow(connection);
                            break;
                        case "0":
                            Console.WriteLine("Exiting...");
                            exitRequested = true;
                            break;
                        default:
                            Console.WriteLine("Invalid option. Select 1, 2, 3, 4, 5, 6 or 0: ");
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
                    WaitForKey();
                    Console.Clear();
                }
            }

            if (connection.AttachedToExisting)
                Console.WriteLine("\nAttached TIA Portal left open (same state as found).");
            else
                Console.WriteLine("\nClosing headless TIA Portal instance...");

            Console.WriteLine("Done. Press any key to exit.");
            WaitForKey();
        }

        /// <summary>
        /// Blocks for a keypress only when a real console is attached. With redirected
        /// stdin (VS Code, CI, pipes) Console.ReadKey throws, so fall back to ReadLine.
        /// </summary>
        private static void WaitForKey()
        {
            if (Console.IsInputRedirected)
                Console.ReadLine();
            else
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

            if (command == "--fix-allowlist")
            {
                FixAllowlist();
                return;
            }

            if (command == "--remove-allowlist")
            {
                RemoveAllowlist();
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
                    Console.WriteLine("Usage: TiaOpennessApp --generate-template <outXlsx> [docsDir] [catalogCsv]");
                    return;
                }

                string outPath = args[1];
                string docsDir = args.Length >= 3
                    ? args[2]
                    : AppConfig["Import:DocsPath"] ?? string.Empty;
                string catalogPath = args.Length >= 4
                    ? args[3]
                    : ResolveCatalogPath();

                if (string.IsNullOrWhiteSpace(docsDir) || !Directory.Exists(docsDir))
                {
                    Console.WriteLine($"Docs directory not found: {docsDir}");
                    return;
                }

                GenerationParseResult csvResult = ReadCsvDocs(docsDir);
                PrintIssues(csvResult.Issues);

                if (csvResult.HasErrors)
                {
                    Console.WriteLine("Cannot generate template: CSV has errors.");
                    return;
                }

                new ExcelTemplateGenerator(catalogPath).SaveDocument(outPath, csvResult.Document);
                Console.WriteLine($"Template saved: {outPath} " +
                                  $"(devices={csvResult.Document.Devices.Count}, modules={csvResult.Document.Modules.Count}, " +
                                  $"subnets={csvResult.Document.Subnets.Count}, interfaces={csvResult.Document.Interfaces.Count}, " +
                                  $"iotags={csvResult.Document.IoTags.Count}, moduleAddresses={csvResult.Document.ModuleAddresses.Count}, " +
                                  $"blocks={csvResult.Document.Blocks.Count})");
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
                                  $"subnets={xlsxResult.Document.Subnets.Count}, interfaces={xlsxResult.Document.Interfaces.Count}, " +
                                  $"iotags={xlsxResult.Document.IoTags.Count}, moduleAddresses={xlsxResult.Document.ModuleAddresses.Count}, " +
                                  $"blocks={xlsxResult.Document.Blocks.Count})");
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
                var validator = new ValidationService(catalog, docsDir);
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
                var validator = new ValidationService(catalog, docsDir);
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
                    Console.WriteLine("Usage: TiaOpennessApp --generate-project <docsDir> [catalogCsv] [targetDirectory] [projectName] [goldenAml]");
                    return;
                }

                string docsDir = args[1];
                string catalogPath = args.Length >= 3
                    ? args[2]
                    : ResolveCatalogPath();
                string? targetDirectory = args.Length >= 4 ? args[3] : null;
                string? projectName = args.Length >= 5 ? args[4] : null;
                string? goldenAml = args.Length >= 6 ? args[5] : null;

                using var genConnection = new TiaConnection();
                genConnection.ConnectHeadless();
                Console.WriteLine("Connected to TIA Portal (headless).");

                RunFullGenerationPipeline(genConnection, docsDir, catalogPath, targetDirectory, projectName, goldenAml);
                return;
            }

            if (command == "--export-cax-project")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --export-cax-project <projectPath> <outAml> [logFile]");
                    return;
                }

                using var caxConnection = new TiaConnection();
                RunExportCaxProject(caxConnection, args[1], args[2], args.Length >= 4 ? args[3] : null);
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
                    : (AppConfig["Export:DocsPath"] ?? AppPaths.ExportsDir);
                string catalogPath = args.Length >= 4
                    ? args[3]
                    : ResolveCatalogPath();

                List<NetworkDeviceInfo> exportDevices;
                List<IoTagSpec>? exportTags = null;
                List<ModuleAddressSpec>? exportModuleAddresses = null;
                List<BlockSpec>? exportBlocks = null;
                BlockDocsExportReport? blockReport = null;
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
                    Console.WriteLine("Extraient blocs de programa...");
                    blockReport = new BlocksExportService(docsConnection.Project).ExportDocs(docsDir);
                    exportBlocks = blockReport.Blocks;
                }

                var catalog = CatalogService.Load(catalogPath);
                Console.WriteLine($"Catàleg: {catalog.Count} entrada(s)");

                var docsReport = new DocsExportService(docsDir, catalog).Export(
                    exportDevices, exportTags, exportModuleAddresses, exportBlocks);
                Console.WriteLine();
                Console.WriteLine($"Docs generats a: {docsReport.OutputDir}");
                Console.WriteLine($"  Devices:    {docsReport.Devices}");
                Console.WriteLine($"  Modules:    {docsReport.Modules}");
                Console.WriteLine($"  Subnets:    {docsReport.Subnets}");
                Console.WriteLine($"  Interfaces: {docsReport.Interfaces}");
                Console.WriteLine($"  IoTags:     {docsReport.Tags}");
                Console.WriteLine($"  HwAddr:     {docsReport.ModuleAddresses}");
                Console.WriteLine($"  Blocks:     {docsReport.Blocks}");

                if (blockReport != null)
                {
                    foreach (string blockError in blockReport.Errors)
                        Console.WriteLine($"  BLOCK WARN: {blockError}");
                }
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

            if (command == "--export-blocks")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --export-blocks <projectPath> <outDocsDir> [deviceFilter]");
                    return;
                }

                string outDocsDir = args[2];
                string? deviceFilter = args.Length >= 4 ? args[3] : null;

                using var exportBlocksConnection = new TiaConnection();
                exportBlocksConnection.ConnectHeadless();
                exportBlocksConnection.OpenProject(args[1]);
                Console.WriteLine($"Project: {exportBlocksConnection.Project.Name}");
                BlockDocsExportReport blockReport =
                    new BlocksExportService(exportBlocksConnection.Project).ExportDocs(outDocsDir, deviceFilter);

                new CsvGenerationWriter(outDocsDir).WriteBlocks(blockReport.Blocks);
                Console.WriteLine($"Blocks exported: {blockReport.Exported}, skipped: {blockReport.Skipped}, " +
                                  $"errors: {blockReport.Errors.Count}");
                foreach (string error in blockReport.Errors)
                    Console.WriteLine($"  BLOCK WARN: {error}");
                Console.WriteLine($"Blocks manifest: {Path.Combine(outDocsDir, "Blocks.csv")}");
                return;
            }

            if (command == "--generate-blocks")
            {
                if (args.Length < 5)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --generate-blocks <templateProject> <docsDir> <targetDir> <projectName> [--override] [--no-compile]");
                    return;
                }

                bool overrideExisting = args.Skip(5)
                    .Any(arg => arg.Equals("--override", StringComparison.OrdinalIgnoreCase));
                bool noCompile = args.Skip(5)
                    .Any(arg => arg.Equals("--no-compile", StringComparison.OrdinalIgnoreCase));

                using var generateBlocksConnection = new TiaConnection();
                RunBlocksGeneration(
                    generateBlocksConnection,
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    overrideExisting,
                    !noCompile);
                return;
            }

            if (command == "--dump-blocks")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --dump-blocks <projectPath> [deviceFilter]");
                    return;
                }

                using var dumpBlocksConnection = new TiaConnection();
                dumpBlocksConnection.ConnectHeadless();
                dumpBlocksConnection.OpenProject(args[1]);
                string? blockDeviceFilter = args.Length >= 3 ? args[2] : null;
                Console.WriteLine($"Project: {dumpBlocksConnection.Project.Name}");
                Console.WriteLine("=== PROGRAM BLOCKS DIAGNOSTICS ===");
                new BlocksExportService(dumpBlocksConnection.Project).Dump(blockDeviceFilter);
                return;
            }

            if (command == "--blocks-poc")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --blocks-poc <projectPath> <outDir> [blockName] [deviceFilter]");
                    return;
                }

                using var blocksPocConnection = new TiaConnection();
                RunBlocksPoc(blocksPocConnection, args);
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

            if (command == "--gsd-cax-poc")
            {
                if (args.Length < 4)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --gsd-cax-poc <srcProject> <targetDir> <projectName> [deviceName]");
                    return;
                }

                using var pocConnection = new TiaConnection();
                RunGsdCaxPoc(pocConnection, args);
                return;
            }

            if (command == "--import-db")
            {
                var databaseService = new DatabaseImportService();
                string dbPath = args.Length >= 2 ? args[1] : databaseService.DefaultDbPath;
                string importsDir = args.Length >= 3 ? args[2] : databaseService.DefaultImportsDir;

                DbImportResult dbResult = databaseService.ImportFromImports(dbPath, importsDir);

                Console.WriteLine($"Imports directory: {dbResult.ImportsDir}");
                Console.WriteLine($"Database file:     {dbResult.DbPath}");
                foreach (DbTableCount count in dbResult.TableCounts)
                    Console.WriteLine($"  {count.TableName,-20} {count.Count,8}");
                foreach (string warning in dbResult.Warnings)
                    Console.WriteLine($"  WARN: {warning}");
                foreach (string error in dbResult.Errors)
                    Console.WriteLine($"  ERROR: {error}");
                Console.WriteLine(dbResult.Success
                    ? $"Import completed in {dbResult.Elapsed.TotalSeconds:0.00}s." +
                      (dbResult.Warnings.Count > 0 ? $" {dbResult.Warnings.Count} row(s) skipped." : string.Empty)
                    : $"Import finished with {dbResult.Errors.Count} error(s) in {dbResult.Elapsed.TotalSeconds:0.00}s.");
                return;
            }

            if (command == "--db-to-csv")
            {
                var dbExportService = new DatabaseExportService();
                string dbPath = args.Length >= 2 ? args[1] : dbExportService.DefaultDbPath;
                string outputDir = args.Length >= 3
                    ? args[2]
                    : AppConfig["Export:DocsPath"] ?? dbExportService.DefaultOutputDir;
                string catalogPath = args.Length >= 4
                    ? args[3]
                    : AppConfig["Export:CatalogPath"] ?? dbExportService.DefaultCatalogPath;

                DbExportResult dbExportResult = dbExportService.ExportToCsv(dbPath, outputDir, catalogPath);

                Console.WriteLine($"Database file:  {dbExportResult.DbPath}");
                Console.WriteLine($"CSV output dir: {dbExportResult.OutputDir}");
                Console.WriteLine($"Catalog file:   {dbExportResult.CatalogPath}");
                foreach (DbFileCount count in dbExportResult.FileCounts)
                    Console.WriteLine($"  {Path.GetFileName(count.FileName),-22} {count.Count,8}");
                foreach (string error in dbExportResult.Errors)
                    Console.WriteLine($"  ERROR: {error}");
                Console.WriteLine(dbExportResult.Success
                    ? $"Export completed in {dbExportResult.Elapsed.TotalSeconds:0.00}s."
                    : $"Export finished with {dbExportResult.Errors.Count} error(s) in {dbExportResult.Elapsed.TotalSeconds:0.00}s.");
                return;
            }

            Console.WriteLine($"Unknown command: {args[0]}");
            Console.WriteLine("Supported: --gen-catalog-from-export | --generate-template | --csv-to-xlsx | " +
                              "--xlsx-to-csv | --verify-readers | --validate-docs | --gen-devices-sandbox | " +
                              "--generate-project | --roundtrip-test | --diff-topology | --dump-type-id | " +
                              "--plug-diagnose | --export-docs | --dump-subnets | --dump-interfaces | --dump-tags | " +
                              "--export-blocks | --generate-blocks | --dump-blocks | --blocks-poc | --dump-hw-items | --gsd-cax-poc | " +
                              "--export-cax-project | --import-db | --db-to-csv");
        }

        private static GenerationParseResult ReadCsvDocs(string docsDir, bool hardwareOptional = false)
        {
            string GetPath(string fileName) => Path.Combine(docsDir, fileName);
            var reader = new CsvGenerationReader(
                GetPath("Devices.csv"),
                GetPath("Modules.csv"),
                GetPath("Subnets.csv"),
                GetPath("Interfaces.csv"),
                GetPath("IoTags.csv"),
                GetPath("ModuleAddresses.csv"),
                GetPath("Blocks.csv"))
            {
                HardwareOptional = hardwareOptional,
            };
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

        private static void ImportDatabaseFlow()
        {
            Console.WriteLine("\n═══ IMPORT CSVS TO SQLITE DATABASE ═══");

            var service = new DatabaseImportService();
            string dbPath = service.DefaultDbPath;
            string importsDir = service.DefaultImportsDir;

            Console.WriteLine($"Imports directory: {importsDir}");
            Console.WriteLine($"Database file:     {dbPath}");

            if (File.Exists(dbPath))
            {
                Console.Write("\nThe database already exists and will be overwritten. Continue? [y/N]: ");
                string? answer = Console.ReadLine()?.Trim();
                if (answer == null || !answer.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Cancelled.");
                    return;
                }
            }

            DbImportResult result = service.ImportFromImports(dbPath, importsDir);

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nImport completed in {result.Elapsed.TotalSeconds:0.00}s.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nImport finished with {result.Errors.Count} error(s) in {result.Elapsed.TotalSeconds:0.00}s.");
                Console.ResetColor();
            }

            if (result.TableCounts.Count > 0)
            {
                Console.WriteLine("\nRows per table:");
                foreach (DbTableCount count in result.TableCounts)
                    Console.WriteLine($"  {count.TableName,-20} {count.Count,8}");
            }

            if (result.Warnings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nSkipped rows ({result.Warnings.Count}):");
                Console.ResetColor();
                foreach (string warning in result.Warnings)
                    Console.WriteLine($"  WARN: {warning}");
            }

            foreach (string error in result.Errors)
                Console.WriteLine($"  ERROR: {error}");
        }

        private static void ExportDatabaseFlow()
        {
            Console.WriteLine("\n═══ EXPORT SQLITE DATABASE TO CSVS ═══");

            var service = new DatabaseExportService();
            string dbPath = service.DefaultDbPath;
            string? outputDir = AppConfig["Export:DocsPath"];
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = service.DefaultOutputDir;
            string? catalogPath = AppConfig["Export:CatalogPath"];
            if (string.IsNullOrWhiteSpace(catalogPath))
                catalogPath = service.DefaultCatalogPath;

            Console.WriteLine($"Database file:  {dbPath}");
            Console.WriteLine($"CSV output dir: {outputDir}");
            Console.WriteLine($"Catalog file:   {catalogPath}");

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"\nDatabase not found: {dbPath}");
                Console.WriteLine("Run option 4 (or --import-db) first.");
                return;
            }

            DbExportResult result = service.ExportToCsv(dbPath, outputDir, catalogPath);

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nExport completed in {result.Elapsed.TotalSeconds:0.00}s.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nExport finished with {result.Errors.Count} error(s) in {result.Elapsed.TotalSeconds:0.00}s.");
                Console.ResetColor();
            }

            if (result.FileCounts.Count > 0)
            {
                Console.WriteLine("\nRows per file:");
                foreach (DbFileCount count in result.FileCounts)
                    Console.WriteLine($"  {Path.GetFileName(count.FileName),-22} {count.Count,8}");
            }

            foreach (string error in result.Errors)
                Console.WriteLine($"  ERROR: {error}");
        }

        private static void GenerateBlocksFromDocumentsFlow(TiaConnection connection)
        {
            Console.WriteLine("\n═══ GENERATE BLOCKS INTO TEMPLATE CLONE ═══");

            // Per defecte, el directori d'import si existeix; si no, el d'export
            // (on deixa els docs l'opció "Export Project to CSV files").
            string defaultDocs = ResolveDocsDirectory();
            string? exportDocs = AppConfig["Export:DocsPath"];
            if (!Directory.Exists(defaultDocs) && !string.IsNullOrWhiteSpace(exportDocs) && Directory.Exists(exportDocs))
                defaultDocs = exportDocs!;

            string docsDir = ReadRequiredValue("Docs directory (Blocks.csv + blocks/types/tags)", Path.GetFullPath(defaultDocs));
            if (!File.Exists(Path.Combine(docsDir, "Blocks.csv")))
            {
                Console.WriteLine($"Abortat: no hi ha Blocks.csv a: {docsDir}");
                return;
            }

            string configuredTemplate = AppConfig["Import:TemplateProjectPath"] ?? string.Empty;
            string templateInput = ReadRequiredValue(
                "Template project (.ap21 path, project folder or project name)",
                configuredTemplate);
            string? templatePath = ResolveProjectFile(templateInput);
            if (templatePath == null)
            {
                Console.WriteLine($"Abortat: no s'ha trobat cap projecte plantilla per a '{templateInput}'.");
                Console.WriteLine("  Indica el camí complet del .ap21, la carpeta del projecte o el nom d'un " +
                                  $"projecte dins de '{AppConfig["TiaPortal:ProjectPath"]}'.");
                return;
            }

            Console.WriteLine($"Plantilla: {templatePath}");
            if (!string.Equals(templatePath, configuredTemplate, StringComparison.OrdinalIgnoreCase))
                PersistAppSettings(("Import:TemplateProjectPath", templatePath));

            string targetDirectory = ReadAndRememberValue(
                "Destination directory",
                "TiaPortal:NewProjectPath");
            string projectName = ReadAndRememberValue(
                "Project name",
                "TiaPortal:NewProjectName");

            Console.Write("Override existing blocks in the same group? (y/s = yes, N): ");
            string? answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            bool overrideExisting = answer is "y" or "yes" or "s" or "si" or "sí";

            RunBlocksGeneration(
                connection,
                templatePath,
                docsDir,
                targetDirectory,
                projectName,
                overrideExisting,
                compile: true);
        }

        private static void GenerateProjectFromDocumentsFlow(TiaConnection connection)
        {
            Console.WriteLine("\n═══ GENERATE PROJECT FROM DOCUMENTS ═══");

            string docsDir = ResolveDocsDirectory();
            string catalogPath = ResolveCatalogPath();

            string targetDirectory = ReadAndRememberValue("Destination directory", "TiaPortal:NewProjectPath");
            string projectName = ReadAndRememberValue("Project name", "TiaPortal:NewProjectName");

            string? goldenAml = ResolveGoldenAmlForMenu(connection, projectName);

            RunFullGenerationPipeline(connection, docsDir, catalogPath, targetDirectory, projectName, goldenAml);
        }

        /// <summary>
        /// Resolves the golden AML used to import GSD devices in the interactive menu:
        /// exports it automatically from the currently open project when possible, otherwise
        /// prompts the user for an existing AML file (optional, remembered in appsettings).
        /// </summary>
        private static string? ResolveGoldenAmlForMenu(TiaConnection connection, string projectName)
        {
            if (connection.HasOpenProject)
            {
                // Never export the golden from the destination sandbox: it only contains the
                // devices created so far, so the GSD devices would be missing.
                if (string.Equals(connection.Project.Name, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    string? configuredGolden = AppConfig["Import:GoldenAmlPath"];
                    if (!string.IsNullOrWhiteSpace(configuredGolden) && File.Exists(configuredGolden))
                    {
                        Console.WriteLine($"\nProjecte obert = destí '{projectName}'; s'usarà el golden AML configurat: {configuredGolden}");
                        connection.CloseProject();
                        return Path.GetFullPath(configuredGolden!);
                    }

                    Console.WriteLine($"\n(avís) el projecte obert és el destí '{projectName}' i no pot generar el golden dels GSD.");
                    connection.CloseProject();
                    return ResolveConfiguredGoldenAml();
                }

                string goldenAml = Path.Combine(Path.GetTempPath(), $"golden_menu_{projectName}_{DateTime.Now:HHmmss}.aml");
                string goldenLog = goldenAml + ".log";
                string? result = null;
                try
                {
                    Console.WriteLine("\nProjecte obert detectat: exportant golden AML per importar els dispositius GSD...");
                    bool goldenExportOk = CaxTransferService.ExportProject(connection.Project, new FileInfo(goldenAml), new FileInfo(goldenLog));
                    FileInfo goldenFile = new FileInfo(goldenAml);
                    if (goldenFile.Exists && goldenFile.Length > 0)
                    {
                        Console.WriteLine($"Golden AML: generat ({goldenFile.Length} bytes, success={goldenExportOk}) -> {goldenAml}");
                        PersistAppSettings(("Import:GoldenAmlPath", goldenAml));
                        result = goldenAml;
                    }
                    else
                    {
                        Console.WriteLine($"(avís) golden AML no generat (success={goldenExportOk}); sense golden no s'importaran els dispositius GSD.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"(avís) no s'ha pogut exportar el golden AML: {ex.Message}");
                }

                // TIA només permet un projecte obert: es tanca abans que el pipeline n'obri el sandbox.
                connection.CloseProject();
                return result;
            }

            return ResolveConfiguredGoldenAml();
        }

        private static string? ResolveConfiguredGoldenAml()
        {
            string configuredGolden = AppConfig["Import:GoldenAmlPath"] ?? string.Empty;
            string goldenInput = ReadOptionalValue("Golden AML path (buit = sense importar GSD)", configuredGolden);
            if (!string.Equals(goldenInput, configuredGolden, StringComparison.Ordinal))
                PersistAppSettings(("Import:GoldenAmlPath", goldenInput));

            if (string.IsNullOrWhiteSpace(goldenInput))
                return null;
            if (!File.Exists(goldenInput))
            {
                Console.WriteLine($"(avís) el golden AML no existeix: {goldenInput}");
                return null;
            }
            return Path.GetFullPath(goldenInput);
        }

        private static void RunExportCaxProject(TiaConnection connection, string projectPath, string outAml, string? logFile)
        {
            Console.WriteLine("═══ EXPORT CAx PROJECT ═══");
            try { connection.ConnectHeadless(); }
            catch (Exception ex) { Console.WriteLine($"FAIL: no s'ha pogut connectar a TIA Portal: {ex.Message}"); return; }

            try { connection.OpenProject(projectPath); }
            catch (Exception ex) { Console.WriteLine($"FAIL: no s'ha pogut obrir el projecte: {ex.Message}"); return; }

            string? outDir = Path.GetDirectoryName(Path.GetFullPath(outAml));
            if (!string.IsNullOrWhiteSpace(outDir))
                Directory.CreateDirectory(outDir);

            string logPath = string.IsNullOrWhiteSpace(logFile) ? outAml + ".log" : logFile!;
            bool exportOk = CaxTransferService.ExportProject(connection.Project, new FileInfo(outAml), new FileInfo(logPath));
            Console.WriteLine($"Export {connection.Project.Name}: success={exportOk}");
            if (File.Exists(logPath))
            {
                foreach (string line in File.ReadAllLines(logPath).Take(50))
                    Console.WriteLine("  " + line);
            }

            FileInfo outFile = new FileInfo(outAml);
            Console.WriteLine(outFile.Exists && outFile.Length > 0
                ? $"AML generat: {outFile.FullName} ({outFile.Length} bytes)"
                : "FAIL: el fitxer AML no s'ha generat o és buit.");
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
            string? projectName = null,
            string? goldenAmlPath = null)
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
            var validator = new ValidationService(catalog, docsDir);
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

            List<CatalogEntry> gsdCatalogEntries = catalog
                .Where(e => e.Identifier?.StartsWith("GSD:", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (gsdCatalogEntries.Count > 0)
            {
                var gsdTypeKeys = new HashSet<string>(gsdCatalogEntries.Select(e => e.TypeKey),
                    StringComparer.OrdinalIgnoreCase);
                var keepNames = parseResult.Document.Devices
                    .Where(d => gsdTypeKeys.Contains(d.TypeKey))
                    .Select(d => d.DeviceName)
                    .ToList();

                if (keepNames.Count == 0)
                {
                    Console.WriteLine("(avís) el catàleg té entrades GSD però cap DeviceSpec les utilitza.");
                }
                else if (string.IsNullOrWhiteSpace(goldenAmlPath) || !File.Exists(goldenAmlPath))
                {
                    Console.WriteLine($"(avís) sense golden AML no s'importaran els {keepNames.Count} dispositiu(s) GSD; " +
                                      "el pas de device generation els crearà amb CreateWithItem (error de llicència gsdml probable).");
                }
                else
                {
                    Console.WriteLine("\n═══ CAX GOLDEN IMPORT ═══");
                    var gsdImport = new GsdCaxImportService(project);
                    string resolvedGoldenAml = Path.GetFullPath(goldenAmlPath!);
                    GsdCaxImportReport goldenReport = gsdImport.ImportGoldenAndKeep(resolvedGoldenAml, keepNames);
                    string status = goldenReport.ImportOk ? "OK" : "AMB ERRORS";
                    Console.WriteLine($"  Import {status}: {goldenReport.ImportedDeviceCount} dispositiu(s) al projecte, " +
                                      $"{goldenReport.ErrorCount} error(s), {goldenReport.WarningCount} warning(s).");
                    if (goldenReport.KeptDevices.Count > 0)
                        Console.WriteLine($"  Conservats ({goldenReport.KeptDevices.Count}): {string.Join(", ", goldenReport.KeptDevices)}");
                    if (goldenReport.RemovedDevices.Count > 0)
                        Console.WriteLine($"  Esborrats ({goldenReport.RemovedDevices.Count}): {string.Join(", ", goldenReport.RemovedDevices)}");
                    if (goldenReport.MissingExpectedDevices.Count > 0)
                        Console.WriteLine($"  (avís) GSD esperats no trobats al golden: {string.Join(", ", goldenReport.MissingExpectedDevices)}");
                    foreach (string warning in goldenReport.Warnings.Take(20))
                        Console.WriteLine($"  warn: {warning}");
                    foreach (string error in goldenReport.Errors)
                        Console.WriteLine($"  ERROR: {error}");
                }
            }

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

            // Blocs de programa: XML SimaticML a workDir\blocks + files per a Blocks.csv.
            string blocksDir = Path.Combine(workDir, "blocks");
            if (Directory.Exists(blocksDir))
            {
                try { Directory.Delete(blocksDir, true); }
                catch (Exception ex) { Console.WriteLine($"  (avís) no s'ha pogut netejar {blocksDir}: {ex.Message}"); }
            }
            BlockDocsExportReport sourceBlocksReport = new BlocksExportService(connection.Project).ExportDocs(workDir);
            List<BlockSpec> sourceBlocks = sourceBlocksReport.Blocks;

            var docsReport = new DocsExportService(workDir, catalog)
                .Export(sourceDevices, sourceTags, sourceModuleAddresses, sourceBlocks);
            Console.WriteLine($"Docs a {workDir}: {docsReport.Devices} devices, {docsReport.Modules} modules, " +
                              $"{docsReport.Subnets} subnets, {docsReport.Interfaces} interfaces, " +
                              $"{docsReport.Tags} I/O tags, {docsReport.ModuleAddresses} hw adreces, " +
                              $"{docsReport.Blocks} blocs ({sourceBlocksReport.Skipped} protegits saltats).");
            foreach (string blockError in sourceBlocksReport.Errors)
                Console.WriteLine($"  BLOCK WARN: {blockError}");
            Console.WriteLine("  (info) els I/O tags, les adreces de maquinari i els blocs s'exporten i es regeneraran al pas 4.");
            if (docsReport.TypeKeysMissing.Count > 0)
                Console.WriteLine($"  (avís) TypeKeys no resolts: {string.Join(", ", docsReport.TypeKeysMissing)}");

            // Golden AML: export del projecte sencer; es farà servir per importar els dispositius
            // GSD al pas 4 sense la llicència gsdml.
            string goldenAml = Path.Combine(workDir, "golden.aml");
            string goldenLog = goldenAml + ".log";
            try
            {
                bool goldenExportOk = CaxTransferService.ExportProject(connection.Project, new FileInfo(goldenAml), new FileInfo(goldenLog));
                FileInfo goldenFile = new FileInfo(goldenAml);
                Console.WriteLine(goldenFile.Exists && goldenFile.Length > 0
                    ? $"Golden AML: generat ({goldenFile.Length} bytes, success={goldenExportOk}) -> {goldenAml}"
                    : $"(avís) golden AML no generat (success={goldenExportOk}); els dispositius GSD no es podran importar.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(avís) no s'ha pogut exportar el golden AML: {ex.Message}");
            }

            connection.CloseProject();

            // PAS 4: neteja del resultat anterior i generació
            string resultFolder = Path.Combine(targetDir, projectName);
            if (Directory.Exists(resultFolder))
            {
                Console.WriteLine($"\nNetejant resultat anterior: {resultFolder}");
                try { Directory.Delete(resultFolder, true); }
                catch (Exception ex) { Console.WriteLine($"  (avís) no s'ha pogut netejar: {ex.Message}"); }
            }

            GenerationRunResult? gen = RunFullGenerationPipeline(connection, workDir, catalogPath, targetDir, projectName, goldenAml);
            if (gen?.Project == null)
            {
                Console.WriteLine("\nROUNDTRIP FAILED: la generació no ha completat.");
                Environment.ExitCode = 1;
                return;
            }

            // PAS 4b: importar els blocs de programa al projecte generat (ja té el maquinari).
            BlocksGenerationReport? blocksGen = null;
            if (sourceBlocks.Count > 0)
            {
                Console.WriteLine("\n═══ BLOCK GENERATION ═══");
                try
                {
                    blocksGen = new BlocksGenerationService(gen.Project, workDir).Generate(
                        new GenerationDocument { Blocks = sourceBlocks },
                        new BlocksGenerationOptions { OverrideExisting = false, Compile = true });
                    PrintBlocksGenerationReport(blocksGen);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ERROR inesperat important blocs: {ex.Message}");
                }

                // Es desa sempre (és un projecte de prova) per poder inspeccionar el resultat.
                try
                {
                    gen.Project.Save();
                    Console.WriteLine("  Projecte desat amb els blocs.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) no s'ha pogut desar després dels blocs: {ex.Message}");
                }
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

            // Re-inspecció dels blocs del projecte generat per verificar-los amb la font.
            int blocksFailed = blocksGen?.FailedCount ?? 0;
            if (sourceBlocks.Count > 0 && blocksGen == null)
                blocksFailed = sourceBlocks.Count;
            List<string> blocksMissing = new List<string>();
            int actualBlockCount = 0;
            if (sourceBlocks.Count > 0)
            {
                BlockInspectionResult actualBlocks = new BlocksExportService(gen.Project).Inspect();
                actualBlockCount = actualBlocks.Blocks.Count;
                blocksMissing = CompareBlocks(sourceBlocks, actualBlocks.Blocks);
                foreach (string missing in blocksMissing.Take(20))
                    Console.WriteLine($"  BLOC NO REPRODUÏT: {missing}");
                if (blocksMissing.Count > 20)
                    Console.WriteLine($"  ... i {blocksMissing.Count - 20} més");
            }
            int compileErrors = blocksGen?.CompileResult?.ErrorCount ?? 0;
            int compileWarnings = blocksGen?.CompileResult?.WarningCount ?? 0;

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
            Console.WriteLine($"  Blocks   : {blocksGen?.ImportedCount ?? 0} importats | {blocksFailed} fallits | " +
                              $"{blocksMissing.Count} no reproduïts | re-inspeccionats {actualBlockCount} " +
                              $"(font: {sourceBlocks.Count}, {sourceBlocksReport.Skipped} protegits saltats) | " +
                              $"compilació {compileErrors} errors, {compileWarnings} warnings");

            bool pass = hardDeviceErrors == 0 && subnetErrors == 0
                        && blocksFailed == 0 && blocksMissing.Count == 0;
            Console.WriteLine($"\n  RESULTAT: {(pass ? "PASS" : "FAIL")}  " +
                              $"({(pass ? "cap error dur al camí Siemens; els GSD coneguts no compten" : "hi ha errors durs a resoldre")})");
            Environment.ExitCode = pass ? 0 : 1;
        }

        // TIA Portal Openness V21 firewall entry (version-independent AllowList):
        //   HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\<exe>\Entry
        //     Path         = full path of the executable
        //     DateModified = last write time in UTC, "yyyy/MM/dd HH:mm:ss.fff"
        //     FileHash     = SHA-256 of the executable, base64 encoded
        // V20 and older used SHA1 + local time under ...\<version>\Whitelist, which is
        // why the legacy scripts no longer satisfy the V21 firewall.
        private const string AllowListRoot =
            @"SOFTWARE\Siemens\Automation\Openness\AllowList";

        private static void CheckAllowlist()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TiaOpennessApp.exe");
            bool exists = File.Exists(exePath);
            string name = Path.GetFileName(exePath);

            string expectedPath = exePath;
            string expectedDate = exists
                ? File.GetLastWriteTimeUtc(exePath).ToString("yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : "(exe not found)";
            string expectedHash = exists ? ComputeSha256Base64(exePath) : "(exe not found)";

            Console.WriteLine($"Exe         : {exePath}");
            Console.WriteLine($"Existeix    : {exists}");
            Console.WriteLine($"Path        : {expectedPath}");
            Console.WriteLine($"DateModified: {expectedDate} (UTC)");
            Console.WriteLine($"FileHash    : {expectedHash} (SHA-256 base64)");
            Console.WriteLine($"Consola     : {(IsAdministrator() ? "elevada" : "NO elevada")}");
            Console.WriteLine();

            string keyPath = $@"{AllowListRoot}\{name}";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyPath);
                if (key == null)
                {
                    Console.WriteLine("AllowList   : cap entrada -> TIA rebutjarà l'app.");
                    Console.WriteLine("              Executa fix_allowlist.bat com a administrador.");
                    Environment.ExitCode = 1;
                    return;
                }

                string[] entryNames = key.GetSubKeyNames();
                var matches = new List<string>();
                string? newestPath = null;
                string newestDate = string.Empty;

                foreach (string entryName in entryNames)
                {
                    using var entry = key.OpenSubKey(entryName);
                    if (entry == null) continue;

                    string path = (entry.GetValue("Path") as string) ?? string.Empty;
                    string date = (entry.GetValue("DateModified") as string) ?? string.Empty;
                    string hash = (entry.GetValue("FileHash") as string) ?? string.Empty;

                    bool ok = exists
                        && string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(date, expectedDate, StringComparison.Ordinal)
                        && string.Equals(hash, expectedHash, StringComparison.Ordinal);

                    if (ok) matches.Add(entryName);
                    if (string.CompareOrdinal(date, newestDate) > 0)
                    {
                        newestDate = date;
                        newestPath = path;
                    }
                }

                if (matches.Count > 0)
                {
                    Console.WriteLine($"AllowList OK: {matches.Count} entrada(es) coincideixen " +
                                      $"({string.Join(", ", matches)}).");
                    Environment.ExitCode = 0;
                }
                else
                {
                    Console.WriteLine($"AllowList DESACTUALITZADA: {entryNames.Length} entrada(es), cap coincideix.");
                    if (newestPath != null)
                        Console.WriteLine($"  mes recent: {newestDate} -> {newestPath}");
                    Console.WriteLine("Executa fix_allowlist.bat com a administrador despres de recompilar.");
                    Environment.ExitCode = 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error llegint el registre: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }

        /// <summary>
        /// Registers (or refreshes) the V21 AllowList entry for this executable.
        /// Must run elevated because the AllowList lives under HKLM.
        /// </summary>
        private static void FixAllowlist()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TiaOpennessApp.exe");
            if (!File.Exists(exePath))
            {
                Console.WriteLine($"No existeix l'executable: {exePath}");
                Environment.ExitCode = 1;
                return;
            }

            if (!IsAdministrator())
            {
                Console.WriteLine("Cal executar-ho com a administrador: l'AllowList és a HKLM.");
                Console.WriteLine("Obre fix_allowlist.bat (doble clic) o eleva la consola.");
                Environment.ExitCode = 5;
                return;
            }

            string name = Path.GetFileName(exePath);
            string dateModified = File.GetLastWriteTimeUtc(exePath)
                .ToString("yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string fileHash = ComputeSha256Base64(exePath);
            string keyPath = $@"{AllowListRoot}\{name}\Entry";

            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var entry = baseKey.CreateSubKey(keyPath, writable: true);
            entry.SetValue("Path", exePath, RegistryValueKind.String);
            entry.SetValue("DateModified", dateModified, RegistryValueKind.String);
            entry.SetValue("FileHash", fileHash, RegistryValueKind.String);

            Console.WriteLine("AllowList actualitzada:");
            Console.WriteLine($"  HKLM\\{keyPath}");
            Console.WriteLine($"  Path        = {exePath}");
            Console.WriteLine($"  DateModified= {dateModified}");
            Console.WriteLine($"  FileHash    = {fileHash}");
            Environment.ExitCode = 0;
        }

        /// <summary>
        /// Esborra l'entrada de l'AllowList només si apunta a aquest executable
        /// (la crida l'instal·lador en desinstal·lar). Cal ser administrador.
        /// </summary>
        private static void RemoveAllowlist()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TiaOpennessApp.exe");
            string name = Path.GetFileName(exePath);
            string keyPath = $@"{AllowListRoot}\{name}";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyPath, writable: true);
                if (key == null)
                    return;

                foreach (string entryName in key.GetSubKeyNames())
                {
                    string? path;
                    using (var entry = key.OpenSubKey(entryName))
                        path = entry?.GetValue("Path") as string;

                    if (string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        key.DeleteSubKeyTree(entryName, throwOnMissingSubKey: false);
                        Console.WriteLine($"AllowList: esborrada HKLM\\{keyPath}\\{entryName}");
                    }
                }
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error esborrant l'AllowList: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256Base64(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToBase64String(sha256.ComputeHash(stream));
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
        /// <summary>
        /// Retorna els blocs de l'origen que no tenen contrapartida exacta
        /// (dispositiu/grup/nom/tipus/número/llenguatge) al projecte generat.
        /// Llista buida = blocs reproduïts.
        /// </summary>
        private static List<string> CompareBlocks(List<BlockSpec> expected, List<BlockInventoryItem> actual)
        {
            // Els DB de sistema poden acabar al grup de sistema o a l'arrel: no es
            // compara el grup, només que hi siguin.
            static string Key(string device, string group, string name, string type, int? number, string language) =>
                $"{device}|{(BlocksExportService.IsSystemGroupPath(group) ? string.Empty : group)}|{name}|{type}|" +
                $"{number?.ToString(CultureInfo.InvariantCulture) ?? "?"}|{language}";

            var actualSet = new HashSet<string>(
                actual.Where(a => a.Error == null).Select(a => Key(a.DeviceName, a.GroupPath, a.Name,
                    a.BlockType, a.Number, a.ProgrammingLanguage)),
                StringComparer.OrdinalIgnoreCase);

            return expected
                .Where(e => string.IsNullOrWhiteSpace(e.Kind)
                            || string.Equals(e.Kind, "Block", StringComparison.OrdinalIgnoreCase))
                .Where(e => !BlocksGenerationService.IsSafety(e))
                .Select(e => Key(e.Device, e.GroupPath, e.Name, e.BlockType, e.Number, e.ProgrammingLanguage))
                .Where(k => !actualSet.Contains(k))
                .ToList();
        }

        private static int CompareHwAddresses(List<ModuleAddressSpec> expected, List<ModuleAddressSpec> actual)
        {
            static string Key(ModuleAddressSpec a) =>
                $"{a.Device}|{a.ModuleName}|{a.SubModuleName}|{a.IoType}|{a.StartAddress}|{a.Length}";

            var actualSet = new HashSet<string>(actual.Select(Key), StringComparer.OrdinalIgnoreCase);
            return expected.Count(e => !actualSet.Contains(Key(e)));
        }

        private static void RunGsdCaxPoc(TiaConnection connection, string[] args)
        {
            string srcProject = args[1];
            string targetDir = args[2];
            string projectName = args[3];
            string? deviceFilter = args.Length >= 5 ? args[4] : null;

            Console.WriteLine("═══ GSD CAx PoC ═══");

            try
            {
                connection.ConnectHeadless();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut connectar a TIA Portal: {ex.Message}");
                return;
            }

            try
            {
                connection.OpenProject(srcProject);
                Console.WriteLine($"[1] Font: projecte {connection.Project.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut obrir el projecte font: {ex.Message}");
                return;
            }

            Device? gsdDevice = null;
            if (!string.IsNullOrWhiteSpace(deviceFilter))
            {
                foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(connection.Project))
                {
                    if (device.Name.IndexOf(deviceFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        gsdDevice = device;
                        break;
                    }
                }

                if (gsdDevice == null)
                {
                    Console.WriteLine($"FAIL: no s'ha trobat cap dispositiu que contingui '{deviceFilter}' al projecte font.");
                    return;
                }
            }
            else
            {
                foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(connection.Project))
                {
                    if (device.IsGsd)
                    {
                        gsdDevice = device;
                        break;
                    }
                }

                if (gsdDevice == null)
                {
                    Console.WriteLine("FAIL: el projecte font no conté cap dispositiu GSD.");
                    return;
                }
            }

            Console.WriteLine($"    dispositiu: {gsdDevice.Name}  IsGsd={gsdDevice.IsGsd}  " +
                              $"TypeIdentifier={SafeString(gsdDevice.TypeIdentifier)}");

            string gsdDeviceName = gsdDevice.Name;

            string amlDir = Path.Combine(Path.GetTempPath(), "gsd-cax-poc");
            Directory.CreateDirectory(amlDir);
            string safeName = string.Concat(gsdDeviceName.Split(Path.GetInvalidFileNameChars()));
            string amlPath = Path.Combine(amlDir, safeName + ".aml");
            string logPath = amlPath + ".log";
            foreach (string file in new[] { amlPath, logPath })
                if (File.Exists(file))
                    File.Delete(file);

            bool exportOk;
            string exportOrigin;
            try
            {
                exportOk = CaxTransferService.ExportDevice(gsdDevice, new FileInfo(amlPath), new FileInfo(logPath));
                exportOrigin = "dispositiu (3-arg)";
            }
            catch (Exception ex1)
            {
                Console.WriteLine($"    (avís) export per dispositiu (3-arg) fallit: {ex1.Message}");
                Console.WriteLine("    Provant export del projecte complet...");
                foreach (string file in new[] { amlPath, logPath })
                    if (File.Exists(file))
                        File.Delete(file);

                try
                {
                    exportOk = CaxTransferService.ExportProject(connection.Project, new FileInfo(amlPath), new FileInfo(logPath));
                    exportOrigin = "projecte complet";
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"FAIL: l'export CAx ha llançat excepcions en ambdues vies: {ex2.Message}");
                    if (ex2.InnerException != null)
                        Console.WriteLine($"  Inner: {ex2.InnerException.Message}");
                    return;
                }
            }

            Console.WriteLine($"    Export ({exportOrigin}): success={exportOk}");
            if (File.Exists(logPath))
            {
                foreach (string line in File.ReadAllLines(logPath).Take(30))
                    Console.WriteLine($"      {line}");
            }

            FileInfo amlFile = new FileInfo(amlPath);
            if (!amlFile.Exists || amlFile.Length == 0)
            {
                Console.WriteLine("FAIL: el fitxer AML no s'ha generat o és buit.");
                return;
            }

            Console.WriteLine($"    fitxer: {amlPath} ({amlFile.Length} bytes)");

            try
            {
                connection.ForceCloseProject();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(avís) tancant el projecte font: {ex.Message}");
            }

            string sandboxFolder = Path.Combine(Path.GetFullPath(targetDir), projectName);
            if (Directory.Exists(sandboxFolder))
            {
                try
                {
                    Directory.Delete(sandboxFolder, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"(avís) no s'ha pogut netejar el sandbox previ: {ex.Message}");
                }
            }

            Project sandbox;
            try
            {
                sandbox = new ProjectCreationService(connection).CreateNewProject(targetDir, projectName);
                Console.WriteLine($"[2] Projecte sandbox creat: {sandbox.Path!.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut crear el projecte sandbox: {ex.Message}");
                return;
            }

            bool importSucceeded = false;
            try
            {
                var importResult = CaxTransferService.ImportAml(sandbox, amlFile, CaxImportOptions.RetainTiaDevice);
                PrintCaxResult("Import AML", importResult);
                importSucceeded = true;

                if (HasLicenseError(importResult))
                {
                    Console.WriteLine("FAIL (gate): l'import CAx conté errors de llicència 'gsdml'.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    (avís) import de 2 args fallit: {ex.Message}");
                string importLogPath = amlPath + ".import.log";
                if (File.Exists(importLogPath))
                    File.Delete(importLogPath);

                try
                {
                    bool ok = CaxTransferService.ImportAmlWithLog(sandbox, amlFile, new FileInfo(importLogPath), CaxImportOptions.RetainTiaDevice);
                    Console.WriteLine($"    Import AML (3-arg): success={ok}");
                    if (File.Exists(importLogPath))
                    {
                        foreach (string line in File.ReadAllLines(importLogPath).Take(40))
                            Console.WriteLine($"      {line}");
                    }
                    importSucceeded = true;
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"FAIL: l'import CAx ha llançat excepcions en ambdues vies: {ex2.Message}");
                    if (ex2.InnerException != null)
                        Console.WriteLine($"  Inner: {ex2.InnerException.Message}");
                    Console.WriteLine("GATE: comprovar si la via CAx esquiva la llicència 'gsdml'.");
                    return;
                }
            }

            Device? imported = null;
            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(sandbox))
            {
                if (device.Name.Equals(gsdDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    imported = device;
                    break;
                }
            }

            bool moduleFound = false;
            int ioRanges = 0;
            var ranges = new List<string>();
            if (imported != null)
            {
                foreach (DeviceItem top in imported.DeviceItems)
                    ScanPocItem(top, ref moduleFound, ref ioRanges, ranges);
            }

            Console.WriteLine("[3] Verificació al sandbox:");
            Console.WriteLine($"    dispositiu '{gsdDeviceName}' creat: {(imported != null ? "PASS" : "FAIL")}");
            Console.WriteLine($"    submòdul '06 process data words': {(moduleFound ? "TROBAT" : "NO TROBAT")}");
            Console.WriteLine($"    rangs I/Q al dispositiu: {ioRanges}");
            foreach (string range in ranges)
                Console.WriteLine($"      {range}");

            bool pass = importSucceeded && imported != null && ioRanges > 0;
            if (pass)
            {
                Console.WriteLine();
                Console.WriteLine("RESULTAT: PASS — la via CAx crea el dispositiu GSD sense llicència 'gsdml'.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("RESULTAT: FAIL — revisar el motive (llicència, referències AML o materialització d'adreces).");
            }

            try
            {
                sandbox.Save();
                Console.WriteLine($"Projecte sandbox desat a: {sandbox.Path!.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut desar el projecte sandbox: {ex.Message}");
            }
        }

        private static void ScanPocItem(DeviceItem item, ref bool moduleFound, ref int ioRanges, List<string> ranges)
        {
            try
            {
                if (item.Name.IndexOf("06 process data words", StringComparison.OrdinalIgnoreCase) >= 0)
                    moduleFound = true;
            }
            catch { }

            try
            {
                foreach (Address address in item.Addresses)
                {
                    if (address.IoType == AddressIoType.Input || address.IoType == AddressIoType.Output)
                    {
                        ioRanges++;
                        int start = SafeInt(address.StartAddress);
                        int length = SafeInt(address.Length);
                        ranges.Add($"{IsIq(address.IoType)} {start}..{start + Math.Max(length - 1, 0)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      (adreces: {ex.Message})");
            }

            try
            {
                foreach (DeviceItem child in item.DeviceItems)
                    ScanPocItem(child, ref moduleFound, ref ioRanges, ranges);
            }
            catch { }
        }

        private static void RunBlocksGeneration(
            TiaConnection connection,
            string templatePath,
            string docsDir,
            string targetDirectory,
            string projectName,
            bool overrideExisting,
            bool compile)
        {
            Console.WriteLine("═══ GENERATE BLOCKS INTO TEMPLATE CLONE ═══");
            Console.WriteLine($"Template:      {templatePath}");
            Console.WriteLine($"Docs:          {docsDir}");
            Console.WriteLine($"Target parent: {targetDirectory}");
            Console.WriteLine($"Project name:  {projectName}");
            Console.WriteLine($"Override:      {overrideExisting}");
            Console.WriteLine($"Compile:       {compile}");

            // El maquinari ja és al template: només cal Blocks.csv.
            GenerationParseResult parseResult = ReadCsvDocs(docsDir, hardwareOptional: true);
            PrintIssues(parseResult.Issues);
            if (parseResult.HasErrors)
            {
                Console.WriteLine("Abortat: el document té errors de parseig.");
                return;
            }

            if (parseResult.Document.Blocks.Count == 0)
            {
                Console.WriteLine("Abortat: Blocks.csv no conté cap bloc.");
                return;
            }

            var catalog = CatalogService.Load(ResolveCatalogPath());
            var validator = new ValidationService(catalog, docsDir);
            ValidationReport validation = validator.Validate(parseResult.Document);

            foreach (var issue in validation.Issues)
            {
                string level = issue.Level == ParseIssueLevel.Error ? "ERROR" : "WARN ";
                string item = issue.Item.Length > 0 ? issue.Section + " · " + issue.Item : issue.Section;
                Console.WriteLine($"  {level} {item}: {issue.Message}");
            }

            if (validation.HasErrors)
            {
                Console.WriteLine("Abortat: hi ha errors de validació.");
                return;
            }

            string fullTargetParent = Path.GetFullPath(targetDirectory);
            string targetProjectDirectory = Path.Combine(fullTargetParent, projectName);
            if (Directory.Exists(targetProjectDirectory)
                && Directory.EnumerateFileSystemEntries(targetProjectDirectory).Any())
            {
                Console.WriteLine($"Abortat: la carpeta destí ja conté fitxers: {targetProjectDirectory}");
                return;
            }

            Directory.CreateDirectory(fullTargetParent);

            try
            {
                // Des del menú la connexió ja existeix: no s'obre una segona instància de TIA.
                if (!connection.IsConnected)
                    connection.ConnectHeadless();
                connection.OpenProject(templatePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut obrir el projecte template: {ex.Message}");
                return;
            }

            if (!connection.OpenedProject)
            {
                Console.WriteLine("FAIL: el projecte template ja estava obert a TIA Portal; tanqueu-lo abans de generar-ne una còpia.");
                return;
            }

            try
            {
                connection.Project.SaveAs(new DirectoryInfo(targetProjectDirectory));
                string? clonePath = Directory
                    .GetFiles(targetProjectDirectory, "*.ap21", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (clonePath == null)
                    throw new InvalidOperationException("No s'ha trobat el fitxer .ap21 del projecte clonat.");

                connection.ForceCloseProject();
                connection.OpenProject(clonePath);
                Console.WriteLine($"Projecte clonat: {clonePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut clonar el template: {ex.Message}");
                return;
            }

            BlocksGenerationReport report;
            try
            {
                report = new BlocksGenerationService(connection.Project, docsDir).Generate(
                    parseResult.Document,
                    new BlocksGenerationOptions
                    {
                        OverrideExisting = overrideExisting,
                        Compile = compile,
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: error inesperat important blocs: {ex.Message}");
                return;
            }

            PrintBlocksGenerationReport(report);

            // Es desa sempre: és un clon (el template no es toca) i així es pot
            // obrir a TIA per revisar els blocs fallits o els errors de compilació.
            bool compileOk = report.CompileResult == null || report.CompileResult.ErrorCount == 0;
            try
            {
                connection.Project.Save();
                string status = report.FailedCount == 0 && compileOk
                    ? "OK"
                    : $"AMB INCIDÈNCIES ({report.FailedCount} fallits, " +
                      $"{report.CompileResult?.ErrorCount ?? 0} errors de compilació)";
                Console.WriteLine($"Projecte desat {status}: {connection.Project.Path.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut desar el projecte: {ex.Message}");
            }
        }

        private static void PrintBlocksGenerationReport(BlocksGenerationReport report)
        {
            foreach (BlockGenerationItemReport item in report.Items)
            {
                string group = string.IsNullOrWhiteSpace(item.GroupPath) ? "(root)" : item.GroupPath;
                string status = item.Action.ToString().ToUpperInvariant();
                string error = item.Error == null ? string.Empty : $" ERROR={item.Error}";
                string note = item.Note == null ? string.Empty : $" ({item.Note})";
                Console.WriteLine($"  {status,-11} {item.Device}/{group}/{item.Name} ({item.BlockType}){note}{error}");
            }

            if (report.CompileResult != null)
            {
                Console.WriteLine(
                    $"  COMPILE state={report.CompileResult.State} " +
                    $"errors={report.CompileResult.ErrorCount} warnings={report.CompileResult.WarningCount}");
                PrintCompileErrors(report.CompileResult.Messages, string.Empty, 0);
            }

            foreach (string error in report.Errors)
                Console.WriteLine($"  ERROR: {error}");

            Console.WriteLine(
                $"Blocks: {report.ImportedCount} imported, {report.OverriddenCount} overridden, " +
                $"{report.SkippedCount} skipped, {report.FailedCount} failed.");
        }

        /// <summary>
        /// Imprimeix els missatges d'error del compilador (fulles amb estat Error),
        /// amb el camí de l'objecte que els conté. Limitat per no inundar la consola.
        /// </summary>
        private static int PrintCompileErrors(CompilerResultMessageComposition messages, string path, int printed)
        {
            const int max = 60;
            foreach (CompilerResultMessage message in messages)
            {
                if (printed >= max)
                    return printed;
                if (message.State != CompilerResultState.Error && message.ErrorCount == 0)
                    continue;

                if (message.Messages.Count == 0)
                {
                    if (message.State == CompilerResultState.Error)
                    {
                        // El pare és el bloc; la fulla sol ser "Network N".
                        string where = string.Join(" > ",
                            new[] { path, message.Path }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (string.IsNullOrWhiteSpace(message.Path) && string.IsNullOrWhiteSpace(path)
                            && message.Description.StartsWith("Compiling finished", StringComparison.OrdinalIgnoreCase))
                            continue;
                        Console.WriteLine($"    COMPILE ERROR [{where}] {message.Description}");
                        printed++;
                        if (printed == max)
                            Console.WriteLine($"    ... (més errors; obre el projecte a TIA per veure'ls tots)");
                    }
                }
                else
                {
                    string here = string.IsNullOrWhiteSpace(message.Path) ? path : message.Path;
                    printed = PrintCompileErrors(message.Messages, here, printed);
                }
            }
            return printed;
        }

        private sealed class PocExportedBlock
        {
            public BlockInventoryItem Source { get; set; } = new();
            public string FilePath { get; set; } = string.Empty;
            public string TargetGroupPath { get; set; } = string.Empty;
            public bool Imported { get; set; }
        }

        /// <summary>
        /// Phase 0 PoC: export a few real blocks to SimaticML, import them into a
        /// disposable scratch project, verify metadata and remove the scratch project.
        /// No document, validation or generation pipeline is touched.
        /// </summary>
        private static void RunBlocksPoc(TiaConnection connection, string[] args)
        {
            string sourcePath = args[1];
            string outDir = Path.GetFullPath(args[2]);
            string? requestedBlock = args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;
            string? deviceFilter = args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]) ? args[4] : null;

            Directory.CreateDirectory(outDir);

            try
            {
                connection.ConnectHeadless();
                connection.OpenProject(sourcePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: no s'ha pogut obrir el projecte font: {ex.Message}");
                return;
            }

            Console.WriteLine($"[1] Source project: {connection.Project.Name}");
            var source = new BlocksExportService(connection.Project);
            BlockInspectionResult inspection = source.Inspect(deviceFilter);
            foreach (string error in inspection.Errors)
                Console.WriteLine($"  ERROR: {error}");

            List<BlockInventoryItem> samples = SelectPocSamples(inspection.Blocks, requestedBlock);
            if (samples.Count == 0)
            {
                Console.WriteLine(requestedBlock == null
                    ? "FAIL: no OB/FB/FC/GlobalDB/InstanceDB blocks found."
                    : $"FAIL: block '{requestedBlock}' not found.");
                return;
            }

            string deviceName = samples[0].DeviceName;
            string? cpuType = source.GetCpuTypeIdentifier(deviceName);
            string cpuName = source.GetCpuName(deviceName) ?? deviceName;

            Console.WriteLine($"[2] Device: {deviceName}  CPU: {cpuName}  TypeIdent: {cpuType ?? "(unknown)"}");
            Console.WriteLine($"    Samples: {string.Join(", ", samples.Select(s => $"{s.BlockType}:{s.Name}"))}");

            string sourceDir = Path.Combine(outDir, "source", SanitizePathSegment(deviceName));
            Directory.CreateDirectory(sourceDir);

            var exported = new List<PocExportedBlock>();
            foreach (BlockInventoryItem sample in samples)
            {
                string filePath = Path.Combine(sourceDir, SanitizePathSegment(sample.Name) + ".xml");
                try
                {
                    BlockInventoryItem actual = source.ExportBlock(deviceName, sample.Name, new FileInfo(filePath));
                    var file = new FileInfo(filePath);
                    exported.Add(new PocExportedBlock { Source = actual, FilePath = filePath });
                    Console.WriteLine($"  EXPORT {actual.BlockType,-9} {actual.Name,-24} {file.Length,7} bytes  {filePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  EXPORT FAILED {sample.BlockType} {sample.Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.WriteLine($"    Inner: {ex.InnerException.Message}");
                }
            }

            if (exported.Count == 0)
            {
                Console.WriteLine("FAIL: no block could be exported.");
                return;
            }

            if (!connection.OpenedProject)
            {
                Console.WriteLine("FAIL: el projecte font ja estava obert a TIA Portal; tanqueu-lo abans d'executar el PoC.");
                return;
            }

            string scratchName = "BLOCKS_POC_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string scratchDir = Path.Combine(outDir, scratchName);
            string? scratchPath = null;
            bool scratchCreated = false;

            try
            {
                // Clone the source project so languages, UDTs, technology objects and
                // referenced blocks are present. This isolates the import test while
                // still proving the real export/import round trip.
                try
                {
                    connection.Project.SaveAs(new DirectoryInfo(scratchDir));
                    scratchCreated = true;
                    scratchPath = Directory
                        .GetFiles(scratchDir, "*.ap21", SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (scratchPath == null)
                        throw new InvalidOperationException("Saved project file not found in scratch folder.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL: no s'ha pogut clonar el projecte scratch: {ex.Message}");
                    return;
                }

                Console.WriteLine($"[3] Scratch copy: {scratchPath}");
                connection.ForceCloseProject();
                connection.OpenProject(scratchPath);

                Project scratch = connection.Project;
                var target = new BlocksExportService(scratch);
                PlcSoftware? targetSoftware = target.FindPlcSoftware(deviceName);
                if (targetSoftware == null)
                {
                    Console.WriteLine($"FAIL: el PLC '{deviceName}' no existeix al projecte scratch.");
                    return;
                }

                Console.WriteLine("[4] Import samples");
                foreach (PocExportedBlock item in exported)
                {
                    PlcBlockComposition composition = EnsurePocGroup(
                        targetSoftware.BlockGroup,
                        item.Source.GroupPath,
                        out string groupPath);

                    item.TargetGroupPath = groupPath;
                    item.Imported = ImportPocBlock(
                        composition,
                        item.FilePath,
                        item.Source.Name,
                        groupPath,
                        ImportOptions.Override);
                }

                Console.WriteLine("[5] Override re-import");
                PocExportedBlock? firstImported = exported.FirstOrDefault(e => e.Imported);
                if (firstImported != null)
                {
                    PlcBlockComposition overrideComposition = EnsurePocGroup(
                        targetSoftware.BlockGroup,
                        firstImported.TargetGroupPath,
                        out string overrideGroupPath);

                    firstImported.Imported = ImportPocBlock(
                        overrideComposition,
                        firstImported.FilePath,
                        firstImported.Source.Name,
                        overrideGroupPath,
                        ImportOptions.Override);
                }
                else
                {
                    Console.WriteLine("  (avís) no block was imported; override re-import skipped.");
                }

                Console.WriteLine("[6] Verify imported metadata");
                BlockInspectionResult after = target.Inspect(deviceName);
                foreach (string error in after.Errors)
                    Console.WriteLine($"  ERROR: {error}");

                foreach (PocExportedBlock item in exported)
                {
                    if (!item.Imported)
                    {
                        Console.WriteLine($"  SKIP     {item.Source.Name}: import failed");
                        continue;
                    }

                    BlockInventoryItem? actual = after.Blocks.FirstOrDefault(b =>
                        b.Name.Equals(item.Source.Name, StringComparison.OrdinalIgnoreCase)
                        && b.GroupPath.Equals(item.TargetGroupPath, StringComparison.OrdinalIgnoreCase));

                    if (actual == null)
                    {
                        Console.WriteLine(
                            $"  MISMATCH {item.Source.Name}: not found in '{item.TargetGroupPath}' after import");
                        continue;
                    }

                    bool match = actual.BlockType.Equals(item.Source.BlockType, StringComparison.OrdinalIgnoreCase)
                        && actual.Number == item.Source.Number
                        && actual.ProgrammingLanguage.Equals(item.Source.ProgrammingLanguage, StringComparison.OrdinalIgnoreCase);

                    Console.WriteLine(match
                        ? $"  MATCH    {item.Source.Name}: {actual.BlockType} No={actual.Number} {actual.ProgrammingLanguage}"
                        : $"  MISMATCH {item.Source.Name}: source={item.Source.BlockType} No={item.Source.Number} " +
                          $"{item.Source.ProgrammingLanguage}; target={actual.BlockType} No={actual.Number} {actual.ProgrammingLanguage}");
                }

                Console.WriteLine("[6b] Compile scratch PLC");
                try
                {
                    ICompilable? compilable = targetSoftware.GetService<ICompilable>();
                    if (compilable != null)
                    {
                        CompilerResult compileResult = compilable.Compile();
                        Console.WriteLine(
                            $"  Compile state={compileResult.State} errors={compileResult.ErrorCount} warnings={compileResult.WarningCount}");
                    }
                    else
                    {
                        Console.WriteLine("  (avís) no ICompilable service found on PlcSoftware.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) compile failed: {ex.Message}");
                }

                if (firstImported != null)
                {
                    string roundtripDir = Path.Combine(outDir, "roundtrip");
                    string roundtripPath = Path.Combine(
                        roundtripDir,
                        SanitizePathSegment(firstImported.Source.Name) + ".xml");

                    try
                    {
                        target.ExportBlock(deviceName, firstImported.Source.Name, new FileInfo(roundtripPath));
                        Console.WriteLine($"[7] Re-exported imported block: {roundtripPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  (avís) re-export failed: {ex.Message}");
                    }
                }

                try
                {
                    scratch.Save();
                    Console.WriteLine("[8] Scratch project saved.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) scratch save failed: {ex.Message}");
                }
            }
            finally
            {
                if (scratchCreated)
                {
                    try { connection.ForceCloseProject(); }
                    catch (Exception ex) { Console.WriteLine($"(avís) closing scratch project: {ex.Message}"); }

                    TryDeleteDirectory(scratchDir, out string deleteError);
                    Console.WriteLine(string.IsNullOrEmpty(deleteError)
                        ? $"[9] Scratch project removed: {scratchDir}"
                        : $"(avís) no s'ha pogut esborrar el scratch '{scratchDir}': {deleteError}");
                }
            }
        }

        private static List<BlockInventoryItem> SelectPocSamples(
            List<BlockInventoryItem> blocks,
            string? requestedBlock)
        {
            if (!string.IsNullOrWhiteSpace(requestedBlock))
            {
                return blocks
                    .Where(b => b.Name.Equals(requestedBlock, StringComparison.OrdinalIgnoreCase))
                    .Take(1)
                    .ToList();
            }

            var result = new List<BlockInventoryItem>();
            foreach (string type in new[] { "OB", "FB", "FC", "GlobalDB", "InstanceDB" })
            {
                BlockInventoryItem? block = blocks.FirstOrDefault(b =>
                    b.Error == null
                    && !b.IsKnowHowProtected
                    && !IsLikelySafetyBlock(b.Name)
                    && b.BlockType.Equals(type, StringComparison.OrdinalIgnoreCase));

                if (block != null)
                    result.Add(block);
                else
                    Console.WriteLine($"  (avís) no unprotected, non-safety {type} block found.");
            }

            return result;
        }

        private static bool IsLikelySafetyBlock(string name)
        {
            return name.StartsWith("FOB", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("F_", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Safety", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static PlcBlockComposition EnsurePocGroup(
            PlcBlockGroup root,
            string groupPath,
            out string normalizedGroupPath)
        {
            normalizedGroupPath = (groupPath ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrEmpty(normalizedGroupPath))
                return root.Blocks;

            PlcBlockGroup current = root;
            foreach (string segment in normalizedGroupPath.Split(
                         new[] { '/' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                PlcBlockUserGroup child = current.Groups.Find(segment)
                    ?? current.Groups.Create(segment);
                current = child;
            }

            return current.Blocks;
        }

        private static bool ImportPocBlock(
            PlcBlockComposition composition,
            string filePath,
            string expectedName,
            string groupPath,
            ImportOptions importOptions = ImportOptions.None)
        {
            string label = string.IsNullOrEmpty(groupPath) ? "(root)" : groupPath;

            try
            {
                ImportPocBlockCore(composition, filePath, expectedName, label, importOptions, SWImportOptions.None);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  IMPORT {expectedName} -> {label} failed: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"    Inner: {ex.InnerException.Message}");

                Console.WriteLine($"  Retry {expectedName} -> {label} with SWImportOptions.IgnoreMissingReferencedObjects");
                try
                {
                    ImportPocBlockCore(
                        composition,
                        filePath,
                        expectedName,
                        label,
                        importOptions | ImportOptions.SkipInactiveCultures,
                        SWImportOptions.IgnoreMissingReferencedObjects);
                    return true;
                }
                catch (Exception retryEx)
                {
                    Console.WriteLine($"  IMPORT RETRY FAILED {expectedName} -> {label}: {retryEx.Message}");
                    if (retryEx.InnerException != null)
                        Console.WriteLine($"    Inner: {retryEx.InnerException.Message}");
                    return false;
                }
            }
        }

        private static void ImportPocBlockCore(
            PlcBlockComposition composition,
            string filePath,
            string expectedName,
            string label,
            ImportOptions importOptions,
            SWImportOptions swImportOptions)
        {
            IList<PlcBlock> imported = composition.Import(
                new FileInfo(filePath),
                importOptions,
                swImportOptions);

            Console.WriteLine($"  IMPORT {expectedName} -> {label}: OK ({imported.Count} block(s))");
            foreach (PlcBlock block in imported)
            {
                try
                {
                    Console.WriteLine($"    {block.GetType().Name} {block.Name} No={block.Number} {block.ProgrammingLanguage}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    (metadata: {ex.Message})");
                }
            }
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
                builder.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);

            string result = builder.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(result) ? "unnamed" : result;
        }

        private static void TryDeleteDirectory(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "empty path";
                return;
            }

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    if (!Directory.Exists(path))
                        return;

                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    System.Threading.Thread.Sleep(1500);
                }
            }
        }

        private static void PrintCaxResult(string step, TransferResult result)
        {
            Console.WriteLine($"    {step}: State={result.State} Errors={result.ErrorCount} Warnings={result.WarningCount}");
            try
            {
                foreach (TransferResultMessage message in result.Messages)
                {
                    string tag = message.State == TransferResultState.Error ? "ERR" :
                                 message.State == TransferResultState.Warning ? "WRN" :
                                 message.State == TransferResultState.Information ? "INF" : "OK ";
                    Console.WriteLine($"      [{tag}] {message.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      (messages: {ex.Message})");
            }
        }

        private static bool HasLicenseError(TransferResult result)
        {
            if (result.ErrorCount == 0)
                return false;

            try
            {
                foreach (TransferResultMessage message in result.Messages)
                {
                    if (message.State == TransferResultState.Error &&
                        message.Message.IndexOf("llic", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            return false;
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
            return AppPaths.ImportsDir;
        }

        private static string ResolveCatalogPath()
        {
            return CatalogLocator.Resolve(AppConfig["Import:CatalogPath"]);
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
            Console.WriteLine("│  3. Export Project to CSV files    │");
            Console.WriteLine("│  4. Generate Hardware Catalog      │");
            Console.WriteLine("│  0. Return                         │");
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
                ?? AppPaths.ExportsDir;
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
            Console.WriteLine("Extracting hardware addresses from the open project...");
            var moduleAddresses = new TagsExportService(connection).ExportModuleAddresses();
            Console.WriteLine($"  {moduleAddresses.Count} hw address(es) trobades.");
            Console.WriteLine("Extracting program blocks from the open project...");
            BlockDocsExportReport blockReport = new BlocksExportService(connection.Project).ExportDocs(docsDir);
            int udtCount = blockReport.Blocks.Count(b => string.Equals(b.Kind, "Type", StringComparison.OrdinalIgnoreCase));
            int tagTableCount = blockReport.Blocks.Count(b => string.Equals(b.Kind, "TagTable", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine(
                $"  {blockReport.Blocks.Count - udtCount - tagTableCount} bloc(s), {udtCount} UDT(s), " +
                $"{tagTableCount} taula(es) de variables; {blockReport.Errors.Count} avís(os).");
            foreach (string blockError in blockReport.Errors)
                Console.WriteLine($"  (avís bloc) {blockError}");

            Console.WriteLine("Writing CSV files...");
            var report = new DocsExportService(docsDir, catalog).Export(
                devices, tags, moduleAddresses, blockReport.Blocks);
            Console.WriteLine();
            Console.WriteLine($"Docs generats a: {report.OutputDir}");
            Console.WriteLine($"  Devices:    {report.Devices}");
            Console.WriteLine($"  Modules:    {report.Modules}");
            Console.WriteLine($"  Subnets:    {report.Subnets}");
            Console.WriteLine($"  Interfaces: {report.Interfaces}");
            Console.WriteLine($"  IoTags:     {report.Tags}");
            Console.WriteLine($"  HwAddr:     {report.ModuleAddresses}");
            Console.WriteLine($"  Blocks:     {report.Blocks} (blocs + UDT + taules de variables)");

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
                if (choice == null)
                {
                    Console.WriteLine();
                    return false;
                }

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
                WaitForKey();
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

        /// <summary>
        /// Resol l'entrada de l'usuari a un fitxer de projecte TIA (.ap*).
        /// Accepta: camí complet del fitxer, carpeta del projecte, o nom del
        /// projecte dins de TiaPortal:ProjectPath / TiaPortal:NewProjectPath.
        /// Retorna null si no es troba cap projecte.
        /// </summary>
        private static string? ResolveProjectFile(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string value = input.Trim().Trim('"');

            static string? FirstProjectIn(string dir)
            {
                if (!Directory.Exists(dir))
                    return null;
                return Directory.GetFiles(dir, "*.ap*", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetExtension(f).StartsWith(".ap", StringComparison.OrdinalIgnoreCase)
                                && Path.GetExtension(f).Length > 3
                                && char.IsDigit(Path.GetExtension(f)[3]))
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }

            if (File.Exists(value))
                return Path.GetFullPath(value);

            string? inFolder = FirstProjectIn(value);
            if (inFolder != null)
                return Path.GetFullPath(inFolder);

            if (Path.IsPathRooted(value))
                return null;

            foreach (string key in new[] { "TiaPortal:ProjectPath", "TiaPortal:NewProjectPath" })
            {
                string? root = AppConfig[key];
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                string candidate = Path.Combine(root, value);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                string? found = FirstProjectIn(candidate);
                if (found != null)
                    return Path.GetFullPath(found);
            }

            return null;
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
            // Instal·lada: Program Files és de només lectura -> es desa a Documents\TIAO\appsettings.json.
            string configPath = AppPaths.WritableSettingsPath;
            if (!File.Exists(configPath))
            {
                if (AppPaths.UserSettingsPath == null)
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllText(configPath, "{}");
            }

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

        private static string ReadOptionalValue(string label, string configuredDefault)
        {
            string prefix = string.IsNullOrWhiteSpace(configuredDefault)
                ? $"{label}: "
                : $"{label} [{configuredDefault}]: ";

            Console.Write(prefix);
            string input = Console.ReadLine()?.Trim() ?? string.Empty;
            return input.Length > 0 ? input : (configuredDefault ?? string.Empty);
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
