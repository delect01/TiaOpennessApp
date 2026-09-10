using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;

namespace TiaOpennessApp.Services
{
    public class TiaConnection : IDisposable
    {
        private static readonly string TiaPublicApiPath =
            @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

        private TiaPortal? _portal;
        private Project? _project;
        private bool _disposed;

        public TiaPortal Portal => _portal ?? throw new InvalidOperationException("Portal not connected.");
        public Project Project => _project ?? throw new InvalidOperationException("Project not opened.");

        public TiaConnection()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        public void ConnectWithUi()
        {
            _portal = new TiaPortal(TiaPortalMode.WithUserInterface);
        }

        public void ConnectHeadless()
        {
            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        }

        public void OpenProject(string projectPath)
        {
            if (_portal == null)
                throw new InvalidOperationException("Call Connect first.");

            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            FileInfo projectFile = new FileInfo(projectPath);
            _project = _portal.Projects.Open(projectFile);
        }

        public void AttachToExistingPortal()
        {
            var portalProcesses = TiaPortal.GetProcesses();
            if (portalProcesses.Count == 0)
                throw new InvalidOperationException("No running TIA Portal instance found.");

            _portal = new TiaPortal(TiaPortalMode.WithUserInterface);
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            string dllPath = Path.Combine(TiaPublicApiPath, assemblyName.Name + ".dll");
            if (File.Exists(dllPath))
            {
                return Assembly.LoadFrom(dllPath);
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_project != null)
            {
                try { _project.Close(); } catch { }
            }

            if (_portal != null)
            {
                try { _portal.Dispose(); } catch { }
            }

            _disposed = true;
        }
    }
}
