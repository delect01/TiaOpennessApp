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

        /// <summary>True when a project is currently open in this connection (opened by us or reused).</summary>
        public bool HasOpenProject => _project != null;

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

            // Resolve environment variables: treat the argument as an env var name
            // (e.g. RUTA_DEL_PROJECTE_AP21) if it has no directory separators and
            // matches an existing environment variable.
            string resolved = projectPath;
            if (projectPath.IndexOfAny(new[] { '\\', '/' }) < 0)
            {
                string envValue = Environment.GetEnvironmentVariable(projectPath);
                if (envValue != null)
                    resolved = envValue;
            }

            string fullPath = Path.GetFullPath(resolved);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            // If the requested project is already open (by us or by the GUI), reuse it.
            if (_project != null && _project.Path != null &&
                string.Equals(_project.Path.FullName, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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

            // TIA only allows one project open at a time: close the one opened by us first.
            CloseOwnedProject();

            _project = _portal.Projects.Open(new FileInfo(fullPath));
            OpenedProject = true;
        }

        /// <summary>Closes the project only if this connection opened it; never closes a project the GUI already had open.</summary>
        public void CloseOwnedProject()
        {
            if (_project == null || !OpenedProject)
                return;

            try { _project.Close(); }
            catch { }
            _project = null;
            OpenedProject = false;
        }

        /// <summary>
        /// Closes the project owned by this connection and clears the state WITHOUT swallowing errors,
        /// so callers can detect and retry when TIA is still busy (e.g. right after Save).
        /// </summary>
        public void ForceCloseProject()
        {
            if (_project != null)
            {
                _project.Close();
                _project = null;
            }
            OpenedProject = false;
        }

        public void CloseProject()
        {
            if (_project == null)
                return;

            try { _project.Close(); }
            catch { }
            _project = null;
            OpenedProject = false;
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


