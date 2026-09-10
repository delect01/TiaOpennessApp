using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models
{
    public class HardwareInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string TypeIdentifier { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public string GsdName { get; set; } = string.Empty;
        public int PositionNumber { get; set; }
        public string Classification { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public bool IsGsd { get; set; }
        public bool IsPlugged { get; set; }
        public string HierarchyPath { get; set; } = string.Empty;
        public List<string> HardwareIdentifiers { get; set; } = new();
        public List<IoAddressInfo> Addresses { get; set; } = new();
        public List<HardwareInfo> Children { get; set; } = new();
    }

    public class IoAddressInfo
    {
        public string IoType { get; set; } = string.Empty;
        public int StartAddress { get; set; }
        public int Length { get; set; }
        public int EndAddress { get; set; }
    }

    public class DeviceSummary
    {
        public string Name { get; set; } = string.Empty;
        public string TypeIdentifier { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsGsd { get; set; }
        public int TotalItems { get; set; }
        public List<HardwareInfo> HardwareTree { get; set; } = new();
    }

    public class HardwareExportReport
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ExtractionTimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string Operation { get; set; } = string.Empty;
        public ProjectMetadata Project { get; set; } = new();
        public int DeviceCount { get; set; }
        public List<DeviceSummary> Devices { get; set; } = new();
    }

    public class ProjectMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Version { get; set; }
        public DateTime? CreationTime { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
