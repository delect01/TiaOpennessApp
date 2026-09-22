# TiaOpennessApp — Comandes CLI

Document de referència de totes les comandes CLI de `TiaOpennessApp`.

L'executable accepta arguments a la línia d'ordres. Quan es crida **sense**
arguments, s'executa el **mode de menú interactiu**. Quan es crida **amb** un
argument, s'interpreta el primer argument com a comandament (`RunCliMode`,
`Program.cs:127`).

## Invocació bàsica

```
TiaOpennessApp.exe <command> [arguments...]
```

Tots els comandaments es poden escriure en majúscules o minúscules (el primer
argument es normalitza amb `ToLowerInvariant`).

### Execució local (dev)

```powershell
dotnet run -- <command> [arguments...]
```

---

## Comandaments disponibles

| Comandament | Descripció curta |
|-------------|------------------|
| `--gen-catalog-from-export` | Genera/fusiona candidates de catàleg des d'una exportació JSON |
| `--generate-template` | Genera la plantilla XLSX buida des del catàleg |
| `--csv-to-xlsx` | Converteix els CSV de documents a un XLSX |
| `--xlsx-to-csv` | Converteix un XLSX als CSV de documents |
| `--verify-readers` | Verifica que els lectors CSV i XLSX produeixen el mateix document |
| `--validate-docs` | Valida els documents contra el catàleg (sense tocar TIA) |
| `--gen-devices-sandbox` | Genera dispositius/estànders en un projecte sandbox |
| `--generate-project` | Pipeline complet de generació (sense auto-comparació opcional) |
| `--roundtrip-test` | Prova integral: exporta la font, regenera i compara la topologia |
| `--diff-topology` | Compara dues topologies exportades (JSON) |
| `--dump-type-id` | Mostra els `TypeIdentifier` dels dispositius d'un projecte |
| `--plug-diagnose` | Diagnòstic de connexió (plug) de mòduls a un rack |
| `--export-docs` | Genera els CSV de documents des d'un projecte o exportació |
| `--dump-subnets` | Mostra les xarxes (subnets) d'un projecte |
| `--dump-interfaces` | Mostra les interfícies de xarxa d'un projecte |
| `--gsd-cax-poc` | PoC: exporta un dispositiu GSD a AML i l'importa en un sandbox (via CAx) |
| `--export-cax-project` | Exporta un projecte sencer a un fitxer CAx/AML (golden) |

> **Golden AML**: es pot obtenir amb `--export-cax-project`, amb el roundtrip
> (`workDir\golden.aml`) o automàticament des del projecte obert al menú
> interactiu (vegeu §8.1). El camí manual es persisteix a `Import:GoldenAmlPath`.

---

## 1. `--gen-catalog-from-export`

Genera candidates de catàleg des d'un JSON d'exportació de dispositius i les
fusiona amb el catàleg existent.

