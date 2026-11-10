using System;
using System.Collections.Generic;
using System.IO;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Ubicacions de dades de l'aplicació.
    ///  - Desenvolupament (l'exe és dins d'un directori amb TiaOpennessApp.csproj):
    ///    imports/exports/catalog a l'arrel del projecte, BD i config al costat de l'exe.
    ///  - Instal·lada (p.ex. Program Files, només lectura):
    ///    %USERPROFILE%\Documents\TIAO\{imports,exports,catalog}, BD i appsettings d'usuari a
    ///    %USERPROFILE%\Documents\TIAO.
    /// </summary>
    public static class AppPaths
    {
        public const string DataFolderName = "TIAO";
        public const string ImportsFolderName = "imports";
        public const string ExportsFolderName = "exports";
        public const string CatalogFolderName = "catalog";
        public const string CatalogFileName = "Catalog.csv";
        public const string DatabaseFileName = "tia_topology.db";
        public const string SettingsFileName = "appsettings.json";
        private const string ProjectFileName = "TiaOpennessApp.csproj";

        private static readonly Lazy<string?> _projectRoot = new Lazy<string?>(FindProjectRoot);

        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Arrel del projecte font, o null si l'app s'executa instal·lada.</summary>
        public static string? ProjectRoot => _projectRoot.Value;

        public static bool IsDevelopment => ProjectRoot != null;

        public static string DocumentsDirectory
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs))
                    docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
                return docs;
            }
        }

        /// <summary>Carpeta arrel de dades: projecte (dev) o Documents\TIAO (instal·lada).</summary>
        public static string DataRoot => ProjectRoot ?? Path.Combine(DocumentsDirectory, DataFolderName);

        public static string ImportsDir => Path.Combine(DataRoot, ImportsFolderName);
        public static string ExportsDir => Path.Combine(DataRoot, ExportsFolderName);
        public static string CatalogDir => Path.Combine(DataRoot, CatalogFolderName);
        public static string CatalogPath => Path.Combine(CatalogDir, CatalogFileName);

        /// <summary>BD SQLite: al costat de l'exe en dev; a Documents\TIAO si està instal·lada.</summary>
        public static string DatabasePath => IsDevelopment
            ? Path.Combine(BaseDirectory, DatabaseFileName)
            : Path.Combine(DataRoot, DatabaseFileName);

        /// <summary>appsettings.json de l'usuari (només instal·lada): sobreescriu el de l'exe i rep els canvis.</summary>
        public static string? UserSettingsPath => IsDevelopment ? null : Path.Combine(DataRoot, SettingsFileName);

        /// <summary>Fitxer on es desen els valors recordats.</summary>
        public static string WritableSettingsPath => UserSettingsPath ?? Path.Combine(BaseDirectory, SettingsFileName);

        /// <summary>Valors per defecte de configuració (prioritat més baixa).</summary>
        public static IDictionary<string, string?> ConfigDefaults()
        {
            string automation = Path.Combine(DocumentsDirectory, "Automation");
            return new Dictionary<string, string?>
            {
                ["TiaPortal:Mode"] = "Auto",
                ["TiaPortal:Version"] = "21",
                ["TiaPortal:ProjectPath"] = automation,
                ["TiaPortal:NewProjectPath"] = automation,
                ["TiaPortal:ImportPath"] = ImportsDir,
                ["Import:DocsPath"] = ImportsDir,
                ["Import:CatalogPath"] = CatalogPath,
                ["Export:DocsPath"] = ExportsDir,
                ["Export:CatalogPath"] = CatalogPath,
            };
        }

        /// <summary>
        /// Crea les carpetes de dades de l'usuari i, si falta, hi copia el catàleg
        /// inicial instal·lat al costat de l'exe.
        /// </summary>
        public static void EnsureUserData()
        {
            if (IsDevelopment)
                return;

            try
            {
                Directory.CreateDirectory(ImportsDir);
                Directory.CreateDirectory(ExportsDir);
                Directory.CreateDirectory(CatalogDir);

                string seed = Path.Combine(BaseDirectory, CatalogFolderName, CatalogFileName);
                if (!File.Exists(CatalogPath) && File.Exists(seed))
                    File.Copy(seed, CatalogPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(avís) No s'han pogut preparar les carpetes de dades a {DataRoot}: {ex.Message}");
            }
        }

        private static string? FindProjectRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(BaseDirectory);
            for (int depth = 0; depth < 6 && current != null; depth++)
            {
                if (File.Exists(Path.Combine(current.FullName, ProjectFileName)))
                    return current.FullName;
                current = current.Parent;
            }
            return null;
        }
    }
}
