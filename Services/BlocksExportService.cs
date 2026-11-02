using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
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
            block.Export(path, ExportOptions.WithDefaults);
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

            foreach (BlockInventoryItem item in inspection.Blocks)
            {
                string label = FormatItem(item);
                if (item.Error != null)
                {
                    report.Errors.Add($"{label}: {item.Error}");
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

            return report;
        }

        private void ExportBlock(BlockInventoryItem item, FileInfo path)
        {
            Device? device = FindDevice(item.DeviceName)
                ?? throw new InvalidOperationException($"Device '{item.DeviceName}' not found.");

            PlcSoftware? software = FindPlcSoftware(device)
                ?? throw new InvalidOperationException($"Device '{item.DeviceName}' has no PLC software.");

            PlcBlockGroup? group = FindGroup(software.BlockGroup, item.GroupPath);
            if (group == null)
                throw new InvalidOperationException($"Block group '{item.GroupPath}' not found.");

            PlcBlock? block = group.Blocks.Find(item.Name);
            if (block == null)
                throw new InvalidOperationException($"Block '{item.Name}' not found in group '{item.GroupPath}'.");

            path.Directory?.Create();
            block.Export(path, ExportOptions.WithDefaults);
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

        public string? GetCpuTypeIdentifier(string deviceName)
        {
            Device? device = FindDevice(deviceName);
            if (device == null)
                return null;

            return TagsExportService.FindSoftwareContainer(device)?.OwnedBy?.TypeIdentifier;
        }

        public string? GetCpuName(string deviceName)
        {
            Device? device = FindDevice(deviceName);
            if (device == null)
                return null;

            return TagsExportService.FindSoftwareContainer(device)?.OwnedBy?.Name;
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
            var segments = new List<string>
            {
                "blocks",
                SanitizePathSegment(item.DeviceName),
            };

            foreach (string segment in (item.GroupPath ?? string.Empty)
                         .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                segments.Add(SanitizePathSegment(segment));
            }

            string directory = string.Join("/", segments);
            string baseName = SanitizePathSegment(item.Name);
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
