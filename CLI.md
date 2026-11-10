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
| `--generate-project` | Pipeline complet de generació (sense auto-comparació opcional) |
| `--diff-topology` | Compara dues topologies exportades (JSON) |
| `--dump-type-id` | Mostra els `TypeIdentifier` dels dispositius d'un projecte |
| `--plug-diagnose` | Diagnòstic de connexió (plug) de mòduls a un rack |
| `--export-docs` | Genera els CSV de documents des d'un projecte o exportació |
| `--export-blocks` | Exporta els blocs de programa (OB/FB/FC/DB) a XML SimaticML + `Blocks.csv` |
| `--generate-blocks` | Importa els blocs de `Blocks.csv` en un clon d'un projecte plantilla |
| `--dump-blocks` | Mostra l'inventari de blocs de programa d'un projecte (diagnòstic) |
| `--dump-subnets` | Mostra les xarxes (subnets) d'un projecte |
| `--dump-interfaces` | Mostra les interfícies de xarxa d'un projecte |
| `--export-cax-project` | Exporta un projecte sencer a un fitxer CAx/AML (golden) |
| `--import-db` | Importa els CSV (`imports/` + catàleg) a una base de dades SQLite |
| `--db-to-csv` | Regenera els CSV (documents + catàleg) des de la base de dades SQLite |
| `--db-info` | Mostra la versió i el contingut de la base de dades i l'actualitza a l'esquema actual |

