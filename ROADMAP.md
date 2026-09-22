# ROADMAP - Device generation from documents

> Tracking document for the `TiaOpennessApp` project.
> Each phase has a verification/approval gate. Tick the boxes as they are
> completed and update the **Global progress** table.
> Last update: 2026-09-21
> See [`README.md`](./README.md) for the current state of the application.

---

> The **README.md** describes the current state (read/export). This document
> is the execution plan (write/generation). If there is a discrepancy, the
> real code (`Program.cs`, `Services/`, `Models/`) is the source of truth.

## Global progress

| Phase | Status | Approved |
|-------|--------|----------|
| P0 - Foundations and preparation | Done | Yes |
| F1 - Import models | Done | Yes |
| F2 - Reference catalog | Done | Yes |
| F3 - CSV + XLSX readers | Done | Yes |
| F4 - Validator + dry-run | Done | Yes |
| P5 - Device generation | Done | Yes |
| P6 - Modules per slot | Done | Yes |
| P7 - Subnets | Done | Yes |
| P8 - Interfaces and IPs | Done | Yes |
| P9 - Full flow + auto-comparison | Done | Yes |
| D3 - I/O tags + hardware address map | Done | Yes |
| F10+ - Future work | Pending | - |

---

## P0 - Foundations and preparation

- [x] Add **CsvHelper** (33.1.0) and **ClosedXML** (0.105.1) packages to `.csproj`; `dotnet build` OK (0 errors).
- [x] Unify the `OutputPath`/`ExportPath` configuration key - `appsettings.json` now defines `OutputPath`.
- [x] Extend `appsettings.json` with the **Import** section:
      `DevicesPath`, `ModulesPath`, `SubnetsPath`, `InterfacesPath`, `CatalogPath`, `TemplatePath`.
- [x] Create structure: `imports/docs/`, `imports/catalog/` (the sandbox project is created on the first generation attempt).

**Verification**: clean build and the app connects to TIA V21.
**Approval** -> F1.

- [x] Approved

---

## F1 - Import models (DTO, without TIA)

- [x] `Models/Generation/`: `GenerationDocument`, `DeviceSpec`, `ModuleSpec`, `SubnetSpec`, `InterfaceSpec`, `CatalogEntry` (+ enum `CatalogItemRole`). Compilation OK (0 errors).

**Verification**: compilation + instantiation tests.
**Approval** -> F2.

- [x] Approved

---

## F2 - Reference catalog (Catalog.csv) + seeding

- [x] Define `Catalog.csv` format (`TypeKey,MLFB,TiaVersion,Description,Role`).
- [x] Automatic seeding of candidates from real exports
      (`exports/network_devices.json` of AE82 -> 6 entries).
- [x] Distinguish `Role=Root` vs `Module` (CPU and IM -> Root; the rest -> Module; the wildcard rack is discarded).
- [x] New **`Services/CatalogService.cs`** (no TIA): `Load`, `Save`, `Merge` (idempotent by `TypeKey+TiaVersion`), `GenerateCandidates`.
- [x] Menu "4. Generate Catalog -> Catalog.csv" (from the open project) + CLI `--gen-catalog-from-export <json> [csv]`.
- [x] GATE: Manual approval of the Catalog.csv content (F2 -> F3).

**Verification**: every `TypeKey` of the sample docs resolves; no duplicates.
**Approval**: manual content review -> F3.

- [x] Approved

---

## F3 - CSV + XLSX readers + Excel template

- [x] `CsvGenerationReader` (CsvHelper).
- [x] `ExcelGenerationReader` + `ExcelTemplateGenerator` (headers + catalog dropdowns).
- [x] Parse errors with row/column (`ParseIssue` list; `Source:Row:Column`).
- [x] CLI: `--generate-template <xlsx> [catalogCsv]`, `--csv-to-xlsx <docsDir> <xlsx> [catalogCsv]`,
      `--verify-readers <docsDir> <xlsx>` (CSV<->XLSX equivalence by JSON).
- [x] Sample docs in `imports/docs/` (2 devices, 6 modules, 1 subnet, 3 interfaces -> real AE82 data).

**Verification**: same document in CSV and XLSX -> identical DTOs; malformed input -> clear errors.
**Approval** -> F4.

- [x] Approved

---

## F4 - Validator + dry-run report (before touching TIA)

