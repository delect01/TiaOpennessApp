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
    /// <summary>
    /// Read-only inventory and single-block export used by the Phase 0 blocks PoC.
    /// The full document/generation pipeline will be added in later phases.
    /// </summary>
    public class BlockInventoryItem
    {
        public string DeviceName { get; set; } = string.Empty;
        public string GroupPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public int Number { get; set; }
        public bool AutoNumber { get; set; }
        public string BlockType { get; set; } = string.Empty;
        public string ProgrammingLanguage { get; set; } = string.Empty;
        public string MemoryLayout { get; set; } = string.Empty;
        public bool IsKnowHowProtected { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public class BlockInspectionResult
    {
        public List<BlockInventoryItem> Blocks { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public class BlockDocsExportReport
    {
        public int Exported { get; set; }
        public int Skipped { get; set; }
        public List<BlockSpec> Blocks { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public class BlocksExportService
    {
        private readonly Project _project;

        public BlocksExportService(Project project)
        {
            _project = project;
        }

        public BlocksExportService(TiaConnection connection)
        {
            _project = connection.Project;
        }

        public BlockInspectionResult Inspect(string? deviceFilter = null)
        {
            var result = new BlockInspectionResult();
            int matchedDevices = 0;

            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                if (!string.IsNullOrWhiteSpace(deviceFilter)
                    && device.Name.IndexOf(deviceFilter!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                matchedDevices++;
                try
                {
                    PlcSoftware? software = FindPlcSoftware(device);
                    if (software == null)
                        continue;

                    WalkGroup(software.BlockGroup, string.Empty, device.Name, result.Blocks.Add);
                    WalkSystemGroups(software.BlockGroup.SystemBlockGroups, SystemGroupPrefix, device.Name, result.Blocks.Add);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{device.Name}: {ex.Message}");
                }
            }

            if (matchedDevices == 0)
                result.Errors.Add($"No device matches filter '{deviceFilter}'.");

            return result;
        }

        public void Dump(string? deviceFilter = null)
        {
            BlockInspectionResult result = Inspect(deviceFilter);

            foreach (IGrouping<string, BlockInventoryItem> deviceGroup in
                     result.Blocks.GroupBy(b => b.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"DEVICE: {deviceGroup.Key}");
                foreach (BlockInventoryItem block in deviceGroup)
                    Console.WriteLine(Format(block));
            }

            foreach (string error in result.Errors)
                Console.WriteLine($"  ERROR: {error}");

            Console.WriteLine($"Blocks: {result.Blocks.Count}  Error(s): {result.Errors.Count}");
        }

        public BlockInventoryItem ExportBlock(string deviceName, string blockName, FileInfo path)
        {
            Device? device = FindDevice(deviceName)
                ?? throw new InvalidOperationException($"Device '{deviceName}' not found.");

            PlcSoftware? software = FindPlcSoftware(device)
                ?? throw new InvalidOperationException($"Device '{deviceName}' has no PLC software.");

            (PlcBlock? block, string groupPath) = FindBlock(software.BlockGroup, blockName, string.Empty);
            if (block == null)
                throw new InvalidOperationException($"Block '{blockName}' not found in device '{deviceName}'.");

            path.Directory?.Create();
            ExportCompilingIfInconsistent(block, path);
            return CreateItem(device.Name, groupPath, block);
        }

        /// <summary>
        /// Exporta tots els blocs d'usuari a SimaticML i retorna el manifest que
        /// s'ha d'escriure a <c>Blocks.csv</c>. Els blocs protegits o que TIA no
        /// permet exportar es reporten i s'ometen; la resta continua.
        /// </summary>
        public BlockDocsExportReport ExportDocs(string docsDir, string? deviceFilter = null)
        {
            var report = new BlockDocsExportReport();
            BlockInspectionResult inspection = Inspect(deviceFilter);
            report.Errors.AddRange(inspection.Errors);

            var usedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Primer els tipus de dades PLC (UDT): els blocs en depenen i s'han
            // d'importar abans. Es registren a Blocks.csv amb Kind=Type.
            ExportTypes(docsDir, deviceFilter, report, usedRelativePaths);

            // Després les taules de variables PLC (tots els tags + constants d'usuari):
            // el codi fa servir tags de memòria/constants que no són a IoTags.csv.
            ExportTagTables(docsDir, deviceFilter, report, usedRelativePaths);

            int protectedSystemBlocks = 0;
            foreach (BlockInventoryItem item in inspection.Blocks)
            {
                // Blocs de sistema: només els DB (instància única de TON, R_TRIG...).
                // Els FB/FC de sistema els afegeix TIA en compilar.
                if (IsSystemGroupPath(item.GroupPath) && !IsExportableSystemBlock(item.BlockType))
                    continue;

                // De "STEP 7 Safety" només els DB d'instància (ESTOP1_x, ACK_GL_DB...):
                // la resta de blocs F de sistema els regenera TIA en compilar.
                if (IsSystemGroupPath(item.GroupPath)
                    && item.GroupPath.IndexOf("Safety", StringComparison.OrdinalIgnoreCase) >= 0
                    && !string.Equals(item.BlockType, "InstanceDB", StringComparison.OrdinalIgnoreCase))
                    continue;

                string label = FormatItem(item);
                if (item.Error != null)
                {
                    report.Errors.Add($"{label}: {item.Error}");
                    continue;
                }

                if (item.IsKnowHowProtected && IsSystemGroupPath(item.GroupPath))
                {
                    // Blocs F de sistema (DB d'ESTOP1/ACK_GL, Compiler blocks, F-I/O DBs):
                    // TIA els protegeix i no es poden exportar; només es compten.
                    report.Skipped++;
                    protectedSystemBlocks++;
                    continue;
                }

                if (item.IsKnowHowProtected)
                {
                    report.Skipped++;
                    report.Errors.Add($"{label}: bloc protegit (know-how protected); no s'exporta.");
                    continue;
                }

                string sourceFile = BuildSourceFile(item, usedRelativePaths);
                string fullPath = Path.Combine(
                    docsDir,
                    sourceFile.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    ExportBlock(item, new FileInfo(fullPath));
                    report.Blocks.Add(new BlockSpec
                    {
                        Device = item.DeviceName,
                        GroupPath = item.GroupPath,
                        Kind = "Block",
                        Name = item.Name,
                        Namespace = item.Namespace,
                        Number = item.Number,
                        AutoNumber = item.AutoNumber,
                        BlockType = item.BlockType,
                        ProgrammingLanguage = item.ProgrammingLanguage,
                        MemoryLayout = item.MemoryLayout,
                        Title = item.Title,
                        Comment = item.Comment,
                        SourceFile = sourceFile,
                    });
                    report.Exported++;
                }
                catch (Exception ex)
                {
                    try { File.Delete(fullPath); } catch { }
                    report.Errors.Add($"{label}: {ex.Message}");
                }
            }

            if (protectedSystemBlocks > 0)
                report.Errors.Add($"{protectedSystemBlocks} bloc(s) F de sistema protegits (STEP 7 Safety) no exportats: " +
                                  "TIA no en permet l'exportació. Els DB d'instància ESTOP1/ACK_GL/FDBACK es recreen en generar.");
            return report;
        }

        private void ExportTypes(
            string docsDir,
            string? deviceFilter,
            BlockDocsExportReport report,
            HashSet<string> usedRelativePaths)
        {
            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                if (!string.IsNullOrWhiteSpace(deviceFilter)
                    && device.Name.IndexOf(deviceFilter!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                PlcSoftware? software;
                try { software = FindPlcSoftware(device); }
                catch { continue; }
                if (software == null)
                    continue;

                try
                {
                    ExportTypeGroup(software.TypeGroup, string.Empty, device.Name, docsDir, report, usedRelativePaths);
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{device.Name} (UDT): {ex.Message}");
                }
            }
        }

        private static void ExportTypeGroup(
            PlcTypeGroup group,
            string groupPath,
            string deviceName,
            string docsDir,
            BlockDocsExportReport report,
            HashSet<string> usedRelativePaths)
        {
            foreach (PlcType type in group.Types)
            {
                string name = string.Empty;
                try { name = type.Name; } catch { }
                string label = $"{deviceName}/{(groupPath.Length == 0 ? "(root)" : groupPath)}/{name} (UDT)";

                try
                {
                    if (type.IsKnowHowProtected)
                    {
                        report.Skipped++;
                        report.Errors.Add($"{label}: tipus protegit (know-how protected); no s'exporta.");
                        continue;
                    }

                    string sourceFile = BuildSourceFile("types", deviceName, groupPath, name, usedRelativePaths);
                    string fullPath = Path.Combine(docsDir, sourceFile.Replace('/', Path.DirectorySeparatorChar));
                    var file = new FileInfo(fullPath);
                    file.Directory?.Create();
                    if (file.Exists)
                        file.Delete();
                    ExportCompilingIfInconsistent(type, file);

                    report.Blocks.Add(new BlockSpec
                    {
                        Device = deviceName,
                        GroupPath = groupPath,
                        Kind = "Type",
                        Name = name,
                        Namespace = type.Namespace ?? string.Empty,
                        BlockType = "UDT",
                        Title = GetText(type.Title),
                        Comment = GetText(type.Comment),
                        SourceFile = sourceFile,
                    });
                    report.Exported++;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{label}: {ex.Message}");
                }
            }

            foreach (PlcTypeUserGroup child in group.Groups)
            {
                string childPath = groupPath.Length == 0 ? child.Name : groupPath + "/" + child.Name;
                ExportTypeGroup(child, childPath, deviceName, docsDir, report, usedRelativePaths);
            }
        }

        private void ExportTagTables(
            string docsDir,
            string? deviceFilter,
            BlockDocsExportReport report,
            HashSet<string> usedRelativePaths)
        {
            foreach (Device device in DevicesAndNetworksService.EnumerateAllDevices(_project))
            {
                if (!string.IsNullOrWhiteSpace(deviceFilter)
                    && device.Name.IndexOf(deviceFilter!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                PlcSoftware? software;
                try { software = FindPlcSoftware(device); }
                catch { continue; }
                if (software == null)
                    continue;

                try
                {
                    ExportTagTableGroup(software.TagTableGroup, string.Empty, device.Name, docsDir, report, usedRelativePaths);
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{device.Name} (TagTable): {ex.Message}");
                }
            }
        }

        private static void ExportTagTableGroup(
            PlcTagTableGroup group,
            string groupPath,
            string deviceName,
            string docsDir,
            BlockDocsExportReport report,
            HashSet<string> usedRelativePaths)
        {
            foreach (PlcTagTable table in group.TagTables)
            {
                string name = string.Empty;
                try { name = table.Name; } catch { }
                string label = $"{deviceName}/{(groupPath.Length == 0 ? "(root)" : groupPath)}/{name} (TagTable)";

                try
                {
                    string sourceFile = BuildSourceFile("tags", deviceName, groupPath, name, usedRelativePaths);
                    string fullPath = Path.Combine(docsDir, sourceFile.Replace('/', Path.DirectorySeparatorChar));
                    var file = new FileInfo(fullPath);
                    file.Directory?.Create();
                    if (file.Exists)
                        file.Delete();
                    table.Export(file, ExportOptions.WithDefaults);

                    report.Blocks.Add(new BlockSpec
                    {
                        Device = deviceName,
                        GroupPath = groupPath,
                        Kind = "TagTable",
                        Name = name,
                        BlockType = "TagTable",
                        Title = table.IsDefault ? "(default)" : string.Empty,
                        Comment = $"{table.Tags.Count} tags, {table.UserConstants.Count} constants",
                        SourceFile = sourceFile,
                    });
                    report.Exported++;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{label}: {ex.Message}");
                }
            }

            foreach (PlcTagTableUserGroup child in group.Groups)
            {
                string childPath = groupPath.Length == 0 ? child.Name : groupPath + "/" + child.Name;
                ExportTagTableGroup(child, childPath, deviceName, docsDir, report, usedRelativePaths);
            }
        }

        private void ExportBlock(BlockInventoryItem item, FileInfo path)
        {
            Device? device = FindDevice(item.DeviceName)
                ?? throw new InvalidOperationException($"Device '{item.DeviceName}' not found.");

            PlcSoftware? software = FindPlcSoftware(device)
                ?? throw new InvalidOperationException($"Device '{item.DeviceName}' has no PLC software.");

            PlcBlockComposition blocks;
            if (IsSystemGroupPath(item.GroupPath))
            {
                PlcSystemBlockGroup? systemGroup = FindSystemGroup(software.BlockGroup, item.GroupPath)
                    ?? throw new InvalidOperationException($"System block group '{item.GroupPath}' not found.");
                blocks = systemGroup.Blocks;
            }
            else
            {
                PlcBlockGroup? group = FindGroup(software.BlockGroup, item.GroupPath)
                    ?? throw new InvalidOperationException($"Block group '{item.GroupPath}' not found.");
                blocks = group.Blocks;
            }

            PlcBlock? block = blocks.Find(item.Name);
            if (block == null)
                throw new InvalidOperationException($"Block '{item.Name}' not found in group '{item.GroupPath}'.");

            path.Directory?.Create();
            if (path.Exists)
                path.Delete();
            ExportCompilingIfInconsistent(block, path);
        }

        /// <summary>
        /// TIA no exporta blocs/UDT inconsistents. Si ho són, es compilen (només
        /// aquell objecte) i es torna a provar; si la compilació no ho resol,
        /// l'error indica quants errors de compilació té.
        /// </summary>
        private static void ExportCompilingIfInconsistent(PlcBlock block, FileInfo path) =>
            ExportCompilingIfInconsistent(block, () => block.IsConsistent, () => block.Export(path, ExportOptions.WithDefaults));

        private static void ExportCompilingIfInconsistent(PlcType type, FileInfo path) =>
            ExportCompilingIfInconsistent(type, () => type.IsConsistent, () => type.Export(path, ExportOptions.WithDefaults));

        private static void ExportCompilingIfInconsistent(
            IEngineeringServiceProvider target, Func<bool> isConsistent, Action export)
        {
            bool consistent;
            try { consistent = isConsistent(); } catch { consistent = true; }

            if (consistent)
            {
                try
                {
                    export();
                    return;
                }
                catch (Exception ex) when (ex.Message.IndexOf("nconsistent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw;
                }
                catch
                {
                    // Inconsistent tot i que IsConsistent deia que no: compila i reintenta.
                }
            }

            ICompilable? compilable = target.GetService<ICompilable>();
            if (compilable == null)
                throw new InvalidOperationException("Inconsistent i sense servei de compilació; compila'l a TIA i torna a exportar.");

            CompilerResult result = compilable.Compile();
            try
            {
                export();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Inconsistent; s'ha compilat (state={result.State}, errors={result.ErrorCount}) però no s'ha pogut exportar: {ex.Message}");
            }
        }

        public Device? FindDevice(string deviceName)
        {
            return DevicesAndNetworksService.FindDeviceByName(_project, deviceName);
        }

        public PlcSoftware? FindPlcSoftware(string deviceName)
        {
            Device? device = FindDevice(deviceName);
            return device == null ? null : FindPlcSoftware(device);
        }

        private static PlcSoftware? FindPlcSoftware(Device device)
        {
            return TagsExportService.FindSoftwareContainer(device)?.Software as PlcSoftware;
        }

        private static void WalkGroup(
            PlcBlockGroup group,
            string parentPath,
            string deviceName,
            Action<BlockInventoryItem> add)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                try
                {
                    add(CreateItem(deviceName, parentPath, block));
                }
                catch (Exception ex)
                {
                    string blockName = string.Empty;
                    try { blockName = block.Name; } catch { }

                    add(new BlockInventoryItem
                    {
                        DeviceName = deviceName,
                        GroupPath = parentPath,
                        Name = blockName,
                        Error = ex.Message,
                    });
                }
            }

            foreach (PlcBlockUserGroup child in group.Groups)
            {
                string childPath = string.IsNullOrEmpty(parentPath)
                    ? child.Name
                    : parentPath + "/" + child.Name;

                WalkGroup(child, childPath, deviceName, add);
            }
        }

        private static (PlcBlock? Block, string GroupPath) FindBlock(
            PlcBlockGroup group,
            string name,
            string groupPath)
        {
            PlcBlock? block = group.Blocks.Find(name);
            if (block != null)
                return (block, groupPath);

            foreach (PlcBlockUserGroup child in group.Groups)
            {
                string childPath = string.IsNullOrEmpty(groupPath)
                    ? child.Name
                    : groupPath + "/" + child.Name;

                (PlcBlock? found, string foundPath) = FindBlock(child, name, childPath);
                if (found != null)
                    return (found, foundPath);
            }

            return (null, groupPath);
        }

        /// <summary>
        /// Prefix de GroupPath per als blocs de "System blocks" (p. ex.
        /// "[System]/Program resources"): DBs d'instància única de TON/TOF/R_TRIG...
        /// que TIA crea automàticament i que el codi referencia pel nom.
        /// </summary>
        public const string SystemGroupPrefix = "[System]";

        public static bool IsSystemGroupPath(string? groupPath) =>
            (groupPath ?? string.Empty).TrimStart('/').StartsWith(SystemGroupPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Dels blocs de sistema només s'exporten els DB; la resta els crea TIA.</summary>
        public static bool IsExportableSystemBlock(string? blockType) =>
            string.Equals(blockType, "InstanceDB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(blockType, "GlobalDB", StringComparison.OrdinalIgnoreCase);

        private static string SafeName(PlcSystemBlockGroup group)
        {
            try { return group.Name; } catch { return "?"; }
        }

        private static void WalkSystemGroups(
            PlcSystemBlockGroupComposition groups,
            string parentPath,
            string deviceName,
            Action<BlockInventoryItem> add)
        {
            List<PlcSystemBlockGroup> list;
            try { list = groups.ToList(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  (avís) {deviceName} {parentPath}: no es poden llistar els subgrups: {ex.Message}");
                return;
            }

            foreach (PlcSystemBlockGroup group in list)
            {
                string path = parentPath + "/" + SafeName(group);
                List<PlcBlock> blocks;
                try { blocks = group.Blocks.ToList(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) {deviceName} {path}: no es poden llistar els blocs: {ex.Message}");
                    blocks = new List<PlcBlock>();
                }

                foreach (PlcBlock block in blocks)
                {
                    try
                    {
                        add(CreateItem(deviceName, path, block));
                    }
                    catch (Exception ex)
                    {
                        string blockName = string.Empty;
                        try { blockName = block.Name; } catch { }
                        add(new BlockInventoryItem { DeviceName = deviceName, GroupPath = path, Name = blockName, Error = ex.Message });
                    }
                }

                try { WalkSystemGroups(group.Groups, path, deviceName, add); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  (avís) {deviceName} {path}: {ex.Message}");
                }
            }
        }

        /// <summary>Busca un grup de sistema per camí "[System]/A/B".</summary>
        internal static PlcSystemBlockGroup? FindSystemGroup(PlcBlockSystemGroup root, string groupPath)
        {
            string[] segments = (groupPath ?? string.Empty).Trim('/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .ToArray();
            if (segments.Length == 0)
                return null;

            PlcSystemBlockGroupComposition groups = root.SystemBlockGroups;
            PlcSystemBlockGroup? current = null;
            foreach (string segment in segments)
            {
                current = groups.FirstOrDefault(g => string.Equals(g.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                    return null;
                groups = current.Groups;
            }
            return current;
        }

        private static PlcBlockGroup? FindGroup(PlcBlockGroup root, string groupPath)
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
                if (child == null)
                    return null;
                current = child;
            }

            return current;
        }

        private static string BuildSourceFile(
            BlockInventoryItem item,
            HashSet<string> usedRelativePaths)
        {
            return BuildSourceFile("blocks", item.DeviceName, item.GroupPath, item.Name, usedRelativePaths);
        }

        private static string BuildSourceFile(
            string rootFolder,
            string deviceName,
            string? groupPath,
            string name,
            HashSet<string> usedRelativePaths)
        {
            var segments = new List<string>
            {
                rootFolder,
                SanitizePathSegment(deviceName),
            };

            foreach (string segment in (groupPath ?? string.Empty)
                         .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                segments.Add(SanitizePathSegment(segment));
            }

            string directory = string.Join("/", segments);
            string baseName = SanitizePathSegment(name);
            string relative = $"{directory}/{baseName}.xml";
            int suffix = 2;

            while (!usedRelativePaths.Add(relative))
                relative = $"{directory}/{baseName}_{suffix++}.xml";

            return relative;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                builder.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);

            string result = builder.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(result) ? "unnamed" : result;
        }

        private static string FormatItem(BlockInventoryItem item)
        {
            string group = string.IsNullOrEmpty(item.GroupPath) ? "(root)" : item.GroupPath;
            return $"{item.DeviceName}/{group}/{item.Name}";
        }

        private static BlockInventoryItem CreateItem(string deviceName, string groupPath, PlcBlock block)
        {
            return new BlockInventoryItem
            {
                DeviceName = deviceName,
                GroupPath = groupPath,
                Name = block.Name,
                Namespace = block.Namespace ?? string.Empty,
                Number = block.Number,
                AutoNumber = block.AutoNumber,
                BlockType = GetBlockType(block),
                ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
                MemoryLayout = block.MemoryLayout.ToString(),
                IsKnowHowProtected = block.IsKnowHowProtected,
                Title = GetText(block.Title),
                Comment = GetText(block.Comment),
            };
        }

        private static string GetBlockType(PlcBlock block)
        {
            if (block is OB) return "OB";
            if (block is FB) return "FB";
            if (block is FC) return "FC";
            if (block is GlobalDB) return "GlobalDB";
            if (block is InstanceDB) return "InstanceDB";
            if (block is CodeBlock) return "CodeBlock";
            if (block is DataBlock) return "DataBlock";
            return block.GetType().Name;
        }

        private static string GetText(MultilingualText? text)
        {
            if (text == null)
                return string.Empty;

            foreach (MultilingualTextItem item in text.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    return OneLine(item.Text.Trim());
            }

            return string.Empty;
        }

        private static string OneLine(string value)
        {
            return value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }

        private static string Format(BlockInventoryItem block)
        {
            string group = string.IsNullOrEmpty(block.GroupPath) ? "(root)" : block.GroupPath;
            string title = string.IsNullOrEmpty(block.Title) ? string.Empty : $" Title=\"{block.Title}\"";
            string comment = string.IsNullOrEmpty(block.Comment) ? string.Empty : $" Comment=\"{block.Comment}\"";
            string error = block.Error == null ? string.Empty : $" ERROR={block.Error}";

            return $"  [{group}] {block.Name} | {block.BlockType} | {block.ProgrammingLanguage} | " +
                   $"No={block.Number} | Auto={block.AutoNumber} | Mem={block.MemoryLayout} | " +
                   $"Protected={block.IsKnowHowProtected}{title}{comment}{error}";
        }
    }
}
