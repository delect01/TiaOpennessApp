using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models
{
    public class DevicesAndNetworksReport
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ExtractionTimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string Operation { get; set; } = string.Empty;
        public ProjectMetadata Project { get; set; } = new();
        public int GroupCount { get; set; }
        public int DeviceCount { get; set; }
        public int SubnetCount { get; set; }
        public List<DeviceGroupInfo> DeviceGroups { get; set; } = new();
        public List<DeviceSummary> UngroupedDevices { get; set; } = new();
        public List<SubnetInfo> Subnets { get; set; } = new();
    }

    public class DeviceGroupInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int SubGroupCount { get; set; }
        public int DeviceCount { get; set; }
        public List<DeviceGroupInfo> SubGroups { get; set; } = new();
        public List<DeviceSummary> Devices { get; set; } = new();
    }

    public class SubnetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string NetType { get; set; } = string.Empty;
        public string TypeIdentifier { get; set; } = string.Empty;
        public int NodeCount { get; set; }
        public List<NodeInfo> Nodes { get; set; } = new();
    }

    public class NodeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string ConnectedSubnet { get; set; } = string.Empty;
        public string HostPath { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
    }
}