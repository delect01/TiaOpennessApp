# HANDOFF - on ho hem deixat (dilluns 21/09/2026)

> **Punt actual: mapa d'adreces de maquinari I/Q RESOLT, VERIFICAT I ROUNDTRIP NET.** El pipeline
> aplica al projecte generat les adreces manuals/esparses de TEST_TIAO:
> `Summary: 74 adreces aplicades, 60 errors` (els 60 errors són GSD sense llicència, esperats).
> Confirmat visualment a TIA: **ETH03/ETH04/ETH05 coincideixen perfectament** amb la font.
>
> **Roundtrip automàtic (`run_roundtrip.bat`) → PASS**: `HwAddr: 60 discrepàncies` = exactament
> els 60 GSD no creables (0 errors ETH). `Tags: 650 re-exportats (font 650)`, `Devices: 0 errors
> durs`, `Subnets: 0`. Resultat final: `C:\Users\DPTO_ELECT\Documents\Automation\result\result.ap21`.
>
> Allowlist: `run_app.bat` i `run_roundtrip.bat` **calculen el SHA1 en runtime** (`Get-FileHash`)
> i el registren ells mateixos; no cal editar cap hash a mà. Només cal executar-los com a ADMIN.
> (Hash del build Debug actual: `7A9614D40DEE521575486916A8FA7535902B70A2`.)

## Cadena de problemes descoberts i solucionats (sessió 21/09)
1. **`Addresses` buides al pas HW**: TIA només materialitza les adreces dels mòduls en desar i
   reobrir. → Fix: **Pausa 1** a `RunFullGenerationPipeline` (Program.cs): `project.Save()` +
   tancament explícit (`TiaConnection.ForceCloseProject`) + reobertura. **Millorat 22/09**:
   `TryReopenProject` ja no fa reintents cecs; (1) reutilitza el projecte si TIA encara el té obert,
   (2) espera fins que el portal quedi realment buit (teardown lent: flushes/compilació en segon
   pla), (3) si queda obert un projecte NO nostre (ex. obert per la UI de TIA) ho reporta amb nom i
   camí en comptes de bloquejar-se 6×5s amb "Another project is already open".
2. **`FindItemByName` retornava el mòdul en lloc del submòdul**: mòdul i submòdul s'anomenen
   igual (`ETH03_1`), i `FindItemRecursive` comprovava l'arrel abans que els fills → retornava el
   pare (sense adreces). → Fix: cercar només entre els fills (`ModuleAddressesGenerationService`).
3. **`Address.Length` és read-only** → ja no s'assigna; només es compara/assigna `StartAddress`.
4. **Conflictes "address already in use"**: els valors densos auto-assignats (0..33) ocupen els
   mateixos bytes esparsos de la font (1,3,5...). → Fix: **resolució de conflictes GLOBAL en 3 fases**:
   (A) aparcar totes les adreces pendents en una zona alta (>=10000) trobada **per prova i error**
   (`TryParkAddress`, el cursor només creix), (A2) aparcar qualsevol altra adreça del projecte
   (mòduls no mapejats, `Móduloservidor`, **altres dispositius que comparteixen l'espai d'adreces**)
   que ocupi un rang objectiu, (B) assignar els valors finals. L'espai I/Q és **global** (compartit
   entre ETH03/04/05): no n'hi havia prou de mirar el dispositiu actual.
5. **`ModuleAddresses.csv` "buit"**: pot quedar només amb capçalera si es re-exporta des d'un
   projecte equivocat o sense desar → sempre comprovar `HwAddr: 133` a la sortida de
   `--export-docs` sobre TEST_TIAO abans de regenerar.

## Què encara NO es pot fer (límits coneguts)
- **11 dispositius GSD** (MC120/121/122 SEW, UM3A..6A/UM3B..5B DFE, ROBOT1 FANUC): no es creen via
  Openness (`Necessary license 'gsdml-*'` missing) → els seus errors a `DEVICE GENERATION` i
  `HW ADDRESS GENERATION` són esperats (60 errors del resum). Validat a TIA GUI que es creen → és
  restricció de llicència de l'API, no bug del pipeline.
- Roundtrip automàtic `--roundtrip-test`: **executat i PASS** (`run_roundtrip.bat`, genera
  `Automation\result`). `HwAddr` mostra discrepàncies **només dels 60 GSD** (0 errors ETH). El
  `.bat` no imprimeix al terminal perquè redirigeix tota la sortida a `roundtrip_log.txt`
  (i `roundtrip_log.txt.exit` conté `EXITCODE:`).

## Punts de continuació (18/09, històric)
- Diagnòstic tancat: **les adreces del TEST_TIAO són manuals/esparses**; TIA auto-assigna el
  generat. Solució implementada: `ModuleAddresses.csv` + `ModuleAddressesGenerationService`.
