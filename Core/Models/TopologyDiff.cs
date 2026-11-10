using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TiaOpennessApp.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TopologyDiffLevel
    {
        Ok = 0,
        Explained = 1,
        Difference = 2
    }

    public class TopologyDiffItem
    {
        public string Category { get; set; } = string.Empty;

        public TopologyDiffLevel Level { get; set; }

        public string Scope { get; set; } = string.Empty;

        public string Expected { get; set; } = string.Empty;

        public string Actual { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Status =>
            Level == TopologyDiffLevel.Ok ? "OK" :
            Level == TopologyDiffLevel.Difference ? "DIFF" : "EXPLAINED";
    }

    public class TopologyDiffReport
    {
        public int DevicesCompared { get; set; }

        public int ModulesCompared { get; set; }

        public int InterfacesCompared { get; set; }

        public int SubnetsCompared { get; set; }

        public int Differences { get; set; }

        public int Explained { get; set; }

        public int KnownGaps { get; set; }

        public bool Passed => Differences == 0;

        public List<TopologyDiffItem> Items { get; set; } = new List<TopologyDiffItem>();
    }
}