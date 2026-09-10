using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    public class BlockService
    {
        private readonly TiaConnection _connection;

        public BlockService(TiaConnection connection)
        {
            _connection = connection;
        }

        public BlockExportReport ExportAllBlocks()
        {
            var project = _connection.Project;
            var allBlocks = new List<BlockInfo>();

            foreach (Device device in project.Devices)
            {
                foreach (DeviceItem item in device.DeviceItems)
                {
                    var plcSoftware = GetPlcSoftware(item);
                    if (plcSoftware == null) continue;

                    string parentPath = device.Name + "/" + item.Name;
                    foreach (PlcBlock block in plcSoftware.BlockGroup.Blocks)
                    {
                        allBlocks.Add(BuildBlockInfo(block, parentPath));
                    }
                }
            }

            return new BlockExportReport
            {
                Operation = "Export of all PLC blocks from TIA Portal V21 project",
                Project = new ProjectMetadata
                {
                    Name = project.Name,
                    Path = project.Path.FullName,
                    Version = project.Version,
                    CreationTime = project.CreationTime,
                    LastModified = project.LastModified,
                },
                BlockCount = allBlocks.Count,
                Blocks = allBlocks,
            };
        }

        public void PrintBlockList()
        {
            var project = _connection.Project;
            int blockCount = 0;

            foreach (Device device in project.Devices)
            {
                Console.WriteLine($"Device: {device.Name}");

                foreach (DeviceItem item in device.DeviceItems)
                {
                    var plcSoftware = GetPlcSoftware(item);
                    if (plcSoftware == null) continue;

                    Console.WriteLine($"  └── {item.Name}");

                    foreach (PlcBlock block in plcSoftware.BlockGroup.Blocks)
                    {
                        blockCount++;
                        string lang = block.ProgrammingLanguage.ToString();
                        Console.WriteLine($"      [{block.Number}] {block.Name} ({lang})");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine($"Total blocks: {blockCount}");
        }

        public void ExportBlockToFile(PlcBlock block, string outputPath)
        {
            block.Export(new FileInfo(outputPath), ExportOptions.WithDefaults);
        }

        public void ExportAllBlocksToDirectory(string outputDir)
        {
            var project = _connection.Project;

            foreach (Device device in project.Devices)
            {
                string deviceDir = System.IO.Path.Combine(outputDir, SanitizeFileName(device.Name));
                System.IO.Directory.CreateDirectory(deviceDir);

                foreach (DeviceItem item in device.DeviceItems)
                {
                    var plcSoftware = GetPlcSoftware(item);
                    if (plcSoftware == null) continue;

                    string itemDir = System.IO.Path.Combine(deviceDir, SanitizeFileName(item.Name));
                    System.IO.Directory.CreateDirectory(itemDir);

                    foreach (PlcBlock block in plcSoftware.BlockGroup.Blocks)
                    {
                        try
                        {
                            string fileName = $"{block.Number}_{SanitizeFileName(block.Name)}.xml";
                            string filePath = System.IO.Path.Combine(itemDir, fileName);
                            block.Export(new FileInfo(filePath), ExportOptions.WithDefaults);
                            Console.WriteLine($"  Exported: {device.Name}/{item.Name}/{block.Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  FAILED: {block.Name} - {ex.Message}");
                        }
                    }
                }
            }
        }

        private static PlcSoftware? GetPlcSoftware(DeviceItem item)
        {
            try
            {
                var softwareContainer = ((IEngineeringServiceProvider)item).GetService<SoftwareContainer>();
                if (softwareContainer == null) return null;
                return softwareContainer.Software as PlcSoftware;
            }
            catch
            {
                return null;
            }
        }

        private static BlockInfo BuildBlockInfo(PlcBlock block, string parentPath)
        {
            return new BlockInfo
            {
                Name = block.Name,
                BlockName = block.Name,
                TypeName = block.GetType().Name,
                Number = block.Number,
                ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
                Size = block.WorkMemoryLength,
                HierarchyPath = parentPath + "/" + block.Name,
            };
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}