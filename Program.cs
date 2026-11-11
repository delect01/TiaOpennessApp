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

            if (command == "--create-project")
            {
                string? keyPath = args.Skip(1).FirstOrDefault(a => a.StartsWith("--public-key=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("--public-key=".Length);
                string[] positional = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
                if (positional.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --create-project <exportsDir> <projectFolder> [approvedCatalogRoot] [--public-key=<file>]");
                    return;
                }

                ApprovedCatalog? approvedForProject = null;
                if (positional.Length >= 3)
                {
                    try
                    {
                        CatalogKey? trusted = keyPath == null ? null : CatalogKey.Load(keyPath).PublicOnly();
                        approvedForProject = ApprovedCatalog.Load(positional[2], trusted);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                    {
                        Console.WriteLine($"ERROR: {ex.Message}");
                        Environment.ExitCode = 1;
                        return;
                    }
                }

                ProjectFolderResult created = ProjectFolderService.CreateFromExport(positional[0], positional[1], approvedForProject);
                Console.WriteLine($"Project folder: {created.ProjectFolder}");
                if (created.Import != null)
                    foreach (DbTableCount count in created.Import.TableCounts)
                        Console.WriteLine($"  {count.TableName,-20} {count.Count,8}");
                Console.WriteLine($"  source files copied  {created.CopiedFiles,8}");
                foreach (string missing in created.MissingSourceFiles.Take(20))
                    Console.WriteLine($"  MISSING {missing}");
                if (created.Approval != null)
                {
                    foreach (string error in created.Approval.Errors)
                        Console.WriteLine($"  NOT APPROVED {error}");
                    Console.WriteLine(created.Approval.IsApproved
                        ? $"  Approved against catalog v{created.Approval.CatalogVersion} (version recorded in the project)."
                        : $"  Not approved against catalog v{created.Approval.CatalogVersion}.");
                }
                foreach (string warning in created.Warnings)
                    Console.WriteLine($"  WARN: {warning}");
                foreach (string error in created.Errors)
                    Console.WriteLine($"  ERROR: {error}");
                Console.WriteLine(created.Success ? "Project folder created." : "Project folder NOT created.");
                Environment.ExitCode = created.Success ? 0 : 1;
                return;
            }

            if (command == "--check-project-db" || command == "--generate-project-db")
            {
                bool generate = command == "--generate-project-db";
                string? publicKeyPath = args.Skip(1).FirstOrDefault(a => a.StartsWith("--public-key=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("--public-key=".Length);
                string[] positional = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

                if (positional.Length < (generate ? 4 : 2))
                {
                    Console.WriteLine(generate
                        ? "Usage: TiaOpennessApp --generate-project-db <project.db> <approvedCatalogRoot> <targetDirectory> <projectName> [goldenAml] [--public-key=<file>]"
                        : "Usage: TiaOpennessApp --check-project-db <project.db> <approvedCatalogRoot> [--public-key=<file>]");
                    return;
                }

                PreparedProjectDb? prepared = PrepareProjectDb(positional[0], positional[1], publicKeyPath);
                if (prepared == null)
                {
                    Environment.ExitCode = 1;
                    return;
                }

                if (!generate)
                {
                    Console.WriteLine("\nResult: APPROVED and VALID - the project can be generated.");
                    return;
                }

                using var dbGenConnection = new TiaConnection();
                dbGenConnection.ConnectHeadless();
                Console.WriteLine("Connected to TIA Portal (headless).");
                GenerationRunResult? run = RunFullGenerationPipeline(dbGenConnection, prepared.Parse, prepared.Catalog,
                    prepared.ProjectFolder, positional[2], positional[3], positional.Length >= 5 ? positional[4] : null);
                Environment.ExitCode = run == null ? 1 : 0;
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

            if (command == "--catalog-keygen")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --catalog-keygen <outDir>");
                    return;
                }

                string outDir = Path.GetFullPath(args[1]);
                string privatePath = Path.Combine(outDir, "catalog-private-key.json");
                string publicPath = Path.Combine(outDir, "catalog-public-key.json");
                if (File.Exists(privatePath) || File.Exists(publicPath))
                {
                    Console.WriteLine($"ERROR: key files already exist in {outDir}; not overwritten.");
                    return;
                }

                Directory.CreateDirectory(outDir);
                CatalogKey key = CatalogKey.Generate();
                File.WriteAllText(privatePath, key.ToJson());
                File.WriteAllText(publicPath, key.PublicOnly().ToJson());
                Console.WriteLine($"Key id:      {key.KeyId}");
                Console.WriteLine($"Private key: {privatePath}");
                Console.WriteLine("             Keep it on administrator PCs only; never commit or share it.");
                Console.WriteLine($"Public key:  {publicPath}");
                Console.WriteLine("             Copy it to Core\\Security\\catalog-public-key.json and rebuild the apps.");
                return;
            }

            if (command == "--catalog-publish")
            {
                if (args.Length < 5)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --catalog-publish <catalogCsv> <shareRoot> <version> <privateKey> [extraFilesDir]");
                    return;
                }

                List<CatalogEntry> types;
                CatalogKey privateKey;
                string packageDir;
                try
                {
                    types = CatalogService.Load(args[1]);
                    privateKey = CatalogKey.Load(args[4]);
                    packageDir = ApprovedCatalog.Publish(types, args[3], args[2], privateKey,
                        publisher: Environment.UserName, extraFilesDir: args.Length >= 6 ? args[5] : null);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                           || ex is ArgumentException || ex is InvalidOperationException
                                           || ex is InvalidDataException)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                    Environment.ExitCode = 1;
                    return;
                }

                CatalogVerification check = ApprovedCatalog.Verify(packageDir, privateKey.PublicOnly());
                Console.WriteLine($"Published:   {packageDir}");
                Console.WriteLine($"Types:       {types.Count}");
                Console.WriteLine($"Files:       {check.Manifest?.Files.Count ?? 0}");
                Console.WriteLine($"Key id:      {privateKey.KeyId}");
                Console.WriteLine(check.IsValid ? "Verification: OK" : "Verification: FAILED - " + string.Join("; ", check.Errors));
                return;
            }

            if (command == "--catalog-verify")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: TiaOpennessApp --catalog-verify <shareRoot|packageDir> [publicKey]");
                    return;
                }

                CatalogKey? trusted = args.Length >= 3 ? CatalogKey.Load(args[2]).PublicOnly() : null;
                string packageDir = ApprovedCatalog.ResolvePackageDir(args[1]);
                CatalogVerification check = ApprovedCatalog.Verify(packageDir, trusted);
                Console.WriteLine($"Package:     {check.PackageDir}");
                if (check.Manifest != null)
                    Console.WriteLine($"Version:     {check.Manifest.CatalogVersion}  (published {check.Manifest.PublishedUtc} by {check.Manifest.Publisher}, key {check.Manifest.KeyId})");
                foreach (string error in check.Errors)
                    Console.WriteLine($"  ERROR: {error}");
                if (check.IsValid)
                {
                    ApprovedCatalog catalog = ApprovedCatalog.Load(packageDir, trusted);
                    Console.WriteLine($"Types:       {catalog.Types.Count}");
                }
                Console.WriteLine(check.IsValid ? "Result: VALID" : "Result: NOT VALID");
                Environment.ExitCode = check.IsValid ? 0 : 1;
                return;
            }

            if (command == "--db-info")
            {
                // Opens a project database, upgrading its schema in place if it is older.
                string dbPath = args.Length >= 2 ? args[1] : new DatabaseImportService().DefaultDbPath;
                int before;
                using (var readOnly = ProjectDatabase.OpenReadOnly(dbPath))
                    before = ProjectDatabase.TableExists(readOnly, "devices") && ProjectDatabase.GetSchemaVersion(readOnly) == 0
                        ? 1
                        : ProjectDatabase.GetSchemaVersion(readOnly);

                using var db = ProjectDatabase.Open(dbPath);
                int after = ProjectDatabase.GetSchemaVersion(db);
                Console.WriteLine($"Database file:  {Path.GetFullPath(dbPath)}");
                Console.WriteLine(before == after
                    ? $"Schema version: v{after}"
                    : $"Schema version: v{after} (upgraded from v{before})");
                foreach (string key in new[] { ProjectDatabase.InfoKeys.CreatedUtc, ProjectDatabase.InfoKeys.Source, ProjectDatabase.InfoKeys.CatalogVersion })
                {
                    string? value = ProjectDatabase.GetInfo(db, key);
                    if (value != null)
                        Console.WriteLine($"  {key,-20} {value}");
                }
                foreach (string table in ProjectDatabase.TableNames)
                {
                    using var countCommand = db.CreateCommand();
                    countCommand.CommandText = $"SELECT COUNT(*) FROM {table};";
                    Console.WriteLine($"  {table,-20} {countCommand.ExecuteScalar(),8}");
                }
                return;
            }

            Console.WriteLine($"Unknown command: {args[0]}");
            Console.WriteLine("Supported: --gen-catalog-from-export | --generate-template | --csv-to-xlsx | " +
                              "--xlsx-to-csv | --verify-readers | --validate-docs | " +
                              "--generate-project | --diff-topology | --dump-type-id | " +
                              "--plug-diagnose | --export-docs | --dump-subnets | --dump-interfaces | --dump-tags | " +
                              "--export-blocks | --generate-blocks | --dump-blocks | --dump-hw-items | " +
                              "--export-cax-project | --import-db | --db-to-csv | --db-info | " +
                              "--catalog-keygen | --catalog-publish | --catalog-verify | --check-project-db | --generate-project-db | --create-project");
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
            GenerationParseResult parseResult = ReadCsvDocs(docsDir);
            PrintIssues(parseResult.Issues);
            if (parseResult.HasErrors)
            {
                Console.WriteLine("\nAbortat: el document té errors de parseig.");
                return null;
            }

            return RunFullGenerationPipeline(connection, parseResult, CatalogService.Load(catalogPath), docsDir,
                targetDirectory, projectName, goldenAmlPath);
        }

        /// <summary>
        /// Returns the AML to import: the golden itself, or a patched copy (next to it) where every GSD
        /// device of the documents that the golden lacks is a copy of a golden device of the same type.
        /// </summary>
        private static string AddMissingGsdDevicesToGolden(string goldenAml, GenerationDocument doc,
            List<string> gsdDeviceNames, List<CatalogEntry> gsdCatalogEntries)
        {
            List<string> goldenNames;
            try
            {
                goldenNames = GoldenAmlCloner.ListDeviceNames(goldenAml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  (avís) no s'ha pogut llegir el golden AML per afegir dispositius GSD nous: {ex.Message}");
                return goldenAml;
            }

            var inGolden = new HashSet<string>(goldenNames, StringComparer.OrdinalIgnoreCase);
            var requests = new List<AmlCloneRequest>();
            foreach (string name in gsdDeviceNames.Where(n => !inGolden.Contains(n)))
            {
                DeviceSpec? spec = doc.Devices.FirstOrDefault(d => string.Equals(d.DeviceName, name, StringComparison.OrdinalIgnoreCase));
                if (spec == null)
                    continue;

                // Template: another device of the documents with the same type that the golden has.
                string? template = doc.Devices
                    .Where(d => string.Equals(d.TypeKey, spec.TypeKey, StringComparison.OrdinalIgnoreCase) && inGolden.Contains(d.DeviceName))
                    .Select(d => d.DeviceName)
                    .FirstOrDefault();
                string? identifier = gsdCatalogEntries
                    .FirstOrDefault(e => string.Equals(e.TypeKey, spec.TypeKey, StringComparison.OrdinalIgnoreCase))?.Identifier;
                string? ip = doc.Interfaces
                    .FirstOrDefault(i => string.Equals(i.Device, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.IpAddress))
                    ?.IpAddress;

                requests.Add(new AmlCloneRequest { NewName = name, TemplateName = template, TypeIdentifier = identifier, IpAddress = ip });
            }

            if (requests.Count == 0)
                return goldenAml;

            string patched = Path.Combine(Path.GetDirectoryName(goldenAml)!,
                Path.GetFileNameWithoutExtension(goldenAml) + "_new_gsd.aml");
            AmlCloneReport cloneReport = GoldenAmlCloner.AddClones(goldenAml, patched, requests);
            foreach (string cloned in cloneReport.Cloned)
                Console.WriteLine($"  GSD nou al golden: {cloned}");
            foreach (string error in cloneReport.Errors)
                Console.WriteLine($"  (avís) GSD no afegit al golden: {error}");
            return cloneReport.Cloned.Count > 0 ? patched : goldenAml;
        }

        private sealed class PreparedProjectDb
        {
            public GenerationParseResult Parse { get; set; } = new GenerationParseResult();
            public List<CatalogEntry> Catalog { get; set; } = new List<CatalogEntry>();
            public string ProjectFolder { get; set; } = string.Empty;
        }

        /// <summary>
        /// Reads project.db, verifies the approved catalog (signature + files), checks the project
        /// against it and runs the normal validation with the APPROVED types.
        /// Returns null (after printing the reasons) when the project must not be generated.
        /// </summary>
        private static PreparedProjectDb? PrepareProjectDb(string dbPath, string approvedRoot, string? publicKeyPath)
        {
            string fullDb = Path.GetFullPath(dbPath);
            Console.WriteLine($"Project database: {fullDb}");
            if (!File.Exists(fullDb))
            {
                Console.WriteLine("ERROR: project database not found.");
                return null;
            }

            var reader = new DbGenerationReader(fullDb);
            GenerationParseResult parse = reader.Read();
            PrintIssues(parse.Issues);
            if (parse.HasErrors)
            {
                Console.WriteLine("Aborted: the project database cannot be read.");
                return null;
            }
            GenerationDocument doc = parse.Document;
            Console.WriteLine($"  {doc.Devices.Count} devices, {doc.Modules.Count} modules, {doc.Subnets.Count} subnets, " +
                              $"{doc.Interfaces.Count} interfaces, {doc.ModuleAddresses.Count} addresses, " +
                              $"{doc.IoTags.Count} I/O tags, {doc.Blocks.Count} blocks");

            reader.Info.TryGetValue(ProjectDatabase.InfoKeys.CatalogVersion, out string? projectCatalogVersion);

            Console.WriteLine("\n═══ APPROVED CATALOG ═══");
            ApprovedCatalog approved;
            try
            {
                CatalogKey? trusted = publicKeyPath == null ? null : CatalogKey.Load(publicKeyPath).PublicOnly();
                if (trusted != null)
                    Console.WriteLine($"  (test) trusted key from {publicKeyPath}, id {trusted.KeyId}");
                approved = ApprovedCatalog.Load(approvedRoot, trusted,
                    string.IsNullOrWhiteSpace(projectCatalogVersion) ? null : projectCatalogVersion);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine("Aborted: no valid approved catalog.");
                return null;
            }
            Console.WriteLine($"  v{approved.Version} ({approved.Types.Count} types) - {approved.PackageDir}");

            Console.WriteLine("\n═══ APPROVAL CHECK ═══");
            ApprovalReport approval = ApprovalService.Check(doc, reader.ProjectCatalog, approved, projectCatalogVersion);
            foreach (string warning in approval.Warnings)
                Console.WriteLine($"  WARN  {warning}");
            foreach (string error in approval.Errors)
                Console.WriteLine($"  ERROR {error}");
            Console.WriteLine($"Result: {approval.Errors.Count} error(s), {approval.Warnings.Count} warning(s).");
            if (!approval.IsApproved)
            {
                Console.WriteLine("Aborted: the project uses elements that are not approved.");
                return null;
            }

            string projectFolder = Path.GetDirectoryName(fullDb)!;
            List<CatalogEntry> catalog = ApprovalService.GenerationCatalog(approved);
            ValidationReport validation = new ValidationService(catalog, projectFolder).Validate(doc);

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
                Console.WriteLine("Aborted: validation errors.");
                return null;
            }

            return new PreparedProjectDb { Parse = parse, Catalog = catalog, ProjectFolder = projectFolder };
        }

        /// <summary>
        /// Generation from an already-read document (CSV, Excel or project.db). <paramref name="docsDir"/>
        /// is the folder that relative files (block XML) are resolved against.
        /// </summary>
        private static GenerationRunResult? RunFullGenerationPipeline(
            TiaConnection connection,
            GenerationParseResult parseResult,
            List<CatalogEntry> catalog,
            string docsDir,
            string? targetDirectory,
            string? projectName,
            string? goldenAmlPath)
        {
            var result = new GenerationRunResult();

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

                    // GSD devices that are new or renamed in the documents are not in the golden:
                    // copy a device of the same type inside the AML so they can be imported too.
                    resolvedGoldenAml = AddMissingGsdDevicesToGolden(resolvedGoldenAml, parseResult.Document,
                        keepNames, gsdCatalogEntries);
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
