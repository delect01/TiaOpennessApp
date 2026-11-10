namespace TiaOpennessApp.Models.Generation
{
    /// <summary>
    /// Direcció de maquinari (I/Q) d'un mòdul o submòdul, tal com s'ha assignat
    /// al projecte real. Es fa servir per reproduir al projecte generat les
    /// adreces de maquinari del projecte origen (StartAddress i Length per item).
    /// </summary>
    public class ModuleAddressSpec
    {
        /// <summary>Dispositiu on viu el mòdul (device.Name).</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>Nom del mòdul (item de maquinari que penja del rack).</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// Nom de l'item que exposa l'adreça quan no és el mòdul mateix
        /// (submòdul). Buit si l'adreça és del mòdul.
        /// </summary>
        public string SubModuleName { get; set; } = string.Empty;

        /// <summary>Tipus de l'item que exposa l'adreça (DeviceItem.TypeIdentifier).</summary>
        public string TypeIdentifier { get; set; } = string.Empty;

        /// <summary>'I' (entrada) / 'Q' (sortida).</summary>
        public string IoType { get; set; } = string.Empty;

        /// <summary>Adreça d'inici del rang (byte).</summary>
        public int StartAddress { get; set; }

        /// <summary>Longitud del rang (bytes).</summary>
        public int Length { get; set; }
    }
}