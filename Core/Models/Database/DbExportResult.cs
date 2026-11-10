using System;
using System.Collections.Generic;

namespace TiaOpennessApp.Models.Database
{
    public class DbExportResult
    {
        public bool Success { get; set; }

        public string DbPath { get; set; } = string.Empty;

        public string OutputDir { get; set; } = string.Empty;

        public string CatalogPath { get; set; } = string.Empty;

        public List<DbFileCount> FileCounts { get; } = new List<DbFileCount>();

        public List<string> Errors { get; } = new List<string>();

        public TimeSpan Elapsed { get; set; }
    }
}