Rules:
- [x] Valid names (TIA and PROFINET rules).
- [x] Uniqueness (device/subnet/interface).
- [x] Valid IPv4, unique per subnet.
- [x] Coherent subnet mask.
- [x] Slot collisions.
- [x] `TypeKey` present in catalog for TIA21.
- [x] Destination folder empty.

Output:
- [x] List of planned operations + `WARN`/`ERROR`.
- [x] CLI `--validate-docs <docsDir> [catalogCsv] [projectFolder]`.
- [x] Good AE82 docs -> "Result: 0 error(s), 0 warning(s). OK".

**Verification**: good docs = 0 errors; bad docs = expected errors.
**Approval**: dry-run of a real case -> P5.

- [x] Approved

---

## P5 - Device generation (Openness)

- [x] `DevicesGenerationService`: `Find(name)` first (idempotency) + `CreateWithItem` (identifier by MLFB).
- [x] Per-item `try/catch` and report; attempt MLFB with and without spaces; optional firmware suffix.
- [x] CLI `--gen-devices-sandbox <docsDir> [catalogCsv] [outJson]`.
- [x] Validation + sandbox project create/open pipeline (NewProjectPath/NewProjectName).
- [x] `Identifier` column in the catalog (`OrderNumber:.../Vfw`) - the exact type requires firmware; obtained via `--dump-type-id` from the real project.
- [x] Real test: `PLC_82` (S7-1500 + CPU 1515-2 PN) and `ET200SP_01` (ET200SP + IM 155-6 PN ST + BA) created; re-run -> `0 created, 2 already existed`.

**Verification**: in the sandbox, devices exist with the correct type; re-run does not duplicate.
**Approval** -> P6.

- [x] Approved

---

## P6 - Modules per slot

- [x] `ModulesGenerationService`: `HardwareObject.PlugNew(typeIdentifier, name, positionNumber)`.
- [x] Slot occupancy detection via `target.Items.Find(slot)` (idempotency); `CanPlugNew` first.
- [x] Skip the device root item and modules without slot (e.g. BA) with justified skip.
- [x] CLI `--plug-diagnose <projectPath>` (free slots + CanPlugNew per identifier).
- [x] `PositionNumber` in the export; `--export-project` (renamed `--export-docs` in D1) added.
- [x] Real AE82 slots discovered: IM@0, BA@127, DQ@1, DI@2, Server@3 -> `Modules.csv` fixed (were 1/4/5/63).
- [x] Real test: DQ16@1, DI16@2, Server@3 plugged; re-run idempotent (0 plugged, 6 skips); export = real positions.

**Verification**: modules in correct slots in TIA.
**Approval** -> P7.

- [x] Approved

---

## P7 - Subnets

- [x] `project.Subnets.Find(name)` -> if missing: `SubnetComposition.Create(typeIdentifier, name)`.
- [x] Exact `typeIdentifier` confirmed on the first test: **`System:Subnet.Ethernet`** (not `"PN/IE"`; `"PROFIBUS"` -> `System:Subnet.Profibus`).
- [x] `SubnetsGenerationService` + `--dump-subnets <projectPath>` diagnostic.
- [x] Real test: subnet `FIELD BUS` created in the sandbox (same type as real AE82); re-run idempotent -> `0 created, 1 already existed`.

**Verification**: subnets exist and are well named.
**Approval** -> P8.

- [x] Approved

---

## P8 - Interfaces and IPs (nodes)

- [x] Locate interface (`GetService<NetworkInterface>()` + `Nodes`).
- [x] Assign `IPAddress`/`SubnetMask` (via attributes, mirror of the read path).
- [x] `node.ConnectToSubnet(subnet)`.
- [x] Exact write attributes confirmed on the first test: **`Address`** and **`SubnetMask`** live on the `Node`; connection via `Node.ConnectToSubnet(subnet)`.
- [x] Note: `PnDeviceName` is **read-only** on `Node` in V21 (auto-naming `PnDeviceNameSetDirectly=False`; the real AE82 also uses generated names) -> the app reports it as non-fatal and keeps auto-naming.
- [x] Real test: sandbox matches AE82 (X1 `172.16.82.2` @ FIELD BUS, X2 `172.16.0.82` not connected, IE1 `172.16.82.3` @ FIELD BUS; masks `255.255.255.0`); re-run idempotent (`3 skipped, 0 errors`).

**Verification**: correct IPs and connections in TIA (offline check and, if permissions allow, online diff).
**Approval** -> P9.