- La fallada "items ETH sense adreces" es va RESOLDRE el 21/09 (veure secció superior).

## Què s'ha fet avui (18/09)
- **Export d'I/O tags complet**: corregit `TagsExportService.FindSoftwareContainer` perquè trobi el
  `PlcSoftware` al `DeviceItem` (CPU), no al `Device` → **650 tags** exportats de TEST_TIAO
  (`exports/IoTags.csv`), associats per rang d'adreces a mòdul/submòdul/dispositiu.
- **Nova columna `IoDevice`** a `IoTags.csv` (dispositiu I/O de maquinari: ETH03/04/05, ROBOT1, MC120).
  Columnes: `Device, IoDevice, ModuleName, SubModuleName, TagTable, Name, Type, Address, Comment`.
  (`IoTagSpec.IoDevice`, `AddressRange.DeviceName`, `GenerationColumns.IoDevice`, `CsvGenerationWriter`).
- **Generació de I/O tags des de CSV (pas F10)**:
  - `GenerationRowParsing.ParseTagRow` + `CsvGenerationReader.ReadIoTags` (5è CSV, **opcional**; si falta → warning "no es generaran I/O tags").
  - **Nou `Services/TagsGenerationService.cs`**: agrupa per `Device`→`TagTable`, troba el `PlcSoftware`
    (reutilitza `FindSoftwareContainer`), crea la taula si no existeix (`TagTables.Create` + `.Name`),
    crea el tag (`Tags.Create(name, type, address)`), posa el `Comment` via `SetAttribute`,
    i és **idempotent** (salta tags/taules ja existents).
  - **`Program.cs`**: `ReadCsvDocs` passa `IoTags.csv`; `RunFullGenerationPipeline` afegeix el pas
    `═══ I/O TAG GENERATION ═══` + `PrintTagReports`; `GenerationRunResult.Tags`; al roundtrip es
    re-exporten els tags i es comparen amb la font (línia `Tags ...` al resum).
  - **`ValidationService`**: verifica que els tags referencien dispositius existents (`IoTags` section).
- **Reproducció del mapa d'adreces de maquinari (pas HW addrs)**:
  - **Nou `ModuleAddressSpec`** (Device, ModuleName, SubModuleName, TypeIdentifier, IoType, Start, Length).
  - `TagsExportService.ExportModuleAddresses()`: per cada `DeviceItem` amb adreces I/Q, el mòdul
    amfitrió (`FindModuleAncestor`) i cada rang (StartAddress/Length) → `ModuleAddresses.csv`
    (noves columnes a `GenerationColumns`: `TypeIdentifier/IoType/StartAddress/Length`).
  - **Nou `ModuleAddressesGenerationService.cs`**: troba el mòdul/submòdul per nom i assigna
    `Address.StartAddress`/`Address.Length` (propietats **Int32 assignables**) → informe per fila.
  - `DocsExportService.Export` accepta adreces; `--export-docs` i el roundtrip els generen; el
    roundtrip compara origen vs re-exportat (`CompareHwAddresses`, línia `HwAddr` del resum).
  - `--validate-docs` valida les referències d'adreces (dispositiu/mòdul/IoType).
- **Comprovat**: `--validate-docs imports` → 0 errors, 7 warnings (preexistents, TypeKeys Root);
  650 tags OK. `dotnet build` → 0 errors / 0 warnings.

## PLA DE TEST ROUND-TRIP per demà (TEST.ap21 → result.ap21)
> Acordat al final del dia 16/09. Si no surt a la primera, es farà un programa de test que automatitzi el bucle.

### Sequència demanada per l'usuari
1. Obrir projecte TIA V21 existent "TEST.ap21".
2. Generar `catalog.csv` segons aquest projecte.
3. Generar els 4 CSV (`Devices/Modules/Subnets/Interfaces.csv`).
4. Crear un projecte nou "result.ap21" amb el catalog + els 4 CSV del pas 3.
5. Verificar que NO surten errors.
6. Si surten errors → modificar codi o estructura dels CSV → tornar al punt 1.

### Mapatge a les eines actuals
| Pas | Comanda actual | Estat |
|-----|----------------|-------|
| 3 | `--export-docs "<TEST.ap21>" <docsDir> [catalog]` | ✅ existeix (genera els 4 CSV) |
| 4 | `--generate-project <docsDir> [catalog] [targetDir] result` | ✅ existeix |
| 5 | Reports del pipeline + `--export-docs "<result.ap21>"` i comparar CSV / `--diff-topology` | ✅ parcial (veure gap saga) |
| 2 | NO hi ha CLI que generi catalog directament des d'un `.ap21` | ⚠️ GAP: només existeix al menú interactiu (opció 4) o via `--gen-catalog-from-export <json>` (cal JSON previ, i no hi ha comanda "projecte → JSON") |

