using System;
using System.Collections.Generic;
using System.Linq;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public sealed class ApprovalReport
    {
        public string CatalogVersion { get; set; } = string.Empty;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsApproved => Errors.Count == 0;
    }

    /// <summary>
    /// Checks a project against the signed approved catalog before generation.
    /// E0 scope: every device/module type must be approved, device types must be root (station)
    /// types, and the hardware definition stored in the project (order number, type identifier)
    /// must equal the approved one. Kits, locked address/network fields, templates and blocks
    /// are checked from E1 on, once the catalog carries them.
    /// </summary>
    public static class ApprovalService
    {
        public static ApprovalReport Check(GenerationDocument doc, IReadOnlyList<CatalogEntry> projectCatalog,
            ApprovedCatalog approved, string? projectCatalogVersion)
        {
            var report = new ApprovalReport { CatalogVersion = approved.Version };

            if (string.IsNullOrWhiteSpace(projectCatalogVersion))
                report.Warnings.Add($"The project does not record an approved catalog version; checked against v{approved.Version}.");
            else if (!string.Equals(projectCatalogVersion, approved.Version, StringComparison.OrdinalIgnoreCase))
                report.Errors.Add($"The project was built with catalog v{projectCatalogVersion} but is being checked against v{approved.Version}.");

            var projectTypes = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogEntry entry in projectCatalog)
                projectTypes[entry.TypeKey] = entry;

            var checkedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceSpec device in doc.Devices)
            {
                CatalogEntry? type = CheckType(device.TypeKey, $"Device '{device.DeviceName}'", approved, projectTypes, checkedTypes, report);
                if (type != null && type.Role != CatalogItemRole.Root)
                    report.Errors.Add($"Device '{device.DeviceName}': type '{device.TypeKey}' is approved as a module, not as a station.");
            }

            foreach (ModuleSpec module in doc.Modules)
                CheckType(module.TypeKey, $"Module '{module.Device}/{module.ModuleName}'", approved, projectTypes, checkedTypes, report);

            return report;
        }

        private static CatalogEntry? CheckType(string typeKey, string owner, ApprovedCatalog approved,
            Dictionary<string, CatalogEntry> projectTypes, HashSet<string> checkedTypes, ApprovalReport report)
        {
            CatalogEntry? approvedType = approved.FindType(typeKey);
            if (approvedType == null)
            {
                report.Errors.Add($"{owner}: type '{typeKey}' is not in the approved catalog v{approved.Version}.");
                return null;
            }

            // Locked hardware definition: compare once per type.
            if (checkedTypes.Add(typeKey) && projectTypes.TryGetValue(typeKey, out CatalogEntry? projectType))
            {
                if (!SameText(projectType.Mlfb, approvedType.Mlfb))
                    report.Errors.Add($"Type '{typeKey}': order number '{projectType.Mlfb}' differs from approved '{approvedType.Mlfb}'.");
                if (!SameText(projectType.Identifier, approvedType.Identifier))
                    report.Errors.Add($"Type '{typeKey}': type identifier '{projectType.Identifier}' differs from approved '{approvedType.Identifier}'.");
            }

            return approvedType;
        }

        private static bool SameText(string? a, string? b) =>
            string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Approved types to use for generation (only those; never the project's own copy).</summary>
        public static List<CatalogEntry> GenerationCatalog(ApprovedCatalog approved) => approved.Types.ToList();
    }
}
