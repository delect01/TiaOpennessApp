using System;

namespace TiaOpennessApp.Models.Generation
{
    public class DeviceSpec
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string TypeKey { get; set; } = string.Empty;
        public string? Firmware { get; set; }
        public string? Comment { get; set; }
    }
}