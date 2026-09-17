namespace TiaOpennessApp.Models.Generation
{
    public enum ParseIssueLevel
    {
        Warning,
        Error
    }

    public class ParseIssue
    {
        public ParseIssueLevel Level { get; set; } = ParseIssueLevel.Error;
        public string Source { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}