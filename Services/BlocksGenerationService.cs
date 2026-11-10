using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public enum BlockGenerationAction
    {
        Imported,
        Overridden,
        Skipped,
        Failed,
    }

    public class BlockGenerationItemReport
    {
        public string Device { get; set; } = string.Empty;
        public string GroupPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BlockType { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public BlockGenerationAction Action { get; set; }
        public int ImportedCount { get; set; }
        public string? Error { get; set; }

        /// <summary>Motiu informatiu (no és error), p. ex. per què s'ha saltat.</summary>
        public string? Note { get; set; }
    }

    public class BlocksGenerationReport
    {
        public List<BlockGenerationItemReport> Items { get; } = new();
        public List<string> Errors { get; } = new();
        public CompilerResult? CompileResult { get; set; }
        public int ImportedCount => Items.Count(i => i.Action == BlockGenerationAction.Imported);
        public int OverriddenCount => Items.Count(i => i.Action == BlockGenerationAction.Overridden);
        public int SkippedCount => Items.Count(i => i.Action == BlockGenerationAction.Skipped);
        public int FailedCount => Items.Count(i => i.Action == BlockGenerationAction.Failed);
    }

    public class BlocksGenerationOptions
    {
        /// <summary>
        /// Si és cert, reimporta amb <see cref="ImportOptions.Override"/> quan el
        /// bloc ja existeix al mateix GroupPath. Si és fals, el salta.
        /// </summary>
        public bool OverrideExisting { get; set; }

        /// <summary>Compila el PLC després d'importar tots els blocs.</summary>
        public bool Compile { get; set; } = true;

        /// <summary>
        /// Permet reintentar amb <c>IgnoreMissingReferencedObjects</c>. Desactivat
        /// per defecte: en un template vàlid les dependències hi han de ser.
        /// </summary>
        public bool AllowMissingReferences { get; set; }
    }

    /// <summary>
    /// Importa UDTs (Kind=Type) i blocs SimaticML en un projecte/template
    /// existent que ja tingui el maquinari. Primer els UDTs i després els blocs,
    /// cadascun per passades fins que no hi ha progrés, de manera que l'ordre de
    /// Blocks.csv no importa (UDT→UDT, FB multiinstància, InstanceDB→FB).
    /// </summary>
    public class BlocksGenerationService
    {
        private static readonly Dictionary<string, int> ImportOrder =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["GlobalDB"] = 0,
                ["FC"] = 1,
                ["FB"] = 2,
                ["InstanceDB"] = 3,
                ["OB"] = 4,
                ["CodeBlock"] = 5,
                ["DataBlock"] = 6,
            };

        private readonly Project _project;
        private readonly string _docsDir;

        public BlocksGenerationService(Project project, string docsDir)
        {
            _project = project;
            _docsDir = Path.GetFullPath(docsDir);
        }

        public BlocksGenerationReport Generate(
            GenerationDocument document,
            BlocksGenerationOptions? options = null)
        {
            options ??= new BlocksGenerationOptions();
            var report = new BlocksGenerationReport();
            var softwareByDevice = new Dictionary<string, PlcSoftware>(StringComparer.OrdinalIgnoreCase);

            var ordered = document.Blocks
                .Select((block, index) => new { Block = block, Index = index })
                .OrderBy(e => IsType(e.Block) ? -2 : IsTagTable(e.Block) ? -1 : OrderFor(e.Block.BlockType))
                .ThenBy(e => e.Index)
                .Select(e => e.Block)
                .ToList();

            // 1) Preparació: resoldre fitxer/PLC/grup i decidir si se salta.
            var pending = new List<PendingImport>();
            foreach (BlockSpec spec in ordered)
            {
                var item = new BlockGenerationItemReport
                {
                    Device = spec.Device,
                    GroupPath = spec.GroupPath,
                    Name = spec.Name,
                    BlockType = spec.BlockType,
                    SourceFile = spec.SourceFile,
                };
                report.Items.Add(item);

                try
                {
                    string filePath = ResolveSourceFile(spec.SourceFile);
                    Device? device = DevicesAndNetworksService.FindDeviceByName(_project, spec.Device);
                    if (device == null)
                        throw new InvalidOperationException($"Dispositiu '{spec.Device}' no trobat al projecte.");

                    PlcSoftware? software = FindPlcSoftware(device);
                    if (software == null)
                        throw new InvalidOperationException($"El dispositiu '{spec.Device}' no té software PLC.");

                    softwareByDevice[spec.Device] = software;
                    bool preExisting = Exists(software, spec);

                    // Les taules de variables sempre se sobreescriuen: les del template
                    // (només I/O tags generats) són un subconjunt de les de la font.
                    // Els blocs F del template (Main_Safety_RTGx per defecte) s'han de
                    // substituir pels de la font: el programa estàndard en llegeix membres.
                    if (preExisting && !options.OverrideExisting && !IsTagTable(spec) && !IsSafety(spec))
                    {
                        item.Action = BlockGenerationAction.Skipped;
                        item.Note = "ja existeix (sense override)";
                        continue;
                    }

                    pending.Add(new PendingImport(spec, item, software, new FileInfo(filePath), preExisting));
                }
                catch (Exception ex)
                {
                    Fail(report, spec, item, UnwrapMessage(ex));
                }
            }

            // 2) Import per passades: UDTs primer, després blocs. El que falla es
            // reintenta a la passada següent (dependències UDT→UDT, FB→FB
            // multiinstància, InstanceDB→FB). Para quan una passada no avança.
            ImportInPasses(pending.Where(p => IsType(p.Spec)).ToList(), report, options, "UDT");
            ImportInPasses(pending.Where(p => IsTagTable(p.Spec)).ToList(), report, options, "taules de variables");
            ImportInPasses(pending.Where(p => !IsType(p.Spec) && !IsTagTable(p.Spec) && !IsSafety(p.Spec)).ToList(), report, options, "blocs");
            // Blocs F (F_FBD/F_LAD/F_DB): TIA només n'accepta la importació si el
            // programa de seguretat no està protegit amb contrasenya. Si els rebutja,
            // es marquen com a saltats amb el motiu (no com a fallits).
            var safetyImports = pending.Where(p => IsSafety(p.Spec)).ToList();
            ImportInPasses(safetyImports, report, options, "blocs de seguretat", failAsSkipped: true);

            // Els blocs F criden instruccions F (ESTOP1, ACK_GL...) amb DB d'instància
            // única que l'exportació no troba als grups de programa: es creen.
            CreateMissingSingleInstanceDbs(safetyImports
                .Where(p => p.Item.Action == BlockGenerationAction.Imported
                         || p.Item.Action == BlockGenerationAction.Overridden)
                .ToList());

            // Sobreescriure el bloc principal F trenca l'enllaç del grup de runtime F
            // ("does not have a valid main safety block"): es torna a crear el grup.
            RepairRuntimeGroups(safetyImports
                .Where(p => p.Item.Action == BlockGenerationAction.Imported
                         || p.Item.Action == BlockGenerationAction.Overridden)
                .ToList());

            // Els accessos absoluts (P#DB1.DBX0.0, %DB5.DBW2...) depenen del número del
            // bloc: si un bloc de la plantilla o un d'importat abans ha ocupat el número
            // original, es restaura.
            RestoreBlockNumbers(pending
                .Where(p => !IsType(p.Spec) && !IsTagTable(p.Spec)
                         && (p.Item.Action == BlockGenerationAction.Imported
                             || p.Item.Action == BlockGenerationAction.Overridden))
                .ToList());

            if (options.Compile)
                Compile(softwareByDevice.Values, report);

            return report;
        }

        /// <summary>
        /// Torna a posar a cada bloc importat el número que tenia a l'XML exportat.
        /// Si el número l'ocupa un altre bloc que no el reclama, aquest es mou a un número lliure.
        /// </summary>
        private static void RestoreBlockNumbers(List<PendingImport> imported)
        {
            foreach (var bySoftware in imported.GroupBy(p => p.Software))
            {
                PlcSoftware software = bySoftware.Key;
                string device = bySoftware.First().Spec.Device;

                // Número esperat per bloc (de l'XML).
                var expected = new List<(PendingImport P, int Number)>();
                foreach (PendingImport p in bySoftware)
                {
                    int? n = ReadBlockNumber(p.File.FullName);
                    if (n.HasValue && n.Value > 0)
                        expected.Add((p, n.Value));
                }
                if (expected.Count == 0)
                    continue;

                List<PlcBlock> all = EnumerateBlocks(software).ToList();
                var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in expected)
                    claimed.Add(NumberKey(e.P.Spec.BlockType, e.Number));

                int fixedCount = 0;
                var warnings = new List<string>();
                foreach (var (p, number) in expected)
                {
                    PlcBlock? block = all.FirstOrDefault(b =>
                        string.Equals(Safe(() => b.Name), p.Spec.Name, StringComparison.OrdinalIgnoreCase));
                    if (block == null)
                        continue;

                    string kind = NumberKind(block);
                    try
                    {
                        // TIA admet números repetits fins a compilar; llavors renumera el bloc
                        // amb AutoNumber. Per això es mira l'ocupant encara que el número ja coincideixi.
                        PlcBlock? occupant = all.FirstOrDefault(b => !ReferenceEquals(b, block)
                            && NumberKind(b) == kind && Safe(() => (int?)b.Number) == number);
                        bool alreadyOk = Safe(() => (int?)block.Number) == number;
                        if (alreadyOk && occupant == null)
                        {
                            TrySet(() => block.AutoNumber = false);
                            continue;
                        }
                        if (occupant != null)
                        {
                            string occName = Safe(() => occupant.Name) ?? "?";
                            if (bySoftware.Any(o => string.Equals(o.Spec.Name, occName, StringComparison.OrdinalIgnoreCase))
                                && claimed.Contains(NumberKey(kind, number)) && expected.Count(x =>
                                    NumberKind(x.P.Spec.BlockType) == kind && x.Number == number) > 1)
                            {
                                warnings.Add($"{p.Spec.Name}: {kind}{number} reclamat per més d'un bloc");
                                continue;
                            }

                            int free = FirstFreeNumber(all, kind, claimed);
                            occupant.AutoNumber = false;
                            occupant.Number = free;
                            claimed.Add(NumberKey(kind, free));
                            Console.WriteLine($"  Número: {occName} passa a {kind}{free} per alliberar {kind}{number}.");
                        }

                        block.AutoNumber = false;
                        block.Number = number;
                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{p.Spec.Name} → {kind}{number}: {ShortMessage(ex)}");
                    }
                }

                if (fixedCount > 0 || warnings.Count > 0)
                    Console.WriteLine($"  {device}: {fixedCount} bloc(s) renumerats al número original" +
                                      (warnings.Count > 0 ? $", {warnings.Count} no." : "."));
                foreach (string w in warnings.Take(10))
                    Console.WriteLine($"    (avís número) {w}");
            }
        }

        private static string NumberKind(PlcBlock block) => block switch
        {
            DataBlock => "DB",
            FB => "FB",
            FC => "FC",
            OB => "OB",
            _ => block.GetType().Name,
        };

        private static string NumberKind(string? blockType)
        {
            string t = (blockType ?? string.Empty).Trim().ToUpperInvariant();
            if (t.EndsWith("DB", StringComparison.Ordinal)) return "DB";
            if (t.StartsWith("FB", StringComparison.Ordinal)) return "FB";
            if (t.StartsWith("FC", StringComparison.Ordinal)) return "FC";
            if (t.StartsWith("OB", StringComparison.Ordinal)) return "OB";
            return t;
        }

        private static string NumberKey(string kindOrType, int number) =>
            (kindOrType is "DB" or "FB" or "FC" or "OB" ? kindOrType : NumberKind(kindOrType)) + number;

        private static int FirstFreeNumber(List<PlcBlock> all, string kind, HashSet<string> claimed)
        {
            var used = new HashSet<int>(all.Where(b => NumberKind(b) == kind)
                .Select(b => Safe(() => (int?)b.Number) ?? 0));
            int n = 100;
            while (used.Contains(n) || claimed.Contains(kind + n))
                n++;
            return n;
        }

        private static int? ReadBlockNumber(string xmlPath)
        {
            try
            {
                System.Xml.Linq.XElement? attrs = System.Xml.Linq.XDocument.Load(xmlPath).Root?
                    .Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal))?
                    .Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
                string? v = attrs?.Elements().FirstOrDefault(e => e.Name.LocalName == "Number")?.Value;
                return int.TryParse(v, out int n) ? n : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<PlcBlock> EnumerateBlocks(PlcSoftware software)
        {
            var result = new List<PlcBlock>();
            void User(PlcBlockGroup g)
            {
                foreach (PlcBlock b in g.Blocks) result.Add(b);
                foreach (PlcBlockUserGroup c in g.Groups) User(c);
            }
            void Sys(PlcSystemBlockGroupComposition groups)
            {
                try
                {
                    foreach (PlcSystemBlockGroup g in groups)
                    {
                        try { foreach (PlcBlock b in g.Blocks) result.Add(b); } catch { }
                        Sys(g.Groups);
                    }
                }
                catch { /* grup de sistema no accessible */ }
            }
            User(software.BlockGroup);
            Sys(software.BlockGroup.SystemBlockGroups);
            return result;
        }

        /// <summary>
        /// Torna a crear cada grup de runtime F apuntant al bloc principal F (i al
        /// seu DB d'instància) importats. MainSafetyBlockName és de només lectura,
        /// així que l'única manera de reassignar-lo és esborrar i crear el grup,
        /// conservant-ne els temps de cicle i els blocs pre/post.
        /// </summary>
        private void RepairRuntimeGroups(List<PendingImport> imported)
        {
            foreach (var bySoftware in imported.GroupBy(p => p.Software))
            {
                PlcSoftware software = bySoftware.Key;
                List<PendingImport> items = bySoftware.ToList();
                string device = items[0].Spec.Device;

                SafetyAdministration? admin = GetSafetyAdministration(software);
                if (admin == null)
                {
                    Console.WriteLine($"  (avís seguretat) {device}: no s'ha pogut obtenir SafetyAdministration; " +
                                      "assigna el bloc principal F al grup de runtime a mà.");
                    continue;
                }

                foreach (RuntimeGroup group in admin.RuntimeGroups.ToList())
                {
                    string groupName = group.Name;
                    try
                    {
                        string? fbName = Safe(() => group.MainSafetyBlockName);
                        string? dbName = Safe(() => group.MainSafetyBlockIDbName);

                        // Si TIA ja no recorda el nom, es dedueix dels blocs F importats:
                        // DB d'instància F importat (InstanceOfName de l'XML) d'un FB F importat;
                        // si no, FB "X" amb DB d'instància "X_DB".
                        if (string.IsNullOrWhiteSpace(fbName) || string.IsNullOrWhiteSpace(dbName))
                        {
                            foreach (PendingImport d in items.Where(i =>
                                         string.Equals(i.Spec.BlockType, "InstanceDB", StringComparison.OrdinalIgnoreCase)))
                            {
                                string? of = ReadInstanceOfName(d.File.FullName);
                                if (string.IsNullOrWhiteSpace(of)
                                    || (!string.IsNullOrWhiteSpace(fbName) && !string.Equals(of, fbName, StringComparison.OrdinalIgnoreCase))
                                    || !items.Any(f => string.Equals(f.Spec.Name, of, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(f.Spec.BlockType, "FB", StringComparison.OrdinalIgnoreCase)))
                                    continue;
                                fbName = of;
                                dbName = d.Spec.Name;
                                break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(fbName))
                        {
                            PendingImport? fb = items.FirstOrDefault(p =>
                                string.Equals(p.Spec.BlockType, "FB", StringComparison.OrdinalIgnoreCase) &&
                                items.Any(d => string.Equals(d.Spec.Name, p.Spec.Name + "_DB", StringComparison.OrdinalIgnoreCase)));
                            fbName = fb?.Spec.Name;
                        }
                        if (string.IsNullOrWhiteSpace(dbName) && !string.IsNullOrWhiteSpace(fbName))
                            dbName = fbName + "_DB";

                        if (string.IsNullOrWhiteSpace(fbName) || string.IsNullOrWhiteSpace(dbName))
                        {
                            Console.WriteLine($"  (avís seguretat) {device}/{groupName}: no es pot deduir el bloc principal F.");
                            continue;
                        }

                        // Només cal si el bloc principal o el seu DB s'han importat ara.
                        if (!items.Any(p => string.Equals(p.Spec.Name, fbName, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(p.Spec.Name, dbName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        int? maxCycle = Safe<int?>(() => group.MaximumCycleTime);
                        int? warnCycle = Safe<int?>(() => group.WarnCycleTime);
                        string? infoDb = Safe(() => group.InfoDbName);
                        string? pre = Safe(() => group.PreProcessingName);
                        string? post = Safe(() => group.PostProcessingName);

                        group.Delete();

                        // Per si esborrar el grup s'ha endut els blocs, es tornen a importar.
                        PlcBlock? fbBlock = FindBlock(software, fbName!) ?? Reimport(items, fbName!);
                        PlcBlock? dbBlock = FindBlock(software, dbName!) ?? Reimport(items, dbName!);
                        if (fbBlock == null || dbBlock == null)
                            throw new InvalidOperationException(
                                $"no es troba '{(fbBlock == null ? fbName : dbName)}' després d'esborrar el grup");

                        RuntimeGroup created = admin.RuntimeGroups.Create(groupName, fbBlock, dbBlock);
                        TrySet(() => { if (maxCycle.HasValue) created.MaximumCycleTime = maxCycle.Value; });
                        TrySet(() => { if (warnCycle.HasValue) created.WarnCycleTime = warnCycle.Value; });
                        TrySet(() => { if (!string.IsNullOrEmpty(infoDb)) created.InfoDbName = infoDb; });
                        TrySet(() => { if (!string.IsNullOrEmpty(pre)) created.PreProcessingName = pre; });
                        TrySet(() => { if (!string.IsNullOrEmpty(post)) created.PostProcessingName = post; });

                        Console.WriteLine($"  Grup de runtime F '{groupName}' refet amb {fbName} / {dbName}.");

                        // El DB principal F de la plantilla (p.ex. Main_Safety_RTG1_DB, DB1) queda
                        // orfe si els blocs importats en porten un altre (Main_Safety_RTG1_DB_1):
                        // s'esborra perquè no ocupi el número d'un bloc importat.
                        string orphanName = fbName + "_DB";
                        if (!string.Equals(orphanName, dbName, StringComparison.OrdinalIgnoreCase)
                            && !items.Any(i => string.Equals(i.Spec.Name, orphanName, StringComparison.OrdinalIgnoreCase))
                            && FindBlock(software, orphanName) is InstanceDB orphan
                            && string.Equals(Safe(() => orphan.InstanceOfName), fbName, StringComparison.OrdinalIgnoreCase))
                        {
                            int? orphanNumber = Safe(() => (int?)orphan.Number);
                            try
                            {
                                orphan.Delete();
                                Console.WriteLine($"  DB F de la plantilla '{orphanName}' (DB{orphanNumber}) esborrat: el grup F usa {dbName}.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  (avís seguretat) no s'ha pogut esborrar '{orphanName}' de la plantilla: {ShortMessage(ex)}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  (avís seguretat) {device}/{groupName}: no s'ha pogut refer el grup " +
                                          $"de runtime F: {ShortMessage(ex)}");
                    }
                }
            }
        }

        /// <summary>
        /// Llegeix el SimaticML de cada bloc codi importat i, per a cada crida amb
        /// instància global (Part/CallInfo amb &lt;Instance Scope="GlobalVariable"&gt;),
        /// crea el DB d'instància si no existeix al PLC.
        /// </summary>
        private static void CreateMissingSingleInstanceDbs(List<PendingImport> imported)
        {
            foreach (var bySoftware in imported.GroupBy(p => p.Software))
            {
                PlcSoftware software = bySoftware.Key;
                var calls = new Dictionary<string, (string InstanceOf, string? Version, PendingImport Caller)>(StringComparer.OrdinalIgnoreCase);

                foreach (PendingImport p in bySoftware.Where(i =>
                             !string.Equals(i.Spec.BlockType, "InstanceDB", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(i.Spec.BlockType, "GlobalDB", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        foreach (var (dbName, instanceOf, version) in ReadGlobalInstanceCalls(p.File.FullName))
                            if (!calls.ContainsKey(dbName))
                                calls[dbName] = (instanceOf, version, p);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  (avís seguretat) no s'ha pogut llegir {p.File.Name}: {ShortMessage(ex)}");
                    }
                }

                int created = 0;
                var failures = new List<string>();
                foreach (var entry in calls)
                {
                    if (FindBlock(software, entry.Key) != null)
                        continue;

                    PendingImport caller = entry.Value.Caller;
                    // Al mateix grup que el DB d'instància del bloc F (p.ex. 0_SAFETY/3_DB) si n'hi ha.
                    PendingImport? siblingDb = bySoftware.FirstOrDefault(i =>
                        string.Equals(i.Spec.BlockType, "InstanceDB", StringComparison.OrdinalIgnoreCase));
                    string groupPath = (siblingDb ?? caller).Spec.GroupPath ?? string.Empty;

                    PlcBlockComposition target = TargetBlocks(software, groupPath);
                    string? error;
                    if (string.IsNullOrEmpty(entry.Value.Version))
                    {
                        try
                        {
                            // Funciona si l'FB instanciat és un bloc del projecte.
                            target.CreateInstanceDB(entry.Key, true, 1, entry.Value.InstanceOf);
                            created++;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            error = ShortMessage(ex);
                        }
                    }
                    else
                    {
                        error = $"instrucció de llibreria v{entry.Value.Version}";
                    }

                    // Instruccions de llibreria (ESTOP1, ACK_GL, FDBACK...): no són blocs del
                    // projecte; s'importa un SimaticML mínim del DB i TIA hi posa la interfície.
                    string? importError = ImportSyntheticInstanceDb(target, entry.Key, entry.Value.InstanceOf, entry.Value.Version);
                    if (importError == null)
                        created++;
                    else
                        failures.Add($"{entry.Key} ({entry.Value.InstanceOf}): {error} | import: {importError}");
                }

                if (created > 0 || failures.Count > 0)
                    Console.WriteLine($"  DB d'instància de blocs F: {created} creats, {failures.Count} no creats.");
                foreach (string f in failures.Take(10))
                    Console.WriteLine($"    (avís seguretat) {f}");
                if (failures.Count > 10)
                    Console.WriteLine($"    ... i {failures.Count - 10} més.");
            }
        }

        /// <summary>
        /// Importa un DB d'instància F generat (sense interfície o amb seccions buides).
        /// Retorna null si ha anat bé, o l'últim error.
        /// </summary>
        private static string? ImportSyntheticInstanceDb(PlcBlockComposition target, string dbName, string instanceOf, string? version)
        {
            const string emptyInterface =
                "<Interface><Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\" /></Interface>";
            string Esc(string v) => System.Security.SecurityElement.Escape(v);

            // Mateix format que exporta TIA per als DB d'instàncies de sistema (p.ex. IEC_Timer_0_DB):
            // <Namespace /> + OfSystemLibElement/OfSystemLibVersion, sense InstanceOfType.
            bool sysLib = !string.IsNullOrEmpty(version);
            var variants = sysLib
                ? new[] { (Iface: "", Lang: "F_DB"), ("", "DB"), (emptyInterface, "F_DB"), (emptyInterface, "DB") }
                : new[] { (Iface: "", Lang: "F_DB"), (emptyInterface, "F_DB") };

            var errors = new List<string>();
            foreach (var (iface, lang) in variants)
            {
                string path = Path.Combine(Path.GetTempPath(), $"tia_fdb_{Guid.NewGuid():N}.xml");
                try
                {
                    var xml = new System.Text.StringBuilder();
                    xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Document>\n  <Engineering version=\"V21\" />\n");
                    xml.Append("  <SW.Blocks.InstanceDB ID=\"0\">\n    <AttributeList>\n");
                    xml.Append("      <AutoNumber>true</AutoNumber>\n");
                    xml.Append($"      <InstanceOfName>{Esc(instanceOf)}</InstanceOfName>\n");
                    if (!sysLib)
                        xml.Append("      <InstanceOfType>FB</InstanceOfType>\n");
                    if (iface.Length > 0)
                        xml.Append($"      {iface}\n");
                    xml.Append($"      <Name>{Esc(dbName)}</Name>\n");
                    xml.Append("      <Namespace />\n");
                    if (sysLib)
                    {
                        xml.Append($"      <OfSystemLibElement>{Esc(instanceOf)}</OfSystemLibElement>\n");
                        xml.Append($"      <OfSystemLibVersion>{Esc(version!)}</OfSystemLibVersion>\n");
                    }
                    xml.Append($"      <ProgrammingLanguage>{lang}</ProgrammingLanguage>\n");
                    xml.Append("    </AttributeList>\n  </SW.Blocks.InstanceDB>\n</Document>\n");

                    File.WriteAllText(path, xml.ToString(), new System.Text.UTF8Encoding(true));
                    ImportWithRetry(target, new FileInfo(path), ImportOptions.None, true);
                    return null;
                }
                catch (Exception ex)
                {
                    string msg = ShortMessage(ex);
                    if (!errors.Contains(msg))
                        errors.Add(msg);
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }
            return string.Join(" || ", errors);
        }

        /// <summary>(nom del DB, instrucció/bloc instanciat, versió si és instrucció de llibreria) de les crides amb instància global.</summary>
        private static IEnumerable<(string DbName, string InstanceOf, string? Version)> ReadGlobalInstanceCalls(string xmlPath)
        {
            System.Xml.Linq.XDocument doc = System.Xml.Linq.XDocument.Load(xmlPath);
            foreach (System.Xml.Linq.XElement instance in doc.Descendants().Where(e => e.Name.LocalName == "Instance"))
            {
                if (!string.Equals((string?)instance.Attribute("Scope"), "GlobalVariable", StringComparison.Ordinal))
                    continue;

                // <Part Name="ESTOP1"><Instance/></Part> o <CallInfo Name="FB_x"><Instance/></CallInfo>
                System.Xml.Linq.XElement? owner = instance.Parent;
                if (owner == null || (owner.Name.LocalName != "Part" && owner.Name.LocalName != "CallInfo"))
                    continue;

                string? instanceOf = (string?)owner.Attribute("Name");
                string? dbName = instance.Elements().FirstOrDefault(e => e.Name.LocalName == "Component")
                    ?.Attribute("Name")?.Value;
                if (!string.IsNullOrWhiteSpace(instanceOf) && !string.IsNullOrWhiteSpace(dbName))
                    yield return (dbName!, instanceOf!, (string?)owner.Attribute("Version"));
            }
        }

        private PlcBlock? Reimport(List<PendingImport> items, string name)
        {
            PendingImport? p = items.FirstOrDefault(i => string.Equals(i.Spec.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null)
                return null;
            ImportWithRetry(TargetBlocks(p.Software, p.Spec.GroupPath), p.File, ImportOptions.None, false);
            return FindBlock(p.Software, name);
        }

        private static PlcBlock? FindBlock(PlcSoftware software, string name) =>
            FindBlockOwner(software.BlockGroup, name)?.Blocks.Find(name)
            ?? FindSystemBlock(software.BlockGroup.SystemBlockGroups, name);

        private static PlcBlock? FindSystemBlock(PlcSystemBlockGroupComposition groups, string name)
        {
            try
            {
                foreach (PlcSystemBlockGroup group in groups)
                {
                    PlcBlock? block = Safe(() => group.Blocks.Find(name)) ?? FindSystemBlock(group.Groups, name);
                    if (block != null)
                        return block;
                }
            }
            catch { /* grup de sistema no accessible */ }
            return null;
        }

        private static SafetyAdministration? GetSafetyAdministration(PlcSoftware software)
        {
            SafetyAdministration? admin = Safe(() => ((IEngineeringServiceProvider)software).GetService<SafetyAdministration>());
            if (admin != null)
                return admin;

            // El servei també penja del DeviceItem de la CPU (pare del SoftwareContainer).
            IEngineeringObject? parent = software.Parent;
            while (parent != null)
            {
                if (parent is IEngineeringServiceProvider provider)
                {
                    admin = Safe(() => provider.GetService<SafetyAdministration>());
                    if (admin != null)
                        return admin;
                }
                if (parent is Device)
                    break;
                parent = parent.Parent;
            }
            return null;
        }

        private static T? Safe<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }

        private static void TrySet(Action setter)
        {
            try { setter(); }
            catch { /* atribut no modificable en aquesta CPU: es manté el valor per defecte */ }
        }

        private sealed class PendingImport
        {
            public PendingImport(BlockSpec spec, BlockGenerationItemReport item, PlcSoftware software,
                FileInfo file, bool preExisting)
            {
                Spec = spec;
                Item = item;
                Software = software;
                File = file;
                PreExisting = preExisting;
            }

            public BlockSpec Spec { get; }
            public BlockGenerationItemReport Item { get; }
            public PlcSoftware Software { get; }
            public FileInfo File { get; }
            public bool PreExisting { get; }
            public string? LastError { get; set; }
        }

        private void ImportInPasses(
            List<PendingImport> pending,
            BlocksGenerationReport report,
            BlocksGenerationOptions options,
            string label,
            bool failAsSkipped = false)
        {
            int pass = 0;
            while (pending.Count > 0)
            {
                pass++;
                var failed = new List<PendingImport>();
                foreach (PendingImport p in pending)
                {
                    try
                    {
                        // Si una passada anterior ha deixat l'objecte creat a mitges,
                        // cal sobreescriure'l.
                        bool existsNow = p.PreExisting || Exists(p.Software, p.Spec);
                        ImportOptions importOptions = existsNow ? ImportOptions.Override : ImportOptions.None;

                        int count = IsType(p.Spec)
                            ? ImportTypeWithRetry(EnsureTypeGroup(p.Software.TypeGroup, p.Spec.GroupPath).Types,
                                p.File, importOptions, options.AllowMissingReferences).Count
                            : IsTagTable(p.Spec)
                                ? ImportTagTable(p, importOptions)
                                : ImportWithRetry(
                                    (IsSafety(p.Spec) ? FindBlockOwner(p.Software.BlockGroup, p.Spec.Name)?.Blocks : null)
                                        ?? TargetBlocks(p.Software, p.Spec.GroupPath),
                                    p.File, importOptions, options.AllowMissingReferences).Count;

                        p.Item.ImportedCount = count;
                        p.Item.Action = p.PreExisting ? BlockGenerationAction.Overridden : BlockGenerationAction.Imported;
                        p.Item.Error = null;
                    }
                    catch (Exception ex)
                    {
                        p.LastError = ShortMessage(ex);
                        failed.Add(p);
                    }
                }

                int succeeded = pending.Count - failed.Count;
                Console.WriteLine($"  Passada {pass} ({label}): {succeeded} importats, {failed.Count} pendents.");
                if (succeeded == 0)
                {
                    foreach (PendingImport p in failed)
                    {
                        if (failAsSkipped)
                        {
                            p.Item.Action = BlockGenerationAction.Skipped;
                            p.Item.Note = $"bloc de seguretat ({p.Spec.ProgrammingLanguage}) no importat: " +
                                          (p.LastError ?? "error desconegut");
                        }
                        else
                        {
                            Fail(report, p.Spec, p.Item, p.LastError ?? "Error desconegut.");
                        }
                    }
                    return;
                }

                pending = failed;
            }
        }

        private static void Fail(BlocksGenerationReport report, BlockSpec spec, BlockGenerationItemReport item, string error)
        {
            item.Action = BlockGenerationAction.Failed;
            item.Error = error;
            report.Errors.Add(
                $"{spec.Device}/{(string.IsNullOrWhiteSpace(spec.GroupPath) ? "(root)" : spec.GroupPath)}/{spec.Name}: {error}");
        }

        private static bool IsType(BlockSpec spec) =>
            string.Equals(spec.Kind, "Type", StringComparison.OrdinalIgnoreCase);

        private static bool IsTagTable(BlockSpec spec) =>
            string.Equals(spec.Kind, "TagTable", StringComparison.OrdinalIgnoreCase);

        /// <summary>Blocs de seguretat (F_FBD, F_LAD, F_DB, F_STL...).</summary>
        public static bool IsSafety(BlockSpec spec) =>
            (spec.ProgrammingLanguage ?? string.Empty).StartsWith("F_", StringComparison.OrdinalIgnoreCase);

        private static bool Exists(PlcSoftware software, BlockSpec spec)
        {
            if (IsType(spec))
            {
                PlcTypeGroup? typeGroup = FindTypeGroup(software.TypeGroup, spec.GroupPath);
                return typeGroup?.Types.Find(spec.Name) != null;
            }

            // El nom d'una taula de variables és únic a tot el PLC (sigui quin
            // sigui el grup), així que es busca a tot l'arbre.
            if (IsTagTable(spec))
                return FindTagTableOwner(software.TagTableGroup, spec.Name) != null;

            if (BlocksExportService.IsSystemGroupPath(spec.GroupPath))
            {
                // DB de sistema: pot ser al grup de sistema o (si s'hi va importar) a l'arrel.
                PlcSystemBlockGroup? systemGroup = BlocksExportService.FindSystemGroup(software.BlockGroup, spec.GroupPath);
                return systemGroup?.Blocks.Find(spec.Name) != null
                    || software.BlockGroup.Blocks.Find(spec.Name) != null;
            }

            // Blocs F: el template ja en porta (Main_Safety_RTGx) en qualsevol grup.
            if (IsSafety(spec))
                return FindBlockOwner(software.BlockGroup, spec.Name) != null;

            PlcBlockGroup? group = FindBlockGroup(software.BlockGroup, spec.GroupPath);
            return group?.Blocks.Find(spec.Name) != null;
        }

        /// <summary>Grup (a qualsevol nivell) que conté un bloc amb aquest nom.</summary>
        private static PlcBlockGroup? FindBlockOwner(PlcBlockGroup group, string name)
        {
            if (group.Blocks.Find(name) != null)
                return group;
            foreach (PlcBlockUserGroup child in group.Groups)
            {
                PlcBlockGroup? found = FindBlockOwner(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Composició on s'importa el bloc. Per als DB de sistema ("[System]/...")
        /// és el grup de sistema si ja existeix al template; si no, l'arrel de
        /// "Program blocks" (TIA no deixa crear grups de sistema).
        /// </summary>
        private static PlcBlockComposition TargetBlocks(PlcSoftware software, string? groupPath)
        {
            if (BlocksExportService.IsSystemGroupPath(groupPath))
            {
                PlcSystemBlockGroup? systemGroup = BlocksExportService.FindSystemGroup(software.BlockGroup, groupPath ?? string.Empty);
                return systemGroup?.Blocks ?? software.BlockGroup.Blocks;
            }

            return EnsureGroup(software.BlockGroup, groupPath ?? string.Empty).Blocks;
        }

        private static PlcBlockGroup? FindBlockGroup(PlcBlockGroup root, string groupPath)
        {
            PlcBlockGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcBlockUserGroup? child = current.Groups.Find(segment);
                if (child == null)
                    return null;
                current = child;
            }
            return current;
        }

        private static PlcTypeGroup? FindTypeGroup(PlcTypeGroup root, string groupPath)
        {
            PlcTypeGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcTypeUserGroup? child = current.Groups.Find(segment);
                if (child == null)
                    return null;
                current = child;
            }
            return current;
        }

        private static PlcTypeGroup EnsureTypeGroup(PlcTypeGroup root, string groupPath)
        {
            PlcTypeGroup current = root;
            foreach (string segment in SplitGroup(groupPath))
            {
                PlcTypeUserGroup? child = current.Groups.Find(segment);
                child ??= current.Groups.Create(segment);
                current = child;
            }
            return current;
        }

        /// <summary>
        /// Importa una taula de variables. Si ja n'hi ha una amb el mateix nom en
        /// qualsevol grup, se sobreescriu allà mateix (TIA no permet dues taules
        /// amb el mateix nom). Si no, es crea al GroupPath de la font.
        /// </summary>
        private static int ImportTagTable(PendingImport p, ImportOptions importOptions)
        {
            PlcTagTableGroup root = p.Software.TagTableGroup;
            PlcTagTableGroup? owner = FindTagTableOwner(root, p.Spec.Name);
            if (owner != null)
                return ImportTagTableWithRetry(owner.TagTables, p.File, importOptions | ImportOptions.Override);

            return ImportTagTableWithRetry(EnsureTagTableGroup(root, p.Spec.GroupPath).TagTables, p.File, importOptions);
        }

        private static PlcTagTableGroup? FindTagTableOwner(PlcTagTableGroup group, string name)
        {
            if (group.TagTables.Find(name) != null)
                return group;
            foreach (PlcTagTableUserGroup child in group.Groups)
            {
                PlcTagTableGroup? found = FindTagTableOwner(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Resol/crea el grup de taules. Els noms de grup poden contenir '/'
        /// (p. ex. "E/S"), així que a cada nivell es prova primer el prefix més
        /// llarg de segments que coincideixi amb un grup existent. Si no n'hi ha
        /// cap, es crea un sol grup amb tota la resta del camí com a nom.
        /// </summary>
        private static PlcTagTableGroup EnsureTagTableGroup(PlcTagTableGroup root, string groupPath)
        {
            PlcTagTableGroup current = root;
            string[] segments = SplitGroup(groupPath);
            int i = 0;
            while (i < segments.Length)
            {
                PlcTagTableUserGroup? child = null;
                int used = 0;
                for (int len = segments.Length - i; len >= 1 && child == null; len--)
                {
                    child = current.Groups.Find(string.Join("/", segments, i, len));
                    if (child != null)
                        used = len;
                }

                if (child == null)
                {
                    used = segments.Length - i;
                    try
                    {
                        child = current.Groups.Create(string.Join("/", segments, i, used));
                    }
                    catch (Exception) when (used > 1)
                    {
                        // Nom amb '/' no acceptat: crea els grups niats un a un.
                        used = 1;
                        child = current.Groups.Create(segments[i]);
                    }
                }

                current = child;
                i += used;
            }
            return current;
        }

        private static int ImportTagTableWithRetry(
            PlcTagTableComposition composition,
            FileInfo file,
            ImportOptions importOptions)
        {
            try
            {
                composition.Import(file, importOptions);
            }
            catch (Exception first)
            {
                try
                {
                    composition.Import(file, importOptions | ImportOptions.SkipInactiveCultures);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(CombineErrors(first, retry));
                }
            }
            return 1;
        }

        private static string[] SplitGroup(string? groupPath) =>
            (groupPath ?? string.Empty).Trim().Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        private static IList<PlcType> ImportTypeWithRetry(
            PlcTypeComposition composition,
            FileInfo file,
            ImportOptions importOptions,
            bool allowMissingReferences)
        {
            try
            {
                return composition.Import(file, importOptions, SWImportOptions.None);
            }
            catch (Exception first)
            {
                var retryOptions = importOptions | ImportOptions.SkipInactiveCultures;
                var swOptions = allowMissingReferences
                    ? SWImportOptions.IgnoreMissingReferencedObjects
                    : SWImportOptions.None;
                try
                {
                    return composition.Import(file, retryOptions, swOptions);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(CombineErrors(first, retry));
                }
            }
        }

        private static string CombineErrors(Exception first, Exception retry)
        {
            string a = ShortMessage(first);
            string b = ShortMessage(retry);
            return string.Equals(a, b, StringComparison.Ordinal) ? a : $"{a} | Reintent: {b}";
        }

        /// <summary>Darrer missatge útil de la cadena d'excepcions (TIA repeteix els embolcalls).</summary>
        private static string ShortMessage(Exception exception)
        {
            var parts = new List<string>();
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                foreach (string line in e.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("Error when calling method", StringComparison.Ordinal))
                        continue;
                    if (!parts.Contains(t))
                        parts.Add(t);
                }
            }
            return parts.Count == 0 ? exception.Message.Trim() : string.Join(" ", parts);
        }

        private static int OrderFor(string blockType)
        {
            return ImportOrder.TryGetValue(blockType ?? string.Empty, out int order)
                ? order
                : 99;
        }

        private string ResolveSourceFile(string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
                throw new InvalidOperationException("SourceFile és buit.");

            string normalized = sourceFile.Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(
                _docsDir,
                normalized.Replace('/', Path.DirectorySeparatorChar)));

            string root = _docsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SourceFile surt del directori de documents: '{sourceFile}'.");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"No existeix el fitxer SimaticML: '{sourceFile}'.");

            return fullPath;
        }

        private static PlcSoftware? FindPlcSoftware(Device device)
        {
            return TagsExportService.FindSoftwareContainer(device)?.Software as PlcSoftware;
        }

        private static PlcBlockGroup EnsureGroup(PlcBlockGroup root, string groupPath)
        {
            string normalized = (groupPath ?? string.Empty).Trim().Trim('/');
            if (normalized.Length == 0)
                return root;

            PlcBlockGroup current = root;
            foreach (string segment in normalized.Split(
                         new[] { '/' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                PlcBlockUserGroup? child = current.Groups.Find(segment);
                child ??= current.Groups.Create(segment);
                current = child;
            }

            return current;
        }

        private static string? ReadInstanceOfName(string xmlPath)
        {
            try
            {
                return System.Xml.Linq.XDocument.Load(xmlPath).Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "InstanceOfName")?.Value;
            }
            catch
            {
                return null;
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ReadOnlyAttributeError =
            new System.Text.RegularExpressions.Regex(@"Missing 'ReadOnly' XML attribute for the '([A-Za-z0-9_]+)' attribute");

        /// <summary>
        /// Import tolerant amb atributs que la CPU destí té com a només lectura
        /// (p.ex. DBAccessibleFromWebserver): es treuen d'una còpia temporal de l'XML i es reintenta.
        /// </summary>
        private static IList<PlcBlock> ImportWithRetry(
            PlcBlockComposition composition,
            FileInfo file,
            ImportOptions importOptions,
            bool allowMissingReferences)
        {
            var removed = new HashSet<string>(StringComparer.Ordinal);
            string? tempPath = null;
            try
            {
                while (true)
                {
                    try
                    {
                        return ImportWithRetryCore(composition, tempPath == null ? file : new FileInfo(tempPath),
                            importOptions, allowMissingReferences);
                    }
                    catch (Exception ex)
                    {
                        var match = ReadOnlyAttributeError.Match(ex.Message);
                        if (!match.Success || !removed.Add(match.Groups[1].Value) || removed.Count > 5)
                            throw;

                        System.Xml.Linq.XDocument doc = System.Xml.Linq.XDocument.Load(tempPath ?? file.FullName);
                        foreach (System.Xml.Linq.XElement e in doc.Descendants()
                                     .Where(e => removed.Contains(e.Name.LocalName)
                                                 && e.Parent?.Name.LocalName == "AttributeList"
                                                 && e.Parent.Parent?.Name.LocalName.StartsWith("SW.", StringComparison.Ordinal) == true)
                                     .ToList())
                            e.Remove();

                        tempPath ??= Path.Combine(Path.GetTempPath(), $"tia_imp_{Guid.NewGuid():N}_{file.Name}");
                        doc.Save(tempPath);
                    }
                }
            }
            finally
            {
                if (tempPath != null)
                    try { File.Delete(tempPath); } catch { }
            }
        }

        private static IList<PlcBlock> ImportWithRetryCore(
            PlcBlockComposition composition,
            FileInfo file,
            ImportOptions importOptions,
            bool allowMissingReferences)
        {
            try
            {
                return composition.Import(file, importOptions, SWImportOptions.None);
            }
            catch (Exception first)
            {
                var retryOptions = importOptions | ImportOptions.SkipInactiveCultures | ImportOptions.Override;
                var swOptions = allowMissingReferences
                    ? SWImportOptions.IgnoreMissingReferencedObjects
                    : SWImportOptions.None;

                try
                {
                    return composition.Import(file, retryOptions, swOptions);
                }
                catch (Exception retry)
                {
                    throw new InvalidOperationException(CombineErrors(first, retry));
                }
            }
        }

        private static void Compile(IEnumerable<PlcSoftware> softwareItems, BlocksGenerationReport report)
        {
            foreach (PlcSoftware software in softwareItems.Distinct())
            {
                try
                {
                    ICompilable? compilable = software.GetService<ICompilable>();
                    if (compilable == null)
                    {
                        report.Errors.Add($"No s'ha trobat el servei ICompilable per al PLC '{software.Name}'.");
                        continue;
                    }

                    report.CompileResult = compilable.Compile();
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Error de compilació del PLC '{software.Name}': {UnwrapMessage(ex)}");
                }
            }
        }

        private static string UnwrapMessage(Exception exception)
        {
            string message = exception.Message;
            for (Exception? inner = exception.InnerException; inner != null; inner = inner.InnerException)
                message += " | " + inner.Message;
            return message;
        }
    }
}
