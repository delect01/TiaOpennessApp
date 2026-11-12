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

        /// <summary>Approved kit this device was created from (null = not linked to a kit).</summary>
        public string? KitId { get; set; }
    }
}