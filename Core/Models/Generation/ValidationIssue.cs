namespace TiaOpennessApp.Models.Generation
{
    public class ValidationIssue
    {
        public ParseIssueLevel Level { get; set; } = ParseIssueLevel.Error;
        public string Section { get; set; } = string.Empty;
        public string Item { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}