```
TiaOpennessApp --gen-catalog-from-export <exportJson> [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `<exportJson>` | Fitxer JSON amb la llista de `NetworkDeviceInfo` |
| `[catalogCsv]` | Catàleg destí. Per defecte: valor de `Import:CatalogPath` o `bin/catalog/Catalog.csv` |

**Sortida**: missatge amb el nombre d'entrades existents, candidates i afegides.

---

## 2. `--generate-template`

Genera la plantilla XLSX **preomplerta** amb el contingut dels CSV de documents
del directori (incloent-hi `IoTags.csv` i `ModuleAddresses.csv` si existeixen),
amb els desplegables del catàleg a les columnes corresponents.

```
TiaOpennessApp --generate-template <outXlsx> [docsDir] [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `<outXlsx>` | Ruta del fitxer XLSX de sortida |
| `[docsDir]` | Directori amb els CSV (`Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv`, `IoTags.csv`, `ModuleAddresses.csv`). Per defecte: `Import:DocsPath` |
| `[catalogCsv]` | Catàleg font. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |

**Nota**: si el parseig CSV té errors, no es genera la plantilla. `IoTags.csv` i
`ModuleAddresses.csv` són opcionals (si no existeixen surten avisos i les
fullses corresponents queden buides).

---

## 3. `--csv-to-xlsx`

Converteix els CSV de documents (`Devices.csv`, `Modules.csv`, `Subnets.csv`,
`Interfaces.csv` d'un directori) a un XLSX.

```
TiaOpennessApp --csv-to-xlsx <docsDir> <outXlsx> [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `<docsDir>` | Directori amb els fitxers CSV (es busquen els 4 noms fixos) |
| `<outXlsx>` | Ruta del XLSX de sortida |
| `[catalogCsv]` | Catàleg. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |

**Nota**: si el parseig CSV té errors, no es genera el XLSX.

---

## 4. `--xlsx-to-csv`

Operació inversa de `--csv-to-xlsx`: llegeix un XLSX de documents i genera els
6 fitxers CSV (`Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv`,
`IoTags.csv`, `ModuleAddresses.csv`) en un directori de sortida.

```
TiaOpennessApp --xlsx-to-csv <inXlsx> <outDocsDir>
```

| Argument | Descripció |
|----------|------------|
| `<inXlsx>` | Ruta del XLSX d'entrada |
| `<outDocsDir>` | Directori on es desaran els CSV (el directori `Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv`, `IoTags.csv`, `ModuleAddresses.csv`) |

**Nota**: si el parseig XLSX té errors, no es generen els CSV. El directori de
sortida es crea si no existeix.

---

## 5. `--verify-readers`

Comprova que el lector CSV i el lector XLSX generen exactament el mateix
`GenerationDocument` (comparació de JSON serialitzat).

```
TiaOpennessApp --verify-readers <docsDir> <xlsx>
```

| Argument | Descripció |
|----------|------------|
| `<docsDir>` | Directori amb els CSV |
| `<xlsx>` | Ruta del XLSX a comparar |

**Sortida**: estat de parseig de CSV i XLSX, i conclusió `identical` / `DIFFERENT`.

---

## 6. `--validate-docs`

Valida els documents contra el catàleg **sense obrir TIA Portal** i mostra les
operacions planificades. Opcionalment valida la carpeta de destinació del
projecte.

```
TiaOpennessApp --validate-docs <docsDir> [catalogCsv] [projectFolder]
```

| Argument | Descripció |
|----------|------------|
| `<docsDir>` | Directori amb els CSV |
| `[catalogCsv]` | Catàleg. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |
| `[projectFolder]` | Carpetes de destinació del projecte a validar (opcional) |

**Sortida**: seccions `PLANNED OPERATIONS` i `VALIDATION REPORT`, i un
resum `error(s)/warning(s)` amb estat `OK` o `NOT READY`.

---

## 7. `--gen-devices-sandbox`

Executa el pipeline de generació **en un projecte sandbox** (per defecte el de
`TiaPortal:NewProjectPath`/`NewProjectName`), amb sortida de reports de
generació per a dispositius, mòduls, subnets i interfícies. El projecte es
desa.

```
TiaOpennessApp --gen-devices-sandbox <docsDir> [catalogCsv] [outJson]
```

| Argument | Descripció |
|----------|------------|
| `<docsDir>` | Directori amb els CSV |
| `[catalogCsv]` | Catàleg. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |
| `[outJson]` | Ruta opcional per exportar els dispositius de xarxa generats |

**Requisits**: `TiaPortal:NewProjectPath` i `TiaPortal:NewProjectName` han
d'estar configurats a `appsettings.json`.

**Nota**: connecta a TIA Portal en mode headless i crea/obre el projecte sandbox.

---

## 8. `--generate-project`

Executa el **pipeline complet**:
parsejar → validar → generar dispositius/mòduls/subnets/interfícies al projecte
sandbox → aplicar el **mapa d'adreces de maquinari** (`ModuleAddresses.csv`) →
generar les **taules i tags d'I/O** (`IoTags.csv`) → desar.

```
TiaOpennessApp --generate-project <docsDir> [catalogCsv] [targetDirectory] [projectName] [goldenAml]
```

| Argument | Descripció |
|----------|------------|
| `<docsDir>` | Directori amb els CSV |
| `[catalogCsv]` | Catàleg. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |
| `[targetDirectory]` | Directori destí del projecte. Per defecte: `TiaPortal:NewProjectPath` |
| `[projectName]` | Nom del projecte. Per defecte: `TiaPortal:NewProjectName` |
| `[goldenAml]` | Fitxer CAx/AML "golden" (export del projecte sencer). Si el catàleg té entrades GSD (`Identifier` comença per `GSD:`), s'importa amb `RetainTiaDevice` i es conserven només els dispositius del document (s'esborren la resta) abans de generar la resta del projecte. Sense aquest fitxer, els dispositius GSD es crearien amb `CreateWithItem` (error de llicència `gsdml`). |

**Requisits**: si no es passen `[targetDirectory]`/`[projectName]`, els keys
`TiaPortal:NewProjectPath`/`NewProjectName` han d'estar configurats a `appsettings.json`.

**Nota**: connecta a TIA Portal en mode headless.

### 8.1 Golden AML al menú interactiu

L'opció **3. Generate project from documents** del menú resolt el golden AML
automàticament:

1. Si hi ha un projecte obert a la connexió (p. ex. l'has obert pel menú 1),
   se'n **exporta** un golden a `%TEMP%\golden_menu_<projectName>_<time>.aml`
   i s'usa per importar els dispositius GSD.
2. Si no hi ha cap projecte obert, es pregunta:
   `Golden AML path (buit = sense importar GSD)`. El valor teclejat es recorda a
   `appsettings.json` (`Import:GoldenAmlPath`) i es reutilitza com a valor per
   defecte a les següents execucions.
3. Sense golden, els dispositius GSD es generarien amb `CreateWithItem` (error
   de llicència `gsdml`); el pipeline ho avisa i continua.

---

## 9. `--roundtrip-test`

Prova integral del pipeline: obre el projecte font, genera el catàleg i els
CSV de documents (incloent-hi `IoTags.csv` i `ModuleAddresses.csv`), executa la
generació completa en un projecte nou, re-exporta el resultat i compara la
topologia, els tags i el mapa d'adreces amb la font (report `PASS` / `FAIL`).
Durant el PAS 1-3 el round-trip exporta un **golden AML** (`workDir\golden.aml`)
del projecte sencer; al PAS 4 el pipeline l'importa per crear els dispositius GSD
sense necessitar la llicència `gsdml` (els serveis de generació els troben i els
salten). Els dispositius GSD que no vinguin al golden es classifiquen com a
*gaps coneguts* i no fan fallar el test.

```
TiaOpennessApp --roundtrip-test <srcProject> <workDir> <targetDir> <projectName> [catalogSeed]
```

| Argument | Descripció |
|----------|------------|
| `<srcProject>` | Projecte `.ap21` font (es genera el catàleg seed i els documents) |
| `<workDir>` | Directori de treball temporal (per als documents i JSON de comparació) |
| `<targetDir>` | Directori destí del projecte generat (els CSV es generen a partir de la font) |
| `<projectName>` | Nom del projecte generat |
| `[catalogSeed]` | Catàleg inicial. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |

**Sortida**: `ROUNDTRIP SUMMARY` amb dispositius, mòduls, subnets i interfícies,
comparativa de topologia (`TopologyDiff`), re-exportació i comparativa de tags
(`Tags`) i del mapa d'adreces (`HwAddr`), i el resultat final `PASS`/`FAIL`
(exit code `0`/`1`).

**Nota**: connecta a TIA Portal en mode headless i fa diverses obertures/desa
de projectes.

---

## 10. `--diff-topology`

Compara dues topologies exportades (JSON amb llista de `NetworkDeviceInfo`).
Opcionalment admet un fitxer JSON amb la llista de noms de dispositius que es
tracten com a *gaps coneguts* (p. ex. dispositius GSD sense llicència) i no
compten com a diferències.

```
TiaOpennessApp --diff-topology <expectedJson> <actualJson> [knownGapsJson]
```

| Argument | Descripció |
|----------|------------|
| `<expectedJson>` | Topologia esperada (JSON) |
| `<actualJson>` | Topologia actual (JSON) |
| `[knownGapsJson]` | JSON amb llista de noms de dispositius a ignorar (opcional) |

**Sortida**: comparativa de dispositius, mòduls, interfícies i subnets, amb
resum `PASSED` / `NOT PASSED`.

---

## 11. `--dump-type-id`

Obre un projecte en mode headless i mostra els `TypeIdentifier` de tots els
dispositius.

```
TiaOpennessApp --dump-type-id <projectPath>
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Ruta al projecte `.ap21` |

---

## 12. `--plug-diagnose`

Diagnostica la connexió de mòduls al bastidor `ET200SP_01` / interfície `PN03`
funamenta en el llistat d'identificadors del catàleg.

```
TiaOpennessApp --plug-diagnose <projectPath> [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Ruta al projecte `.ap21` |
| `[catalogCsv]` | Catàleg. Per defecte: `Import:CatalogPath` o `bin/catalog/Catalog.csv` |

**Nota**: els identificadors amb `Identifier` no buit del catàleg s'usen com a
referència de plug.

---

## 13. `--export-docs`

Genera els **documents d'entrada** (`Devices.csv`, `Modules.csv`, `Subnets.csv`,
`Interfaces.csv`, `IoTags.csv`, `ModuleAddresses.csv`) a partir d'un projecte
real (o d'una exportació JSON prèvia). Els CSV surten amb les columnes de
`GenerationColumns` i llestos per a `--generate-project`.

```
TiaOpennessApp --export-docs <projectPath|exportJson> [outDocsDir] [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `<projectPath\|exportJson>` | Projecte `.ap21` o bé un JSON amb la llista de `NetworkDeviceInfo` |
| `[outDocsDir]` | Directori de sortida. Per defecte: `Export:DocsPath` (`exports`) |
| `[catalogCsv]` | Catàleg per resoldre `TypeKey`. Per defecte: `Import:CatalogPath` |

### Regles de generació

- **Devices**: un per dispositiu; `TypeKey` resolt pel mòdul *root* (CPU/IM)
  contra el catàleg; `Firmware` extret de l'`Identifier` del catàleg quan hi és.
- **Modules**: es salten els ítems amb OrderNumber comodí (rail/rack); el mòdul
  *root* surt amb `Comment=Root`; els bus adapters (slot 127) surten sense slot.
- **Subnets**: subxarxes úniques de les interfícies connectades; tipus `PN/IE`
  o `PROFIBUS` segons la interfície associada.
- **Interfaces**: una fila per interfície; subxarxa buida si no està connectada.
- **IoTags** (`IoTags.csv`): una fila per I/O tag de totes les taules de tags
  dels PLC del projecte (`Device, ModuleName, SubModuleName, TagTable, Name,
  Type, Address, Comment`). Només s'inclouen tags amb adreça de maquinari
  d'entrada/sortida (`%I...`/`%Q...`); els de memòria (`%M`), `%DB` i similars
  es descarten. `ModuleName`/`SubModuleName` s'omplen fent casar l'adreça del
  tag amb els rangs d'adreces dels mòduls (`DeviceItem.Addresses`).
  ⚠️ Els tags només es poden extreure d'un **projecte obert**, no d'un JSON:
  amb font JSON el fitxer surt amb capçalera i buit.
- **ModuleAddresses** (`ModuleAddresses.csv`): una fila per rang d'adreça I/Q de
  cada mòdul/submòdul (`Device, ModuleName, SubModuleName, TypeIdentifier,
  IoType, StartAddress, Length`). Recull el **direccionament manual/espars** de
  la font; `--generate-project` el reaplica al projecte generat
  (`ModuleAddressesGenerationService`). Igual que els tags, només s'extreu d'un
  **projecte obert**; amb font JSON el fitxer surt buit.
- Els `TypeKey` sense entrada al catàleg s'anoten a la consola i s'usen el nom
  tipus com a fallback.

---

## 14. `--dump-subnets`

Obre un projecte i mostra les xarxes (subnets) definides.

```
TiaOpennessApp --dump-subnets <projectPath>
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Ruta al projecte `.ap21` |

---

## 15. `--dump-interfaces`

Obre un projecte i mostra les interfícies de xarxa dels dispositius.

```
TiaOpennessApp --dump-interfaces <projectPath>
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Ruta al projecte `.ap21` |

---

## 16. `--dump-tags`

Obre un projecte i inspecciona la cadena completa d'I/O tags a runtime:
serveis d'enginyeria del dispositiu, `SoftwareContainer.Software`, grups i
taules de tags, i els **atributs reals** de cada tag (noms + valors). Usat per
diagnosticar per què `IoTags.csv` pot sortir buit (per exemple si l'atribut
d'adreça té un nom diferent del previst a la versió instal·lada de TIA).
Acaba executant `ExportIoTags()` per veure quants tags produeix realment.

```
TiaOpennessApp --dump-tags <projectPath>
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Ruta al projecte `.ap21` |

---

## Comandament desconegut

Qualsevol altre valor mostra el missatge de comandaments suportats:

```
Supported: --gen-catalog-from-export | --generate-template | --csv-to-xlsx |
--xlsx-to-csv | --verify-readers | --validate-docs | --gen-devices-sandbox |
--generate-project | --roundtrip-test | --diff-topology | --dump-type-id |
--plug-diagnose | --export-docs | --dump-subnets | --dump-interfaces | --dump-tags |
--dump-hw-items | --gsd-cax-poc | --export-cax-project
```

---

## 17. `--gsd-cax-poc`

PoC de la via **CAx (AML)** per a dispositius GSD/GSDML de tercers: exporta el
projecte font a un fitxer `.aml` (`CaxProvider.Export`) i l'importa en un
projecte sandbox nou (`CaxProvider.Import` amb `CaxImportOptions.RetainTiaDevice`),
tot sense `CreateWithItem` (bloquejat per la llicència `gsdml`). Verifica que el
dispositiu, el submòdul `06 process data words` i les adreces I/Q es creen
correctament i sense errors de llicència.

> **Nota**: l'export per dispositiu (`Export(Device,...)`) llança una
> `NullReferenceException` amb dispositius GSD a V21; es fa servir l'export del
> **projecte sencer** i l'import de tot el golden AML.
> L'extensió del fitxer CAx és **`.aml`** (no `.amlx`).

```
TiaOpennessApp --gsd-cax-poc <srcProject> <targetDir> <projectName> [deviceName]
```

| Argument | Descripció |
|----------|------------|
| `<srcProject>` | Projecte `.ap21` font amb els dispositius GSD ja inserits (p. ex. `TEST_TIAO.ap21`) |
| `<targetDir>` | Directori on es crearà el projecte sandbox |
| `<projectName>` | Nom del projecte sandbox |
| `[deviceName]` | Fragment del nom del dispositiu GSD a provar. Per defecte: el primer dispositiu `IsGsd` del projecte |

**Sortida**: resultats d'export (`State`, `Errors`, `Warnings`, missatges),
grandària del `.aml`, resultats d'import, verificació al sandbox (dispositiu,
submòdul, rangs I/Q) i `RESULTAT: PASS` / `FAIL`.

**Gates**: si l'export o l'import llancen excepció o errors de llicència
`gsdml`, s'atura amb `FAIL (gate)` per decidir si la via CAx és viable abans
d'integrar-la al pipeline.

---

## 18. `--export-cax-project`

Exporta un projecte sencer a un fitxer CAx **`.aml`** (`CaxProvider.Export(Project,
...)`). És la mateixa via que fa servir el round-trip per generar el **golden AML**
que després es reimporta per crear els dispositius GSD.

```
TiaOpennessApp --export-cax-project <projectPath> <outAml> [logFile]
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Projecte `.ap21` a exportar |
| `<outAml>` | Ruta del fitxer `.aml` de sortida |
| `[logFile]` | Fitxer de log de l'export. Per defecte: `<outAml>.log` |

> **Nota**: l'export torna `false` per projectes amb dispositius GSD tot i generar
> el `.aml` correctament; valideu per existència/grandària del fitxer.

---

## Valors per defecte (appsettings.json)

Les comandes que accepten `[catalogCsv]` opcional el resolen així:

1. Valor de la clau `Import:CatalogPath` (si el fitxer existeix).
2. Si no: `<directori del .exe>/catalog/Catalog.csv` (p. ex.
   `TiaOpennessApp\bin\Debug\net48\catalog\Catalog.csv`).

`--csv-to-xlsx`, `--generate-template` i les comandes de generació esperen dins
de `<docsDir>` els fitxers amb noms fixos:

- `Devices.csv`
- `Modules.csv`
- `Subnets.csv`
- `Interfaces.csv`
- `IoTags.csv` (opcional)
- `ModuleAddresses.csv` (opcional)

El directori de documents per defecte en mode de menú és el valor de
`Import:DocsPath` (`imports`).

## Exemples

```powershell
# Validar documents sense tocar TIA
TiaOpennessApp.exe --validate-docs "imports"

# Generar projecte complet (sense comparació)
TiaOpennessApp.exe --generate-project "imports" "bin\catalog\Catalog.csv"

# Generar els CSV de documents des del projecte real
TiaOpennessApp.exe --export-docs "C:\Users\DPTO_ELECT\Documents\Automation\AE82\AE82.ap21"

# Comparar dues topologies (JSON generats externament)
TiaOpennessApp.exe --diff-topology "topologia\esperada.json" "topologia\actual.json"

# Prova integral (font -> export -> generació -> comparació)
TiaOpennessApp.exe --roundtrip-test "..\Automation\AE82\AE82.ap21" "imports\rt" "..\Automation" "RT_New"

# Generar la plantilla XLSX
TiaOpennessApp.exe --generate-template "imports\template.xlsx"

# Convertir CSV a XLSX
TiaOpennessApp.exe --csv-to-xlsx "imports" "imports\project.xlsx"

# Convertir XLSX a CSV
TiaOpennessApp.exe --xlsx-to-csv "imports\project.xlsx" "imports\generated"
```

> **Nota**: les comandes que obren o generen projectes (`--gen-devices-sandbox`,
> `--generate-project`, `--roundtrip-test`, `--dump-type-id`, `--plug-diagnose`,
> `--export-docs`, `--dump-subnets`, `--dump-interfaces`) requereixen TIA Portal
> V21 amb Openness instal·lat, l'usuari al grup `Siemens TIA Openness` i
> l'executable a l'AllowList (executar `fix_allowlist.bat` com a administrador
> després de recompilar).