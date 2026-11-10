namespace TiaOpennessApp.Models.Generation
{
    /// <summary>
    /// Un I/O tag exportat d'una taula de tags PLC d'un projecte real, amb la
    /// seva associació a dispositiu / mòdul / submòdul quan l'adreça cau dins
    /// del rang d'adreces d'un mòdul de maquinari.
    /// </summary>
    public class IoTagSpec
    {
        /// <summary>Dispositiu PLC propietari de la taula de tags (device.Name).</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>
        /// Dispositiu de maquinari (I/O) al qual pertany el mòdul associat a
        /// l'adreça del tag (ex. "ETH03", "ROBOT1"). Buit si no s'ha pogut
        /// associar. Coincideix amb <see cref="Device"/> quan el tag cau al
        /// propi PLC (CPU, rack central).
        /// </summary>
        public string IoDevice { get; set; } = string.Empty;

        /// <summary>
        /// Mòdul de maquinari associat (nom del DeviceItem) quan l'adreça del tag
        /// cau dins del rang d'adreces del mòdul. Buit si no s'ha pogut associar.
        /// </summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// Submòdul de maquinari associat (nom del DeviceItem fill més profund)
        /// quan l'adreça cau dins del seu rang. Buit si l'associació és al mòdul
        /// o no s'ha pogut associar.
        /// </summary>
        public string SubModuleName { get; set; } = string.Empty;

        /// <summary>Nom del tag (PlcTag.Name).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Tipus de dada del tag (PlcTag.DataTypeName, ex. "Bool", "Int").</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Adreça del tag tal com apareix a TIA (atribut "Path", ex. "%IW72").</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Nom de la taula de tags que conté el tag (PlcTagTable.Name).</summary>
        public string TagTable { get; set; } = string.Empty;

        /// <summary>Comentari del tag (PlcTag.Comment).</summary>
        public string Comment { get; set; } = string.Empty;
    }
}