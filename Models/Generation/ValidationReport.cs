using System.Collections.Generic;
using System.Linq;

namespace TiaOpennessApp.Models.Generation
{
    public class ValidationReport
    {
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
        public List<string> PlannedOperations { get; set; } = new List<string>();

        public bool HasErrors
        {
            get { return Issues.Any(i => i.Level == ParseIssueLevel.Error); }
        }

        public int ErrorCount { get { return Issues.Count(i => i.Level == ParseIssueLevel.Error); } }
        public int WarningCount { get { return Issues.Count(i => i.Level == ParseIssueLevel.Warning); } }
    }
}