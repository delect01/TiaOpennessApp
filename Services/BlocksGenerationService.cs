using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
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

        /// <summary>Motiu informatiu (no és error), p. ex. per què s'ha saltat.</summary>
        public string? Note { get; set; }
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
    /// Importa UDTs (Kind=Type) i blocs SimaticML en un projecte/template
    /// existent que ja tingui el maquinari. Primer els UDTs i després els blocs,
    /// cadascun per passades fins que no hi ha progrés, de manera que l'ordre de
    /// Blocks.csv no importa (UDT→UDT, FB multiinstància, InstanceDB→FB).
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

            var ordered = document.Blocks
                .Select((block, index) => new { Block = block, Index = index })
                .OrderBy(e => IsType(e.Block) ? -2 : IsTagTable(e.Block) ? -1 : OrderFor(e.Block.BlockType))
                .ThenBy(e => e.Index)
                .Select(e => e.Block)
                .ToList();

            // 1) Preparació: resoldre fitxer/PLC/grup i decidir si se salta.
            var pending = new List<PendingImport>();
            foreach (BlockSpec spec in ordered)
            {
                var item = new BlockGenerationItemReport
                {
                    Device = spec.Device,
                    GroupPath = spec.GroupPath,
                    Name = spec.Name,
                    BlockType = spec.BlockType,
                    SourceFile = spec.SourceFile,
                };
                report.Items.Add(item);

                if (IsSafety(spec))
                {
                    // Els blocs F (F_FBD/F_LAD/F_DB) els genera i gestiona TIA amb el
                    // grup de runtime F; no es poden importar com a blocs estàndard.
                    item.Action = BlockGenerationAction.Skipped;
                    item.Note = $"bloc de seguretat ({spec.ProgrammingLanguage}): el gestiona TIA, no s'importa";
                    continue;
                }

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
                    bool preExisting = Exists(software, spec);

                    // Les taules de variables sempre se sobreescriuen: les del template
                    // (només I/O tags generats) són un subconjunt de les de la font.
                    if (preExisting && !options.OverrideExisting && !IsTagTable(spec))
                    {
                        item.Action = BlockGenerationAction.Skipped;
                        item.Note = "ja existeix (sense override)";
                        continue;
                    }

                    pending.Add(new PendingImport(spec, item, software, new FileInfo(filePath), preExisting));
                }
                catch (Exception ex)
                {
                    Fail(report, spec, item, UnwrapMessage(ex));
                }
            }

            // 2) Import per passades: UDTs primer, després blocs. El que falla es
            // reintenta a la passada següent (dependències UDT→UDT, FB→FB
            // multiinstància, InstanceDB→FB). Para quan una passada no avança.
            ImportInPasses(pending.Where(p => IsType(p.Spec)).ToList(), report, options, "UDT");
            ImportInPasses(pending.Where(p => IsTagTable(p.Spec)).ToList(), report, options, "taules de variables");
            ImportInPasses(pending.Where(p => !IsType(p.Spec) && !IsTagTable(p.Spec)).ToList(), report, options, "blocs");

            if (options.Compile)
                Compile(softwareByDevice.Values, report);

            return report;
        }

        private sealed class PendingImport
        {
            public PendingImport(BlockSpec spec, BlockGenerationItemReport item, PlcSoftware software,
                FileInfo file, bool preExisting)
            {
                Spec = spec;
                Item = item;
                Software = software;
                File = file;
                PreExisting = preExisting;
            }

            public BlockSpec Spec { get; }
            public BlockGenerationItemReport Item { get; }
            public PlcSoftware Software { get; }
            public FileInfo File { get; }
            public bool PreExisting { get; }
            public string? LastError { get; set; }
        }

        private void ImportInPasses(
            List<PendingImport> pending,
            BlocksGenerationReport report,
            BlocksGenerationOptions options,
            string label)
        {
            int pass = 0;
            while (pending.Count > 0)
            {
                pass++;
                var failed = new List<PendingImport>();
                foreach (PendingImport p in pending)
                {
                    try
                    {
                        // Si una passada anterior ha deixat l'objecte creat a mitges,
                        // cal sobreescriure'l.
                        bool existsNow = p.PreExisting || Exists(p.Software, p.Spec);
                        ImportOptions importOptions = existsNow ? ImportOptions.Override : ImportOptions.None;

                        int count = IsType(p.Spec)
                            ? ImportTypeWithRetry(EnsureTypeGroup(p.Software.TypeGroup, p.Spec.GroupPath).Types,
                                p.File, importOptions, options.AllowMissingReferences).Count
                            : IsTagTable(p.Spec)
                                ? ImportTagTable(p, importOptions)
                                : ImportWithRetry(TargetBlocks(p.Software, p.Spec.GroupPath),
                                    p.File, importOptions, options.AllowMissingReferences).Count;

                        p.Item.ImportedCount = count;
                        p.Item.Action = p.PreExisting ? BlockGenerationAction.Overridden : BlockGenerationAction.Imported;
                        p.Item.Error = null;
                    }
                    catch (Exception ex)
                    {
                        p.LastError = ShortMessage(ex);
                        failed.Add(p);
                    }
                }

                int succeeded = pending.Count - failed.Count;
                Console.WriteLine($"  Passada {pass} ({label}): {succeeded} importats, {failed.Count} pendents.");
                if (succeeded == 0)
                {
                    foreach (PendingImport p in failed)
                        Fail(report, p.Spec, p.Item, p.LastError ?? "Error desconegut.");
                    return;
                }

                pending = failed;
            }
        }

        private static void Fail(BlocksGenerationReport report, BlockSpec spec, BlockGenerationItemReport item, string error)
        {
            item.Action = BlockGenerationAction.Failed;
            item.Error = error;
            report.Errors.Add(
                $"{spec.Device}/{(string.IsNullOrWhiteSpace(spec.GroupPath) ? "(root)" : spec.GroupPath)}/{spec.Name}: {error}");
        }

        private static bool IsType(BlockSpec spec) =>
            string.Equals(spec.Kind, "Type", StringComparison.OrdinalIgnoreCase);

        private static bool IsTagTable(BlockSpec spec) =>
            string.Equals(spec.Kind, "TagTable", StringComparison.OrdinalIgnoreCase);

        /// <summary>Blocs de seguretat (F_FBD, F_LAD, F_DB, F_STL...).</summary>
        public static bool IsSafety(BlockSpec spec) =>
            (spec.ProgrammingLanguage ?? string.Empty).StartsWith("F_", StringComparison.OrdinalIgnoreCase);

        private static bool Exists(PlcSoftware software, BlockSpec spec)
        {
            if (IsType(spec))
            {
                PlcTypeGroup? typeGroup = FindTypeGroup(software.TypeGroup, spec.GroupPath);
                return typeGroup?.Types.Find(spec.Name) != null;
            }

            // El nom d'una taula de variables és únic a tot el PLC (sigui quin
            // sigui el grup), així que es busca a tot l'arbre.
            if (IsTagTable(spec))
                return FindTagTableOwner(software.TagTableGroup, spec.Name) != null;

            if (BlocksExportService.IsSystemGroupPath(spec.GroupPath))
            {
                // DB de sistema: pot ser al grup de sistema o (si s'hi va importar) a l'arrel.
                PlcSystemBlockGroup? systemGroup = BlocksExportService.FindSystemGroup(software.BlockGroup, spec.GroupPath);
                return systemGroup?.Blocks.Find(spec.Name) != null
                    || software.BlockGroup.Blocks.Find(spec.Name) != null;
            }

            PlcBlockGroup? group = FindBlockGroup(software.BlockGroup, spec.GroupPath);
            return group?.Blocks.Find(spec.Name) != null;
        }

        /// <summary>
        /// Composició on s'importa el bloc. Per als DB de sistema ("[System]/...")
        /// és el grup de sistema si ja existeix al template; si no, l'arrel de
        /// "Program blocks" (TIA no deixa crear grups de sistema).
        /// </summary>
        private static PlcBlockComposition TargetBlocks(PlcSoftware software, string? groupPath)
        {
            if (BlocksExportService.IsSystemGroupPath(groupPath))
            {
                PlcSystemBlockGroup? systemGroup = BlocksExportService.FindSystemGroup(software.BlockGroup, groupPath ?? string.Empty);
                return systemGroup?.Blocks ?? software.BlockGroup.Blocks;
            }

            return EnsureGroup(software.BlockGroup, groupPath ?? string.Empty).Blocks;
        }

        private static PlcBlockGroup? FindBlockGroup(PlcBlockGroup root, string groupPath)
        {
            PlcBlockGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcBlockUserGroup? child = current.Groups.Find(segment);
                if (child == null)
                    return null;
                current = child;
            }
            return current;
        }

        private static PlcTypeGroup? FindTypeGroup(PlcTypeGroup root, string groupPath)
        {
            PlcTypeGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcTypeUserGroup? child = current.Groups.Find(segment);
                if (child == null)
                    return null;
                current = child;
            }
            return current;
        }

        private static PlcTypeGroup EnsureTypeGroup(PlcTypeGroup root, string groupPath)
        {
            PlcTypeGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcTypeUserGroup? child = current.Groups.Find(segment);
                child ??= current.Groups.Create(segment);
                current = child;
            }
            return current;
        }

        /// <summary>
        /// Importa una taula de variables. Si ja n'hi ha una amb el mateix nom en
        /// qualsevol grup, se sobreescriu allà mateix (TIA no permet dues taules
        /// amb el mateix nom). Si no, es crea al GroupPath de la font.
        /// </summary>
        private static int ImportTagTable(PendingImport p, ImportOptions importOptions)
        {
            PlcTagTableGroup root = p.Software.TagTableGroup;
            PlcTagTableGroup? owner = FindTagTableOwner(root, p.Spec.Name);
            if (owner != null)
                return ImportTagTableWithRetry(owner.TagTables, p.File, importOptions | ImportOptions.Override);

            return ImportTagTableWithRetry(EnsureTagTableGroup(root, p.Spec.GroupPath).TagTables, p.File, importOptions);
        }

        private static PlcTagTableGroup? FindTagTableOwner(PlcTagTableGroup group, string name)
        {
            if (group.TagTables.Find(name) != null)
                return group;
            foreach (PlcTagTableUserGroup child in group.Groups)
            {
                PlcTagTableGroup? found = FindTagTableOwner(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Resol/crea el grup de taules. Els noms de grup poden contenir '/'
        /// (p. ex. "E/S"), així que a cada nivell es prova primer el prefix més
        /// llarg de segments que coincideixi amb un grup existent. Si no n'hi ha
        /// cap, es crea un sol grup amb tota la resta del camí com a nom.
        /// </summary>
        private static PlcTagTableGroup EnsureTagTableGroup(PlcTagTableGroup root, string groupPath)
        {
            PlcTagTableGroup current = root;
            string[] segments = SplitGroup(groupPath);
            int i = 0;
            while (i < segments.Length)
            {
                PlcTagTableUserGroup? child = null;
                int used = 0;
                for (int len = segments.Length - i; len >= 1 && child == null; len--)
                {
                    child = current.Groups.Find(string.Join("/", segments, i, len));
                    if (child != null)
                        used = len;
                }

                if (child == null)
                {
                    used = segments.Length - i;
                    try
                    {
                        child = current.Groups.Create(string.Join("/", segments, i, used));
                    }
                    catch (Exception) when (used > 1)
                    {
                        // Nom amb '/' no acceptat: crea els grups niats un a un.
                        used = 1;
                        child = current.Groups.Create(segments[i]);
                    }
                }

                current = child;
                i += used;
            }
            return current;
        }

        private static int ImportTagTableWithRetry(
            PlcTagTableComposition composition,
            FileInfo file,
            ImportOptions importOptions)
        {
            try
            {
                composition.Import(file, importOptions);
            }
            catch (Exception first)
            {
                try
                {
                    composition.Import(file, importOptions | ImportOptions.SkipInactiveCultures);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(CombineErrors(first, retry));
                }
            }
            return 1;
        }

        private static string[] SplitGroup(string? groupPath) =>
            (groupPath ?? string.Empty).Trim().Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        private static IList<PlcType> ImportTypeWithRetry(
            PlcTypeComposition composition,
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
                var retryOptions = importOptions | ImportOptions.SkipInactiveCultures;
                var swOptions = allowMissingReferences
                    ? SWImportOptions.IgnoreMissingReferencedObjects
                    : SWImportOptions.None;
                try
                {
                    return composition.Import(file, retryOptions, swOptions);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(CombineErrors(first, retry));
                }
            }
        }

        private static string CombineErrors(Exception first, Exception retry)
        {
            string a = ShortMessage(first);
            string b = ShortMessage(retry);
            return string.Equals(a, b, StringComparison.Ordinal) ? a : $"{a} | Reintent: {b}";
        }

        /// <summary>Darrer missatge útil de la cadena d'excepcions (TIA repeteix els embolcalls).</summary>
        private static string ShortMessage(Exception exception)
        {
            var parts = new List<string>();
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                foreach (string line in e.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("Error when calling method", StringComparison.Ordinal))
                        continue;
                    if (!parts.Contains(t))
                        parts.Add(t);
                }
            }
            return parts.Count == 0 ? exception.Message.Trim() : string.Join(" ", parts);
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
                    throw new InvalidOperationException(CombineErrors(first, retry));
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
