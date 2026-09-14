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

        /// <summary>True when attached to an already running TIA Portal; it must be left running.</summary>
        public bool AttachedToExisting { get; private set; }

        /// <summary>True when we opened the project ourselves, so we close it again to restore the previous state.</summary>
        public bool OpenedProject { get; private set; }

        public TiaPortal Portal => _portal ?? throw new InvalidOperationException("Portal not connected.");
        public Project Project => _project ?? throw new InvalidOperationException("Project not opened.");

        public TiaConnection()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        public void ConnectWithUi()
        {
            _portal = new TiaPortal(TiaPortalMode.WithUserInterface);
            AttachedToExisting = false;
        }

        public void ConnectHeadless()
        {
            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            AttachedToExisting = false;
        }

        /// <summary>
        /// If a TIA Portal with a user interface is already running, attach to it and leave it running.
        /// Otherwise start a headless instance (no GUI) that is shut down at the end.
        /// </summary>
        public void ConnectAuto()
        {
            foreach (TiaPortalProcess process in TiaPortal.GetProcesses())
            {
                if (process.Mode == TiaPortalMode.WithUserInterface)
                {
                    _portal = process.Attach();
                    // false;
                    AttachedToExisting = true;
                    return;
                }
            }

            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            AttachedToExisting = false;
        }

        public void AttachToExistingPortal()
        {
            foreach (TiaPortalProcess process in TiaPortal.GetProcesses())
            {
                if (process.Mode == TiaPortalMode.WithUserInterface)
                {
                    _portal = process.Attach();
                    // false;
                    AttachedToExisting = true;
                    return;
                }
            }

            throw new InvalidOperationException("No running TIA Portal instance with user interface found.");
        }

        public void OpenProject(string projectPath)
        {
            if (_portal == null)
                throw new InvalidOperationException("Call Connect first.");

            string fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            // If the project is already open in the portal (e.g. open in the GUI),
            // reuse it: do not open a second copy, and do not close it at the end.
            if (AttachedToExisting)
            {
                foreach (Project openProject in _portal.Projects)
                {
                    if (openProject.Path != null &&
                        string.Equals(openProject.Path.FullName, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _project = openProject;
                        OpenedProject = false;
                        return;
                    }
                }
            }

            _project = _portal.Projects.Open(new FileInfo(projectPath));
            OpenedProject = true;
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

            // Close the project only if WE opened it; if it was already open, leave it as it was.
            if (_project != null && OpenedProject)
            {
                try { _project.Close(); } catch { }
            }

            if (_portal != null)
            {
                try { _portal.Dispose(); } catch { }
            }

            _project = null;
            _portal = null;
            _disposed = true;
        }
    }
}


