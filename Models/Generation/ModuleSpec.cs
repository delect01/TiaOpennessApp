using System;

namespace TiaOpennessApp.Models.Generation
{
    public class ModuleSpec
    {
        public string Device { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public int? Slot { get; set; }
        public string TypeKey { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }
}