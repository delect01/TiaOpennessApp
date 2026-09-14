using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
                            ShowNetworkTree(connection);
                            break;
                        case "3":
                            ExportNetworkJson(connection);
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
                if (connection.AttachedToExisting)
                    Console.WriteLine("\nAttached TIA Portal left open (same state as found).");
                else
                    Console.WriteLine("\nClosing headless TIA Portal instance...");
            }

            Console.WriteLine("Done. Press any key to exit.");
            Console.ReadKey(true);
        }

        private static void PrintMenu()
        {
            Console.WriteLine("┌──────────────────────────────────┐");
            Console.WriteLine("│         MAIN MENU                │");
            Console.WriteLine("├──────────────────────────────────┤");
            Console.WriteLine("│  1. Project Info                 │");
            Console.WriteLine("│  2. Network Devices Tree         │");
            Console.WriteLine("│  3. Export Network Devices → JSON│");
            Console.WriteLine("│  0. Exit                         │");
            Console.WriteLine("└──────────────────────────────────┘");
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

        private static void ExportNetworkJson(TiaConnection connection)
        {
            string outputPath = GetOutputPathOrPrompt("network_devices.json", "Output file path");
            Console.WriteLine("Extracting network devices...");
            var svc = new DevicesAndNetworksService(connection);
            var devices = svc.ExportNetworkDevices();

            string json = JsonSerializer.Serialize(devices, JsonOptions);
            WriteFile(outputPath, json);

            Console.WriteLine($"Exported {devices.Count} device(s) to: {outputPath}");
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
