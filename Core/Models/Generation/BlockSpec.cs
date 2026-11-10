namespace TiaOpennessApp.Models.Generation
{
    /// <summary>
    /// Un bloc de programa exportat del projecte real. El codi complet viu al
    /// fitxer SimaticML indicat per <see cref="SourceFile"/>; aquest model només
    /// conté les metadades necessàries per al manifest i per a la validació.
    /// </summary>
    public class BlockSpec
    {
        /// <summary>Dispositiu PLC propietari del bloc (device.Name).</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>Grup de blocs de l'usuari, amb segments separats per '/'. Buit = arrel.</summary>
        public string GroupPath { get; set; } = string.Empty;

        /// <summary>Tipus de document. Per ara sempre "Block".</summary>
        public string Kind { get; set; } = "Block";

        /// <summary>Nom del bloc a TIA Portal.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Namespace del bloc, si en té.</summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>Número de bloc. Null si no s'ha exportat.</summary>
        public int? Number { get; set; }

        /// <summary>Indica si TIA assigna el número automàticament.</summary>
        public bool? AutoNumber { get; set; }

        /// <summary>OB, FB, FC, GlobalDB, InstanceDB, CodeBlock o DataBlock.</summary>
        public string BlockType { get; set; } = string.Empty;

        /// <summary>Llenguatge de programació efectiu (LAD, FBD, SCL, STL, ...).</summary>
        public string ProgrammingLanguage { get; set; } = string.Empty;

        /// <summary>Layout de memòria del bloc (Optimized, Standard, ...).</summary>
        public string MemoryLayout { get; set; } = string.Empty;

        /// <summary>Títol del bloc, en una sola línia.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Comentari del bloc, en una sola línia.</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// Ruta relativa al directori de documents, per exemple
        /// <c>blocks/CPU21/Group/MyBlock.xml</c>. Sempre es desa amb '/'.
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;
    }
}
