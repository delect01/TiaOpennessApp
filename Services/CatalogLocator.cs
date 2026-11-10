using System;
using System.IO;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Resol la ubicació de catalog\Catalog.csv.
    /// Ordre: ruta configurada (si el fitxer o la seva carpeta existeixen) ->
    /// arrel del projecte (carpeta amb TiaOpennessApp.csproj, pujant des del binari) ->
    /// carpeta catalog al costat de l'executable (paquet distribuït).
    /// Mai retorna la còpia dins de bin\...\net48 quan s'executa des del projecte.
    /// </summary>
    public static class CatalogLocator
    {
        public const string CatalogFolderName = "catalog";
        public const string CatalogFileName = "Catalog.csv";
        private const string ProjectFileName = "TiaOpennessApp.csproj";

        public static string Resolve(string? configuredPath = null)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string full = Path.GetFullPath(configuredPath);
                string? dir = Path.GetDirectoryName(full);
                if (File.Exists(full) || (dir != null && Directory.Exists(dir)))
                    return full;
            }

            return DefaultPath;
        }

        public static string DefaultPath
        {
            get { return Path.Combine(DefaultDirectory, CatalogFileName); }
        }

        public static string DefaultDirectory
        {
            get
            {
                string? projectRoot = FindProjectRoot();
                string root = projectRoot ?? AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(root, CatalogFolderName);
            }
        }

        private static string? FindProjectRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
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
