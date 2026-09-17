using System;

namespace TiaOpennessApp.Models.Generation
{
    public enum CatalogItemRole
    {
        Root,
        Module
    }

    public class CatalogEntry
    {
        public string TypeKey { get; set; } = string.Empty;
        public string Mlfb { get; set; } = string.Empty;
        public string TiaVersion { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CatalogItemRole Role { get; set; } = CatalogItemRole.Module;

        /// <summary>
        /// Optional exact Openness TypeIdentifier (e.g. "OrderNumber:6ES7 515-2AN03-0AB0/V2.9")
        /// used to create the device. When empty, the MLFB is used to derive candidates.
        /// </summary>
        public string Identifier { get; set; } = string.Empty;
    }
}