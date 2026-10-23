using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;

namespace TiaOpennessApp.Services
{
    public class ProjectCreationService
    {
        private readonly TiaConnection _connection;

        public ProjectCreationService(TiaConnection connection)
        {
            _connection = connection;
        }

        /// <summary>
        /// Creates a new, empty TIA Portal project through ProjectComposition.Create.
        /// ProjectPath is the common parent of all projects, so the new project is
        /// stored under &lt;targetDirectory&gt;\&lt;projectName&gt;. The destination
        /// folder must not already contain files; it is created when missing.
        /// </summary>
        public Project CreateNewProject(string targetDirectory, string projectName)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("Destination directory is empty.", nameof(targetDirectory));
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name is empty.", nameof(projectName));
            if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Project name contains invalid file-name characters: {projectName}", nameof(projectName));

            string fullPath = Path.GetFullPath(targetDirectory);
            Directory.CreateDirectory(fullPath);

            string destination = Path.Combine(fullPath, projectName);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new InvalidOperationException($"The destination project folder already contains files: {destination}");

            // TIA only allows one project open at a time: close the one opened by us first.
            _connection.CloseOwnedProject();

            Project created = _connection.Portal.Projects.Create(new DirectoryInfo(fullPath), projectName);
            _connection.TrackProject(created);
            return created;
        }
    }
}