### Gaps a resoldre per automatitzar el bucle
- [ ] **GAP A**: comanda CLI `--gen-catalog <projectPath> [catalogCsv]` (obre el `.ap21` i crida la lògica de `GenerateCatalogFromProject` que ja existeix a Program.cs:905).
- [ ] **GAP B (recomanada)**: comanda CLI `--roundtrip-test <srcProject> <docsDir> <catalogCsv> <targetDir> <projectName>` que faci tot el bucle en procés: export → catalog → generate → re-export → `TopologyDiffService.Compare` → 0 errors → PASS/FAIL. Així no cal parsejar text de consola.
- [ ] (Opcional) comanda `--export-project <projectPath> <outJson>` per obtenir el JSON de topologia d'un `.ap21` (dona de menjar a `--gen-catalog-from-export` i `--diff-topology`).

### Pitfalls coneguts que el test toparà (a resoldre demà)
1. **Noms d'interfície**: els CSV exportats porten els noms de l'item del projecte origen (ex. `Interfaz PROFINET_1` — noms personalitzats en castellà). Els dispositius **nous** (creats per `CreateWithItem`) reben el nom per defecte que TIA assigna. `FindItemByName` (InterfacesGenerationService.cs:323-345) és **exacte i case-sensitive** → error "Item d'interfície no trobat". El round-trip ho exposarà. Fixes possibles (a decidir): (A) a l'export, usar **TypeIdentifier** com a selector enlloc del nom; (B) al `--generate-project`, resoldre la interfície per servei `NetworkInterface` (qualsevol node Ethernet) en lloc de per nom; (C) fer cas-insensitive/localització per TypeKey.
2. **GSD de tercers (11)**: SEW ×3, DFE ×7, FANUC ×1 no es crearan (llicència `gsdml-*.xml` missing per Openness). El test ha de distingir **errors durs (Siemens)** de **gaps GSD documentats (coneguts, no bloquejants)** — o fer el round-trip damunt d'un TEST projecte només-Siemens perquè sigui net.

### Punt de continuació immediat (de la conversa)
- Revisar configuració d'interfícies dels dispositius Siemens (CPU21/ETH03/04/05): l'usuari va proposar verificar/arreglar-ho com a següent pas abans del GSD manual. Test de diagnostic ja disponible: `--dump-interfaces "<result.ap21>"`.

---
## RESUM DEL DIA 16/09

## Estat actual
- Pipeline complet (P0-P9 + D1 export-docs) **aprovat** → veure `TiaOApp/ROADMAP.md`.
- Última execució headless d'avui: `run_gen_headless.bat` → projecte `C:\Users\DPTO_ELECT\Documents\Automation\TEST_OPEN_2\TEST_OPEN_2.ap21` (log: `gen_headless_log.txt`).
- La build s'ha re-whitelistejat avui a les 18:24 via `reallowlist.bat` (log OK). Si demà es recompila `TiaOpennessApp`, cal tornar a executar `reallowlist.bat` (hash del binari).

## El que ha anat bé avui
- Dades completes d'AE82 (`imports/Devices|Modules|Subnets|Interfaces.csv`): **Siemens OK** — CPU21 + ETH03/04/05 (ET200SP), **65 mòduls** plugueats, 2 subxarxes, interfícies/IPs configurades.
- Catàleg plens d'identifiers reals de TIA via `--dump-type-id` sobre TEST_TIAO.

## Bloqueig detectat (punt de continuació)
- **11 dispositius GSD de tercers no es poden crear via Openness `CreateWithItem`**:
  - SEW UFRXXB ×3 (MC120/121/122)
  - DFE MDX61B + DFE32B ×7 (UM3A/B, UM4A/B, UM5A/B, UM6A)
  - FANUC A05B-2600-R834 ×1 (ROBOT1)
- Error TIA: `Necessary license 'gsdml-...xml' is missing` (mode headless i attach).
- CONFIRMAT: es creen correctament des de la GUI de TIA (test TEST_TIAO_2) → restricció de llicència GSD de TIA a la via API, **no** és un defecte del pipeline.

## Per on continuar (proposta per demà)
1. **Afegir a mà des de la GUI** els 11 GSD a TEST_OPEN_2 (testar si llavors l'app ho detecta com a ja existents i plugueja/connecta la resta).
2. O bé investigar si hi ha una via per registrar/habilitar les llicències GSD per a Openness.
3. Opcional (roadmap F10+): start amb Blocks/DBs/Tag tables/HMI.

## Nota
- Encara **no hi ha cap commit** a git (tot es troba untracked). Si es vol, demà es pot fer el primer commit.