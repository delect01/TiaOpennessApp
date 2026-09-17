using System.Collections.Generic;
using System.Linq;

namespace TiaOpennessApp.Models.Generation
{
    public class GenerationParseResult
    {
        public GenerationDocument Document { get; set; } = new GenerationDocument();
        public List<ParseIssue> Issues { get; set; } = new List<ParseIssue>();

        public bool HasErrors
        {
            get { return Issues.Any(i => i.Level == ParseIssueLevel.Error); }
        }
    }
}