> **Golden AML**: es pot obtenir amb `--export-cax-project` o automàticament des del projecte obert al menú
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
--xlsx-to-csv | --verify-readers | --validate-docs |
--generate-project | --diff-topology | --dump-type-id |
--plug-diagnose | --export-docs | --dump-subnets | --dump-interfaces | --dump-tags |
--export-blocks | --generate-blocks | --dump-blocks | --dump-hw-items |
--export-cax-project | --import-db | --db-to-csv | --db-info
```

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

## 19. `--import-db`

Crea (o sobreescriu) una base de dades **SQLite** a partir dels CSV de documents
i del catàleg, amb taules normalitzades, claus foranes i vistes. No connecta a
TIA Portal.

```
TiaOpennessApp --import-db [dbPath] [importsDir]
```

| Argument | Descripció |
|----------|------------|
| `[dbPath]` | Fitxer SQLite de sortida. Per defecte: `<directori del .exe>\tia_topology.db` |
| `[importsDir]` | Directori amb `Devices.csv`, `Modules.csv`, `Subnets.csv`, `Interfaces.csv`, `ModuleAddresses.csv` i `IoTags.csv`. Per defecte: es busca `imports` pujant des del directori del .exe |

El catàleg es resol a `<pare de importsDir>\catalog\Catalog.csv` o, si no hi és,
a `<directori del .exe>\catalog\Catalog.csv`.

**Taules**: `catalog`, `devices`, `subnets`, `modules`, `interfaces`,
`module_addresses`, `io_tags`.

**Relacions**:
- `devices.type_key` i `modules.type_key` són `NOT NULL` i han d'existir a
  `catalog.type_key` (també es rebutja una clau buida o només espais).
- `modules.device`, `interfaces.device`, `module_addresses.device` i
  `io_tags.device`/`io_tags.io_device` han d'existir a `devices.device_name`.
- `interfaces.subnet` és opcional, però si hi és ha d'existir a `subnets.name`.
- Totes les FK són `ON UPDATE CASCADE ON DELETE RESTRICT`. A més, hi ha triggers
  `BEFORE INSERT/UPDATE/DELETE` per a cada relació, de manera que les regles es
  compleixen encara que la connexió tingui `PRAGMA foreign_keys=OFF`.

**Files descartades**: si un `TypeKey`, dispositiu o subxarxa no existeix, la fila
s'omet, es mostra un avís `WARN` i l'import continua amb la resta. Els errors
d'E/S o d'esquema provoquen rollback (`Success=false`).

**Vistes**: `vw_device_topology` (dispositius + catàleg + interfícies) i
`vw_io_mapping` (tags units al rang d'adreces del submòdul, filtrat per I/Q).

**Sortida**: nombre de files per taula, avisos de files descartades i, si n'hi
ha, errors. El mateix import també és accessible al menú interactiu amb l'opció
**4. Import CSVs to SQLite database**.

---

## 20. `--db-to-csv`

Operació inversa de `--import-db`: llegeix la base de dades SQLite i regenera els
6 CSV de documents (a `exports/` per defecte) i `catalog/Catalog.csv`. Els fitxers
es tornen a escriure amb el mateix format (UTF-8 amb BOM per als documents, sense
BOM per al catàleg) i les mateixes capçaleres i ordre de columnes.

```
TiaOpennessApp --db-to-csv [dbPath] [outDocsDir] [catalogCsv]
```

| Argument | Descripció |
|----------|------------|
| `[dbPath]` | Fitxer SQLite d'origen. Per defecte: `<directori del .exe>\tia_topology.db` |
| `[outDocsDir]` | Directori dels 6 CSV. Per defecte: `Export:DocsPath` (`exports`) |
| `[catalogCsv]` | Catàleg de sortida. Per defecte: `Export:CatalogPath` (`catalog\Catalog.csv`) |

**Sortida**: nombre de files per fitxer i, si n'hi ha, errors. El mateix export
també és accessible al menú interactiu amb l'opció **5. Export SQLite database to
CSVs**.

Amb l'esquema v2 també s'escriu `Blocks.csv` (la llista de blocs de programa), si
la base de dades en té.

---

## 20b. `--db-info`

Obre una base de dades de projecte, la porta a l'esquema actual si és més antiga
(p. ex. v1 → v2) i mostra la versió, les dades del projecte (`project_info`) i el
nombre de files de cada taula.

```
TiaOpennessApp --db-info [dbPath]
```

| Argument | Descripció |
|----------|------------|
| `[dbPath]` | Fitxer SQLite. Per defecte: el mateix que `--import-db` |

**Esquema v2**: afegeix `project_info`, `blocks` (importat de `Blocks.csv` si
existeix) i regles d'unicitat: una IP i un nom PROFINET per subxarxa, i un nom
d'I/O tag per PLC. Una importació que les incompleix es desfà sencera i indica el
fitxer i la fila.

---

## 21. `--export-blocks`

Exporta els **blocs de programa** (OB, FB, FC, DB globals i d'instància) de tots
els PLC del projecte a fitxers XML SimaticML i escriu el manifest `Blocks.csv` al
directori de documents.

```
TiaOpennessApp --export-blocks <projectPath> <outDocsDir> [deviceFilter]
```

| Argument | Descripció |
|----------|------------|
| `<projectPath>` | Projecte `.ap21` d'origen (s'obre en mode headless) |
| `<outDocsDir>` | Directori de documents on es crea `Blocks.csv` i la carpeta `blocks/` |
| `[deviceFilter]` | Limita l'export als dispositius PLC que coincideixin amb aquest nom |

**Estructura de sortida**:

- `Blocks.csv`: una fila per bloc exportat, amb les columnes `Device, GroupPath,
  Kind, Name, Namespace, Number, AutoNumber, BlockType, ProgrammingLanguage,
  MemoryLayout, Title, Comment, SourceFile`.
- `blocks/<Device>/<grup>/<subgrup>/<Nom>.xml`: el codi complet del bloc
  (`ExportOptions.WithDefaults`). `SourceFile` hi apunta amb una ruta relativa
  que sempre fa servir `/`. Els noms es netegen de caràcters no vàlids i, si dos
  blocs coincideixen, s'afegeix un sufix numèric.
- `GroupPath` és la ruta del grup d'usuari separada per `/`; buit = arrel.
- **Tipus de dades PLC (UDT)**: s'exporten a `types/<Device>/<grup>/<Nom>.xml`
  i surten primer a `Blocks.csv` amb `Kind=Type` i `BlockType=UDT`. Els blocs en
  depenen, i sense ells la importació dels FB/DB que els fan servir falla.
- **Taules de variables PLC** (tots els tags, no només I/O, i les constants
  d'usuari): s'exporten a `tags/<Device>/<grup>/<Taula>.xml` amb
  `Kind=TagTable`. El codi fa servir tags de memòria (`%M`), constants, etc.,
  i sense ells hi ha errors d'importació (p. ex. `OPEN DB [MW12]`) i de compilació.
- **System blocks**: dels grups de sistema (p. ex. *Program resources*) només
  s'exporten els **DB** (instància única de `TON`, `R_TRIG`...: `IEC_Timer_0_DB`),
  amb `GroupPath = [System]/<grup>`. En importar van al mateix grup de sistema si
  existeix al template, o a l'arrel de *Program blocks*. Els FB/FC de sistema els
  afegeix TIA en compilar.
- Les **constants de sistema** (`Local~PROFINET_IO-System`, HW IDs...) no
  s'exporten: TIA les genera del maquinari. Perquè coincideixin, el nom de l'IO
  system es copia a `Interfaces.csv` (`MasterIOSystem`) amb el seu número
  (`MasterIOSystemNumber`, p. ex. 100/101). El número identifica cada IO system
  quan n'hi ha diversos amb el mateix nom (una CPU amb dues interfícies
  controladores): cada IO device es connecta al del seu número.

**Regles**:

- Els blocs **know-how protected** no es poden exportar: es compten com a
  `skipped` i es llisten com a avís.
- Un error en un bloc no atura l'export: s'esborra el fitxer parcial, s'anota un
  `BLOCK WARN` i es continua amb la resta.

**Sortida**: `Blocks exported: N, skipped: N, errors: N`, els avisos i el camí del
manifest.

---

## 22. `--generate-blocks`

Clona un **projecte plantilla** (que ja conté el maquinari: PLC, mòduls, xarxes)
i hi importa els blocs de `Blocks.csv`. Està pensat per encadenar-se amb
`--export-blocks` o amb un projecte generat per `--generate-project`.

```
TiaOpennessApp --generate-blocks <templateProject> <docsDir> <targetDir> <projectName> [--override] [--no-compile]
```

| Argument | Descripció |
|----------|------------|
| `<templateProject>` | Projecte `.ap21` plantilla. **No es modifica**: se'n desa una còpia amb `SaveAs` |
| `<docsDir>` | Directori de documents amb `Blocks.csv` i la carpeta `blocks/` |
| `<targetDir>` | Carpeta pare del projecte nou |
| `<projectName>` | Nom del projecte nou; es crea `<targetDir>\<projectName>` |
| `--override` | Reimporta (`ImportOptions.Override`) els blocs que ja existeixen al template. Sense aquesta opció se salten (`SKIPPED`) |
| `--no-compile` | No compila el software PLC després d'importar |

**Flux**:

1. Llegeix i valida els documents de `<docsDir>` (vegeu els requisits més avall).
2. Comprova que `<targetDir>\<projectName>` no existeix o és buida.
3. Obre la plantilla, la desa com a `<targetDir>\<projectName>` i obre el clon.
4. Importa primer els UDT (`Kind=Type`), després les taules de variables
   (`Kind=TagTable`; **sempre se sobreescriuen**, perquè les del template només
   tenen els I/O tags generats) i després els blocs (`GlobalDB` → `FC` → `FB` →
   `InstanceDB` → `OB` → la resta). Els blocs de seguretat (`F_FBD`, `F_DB`...)
   se salten: els genera TIA amb el grup de runtime F. Els grups es creen si cal. Cada
   fase es fa **per passades**: el que falla es torna a provar a la passada
   següent, fins que una passada no n'importa cap més (`Passada N: X importats,
   Y pendents`). Així es resolen les dependències UDT→UDT, FB→FB
   (multiinstància) i InstanceDB→FB sense dependre de l'ordre de `Blocks.csv`.
   Cada intent es reintenta una vegada amb `SkipInactiveCultures`.
5. Compila cada PLC afectat (llevat de `--no-compile`).
6. Desa **sempre** el clon (el template no es toca), indicant `OK` o
   `AMB INCIDÈNCIES (N fallits, M errors de compilació)`, perquè es pugui obrir
   a TIA i revisar-ho.

**Requisits i limitacions**:

- Només cal `Blocks.csv` + `blocks/`: n'hi ha prou amb un directori creat per
  `--export-blocks`. `Devices.csv`, `Modules.csv`, `Subnets.csv` i
  `Interfaces.csv` són opcionals (el maquinari ja és al template); si falten
  surt un avís. Si hi són, es validen com sempre.
- El dispositiu de cada bloc ha d'existir al template i tenir software PLC. Si
  no apareix a `Devices.csv` només surt un avís de validació.
- `GroupPath` no pot començar ni acabar amb `/` ni contenir segments `.`/`..`.
- `Kind` diferent de `Block` encara no s'admet (avís).
- La plantilla no pot estar oberta a la UI de TIA Portal.

**Sortida**: una línia per bloc (`IMPORTED`, `OVERRIDDEN`, `SKIPPED`, `FAILED`),
el resultat de compilació (`COMPILE state=... errors=... warnings=...`) i el camí
del projecte desat.

---

## 23. `--dump-blocks`

Diagnòstic: obre un projecte i llista tots els blocs de programa (dispositiu,
grup, nom, número, tipus, llenguatge, layout, protecció, títol i comentari)
sense escriure cap fitxer.

```
TiaOpennessApp --dump-blocks <projectPath> [deviceFilter]
```

---

## Valors per defecte (appsettings.json)

Les comandes que accepten `[catalogCsv]` opcional el resolen així:

1. Valor de la clau `Import:CatalogPath` (si el fitxer existeix).
2. Si no: `catalog\Catalog.csv` a l'arrel del projecte (la carpeta amb
   `TiaOpennessApp.csproj`, buscant des de l'executable).
3. Si no: `catalog\Catalog.csv` al costat de l'executable (paquet distribuït).

`--csv-to-xlsx`, `--generate-template` i les comandes de generació esperen dins
de `<docsDir>` els fitxers amb noms fixos:

- `Devices.csv`
- `Modules.csv`
- `Subnets.csv`
- `Interfaces.csv`
- `IoTags.csv` (opcional)
- `ModuleAddresses.csv` (opcional)
- `Blocks.csv` (opcional; només el fan servir `--generate-blocks` i la validació)

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

# Generar la plantilla XLSX
TiaOpennessApp.exe --generate-template "imports\template.xlsx"

# Convertir CSV a XLSX
TiaOpennessApp.exe --csv-to-xlsx "imports" "imports\project.xlsx"

# Convertir XLSX a CSV
TiaOpennessApp.exe --xlsx-to-csv "imports\project.xlsx" "imports\generated"

# Generar la base de dades SQLite a partir dels CSV
TiaOpennessApp.exe --import-db

# Regenerar els CSV a partir de la base de dades SQLite
TiaOpennessApp.exe --db-to-csv
```

> **Nota**: les comandes que obren o generen projectes (`--generate-project`,
> `--dump-type-id`, `--plug-diagnose`,
> `--export-docs`, `--dump-subnets`, `--dump-interfaces`) requereixen TIA Portal
> V21 amb Openness instal·lat, l'usuari al grup `Siemens TIA Openness` i
> l'executable a l'AllowList (executar `fix_allowlist.bat` com a administrador
> després de recompilar).