- [x] Approved

---

## P9 - Full flow integration + auto-comparison

- [x] New menu in `Program.cs`: "Generate project from documents" -> full pipeline, final per-item report, `Save()`.
- [x] Self-check: generate the project from the documents; then use
      `DevicesAndNetworksService.ExportNetworkDevices()` on the generated project and on the source
      to produce a topology diff report (devices, modules, IPs, subnets).

**Final verification**: diff report = zero differences (or explained differences).
Real test (2026-09-15, elevated + real AE82 reference export): `--generate-project imports/docs imports/catalog/Catalog.csv exports/real_network_devices.json`
-> devices 2, modules 7, interfaces 3, subnets 1 compared; **0 differences, 5 explained (naming only); PASSED**.
Artifacts: `exports/sandbox_network_devices.json`, `exports/topology_diff.json`.
Offline tool: `--diff-topology <expected.json> <actual.json>`.
**Final approval**. Approved.

- [x] Approved

---

## F10+ - Future work (out of this plan, same pipeline)

- [ ] Blocks (`PlcBlockComposition.CreateFrom(...)`).
- [ ] DBs.
- [x] Tag tables (`PlcTagComposition.Create`; see `TagsGenerationService` + `IoTags.csv`, done 2026-09-21).
- [ ] HMI.

### Done outside the original plan (2026-09-21)

- [x] **I/O tag export + generation** (`TagsExportService`, `TagsGenerationService`, `IoTags.csv`): 650 tags exported from TEST_TIAO and recreated idempotently in the generated project (`Tags: 650 re-exported, source 650`).
- [x] **Manual I/Q hardware address map** (`TagsExportService.ExportModuleAddresses`, `ModuleAddressSpec`, `ModuleAddressesGenerationService`, `ModuleAddresses.csv`): reproduces the source sparse byte addressing on the generated project. Global (shared address space) three-pass conflict resolution. Verified: 74 addresses applied; ETH03/ETH04/ETH05 match TEST_TIAO; round-trip `HwAddr` discrepancies = 60 GSD-only rows.

> Blocks, DBs and HMI remain as the next extension.

---

## Change log

