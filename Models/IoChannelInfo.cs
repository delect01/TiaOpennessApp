using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models
{
    public class IoChannelExportReport
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ExtractionTimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string Operation { get; set; } = string.Empty;
        public ProjectMetadata Project { get; set; } = new();
        public int StationCount { get; set; }
        public int ModuleCount { get; set; }
        public int ChannelCount { get; set; }
        public List<IoStationInfo> Stations { get; set; } = new();
    }

    public class IoStationInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<IoRackInfo> Racks { get; set; } = new();
    }

    public class IoRackInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<IoModuleInfo> Modules { get; set; } = new();
    }

    public class IoModuleInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<IoChannelInfo> Channels { get; set; } = new();
    }

    public class IoChannelInfo
    {
        public int Number { get; set; }
        public string Address { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }
}