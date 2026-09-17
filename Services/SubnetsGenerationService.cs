using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class SubnetGenerationItemReport
    {
        public string Name { get; set; } = string.Empty;
        public string SubnetType { get; set; } = string.Empty;
        public bool Created { get; set; }
        public bool Skipped { get; set; }
        public string? Error { get; set; }
    }

    public class SubnetsGenerationService
    {
        private readonly Project _project;

        public SubnetsGenerationService(Project project)
        {
            _project = project;
        }

        public List<SubnetGenerationItemReport> Generate(IEnumerable<SubnetSpec> subnets)
        {
            var results = new List<SubnetGenerationItemReport>();
            SubnetComposition subnetComposition = _project.Subnets;

            foreach (var spec in subnets)
            {
                var report = new SubnetGenerationItemReport
                {
                    Name = spec.Name,
                    SubnetType = spec.SubnetType,
                };

                try
                {
                    if (subnetComposition.Find(spec.Name) != null)
                    {
                        report.Skipped = true;
                        results.Add(report);
                        continue;
                    }

                    string typeIdentifier = ResolveTypeIdentifier(spec.SubnetType);
                    subnetComposition.Create(typeIdentifier, spec.Name);
                    report.Created = true;
                }
                catch (Exception ex)
                {
                    report.Error = ex.Message;
                }

                results.Add(report);
            }
            return results;
        }

        public void DumpSubnets()
        {
            foreach (Subnet subnet in _project.Subnets)
                Console.WriteLine($"SUBNET: {subnet.Name}  TypeIdent={subnet.TypeIdentifier}");
        }

        private static string ResolveTypeIdentifier(string subnetType)
        {
            string type = subnetType.Trim().ToUpperInvariant();
            switch (type)
            {
                case "PN/IE":
                case "PROFINET":
                case "IE":
                case "ETHERNET":
                    return "System:Subnet.Ethernet";
                case "PROFIBUS":
                case "MPI":
                case "DP":
                    return "System:Subnet.Profibus";
                default:
                    return subnetType.Trim();
            }
        }
    }
}