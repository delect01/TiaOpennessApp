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

        /// <summary>Número de l'IO system (100, 101...). Distingeix IO systems amb el mateix nom.</summary>
        public int? MasterIoSystemNumber { get; set; }
    }
}