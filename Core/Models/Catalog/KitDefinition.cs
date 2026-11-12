using System.Collections.Generic;

namespace TiaOpennessApp.Models.Catalog
{
    /// <summary>
    /// An approved kit: a device type with its fixed modules, network role, submodule I/Q lengths
    /// and optional tag templates. Users add copies of a kit to a project and fill in only the free
    /// fields (device name, IP, PROFINET name, comment); everything here is locked.
    /// Text fields may contain the placeholder <see cref="DevicePlaceholder"/>, replaced by the
    /// device name of each copy (e.g. module "{DEVICE}_1" → "ETH06_1").
    /// </summary>
    public sealed class KitDefinition
    {
        public const string DevicePlaceholder = "{DEVICE}";

        /// <summary>Stable identifier (letters, digits, '_', '-', '.').</summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Approved station type (catalog type with role Root).</summary>
        public string DeviceTypeKey { get; set; } = string.Empty;

        public string Firmware { get; set; } = string.Empty;

        /// <summary>Default device comment for new copies.</summary>
        public string DeviceComment { get; set; } = string.Empty;

        /// <summary>Fixed modules (always present in every copy).</summary>
        public List<KitModule> Modules { get; set; } = new List<KitModule>();
        public List<KitInterface> Interfaces { get; set; } = new List<KitInterface>();

        /// <summary>I/Q ranges of the fixed modules.</summary>
        public List<KitAddress> Addresses { get; set; } = new List<KitAddress>();
        public List<KitTag> Tags { get; set; } = new List<KitTag>();

        /// <summary>
        /// Modular kits (ET200SP station, SEW gateway...): module types the user may add to each copy,
        /// besides the fixed ones. Empty = fixed layout (every copy identical).
        /// </summary>
        public List<KitModuleOption> AllowedModules { get; set; } = new List<KitModuleOption>();

        public bool IsModular => AllowedModules.Count > 0;

        /// <summary>Placeholder for the module name inside <see cref="KitModuleOption.Addresses"/>.</summary>
        public const string ModulePlaceholder = "{MODULE}";
    }

    /// <summary>
    /// A module the user may plug into a copy of a modular kit. Identified by its catalog
    /// <see cref="TypeKey"/> (e.g. DI16X24VDCST) or, for GSD submodules without a catalog row,
    /// by the type identifiers of its I/Q ranges (empty TypeKey).
    /// </summary>
    public sealed class KitModuleOption
    {
        public string Name { get; set; } = string.Empty;
        public string TypeKey { get; set; } = string.Empty;

        /// <summary>Optional limits (null = any).</summary>
        public int? MinSlot { get; set; }
        public int? MaxSlot { get; set; }
        public int? MaxCount { get; set; }

        /// <summary>I/Q ranges of one such module; ModuleName is "{MODULE}", SubModuleName may contain it.</summary>
        public List<KitAddress> Addresses { get; set; } = new List<KitAddress>();
    }

    public sealed class KitModule
    {
        public string ModuleName { get; set; } = string.Empty;
        public int? Slot { get; set; }
        public string TypeKey { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>Network role of an interface: which subnet and IO system it joins (IP/name are per copy).</summary>
    public sealed class KitInterface
    {
        public string InterfaceItem { get; set; } = string.Empty;
        public string Subnet { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string MasterIoSystem { get; set; } = string.Empty;
    }

    /// <summary>I/Q range of a submodule: only the length is fixed; the start is the next free address.</summary>
    public sealed class KitAddress
    {
        public string ModuleName { get; set; } = string.Empty;
        public string SubModuleName { get; set; } = string.Empty;
        public string TypeIdentifier { get; set; } = string.Empty;

        /// <summary>"I" or "Q".</summary>
        public string IoType { get; set; } = string.Empty;

        /// <summary>Length in bits (as in ModuleAddresses.csv).</summary>
        public int LengthBits { get; set; }
    }

    /// <summary>
    /// Tag created with every copy. The address is relative to the start of a kit submodule range:
    /// <see cref="BitOffset"/> bits from the start (e.g. 0 = first bit, 16 = byte 2).
    /// </summary>
    public sealed class KitTag
    {
        public string TagTable { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string SubModuleName { get; set; } = string.Empty;
        public string IoType { get; set; } = string.Empty;
        public int BitOffset { get; set; }
        public string Comment { get; set; } = string.Empty;
    }
}
