using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaOpennessApp
{
    class Program
    {
        private static readonly string TiaPublicApiPath =
            @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            Console.WriteLine("TIA Portal Openness - Base Application");
            Console.WriteLine("======================================");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            string projectPath = configuration["TiaPortal:ProjectPath"]
                ?? throw new InvalidOperationException("ProjectPath not configured in appsettings.json");

            if (!File.Exists(projectPath))
            {
                Console.WriteLine($"Error: Project file not found: {projectPath}");
                Console.WriteLine("Please check appsettings.json configuration.");
                return;
            }

            TiaPortal? portal = null;
            Project? project = null;

            try
            {
                Console.WriteLine($"Opening project: {projectPath}");

                // Try to attach to running TIA Portal or create new instance
                portal = new TiaPortal(TiaPortalMode.WithUserInterface);

                Console.WriteLine("Connected to TIA Portal");

                FileInfo projectFile = new FileInfo(projectPath);
                project = portal.Projects.Open(projectFile);

                Console.WriteLine($"Project opened: {project.Name}");

                Console.WriteLine("\nDevices in project:");
                Console.WriteLine("-------------------");

                foreach (Device device in project.Devices)
                {
                    Console.WriteLine($"  - {device.Name}");

                    foreach (DeviceItem item in device.DeviceItems)
                    {
                        Console.WriteLine($"    └─ {item.Name}");
                    }
                }

                Console.WriteLine("\nOperation completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner: {ex.InnerException.Message}");
                }
            }
            finally
            {
                if (project != null)
                {
                    project.Close();
                }

                if (portal != null)
                {
                    portal.Dispose();
                }

                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }
        private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            string dllPath = Path.Combine(TiaPublicApiPath, assemblyName.Name + ".dll");
            if (File.Exists(dllPath))
            {
                return Assembly.LoadFrom(dllPath);
            }
            return null;
        }
    }
}
