using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public enum BlockGenerationAction
    {
        Imported,
        Overridden,
        Skipped,
        Failed,
    }

    public class BlockGenerationItemReport
    {
        public string Device { get; set; } = string.Empty;
        public string GroupPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BlockType { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public BlockGenerationAction Action { get; set; }
        public int ImportedCount { get; set; }
        public string? Error { get; set; }
    }

    public class BlocksGenerationReport
    {
        public List<BlockGenerationItemReport> Items { get; } = new();
        public List<string> Errors { get; } = new();
        public CompilerResult? CompileResult { get; set; }
        public int ImportedCount => Items.Count(i => i.Action == BlockGenerationAction.Imported);
        public int OverriddenCount => Items.Count(i => i.Action == BlockGenerationAction.Overridden);
        public int SkippedCount => Items.Count(i => i.Action == BlockGenerationAction.Skipped);
        public int FailedCount => Items.Count(i => i.Action == BlockGenerationAction.Failed);
    }

    public class BlocksGenerationOptions
    {
        /// <summary>
        /// Si és cert, reimporta amb <see cref="ImportOptions.Override"/> quan el
        /// bloc ja existeix al mateix GroupPath. Si és fals, el salta.
        /// </summary>
        public bool OverrideExisting { get; set; }

        /// <summary>Compila el PLC després d'importar tots els blocs.</summary>
        public bool Compile { get; set; } = true;

        /// <summary>
        /// Permet reintentar amb <c>IgnoreMissingReferencedObjects</c>. Desactivat
        /// per defecte: en un template vàlid les dependències hi han de ser.
        /// </summary>
        public bool AllowMissingReferences { get; set; }
    }

    /// <summary>
    /// Importa blocs SimaticML en un projecte/template existent. No crea el
    /// projecte ni exporta UDTs: el cridador ha de clonar un template que ja
    /// contingui els UDTs, llenguatges i blocs referenciats.
    /// </summary>
    public class BlocksGenerationService
    {
        private static readonly Dictionary<string, int> ImportOrder =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["GlobalDB"] = 0,
                ["FC"] = 1,
                ["FB"] = 2,
                ["InstanceDB"] = 3,
                ["OB"] = 4,
                ["CodeBlock"] = 5,
                ["DataBlock"] = 6,
            };

        private readonly Project _project;
        private readonly string _docsDir;

        public BlocksGenerationService(Project project, string docsDir)
        {
            _project = project;
            _docsDir = Path.GetFullPath(docsDir);
        }

        public BlocksGenerationReport Generate(
            GenerationDocument document,
            BlocksGenerationOptions? options = null)
        {
            options ??= new BlocksGenerationOptions();
            var report = new BlocksGenerationReport();
            var softwareByDevice = new Dictionary<string, PlcSoftware>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in document.Blocks
                         .Select((block, index) => new { Block = block, Index = index })
                         .OrderBy(e => OrderFor(e.Block.BlockType))
                         .ThenBy(e => e.Index))
            {
                BlockSpec spec = entry.Block;
                var item = new BlockGenerationItemReport
                {
                    Device = spec.Device,
                    GroupPath = spec.GroupPath,
                    Name = spec.Name,
                    BlockType = spec.BlockType,
                    SourceFile = spec.SourceFile,
                };
                report.Items.Add(item);

                try
                {
                    string filePath = ResolveSourceFile(spec.SourceFile);
                    Device? device = DevicesAndNetworksService.FindDeviceByName(_project, spec.Device);
                    if (device == null)
                        throw new InvalidOperationException($"Dispositiu '{spec.Device}' no trobat al projecte.");

                    PlcSoftware? software = FindPlcSoftware(device);
                    if (software == null)
                        throw new InvalidOperationException($"El dispositiu '{spec.Device}' no té software PLC.");

                    softwareByDevice[spec.Device] = software;
                    PlcBlockGroup group = EnsureGroup(software.BlockGroup, spec.GroupPath);
                    bool exists = group.Blocks.Find(spec.Name) != null;

                    if (exists && !options.OverrideExisting)
                    {
                        item.Action = BlockGenerationAction.Skipped;
                        continue;
                    }

                    ImportOptions importOptions = exists
                        ? ImportOptions.Override
                        : ImportOptions.None;

                    IList<PlcBlock> imported = ImportWithRetry(
                        group.Blocks,
                        new FileInfo(filePath),
                        importOptions,
                        options.AllowMissingReferences);

                    item.ImportedCount = imported.Count;
                    item.Action = exists
                        ? BlockGenerationAction.Overridden
                        : BlockGenerationAction.Imported;
                }
                catch (Exception ex)
                {
                    item.Action = BlockGenerationAction.Failed;
                    item.Error = UnwrapMessage(ex);
                    report.Errors.Add(
                        $"{spec.Device}/{(string.IsNullOrWhiteSpace(spec.GroupPath) ? "(root)" : spec.GroupPath)}/{spec.Name}: {item.Error}");
                }
            }

            if (options.Compile)
                Compile(softwareByDevice.Values, report);

            return report;
        }

        private static int OrderFor(string blockType)
        {
            return ImportOrder.TryGetValue(blockType ?? string.Empty, out int order)
                ? order
                : 99;
        }

        private string ResolveSourceFile(string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
                throw new InvalidOperationException("SourceFile és buit.");

            string normalized = sourceFile.Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(
                _docsDir,
                normalized.Replace('/', Path.DirectorySeparatorChar)));

            string root = _docsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SourceFile surt del directori de documents: '{sourceFile}'.");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"No existeix el fitxer SimaticML: '{sourceFile}'.");

            return fullPath;
        }

        private static PlcSoftware? FindPlcSoftware(Device device)
        {
            return TagsExportService.FindSoftwareContainer(device)?.Software as PlcSoftware;
        }

        private static PlcBlockGroup EnsureGroup(PlcBlockGroup root, string groupPath)
        {
            string normalized = (groupPath ?? string.Empty).Trim().Trim('/');
            if (normalized.Length == 0)
                return root;

            PlcBlockGroup current = root;
            foreach (string segment in normalized.Split(
                         new[] { '/' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                PlcBlockUserGroup? child = current.Groups.Find(segment);
                child ??= current.Groups.Create(segment);
                current = child;
            }

            return current;
        }

        private static IList<PlcBlock> ImportWithRetry(
            PlcBlockComposition composition,
            FileInfo file,
            ImportOptions importOptions,
            bool allowMissingReferences)
        {
            try
            {
                return composition.Import(file, importOptions, SWImportOptions.None);
            }
            catch (Exception first)
            {
                var retryOptions = importOptions | ImportOptions.SkipInactiveCultures | ImportOptions.Override;
                var swOptions = allowMissingReferences
                    ? SWImportOptions.IgnoreMissingReferencedObjects
                    : SWImportOptions.None;

                try
                {
                    return composition.Import(file, retryOptions, swOptions);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(
                        $"Import fallit. Primer intent: {UnwrapMessage(first)}. Reintent: {UnwrapMessage(retry)}.",
                        retry);
                }
            }
        }

        private static void Compile(IEnumerable<PlcSoftware> softwareItems, BlocksGenerationReport report)
        {
            foreach (PlcSoftware software in softwareItems.Distinct())
            {
                try
                {
                    ICompilable? compilable = software.GetService<ICompilable>();
                    if (compilable == null)
                    {
                        report.Errors.Add($"No s'ha trobat el servei ICompilable per al PLC '{software.Name}'.");
                        continue;
                    }

                    report.CompileResult = compilable.Compile();
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Error de compilació del PLC '{software.Name}': {UnwrapMessage(ex)}");
                }
            }
        }

        private static string UnwrapMessage(Exception exception)
        {
            string message = exception.Message;
            for (Exception? inner = exception.InnerException; inner != null; inner = inner.InnerException)
                message += " | " + inner.Message;
            return message;
        }
    }
}