| Date | Phase | Change |
|------|-------|--------|
| 2026-09-14 | - | Document created from the roadmap. |
| 2026-09-14 | P0 | CsvHelper+ClosedXML packages, unified `OutputPath`, `Import` section, folders, build OK, AllowList registered. Approved. |
| 2026-09-14 | F1 | `Models/Generation/*` models (6 classes) without TIA. Build OK. Approved. |
| 2026-09-14 | F2 | `CatalogService`, menu 4 + CLI `--gen-catalog-from-export`, initial seeding of 6 entries, idempotent merge. Approved. |
| 2026-09-14 | F3 | `IGenerationReader`, `CsvGenerationReader`, `ExcelGenerationReader`, `ExcelTemplateGenerator`, CLI `--generate-template`/`--csv-to-xlsx`/`--verify-readers`, AE82 sample docs, malformed input detected. CSV==XLSX identical (0 errors). Approved. |
| 2026-09-14 | F4 | Validator + dry-run started. |
| 2026-09-14 | F4 | `ValidationService` + `ValidationReport`/`ValidationIssue`; CLI `--validate-docs`; rules names/uniqueness/IP/mask/slots/catalog/folder; good docs 0 errors, bad docs 11 errors (all expected). Approved. |
| 2026-09-14 | P5 | `DevicesGenerationService` + CLI `--gen-devices-sandbox`; firmware suffix required in identifiers (discovered via `--dump-type-id`); `Identifier` column added to the catalog; real test OK (2 created, correct types) + idempotent re-run (0 created). Approved. |
| 2026-09-15 | P6 | `ModulesGenerationService` (PlugNew/CanPlugNew, per-slot idempotency); `--plug-diagnose` and `--export-project`; `PositionNumber` in the export; real AE82 slots (IM@0, DQ@1, DI@2, Server@3) -> docs fixed; real test 3 plugged OK + idempotent re-run. Approved. |
| 2026-09-15 | P7 | `SubnetsGenerationService` + `--dump-subnets`; exact typeIdentifier confirmed: `System:Subnet.Ethernet` (not "PN/IE"; PROFIBUS -> `System:Subnet.Profibus`); subnet `FIELD BUS` created in sandbox; re-run idempotent (0 created, 1 already existed). Approved. |
| 2026-09-15 | P8 | `InterfacesGenerationService` + `--dump-interfaces`; write attrs confirmed: Node `Address` / `SubnetMask` + `node.ConnectToSubnet(subnet)`; `PnDeviceName` read-only on Node in V21 (auto-naming kept, matches AE82); sandbox matches AE82 (172.16.82.2/.3 @ FIELD BUS, 172.16.0.82 disconnected, masks set); re-run idempotent (3 skipped, 0 errors). Approved. |
| 2026-09-15 | P9 | `TopologyDiffService` + `TopologyDiffItem/Report`; CLI `--generate-project` (full pipeline: parse→validate→generate→Save→export→diff) and `--diff-topology` (offline); menu principal opció 3; real test PASSED: 2 devices/7 modules/3 interfaces/1 subnet compared, 0 differences, 5 explained (naming dels documents); artifacts `exports/sandbox_network_devices.json` + `exports/topology_diff.json`. Approved. |
| 2026-09-15 | P9 | Ajust de menús: `0. Exit` del menú de projecte torna al menú principal; el menú principal fa bucle i `0. Sortir` finalitza l'aplicació. Approved. |
| 2026-09-15 | P9 | Generació sense auto-comparació: es treu la comparació del flux interactiu i de `--generate-project` (es manté `--diff-topology` per comparar 2 projectes en el futur). Approved. |
| 2026-09-16 | D1 | Export de documents (Propuesta 3, inversa de `--export-project`): `--export-docs <project|exportJson> [outDocsDir] [catalogCsv]`; `DocsExportService`; `NetworkInterfaceInfo` ampliat amb `SubnetMask`/`PnDeviceName`; test offline PASSED des de `real_network_devices.json` (2/6/1/3, 0 TypeKeys faltants). |
| 2026-09-16 | D1 | Ajusts `--export-docs`: noms de mòdul GSD sanititzats (`BA 2xRJ45`→`BA2xRJ45`) per passar la validació de noms TIA; màscara per defecte `/24` quan hi ha IP; `imports\docs` adoptat amb els docs generats des de l'AE82 real. |
| 2026-09-16 | D1 | Round-trip real amb TIA PASSED: `--generate-project` des de `imports\docs` → AE82_New (2 devices, 3 mòduls plugueats, 1 subnet, 3 interfícies; 0 errors) + `--diff-topology` real vs sandbox: 0 diferències dures, 3 explicades (naming). Approved. |
| 2026-09-16 | P5 | Pipeline amb dades completes d'AE82 (imports\Devices/Modules/Subnets/Interfaces.csv, 15 dispositius): **Siemens OK** (CPU21 + ETH03/04/05 ET200SP, 65 mòduls plugueats, 2 subxarxes). `Identifier` del catàleg omplert amb els TypeIdentifier reals de TIA via `--dump-type-id` sobre TEST_TIAO (`GSD:.../D`, `OrderNumber:.../Vfw`). |
| 2026-09-16 | GSD | **Limitació detectada**: els 11 dispositius GSD de tercers (SEW UFRXXB ×3, DFE MDX61B+DFE32B ×7, FANUC ×1) NO es poden crear via `CreateWithItem` (Openness) amb cap error de codi: TIA retorna `Necessary license 'gsdml-...xml' is missing` tant en mode headless com en attach. Es confirmen **creables des de la GUI de TIA** (test TEST_TIAO_2: SEW + FANUC inserits i compilats sense error). Conclusió: restricció de llicències GSD aplicada per TIA a la via API; no és un defecte del pipeline. Els GSD s'afegiran manualment des de la GUI. |
| 2026-09-17 | D1 | Commit `Generación CSV/Excel, validación y nuevos servicios`: afegit `--roundtrip-test <srcProject> <workDir> <targetDir> <projectName> [catalogSeed]` (obrir font → generar catàleg i 4 CSVs → generació completa → re-export → `TopologyDiff` contra la font; gaps GSD classificats com a coneguts; exit code PASS/FAIL). Menú principal consolidat (1. Obrir / 2. Crear / 3. Generar / 0. Sortir) amb submenú de projecte que inclou l'opció 4 "Generate Catalog → Catalog.csv". |
| 2026-09-18 | D2 | Working tree: els valors teclejats al menú (rutes/noms) es persisteixen a `appsettings.json` (`ReadAndRememberValue`/`PersistAppSettings`); el bucle del menú recupera errors amb "Press any key to continue" sense tancar l'aplicació; fallada de connexió amb missatge clar i sortida. CLI.md actualitzat amb `--roundtrip-test`, signatura ampliada de `--generate-project` (targetDirectory/projectName) i arg `[knownGapsJson]` de `--diff-topology`. |
| 2026-09-18 | D2 | Config keys: la clau històrica `OutputPath` (P0) queda substituïda per la secció `Export:*` (`DocsPath`, `DevicesPath`, `ModulesPath`, `SubnetsPath`, `InterfacesPath`, `CatalogPath`, `TemplatePath`) usada per `--export-docs`, `--dump-*` i la resolució de catàleg. Es manté `Import:*` per als documents d'entrada. |
| 2026-09-21 | D3 | **I/O tags**: `TagsExportService.ExportIoTags` + `IoTags.csv` (5è/6è document), `IoTagSpec`, `TagsGenerationService` (recrea taules i tags idempotent), `--dump-tags`, pas `I/O TAG GENERATION` al pipeline; `CsvGenerationWriter` i `--export-docs` ampliats. Test real: 650 tags exportats de TEST_TIAO i re-exportats idèntics al generat. |
| 2026-09-21 | D3 | **Mapa d'adreces de maquinari I/Q**: `TagsExportService.ExportModuleAddresses` + `ModuleAddresses.csv`, `ModuleAddressSpec`, `ModuleAddressesGenerationService`, comparativa `HwAddr` al roundtrip; `--export-docs` i `--generate-project` ampliats. TIA auto-assigna adreces denses en crear maquinari, cal re-assignar `Address.StartAddress` als valors manuals/esparsos de la font. |
| 2026-09-21 | D3 | **Fix conflictes "address already in use"**: l'espai d'adreces I/Q és **global** (compartit entre ETH03/04/05). Resolució en 3 fases: (A) aparcar pendents en zona alta trobada per *probe*, (A2) aparcar qualsevol altra adreça del projecte que ocupi un rang objectiu, (B) assignar valors finals. Requereix desar+reobrir el projecte abans del pas HW (`RunFullGenerationPipeline`) perquè TIA materialitzi les adreces. Verificat a TIA: ETH03/04/05 coincideixen amb la font; 74 adreces aplicades, 60 errors (GSD). |
| 2026-09-21 | D3 | **Roundtrip PASS**: `run_roundtrip.bat` → `HwAddr: 60 discrepàncies` (= 60 GSD no creables, 0 errors ETH), `Tags: 650 re-exportats (font 650)`, `Devices: 0 errors durs`, `Subnets: 0`. Resultat: `Automation\result\result.ap21`. |
| 2026-09-22 | GSD/CAx | **Gate CAx superat**: la via `CaxProvider.Import(.aml, RetainTiaDevice)` importa els dispositius GSD **sense** la llicència `gsdml` (PoC `--gsd-cax-poc`: export del projecte sencer → import en sandbox → submòdul `06 process data words` + adreces `I/Q 816..911` correctes; `State=Warning, Errors=0`). L'export per dispositiu llança `NullReferenceException` a V21 → només projecte sencer; extensió `.aml` (no `.amlx`). |
| 2026-09-22 | D4 | **Integració golden AML (Fase 2)**: nou `--export-cax-project <projectPath> <outAml>`; el round-trip exporta `workDir\golden.aml` al PAS 1-3; `RunFullGenerationPipeline` rep `[goldenAml]` (opcional); si el catàleg té entrades `Identifier` prefix `GSD:` → `GsdCaxImportService.ImportGoldenAndKeep` (import `RetainTiaDevice` + conserva només els dispositius del document, `Device.Delete()` la resta) abans de DEVICE GENERATION. Finders de dispositius de 5 serveis ampliats a grups (`DevicesAndNetworksService.FindDeviceByName` recursiu) perquè els GSD importats (que poden caure en grups) es trobin i es saltin. Build 0 errors / 0 warnings. Pendent: execució del round-trip elevat per validar. |
| 2026-09-22 | D4 | **Roundtrip amb golden validat a TIA (run_roundtrip_golden.bat)**: `CAX GOLDEN IMPORT` conserva els 11 GSD (MC120/UM*/MC121/122/ROBOT1); `Devices: 0 GSD coneguts (0 errors)` — **gaps de llicència ELIMINATS**; `HwAddr: 0 errors, 134/134` (abans 60 discrepàncies GSD); `Modules: 0 errors`; `Tags: 650 creats`; `RESULTAT: PASS`. Golden AML 1.6 MB (`exports\roundtrip\golden.aml`). Bug detectat al bucle d'esborrat (objectes `Device` invalidats per TIA després del primer `Delete` → "disposed object"; CPU21 esborrada + ETH03-05 romanien com a importats) → fixat re-buscant cada dispositiu per nom abans d'esborrar i aïllant errors per dispositiu. |
| 2026-09-22 | D4 | **Fix del bucle d'esborrat validat a TIA (font `TEST\TEST.ap21`, 5 dispositius)**: `CAX GOLDEN IMPORT` OK → conserva `UM6A_PR` (GSD), **esborra CPU21/ETH03/04/05** → `[CREATE]` regenerats pel pipeline + `[EXISTS] UM6A_PR`. `Modules: 65 plugged/8 skipped/0 errors`; `Interfaces: 6 connected/0 errors`; `HwAddr: 76 aplicades, re-export 76/76, 0 errors`; `Tags: 662/662`; `Topology: 0 dures/1 explicada (naming root UM6A_PR)`. **RESULTAT: PASS**. Fase 1+2 (via CAx golden per crear els GSD) completada: gaps de llicència GSD eliminats. |
| 2026-09-22 | D4 | **Warnings del Validator netejades (GSD-aware)**: `ValidationService.IsGsdDevice(TypeKey)` (entrada de catàleg amb `Identifier` prefix `GSD:`); (1) l'advertència "Mòdul no present a Modules.csv" ja no es dispara per a submòduls interns de dispositius GSD (p. ex. `06 process data words`), (2) l'advertència "TypeKey '...' és un Root del catàleg" es suprimeix quan el dispositiu és GSD (root de l'estació vs. TypeKey de l'artefacte, p. ex. `UM6A_PR` = `UFRXXBV17` vs `MDX61BDFE32BV14`), (3) text del warning de "dispositiu no present" simplificat (ja no depèn de la llicència). Validat a TIA: `Result: 0 warning(s)` (el run es va aturar per l'error de slot de la font nova, veure fila següent). |
| 2026-09-22 | D4 | **Font TEST amb dispositiu GSD renombrat (`UM6A_PR_1` / item `UM6B_PR`)**: el nou dispositiu copiat exportava DOS files slot 0 (item estació `UM6B_PR` + root auto `MDX61B+DFE32B V1.4`, mateix OrderNumber) perquè el dedupe de `DocsExportService` només treia la fila `Name == DeviceName` → ERROR de col·lisió de slot. Fix: dedupe generalitzat per (slot + OrderNumber) preferint l'item auto-anomenat (`Name == TypeName`), més `ValidateSlots` GSD-aware (col·lisió en dispositius GSD no bloqueja: l'estructura real ve de l'import golden). Build 0/0. |
| 2026-09-22 | D4 | **Roundtrip final validat a TIA (font `TEST\TEST.ap21` amb 7 dispositius: UM6A_PR, UM6B_PR, MC120 GSD)**: VALIDATION `0 error(s), 0 warning(s)` (abans 3 warnings + error de slot); golden import OK → conservats els 3 GSD, esborrats CPU21/ETH03-05 i regenerats; `Devices: 0 errors/0 gaps`; `Modules/Subnets/Interf.: 0 errors`; `Tags: 722/722`; `HwAddr: 88/88, 0 errors`; `Topology: 0 dures, 3 explicades`; **RESULTAT: PASS** (exit 0). **Abast completat**: via CAx golden integrada al pipeline + warnings del Validator netejades. |
| 2026-09-22 | D4 | **Golden AML al menú interactiu**: l'opció `3. Generate project from documents` ara resol el golden abans de generar: (a) si hi ha projecte obert a la connexió (menú 1) l'exporta automàticament a `%TEMP%\golden_menu_<name>_<time>.aml` (via `CaxTransferService.ExportProject`) i tanca el projecte abans d'obrir el sandbox; (b) si no, prompt "Golden AML path (buit = sense importar GSD)" persistent a la nova clau `Import:GoldenAmlPath` (helper `ReadOptionalValue`). `TiaConnection.HasOpenProject` nou per detectar el projecte obert. Build 0 errors / 0 warnings. |