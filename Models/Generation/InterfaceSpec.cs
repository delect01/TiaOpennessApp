using System;

namespace TiaOpennessApp.Models.Generation
{
    public class InterfaceSpec
    {
        public string Device { get; set; } = string.Empty;
        public string InterfaceItem { get; set; } = string.Empty;
        public string Subnet { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string PnDeviceName { get; set; } = string.Empty;
        public string MasterIoSystem { get; set; } = string.Empty;
    }
}