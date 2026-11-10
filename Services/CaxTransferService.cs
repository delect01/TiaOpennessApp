using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.HW;

namespace TiaOpennessApp.Services
{
    public static class CaxTransferService
    {

        public static bool ExportProject(Project project, FileInfo exportPath, FileInfo logPath)
        {
            return ((IEngineeringServiceProvider)project).GetService<CaxProvider>().Export(project, exportPath, logPath);
        }

        public static TransferResult ImportAml(IEngineeringServiceProvider provider, FileInfo aml, CaxImportOptions options)
        {
            return provider.GetService<CaxProvider>().Import(aml, options);
        }

    }
}