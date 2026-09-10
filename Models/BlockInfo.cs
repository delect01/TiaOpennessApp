using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models
{
    public class BlockInfo
    {
        public string Name { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public int Number { get; set; }
        public string ProgrammingLanguage { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long Size { get; set; }
        public string HierarchyPath { get; set; } = string.Empty;
        public List<BlockInfo> Children { get; set; } = new();
    }

    public class BlockExportReport
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ExtractionTimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string Operation { get; set; } = string.Empty;
        public ProjectMetadata Project { get; set; } = new();
        public int BlockCount { get; set; }
        public List<BlockInfo> Blocks { get; set; } = new();
    }
}
