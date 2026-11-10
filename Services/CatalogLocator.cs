using System.IO;

namespace TiaOpennessApp.Services
{
    /// <summary>
    /// Resol la ubicació de catalog\Catalog.csv.
    /// Ordre: ruta configurada (si el fitxer o la seva carpeta existeixen) ->
    /// AppPaths.CatalogPath (arrel del projecte en dev, Documents\TIAO\catalog instal·lada).
    /// </summary>
    public static class CatalogLocator
    {
        public const string CatalogFolderName = AppPaths.CatalogFolderName;
        public const string CatalogFileName = AppPaths.CatalogFileName;

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

        public static string DefaultPath => AppPaths.CatalogPath;

        public static string DefaultDirectory => AppPaths.CatalogDir;
    }
}
