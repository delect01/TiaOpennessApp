using System.Collections.Generic;

namespace TiaOpennessApp.Models.Generation
{
    public class GenerationDocument
    {
        public List<DeviceSpec> Devices { get; set; } = new List<DeviceSpec>();
        public List<ModuleSpec> Modules { get; set; } = new List<ModuleSpec>();
        public List<SubnetSpec> Subnets { get; set; } = new List<SubnetSpec>();
        public List<InterfaceSpec> Interfaces { get; set; } = new List<InterfaceSpec>();
        public List<IoTagSpec> IoTags { get; set; } = new List<IoTagSpec>();
        public List<ModuleAddressSpec> ModuleAddresses { get; set; } = new List<ModuleAddressSpec>();
    }
}