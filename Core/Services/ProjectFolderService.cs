using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TiaOpennessApp.Models.Database;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public sealed class ProjectFolderResult
    {
        public string ProjectFolder { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public DbImportResult? Import { get; set; }
        public int CopiedFiles { get; set; }
        public List<string> MissingSourceFiles { get; } = new List<string>();
        public ApprovalReport? Approval { get; set; }
        public KitAssignmentResult? KitAssignment { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// Creates a self-contained project folder from an export folder:
    /// <code>
    /// &lt;project&gt;\project.db              documents (CSV) + block list, schema v2
    /// &lt;project&gt;\blocks\ types\ tags\   SimaticML files referenced by the block list
    /// </code>
    /// Optionally checks the project against the approved catalog and, if it is approved,
    /// records that catalog version in project_info (later checks then use that version).
    /// </summary>
    public static class ProjectFolderService
    {
        public const string DatabaseFileName = "project.db";
        public static readonly string[] SourceFolders = { "blocks", "types", "tags" };

        public static ProjectFolderResult CreateFromExport(string exportsDir, string projectFolder,
            ApprovedCatalog? approvedCatalog = null)
        {
            string source = Path.GetFullPath(exportsDir);
            string target = Path.GetFullPath(projectFolder);
            var result = new ProjectFolderResult
            {
                ProjectFolder = target,
                DatabasePath = Path.Combine(target, DatabaseFileName),
            };

            if (!Directory.Exists(source))
            {
                result.Errors.Add($"Export folder not found: {source}");
                return result;
            }
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                result.Errors.Add($"Project folder is not empty: {target}");
                return result;
            }
            if (IsSameOrInside(target, source))
            {
                result.Errors.Add("The project folder cannot be the export folder or inside it.");
                return result;
            }

            Directory.CreateDirectory(target);
            try
            {
                // 1) Documents -> project.db (the import validates references and rolls back on errors).
                result.Import = new DatabaseImportService().ImportFromImports(result.DatabasePath, source);
                if (!result.Import.Success)
                {
                    result.Errors.AddRange(result.Import.Errors.Select(e => "Import: " + e));
                    return Cleanup(result);
                }
                result.Warnings.AddRange(result.Import.Warnings);

                // 2) SimaticML sources.
                foreach (string folder in SourceFolders)
                {
                    string from = Path.Combine(source, folder);
                    if (Directory.Exists(from))
                        result.CopiedFiles += CopyDirectory(from, Path.Combine(target, folder));
                }

                // 3) Every block list entry must point to a file inside the project folder.
                GenerationParseResult parsed = new DbGenerationReader(result.DatabasePath).Read();
                foreach (BlockSpec block in parsed.Document.Blocks)
                {
                    if (string.IsNullOrWhiteSpace(block.SourceFile))
                    {
                        result.MissingSourceFiles.Add($"{block.Device}/{block.Name}: no source file");
                        continue;
                    }
                    string full = Path.GetFullPath(Path.Combine(target, block.SourceFile));
                    if (!IsSameOrInside(full, target) || !File.Exists(full))
                        result.MissingSourceFiles.Add($"{block.Device}/{block.Name}: {block.SourceFile}");
                }
                if (result.MissingSourceFiles.Count > 0)
                    result.Warnings.Add($"{result.MissingSourceFiles.Count} block(s) without their source file in the project folder.");

                // 4) Optional: approval against the signed catalog; record its version when approved.
                if (approvedCatalog != null)
                {
                    // Link each device to the approved kit it is a copy of (before the approval check).
                    if (approvedCatalog.Kits.Count > 0)
                        result.KitAssignment = KitMatcher.AssignKits(result.DatabasePath, approvedCatalog.Kits);

                    var reader = new DbGenerationReader(result.DatabasePath);
                    GenerationParseResult doc = reader.Read();
                    result.Approval = ApprovalService.Check(doc.Document, reader.ProjectCatalog, approvedCatalog, null);
                    if (result.Approval.IsApproved)
                    {
                        using (SqliteConnection db = ProjectDatabase.Open(result.DatabasePath))
                            ProjectDatabase.SetInfo(db, null, ProjectDatabase.InfoKeys.CatalogVersion, approvedCatalog.Version);
                        SqliteConnection.ClearAllPools();
                    }
                    else
                    {
                        result.Warnings.Add("Not approved: the catalog version was not recorded; fix the project before generating.");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                return Cleanup(result);
            }

            return result;
        }

        /// <summary>On failure, remove what was created so the folder can be reused.</summary>
        private static ProjectFolderResult Cleanup(ProjectFolderResult result)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(result.ProjectFolder))
                    Directory.Delete(result.ProjectFolder, true);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not clean up {result.ProjectFolder}: {ex.Message}");
            }
            return result;
        }

        private static int CopyDirectory(string source, string target)
        {
            string root = Path.GetFullPath(source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, file.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
                count++;
            }
            return count;
        }

        private static bool IsSameOrInside(string path, string folder)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
    }
}
