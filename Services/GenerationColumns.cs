namespace TiaOpennessApp.Services
{
    public static class GenerationColumns
    {
        public const string Devices = "Devices";
        public const string Modules = "Modules";
        public const string Subnets = "Subnets";
        public const string Interfaces = "Interfaces";
        public const string IoTags = "IoTags";
        public const string ModuleAddresses = "ModuleAddresses";

        public const string DeviceName = "DeviceName";
        public const string Group = "Group";
        public const string TypeKey = "TypeKey";
        public const string Firmware = "Firmware";
        public const string Comment = "Comment";
        public const string Device = "Device";
        public const string IoDevice = "IoDevice";
        public const string ModuleName = "ModuleName";
        public const string SubModuleName = "SubModuleName";
        public const string Slot = "Slot";
        public const string Name = "Name";
        public const string SubnetType = "SubnetType";
        public const string InterfaceItem = "InterfaceItem";
        public const string Subnet = "Subnet";
        public const string IpAddress = "IPAddress";
        public const string SubnetMask = "SubnetMask";
        public const string PnDeviceName = "PNDeviceName";
        public const string MasterIoSystem = "MasterIOSystem";
        public const string TagTable = "TagTable";
        public const string Type = "Type";
        public const string Address = "Address";
        public const string TypeIdentifier = "TypeIdentifier";
        public const string IoType = "IoType";
        public const string StartAddress = "StartAddress";
        public const string Length = "Length";

        public static readonly string[] DevicesColumns = { DeviceName, Group, TypeKey, Firmware, Comment };
        public static readonly string[] ModulesColumns = { Device, ModuleName, Slot, TypeKey, Comment };
        public static readonly string[] SubnetsColumns = { Name, SubnetType };
        public static readonly string[] InterfacesColumns = { Device, InterfaceItem, Subnet, IpAddress, SubnetMask, PnDeviceName, MasterIoSystem };
        public static readonly string[] IoTagsColumns = { Device, IoDevice, ModuleName, SubModuleName, TagTable, Name, Type, Address, Comment };
        public static readonly string[] ModuleAddressesColumns = { Device, ModuleName, SubModuleName, TypeIdentifier, IoType, StartAddress, Length };
    }
}