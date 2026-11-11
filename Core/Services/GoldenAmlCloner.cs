using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TiaOpennessApp.Services
{
    /// <summary>A device to add to a golden AML by copying an existing device of the same type.</summary>
    public sealed class AmlCloneRequest
    {
        public string NewName { get; set; } = string.Empty;

        /// <summary>Preferred template: a device of the golden with the same type (by name).</summary>
        public string? TemplateName { get; set; }

        /// <summary>Fallback: any golden device containing this TypeIdentifier (catalog identifier).</summary>
        public string? TypeIdentifier { get; set; }

        /// <summary>Optional IP for the copied interface, so the import does not see a duplicate address.</summary>
        public string? IpAddress { get; set; }
    }

    public sealed class AmlCloneReport
    {
        public List<string> Cloned { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Adds devices to a golden AML (CAx export of a TIA project) by deep-copying a device of the
    /// same type. GSD devices cannot be created through Openness without the gsdml licence, but they
    /// can be imported from AML; this lets new or renamed GSD devices be generated as long as the
    /// golden contains at least one device of the same type.
    /// The copy gets new IDs and the new name (device and head module). Subnet / IO-system / tag
    /// links live outside the device and are not copied: the normal generation steps connect it.
    /// </summary>
    public static class GoldenAmlCloner
    {
        private const string DeviceRole = "AutomationProjectConfigurationRoleClassLib/Device";

        /// <summary>Names of the device (station) elements in the AML.</summary>
        public static List<string> ListDeviceNames(string amlPath) =>
            Devices(XDocument.Load(amlPath)).Select(e => (string?)e.Attribute("Name") ?? string.Empty).ToList();

        /// <summary>Writes <paramref name="outAml"/> = <paramref name="inAml"/> + one copy per request.</summary>
        public static AmlCloneReport AddClones(string inAml, string outAml, IReadOnlyList<AmlCloneRequest> requests)
        {
            var report = new AmlCloneReport();
            XDocument doc = XDocument.Load(inAml);
            List<XElement> devices = Devices(doc).ToList();

            foreach (AmlCloneRequest request in requests)
            {
                if (devices.Any(d => SameName(d, request.NewName)))
                {
                    report.Errors.Add($"{request.NewName}: already in the golden AML.");
                    continue;
                }

                XElement? template = null;
                if (!string.IsNullOrWhiteSpace(request.TemplateName))
                    template = devices.FirstOrDefault(d => SameName(d, request.TemplateName!));
                if (template == null && !string.IsNullOrWhiteSpace(request.TypeIdentifier))
                    template = devices.FirstOrDefault(d => d.Descendants("Attribute")
                        .Any(a => (string?)a.Attribute("Name") == "TypeIdentifier"
                                  && string.Equals(a.Element("Value")?.Value, request.TypeIdentifier, StringComparison.OrdinalIgnoreCase)));
                if (template == null)
                {
                    report.Errors.Add($"{request.NewName}: no device of the same type in the golden AML to copy.");
                    continue;
                }

                var clone = new XElement(template);
                RenewIds(clone);
                clone.SetAttributeValue("Name", request.NewName);

                // Head module carries the visible device name in TIA (GSD devices).
                XElement? head = clone.Descendants("InternalElement").FirstOrDefault(e => e.Elements("Attribute")
                    .Any(a => (string?)a.Attribute("Name") == "DeviceItemType" && a.Element("Value")?.Value == "HeadModule"));
                head?.SetAttributeValue("Name", request.NewName);

                if (!string.IsNullOrWhiteSpace(request.IpAddress))
                {
                    foreach (XElement address in clone.Descendants("Attribute")
                                 .Where(a => (string?)a.Attribute("Name") == "NetworkAddress"))
                        address.SetElementValue("Value", request.IpAddress);
                }

                template.AddAfterSelf(clone);
                devices.Add(clone);
                report.Cloned.Add($"{request.NewName} (copy of {template.Attribute("Name")?.Value})");
            }

            doc.Save(outAml);
            return report;
        }

        private static IEnumerable<XElement> Devices(XDocument doc) =>
            doc.Descendants("InternalElement").Where(e => e.Elements("SupportedRoleClass")
                .Any(r => string.Equals((string?)r.Attribute("RefRoleClassPath"), DeviceRole, StringComparison.Ordinal)));

        private static bool SameName(XElement element, string name) =>
            string.Equals((string?)element.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase);

        private static void RenewIds(XElement root)
        {
            foreach (XElement element in root.DescendantsAndSelf())
            {
                XAttribute? id = element.Attribute("ID");
                if (id != null)
                    id.Value = Guid.NewGuid().ToString();
            }
        }
    }
}
