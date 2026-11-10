using System.Collections.Generic;

namespace TiaOpennessApp.Models
{
    public class NetworkDeviceInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string StationType { get; set; } = string.Empty;
        public string DeviceOrderNumber { get; set; } = string.Empty;
        public List<NetworkInterfaceInfo> Interfaces { get; set; } = new List<NetworkInterfaceInfo>();
        public List<NetworkModuleInfo> Modules { get; set; } = new List<NetworkModuleInfo>();

        /// <summary>
        /// Diagnòstic: elements sense OrderNumber ("Nom [TypeIdentifier]"), que no
        /// entren a <see cref="Modules"/>. No es serialitza.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<string> ItemsWithoutOrderNumber { get; set; } = new List<string>();
    }

    public class NetworkInterfaceInfo
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Subnet { get; set; } = string.Empty;
        public string MasterIoSystem { get; set; } = string.Empty;

        /// <summary>Número de l'IO system (100, 101...). Distingeix IO systems amb el mateix nom.</summary>
        public int? MasterIoSystemNumber { get; set; }
        public string SubnetMask { get; set; } = string.Empty;
        public string PnDeviceName { get; set; } = string.Empty;
    }

    public class NetworkModuleInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public string TypeIdentifier { get; set; } = string.Empty;
        public int PositionNumber { get; set; }
    }
}