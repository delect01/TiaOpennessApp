using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TiaOpennessApp.Models;
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

        private static readonly Lazy<IConfigurationRoot> Config = new(() =>
            new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build());

        private static IConfigurationRoot AppConfig => Config.Value;

        static void Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   TIA Portal Openness - Engineering Tool     ║");
            Console.WriteLine("║            V21 - .NET 4.8                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            using var connection = new TiaConnection();

            try
            {
                Console.WriteLine("Connecting to TIA Portal...");
                connection.ConnectWithUi();
                Console.WriteLine("Connected.\n");

                Console.Write("Project path [Enter = appsettings.json]: ");
                string? inputPath = Console.ReadLine()?.Trim();
                string projectPath;

                if (string.IsNullOrEmpty(inputPath))
                {
                    projectPath = AppConfig["TiaPortal:ProjectPath"]
                        ?? throw new InvalidOperationException("ProjectPath not configured.");
                    Console.WriteLine($"Using configured path: {projectPath}");
                }
                else
                {
                    projectPath = inputPath!;
                }

                Console.WriteLine("Opening project (read-only)...");
                connection.OpenProject(projectPath);
                Console.WriteLine("Project opened.\n");

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
                            ListDevices(connection);
                            break;
                        case "3":
                            ExportHardwareJson(connection);
                            break;
                        case "0":
                            running = false;
                            break;
                        default:
                            Console.WriteLine("Invalid option.");
                            break;
                    }

                    if (running)
                    {
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(true);
                        Console.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nERROR: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                Console.ResetColor();
            }
            finally
            {
                Console.WriteLine("\nClosing project...");
            }

            Console.WriteLine("Done. Press any key to exit.");
            Console.ReadKey(true);
        }

        private static void PrintMenu()
        {
            Console.WriteLine("┌─────────────────────────────────┐");
            Console.WriteLine("│         MAIN MENU               │");
            Console.WriteLine("├─────────────────────────────────┤");
            Console.WriteLine("│  1. Project Info                │");
            Console.WriteLine("│  2. List Devices & HW Tree      │");
            Console.WriteLine("│  3. Export Hardware → JSON      │");
            Console.WriteLine("│  0. Exit                        │");
            Console.WriteLine("└─────────────────────────────────┘");
            Console.Write("Select: ");
        }

        private static void ShowProjectInfo(TiaConnection connection)
        {
            Console.WriteLine("\n═══ PROJECT INFO ═══");
            var svc = new ProjectService(connection);
            svc.PrintProjectInfo();
        }

        private static void ListDevices(TiaConnection connection)
        {
            Console.WriteLine("\n═══ DEVICES & NETWORKS ═══\n");
            var svc = new HardwareService(connection);
            svc.PrintDeviceTree();
        }

        private static void ExportHardwareJson(TiaConnection connection)
        {
            string outputPath = GetOutputPathOrPrompt("hardware_export.json", "Output file path");
            Console.WriteLine("Extracting hardware...");
            var svc = new HardwareService(connection);
            var report = svc.ExportFullHardware();

            string json = JsonSerializer.Serialize(report, JsonOptions);
            WriteFile(outputPath, json);

            Console.WriteLine($"Exported {report.DeviceCount} devices to: {outputPath}");
        }

        private static void ListBlocks(TiaConnection connection)
        {
            Console.WriteLine("\n═══ PLC BLOCKS ═══\n");
            var svc = new BlockService(connection);
            svc.PrintBlockList();
        }

        private static void ExportBlocksJson(TiaConnection connection)
        {
            string outputPath = GetOutputPathOrPrompt("blocks_export.json", "Output file path");
            Console.WriteLine("Extracting blocks...");
            var svc = new BlockService(connection);
            var report = svc.ExportAllBlocks();

            string json = JsonSerializer.Serialize(report, JsonOptions);
            WriteFile(outputPath, json);

            Console.WriteLine($"Exported {report.BlockCount} blocks to: {outputPath}");
        }

        private static void ExportBlocksToXml(TiaConnection connection)
        {
            string outputDir = GetOutputPathOrPrompt("blocks_export", "Output directory");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            Console.WriteLine("Exporting blocks to XML...");
            var svc = new BlockService(connection);
            svc.ExportAllBlocksToDirectory(outputDir!);
            Console.WriteLine("Block export complete.");
        }

        private static void ShowDevicesAndNetworks(TiaConnection connection)
        {
            Console.WriteLine("\n═══ DEVICES & NETWORKS ═══\n");
            var svc = new DevicesAndNetworksService(connection);
            svc.PrintDevicesAndNetworks();

            Console.WriteLine();
            var ioChannels = new IoChannelService(connection);
            ioChannels.PrintIoChannels();
        }

        private static void ExportIoChannelsXml(TiaConnection connection)
        {
            string outputPath = GetOutputPathOrPrompt("io_channels.xml", "Output file path");
            Console.WriteLine("Extracting I/O channels...");
            var svc = new IoChannelService(connection);
            svc.ExportToXml(outputPath);
        }

        private static void ExportDevicesAndNetworksJson(TiaConnection connection)
        {
            string outputPath = GetOutputPathOrPrompt("devices_and_networks.json", "Output file path");
            Console.WriteLine("Extracting devices...");
            var svc = new DevicesAndNetworksService(connection);
            var report = svc.ExportAll(includeSubnets: false);

            string json = JsonSerializer.Serialize(report, JsonOptions);
            WriteFile(outputPath, json);

            Console.WriteLine($"Exported {report.DeviceCount} devices to: {outputPath}");
        }

        private static string GetOutputPathOrPrompt(string defaultFileName, string label)
        {
            string configured = GetOutputPath(defaultFileName);
            if (!string.IsNullOrEmpty(configured))
                return configured;

            Console.Write($"{label} [{defaultFileName}]: ");
            string? input = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(input) ? defaultFileName : input!;
        }

        private static string GetOutputPath(string defaultFileName)
        {
            string? configured = AppConfig["TiaPortal:OutputPath"];
            if (string.IsNullOrWhiteSpace(configured))
                return string.Empty;

            string path = configured!;
            bool isDirectory = Directory.Exists(path)
                || path.EndsWith("\\") || path.EndsWith("/")
                || string.IsNullOrEmpty(Path.GetExtension(path));

            return isDirectory ? Path.Combine(path, defaultFileName)! : path;
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
