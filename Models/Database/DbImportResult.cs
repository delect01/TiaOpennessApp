using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models.Database
{
    public class DbImportResult
    {
        public bool Success { get; set; }

        public string DbPath { get; set; } = string.Empty;

        public string ImportsDir { get; set; } = string.Empty;

        public List<DbTableCount> TableCounts { get; } = new List<DbTableCount>();

        public List<string> Errors { get; } = new List<string>();

        public TimeSpan Elapsed { get; set; }
    }
}
