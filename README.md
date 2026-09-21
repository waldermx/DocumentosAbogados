# DocumentosAbogados

Genera documentos Word a partir de un registro que vive en Google Sheets.

- **`server/DocApi`** — ASP.NET Core Minimal API. Es el único componente que conoce las credenciales de Google. Lee una columna de la hoja, la parsea con un regex configurable y la sirve cacheada. Solo lectura: nunca escribe en Sheets ni genera documentos.
- **`client/DocGenApp`** — App de escritorio Avalonia para Windows. Consume la API, muestra los registros y genera el `.docx` **en el equipo del usuario** reemplazando los marcadores de una plantilla.

```
Google Sheets ──(solo lectura)──> DocApi ──(HTTP + Bearer)──> DocGenApp ──> documento.docx
```

El cliente nunca ve credenciales de Google: solo conoce la URL del servidor y la password maestra.

## Requisitos

- .NET SDK 10
- Windows (para el cliente; el servidor corre en cualquier plataforma o en Docker)

## Puesta en marcha

```bash
dotnet build          # compila los 4 proyectos
dotnet test           # 74 tests
```

### Servidor

```bash
cd server/DocApi
dotnet run
```

Arranca en `http://localhost:5000`. **Sin credenciales de Google configuradas la app igual levanta**: los endpoints responden con el caché vacío y `POST /sync` devuelve 502 con un mensaje explicando qué falta. Así se puede probar todo el circuito antes de tener acceso a la hoja.

En `Development` la password maestra es `dev-password-cambiar` (ver `appsettings.Development.json`).

### Cliente

```bash
cd client/DocGenApp
dotnet run
```

La primera vez pide la password maestra. Se guarda cifrada con DPAPI (ámbito del usuario de Windows) en `%LOCALAPPDATA%\DocGenApp\credential.bin` — nunca en texto plano. El botón «Cambiar password» la borra.

## Configuración del servidor

Todo se puede sobreescribir con variables de entorno usando doble guion bajo (`Auth__MasterPassword`). **Ningún secreto se versiona.**

| Clave | Env var | Descripción |
|---|---|---|
| `Auth:MasterPassword` | `Auth__MasterPassword` | Password que el cliente envía como `Authorization: Bearer`. **Obligatoria.** |
| `GoogleSheets:SpreadsheetId` | `GoogleSheets__SpreadsheetId` | Id de la hoja. **Obligatoria** para sincronizar. |
| `GoogleSheets:ServiceAccountJson` | `GoogleSheets__ServiceAccountJson` | Contenido del JSON de service account (cómodo en Docker). |
| `GoogleSheets:ServiceAccountJsonPath` | `GoogleSheets__ServiceAccountJsonPath` | Alternativa: ruta al archivo JSON. |
| `GoogleSheets:Range` | `GoogleSheets__Range` | Rango A1 de la columna principal (la que pasa por el regex). Por defecto `Hoja1!C:C`. |
| `GoogleSheets:ColumnasExtra` | `GoogleSheets__ColumnasExtra__0__Range` | Columnas adicionales que se copian tal cual. Ver abajo. |
| `GoogleSheets:TieneEncabezado` | `GoogleSheets__TieneEncabezado` | Si la primera fila del rango es encabezado. Por defecto `true`. |
| `Parsing:Regexes` | `Parsing__Regexes__0` | Lista de patrones .NET con grupos con nombre. Ver abajo. |
| `Sync:PollingIntervalMinutes` | `Sync__PollingIntervalMinutes` | Cada cuánto se consulta si la hoja cambió. Por defecto `5`. |
| `Sync:CacheFilePath` | `Sync__CacheFilePath` | Dónde persistir el caché. Vacío = solo memoria. |

La service account necesita permiso de **lectura** sobre la hoja: compártela con el email de la service account. Los scopes que se piden son `spreadsheets.readonly` y `drive.readonly`.

### Los regex

`Parsing:Regexes` es una **lista**: se prueban en orden contra cada celda y gana el primero que coincida. Por defecto:

```
1) ^(?:(?!\d)\S+\s+)*(?<consecutivo>\d+/\d{2,4})\s+(?<colegio>.+?)\s+DEL\s+(?<circuito>.+)$
2) ^(?:(?!\d)\S+\s+)*(?<consecutivo>\d+/\d{2,4})\s+(?<colegio>.+)$
3) ^(?<colegio>.+?)\s+(?<consecutivo>\d+/\d{2,4})$
```

El primero es el formato canónico. Sobre `644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO` extrae:

| Grupo | Valor |
|---|---|
| `consecutivo` | `644/2026` |
| `colegio` | `TERCER COLEGIADO` |
| `circuito` | `DECIMOPRIMER CIRCUITO` |

Los tres patrones toleran, en cualquier combinación:

- **Año de 2 o 4 dígitos** en el consecutivo (`21` o `2026`) — `\d{2,4}`, no `\d{4}` fijo.
- **Sin `DEL <circuito>` al final** (patrón 2): no todas las filas lo llevan. Sobre `777/2026 SEGUNDO TRIBUNAL UNITARIO` extrae `consecutivo` = `777/2026` y `colegio` = `SEGUNDO TRIBUNAL UNITARIO`, sin `circuito`. El marcador `{{circuito}}` sin valor se deja visible en el documento y se avisa en la app, como cualquier otro campo faltante.
- **Una o más palabras antes del consecutivo** (patrones 1 y 2): `REVISIÓN 1/2022 SEXTO COLEGIADO` → `consecutivo` = `1/2022`, `colegio` = `SEXTO COLEGIADO`. La(s) palabra(s) delante no se capturan ni aparecen en ningún marcador.
- **El consecutivo al final en vez de al principio** (patrón 3, el último recurso): `Primer Colegiado 33/2022` → `colegio` = `Primer Colegiado`, `consecutivo` = `33/2022`.

Cada registro expone `patronUsado` (índice 0-based) para ver de un vistazo qué filas cayeron en cuál. `GET /diagnostico` (ver más abajo) también reporta `filasPorPatron`. Si aparece una variante nueva que ninguno de los tres cubre, se añade un patrón más a la lista, del más específico al más laxo: es configuración, no código.

**Los nombres de los grupos son libres**: cada grupo con nombre se convierte automáticamente en un marcador `{{nombre}}` disponible en la plantilla. Una fila que no coincide con **ningún** patrón sigue sin interrumpir la sincronización: se lista aparte en la app.

También se acepta `Parsing:Regex` en singular (la forma anterior); solo se usa si `Regexes` está vacío.

### Columnas extra

Además de la columna principal, se pueden leer otras columnas de la misma hoja que **no** pasan por el regex: se copian tal cual y se unen a cada registro **por número de fila**.

```json
"GoogleSheets": {
  "Range": "Hoja1!C:C",
  "ColumnasExtra": [
    { "Nombre": "nombre", "Range": "Hoja1!D:D" }
  ]
}
```

Eso hace que `{{nombre}}` esté disponible en la plantilla con el contenido de la columna D de esa misma fila. Se pueden declarar varias; cada `Nombre` debe ser único (si no, el servidor falla al arrancar con un mensaje explícito).

Detalles que importan:

- **Sigue siendo una sola llamada a Sheets por sync**: la columna principal y las extra se piden juntas con `values.batchGet`.
- Todos los rangos deben **arrancar en la misma fila** (`C:C` y `D:D`, no `C:C` y `D5:D`), porque la correspondencia es por posición de fila.
- Una celda extra vacía **no descarta la fila**: el campo queda vacío y el marcador se reporta como faltante.
- El diff por fila compara la fila entera, así que **editar solo la columna del nombre también reparsea esa fila** (y solo esa).
- Si una columna extra se llama igual que un grupo del regex, gana la columna extra: es un dato explícito.

## Endpoints

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| `GET` | `/health` | No | Estado y última sincronización. Para Traefik/Dokploy. |
| `GET` | `/diagnostico` | Sí | Qué configuración tiene cargada **esta instancia**: patrones activos, columnas extra y cuántas filas cayó en cada patrón. |
| `GET` | `/registros` | Sí | Registros parseados + errores de parseo + `ultimaSync`. |
| `GET` | `/registros/{fila}` | Sí | Un registro. |
| `POST` | `/sync` | Sí | Fuerza un refresco. `?forzar=true` salta el chequeo de cambios y reparsea todo. |

## Cómo se sincroniza (y por qué es barato)

Cada ciclo de polling y cada `POST /sync` pasan por el mismo coordinador, en tres pasos:

1. **Chequeo barato** — se pregunta a Drive solo por `modifiedTime` de la hoja. Si no cambió, **se corta ahí**: no se lee la columna ni se reparsea nada.
2. **Lectura** — solo si la hoja cambió, una única llamada a Sheets (`values.batchGet`) que trae la columna principal y todas las columnas extra a la vez.
3. **Diff por fila** — cada fila se compara contra la de la sync anterior, **incluidas sus columnas extra**. Solo las filas que cambiaron vuelven a pasar por los regex; el resto conserva su resultado.

Un `SemaphoreSlim` garantiza que varias solicitudes simultáneas (polling + botón «Actualizar») no disparen varias sincronizaciones: se coalescen en una.

Los logs reportan `Filas reparseadas` / `reutilizadas` en cada sync, que es la forma de comprobar que el diff funciona.

## La plantilla Word

`client/DocGenApp/Templates/plantilla.docx` es un **ejemplo**: reemplázalo por la plantilla real. Los marcadores se escriben como `{{nombre}}`:

| Marcador | Contenido |
|---|---|
| `{{consecutivo}}`, `{{colegio}}`, `{{circuito}}` | Los grupos del regex (o los que definas) |
| `{{nombre}}` | La columna extra configurada (o las que definas) |
| `{{abogadoNombre}}`, `{{abogadoFirel}}`, `{{abogadoCedula}}` | El **primer** abogado capturado en la app |
| `{{abogado1Nombre}}`, `{{abogado2Nombre}}`, ... | Cada abogado capturado, numerado por su posición en la lista — ver [Datos de los abogados](#datos-de-los-abogados) |
| `{{valorCrudo}}` | El texto original de la celda |
| `{{fila}}` | Número de fila en la hoja |
| `{{fecha}}` | Fecha de generación, `dd/MM/yyyy` |

El panel de detalle de cada registro muestra el marcador exacto de cada campo, para copiarlo a la plantilla.

Funciona en el cuerpo, encabezados y pies. Word suele partir un marcador en varios fragmentos internos; el generador reconstruye el texto del párrafo antes de sustituir, así que `{{consecutivo}}` se reemplaza aunque Word lo haya fragmentado. Un marcador sin valor **se deja visible** en el documento y se avisa en la app, en vez de quedar en blanco silenciosamente.

Para regenerar la plantilla de ejemplo:

```bash
dotnet run --project client/DocGenApp -- --crear-plantilla
```

## Configuración del cliente

`client/DocGenApp/appsettings.json` (o env vars con prefijo `DOCGEN_`):

| Clave | Descripción |
|---|---|
| `ServerBaseUrl` | URL del servidor. |
| `DefaultOutputFolder` | Carpeta de salida. **Vacío = preguntar con un diálogo cada vez.** |
| `TemplatePath` | Ruta a la plantilla; relativa al directorio de la app. |
| `TimeoutSeconds` | Timeout de las llamadas HTTP. |

### Datos de los abogados

Nombre, usuario FIREL y cédula profesional **no vienen de la hoja**: se capturan en la app y se guardan en `%LOCALAPPDATA%\DocGenApp\abogados.json`.

Se puede dar de alta **más de un abogado** (un despacho con varios, o un documento firmado por dos). No hay "seleccionar uno": **todos los abogados de la lista se aplican a cada documento generado**, cada uno con sus propios marcadores según su posición:

| Posición | Marcadores |
|---|---|
| 1º | `{{abogado1Nombre}}`, `{{abogado1Firel}}`, `{{abogado1Cedula}}` — y también `{{abogadoNombre}}`, `{{abogadoFirel}}`, `{{abogadoCedula}}` sin numerar |
| 2º | `{{abogado2Nombre}}`, `{{abogado2Firel}}`, `{{abogado2Cedula}}` |
| 3º, 4º... | Igual, con su número |

El panel «Datos de los abogados» lista una tarjeta por abogado con esos marcadores impresos al lado de cada campo, igual que hace el panel de detalle con los campos de la hoja. «+ Agregar abogado» añade una tarjeta vacía al final; «Eliminar» quita esa tarjeta; las flechas ↑↓ reordenan — **el orden decide el número**, así que subir un abogado lo convierte en `abogado1` y baja de puesto al que estaba ahí.

Una plantilla con un solo firmante no necesita cambiar nada: el primer abogado de la lista sigue llenando los marcadores sin numerar de siempre. Una plantilla con dos o más firmantes usa los numerados.

Van en claro a propósito: son datos identificativos, no credenciales — la password maestra sigue siendo lo único cifrado con DPAPI. Si un marcador no tiene abogado correspondiente (p. ej. `{{abogado2Nombre}}` con solo un abogado capturado), el documento se genera igual y el marcador queda visible, como cualquier otro campo sin valor.

El archivo de una versión anterior a este cambio (`abogado.json`, un único abogado sin lista) se migra automáticamente la primera vez que arranca la app nueva: se envuelve en una lista de un elemento. El archivo viejo no se borra.

### Generación masiva

La lista de registros tiene una casilla por fila y una casilla **«Seleccionar todos»** en la cabecera, que marca o desmarca lo que el filtro deja a la vista. El flujo previsto es: filtrar → seleccionar todos → generar.

- El cuadro de filtro busca en el texto original, en la columna de nombre y en el número de fila.
- «Seleccionar todos» actúa solo sobre lo visible; **«Limpiar» desmarca todo**, también lo que el filtro esconde, para que no queden marcas invisibles que igual se generarían.
- El botón principal dice cuántos documentos va a generar. Se pide **una carpeta una sola vez** (o se usa `DefaultOutputFolder` si está configurada) y ahí cae un `.docx` por registro.
- Si dos registros comparten consecutivo, al segundo se le añade `-fila<N>` en vez de sobrescribir al primero en silencio.
- Un fallo en una fila **no aborta el lote**: al final se informa de cuántos se generaron, cuántos fallaron y qué marcadores quedaron sin valor en alguno.
- La generación corre fuera del hilo de UI, así que la ventana no se congela con lotes grandes.

### Ver el documento generado

El banner verde que aparece tras generar trae un botón contextual junto al mensaje:

- Al generar **un solo documento**: «Ver documento» lo abre con la app asociada de Windows (Word, o la que corresponda), igual que hacer doble clic en el archivo.
- Al generar **en lote**: «Abrir carpeta» abre en el Explorador la carpeta donde cayeron todos los `.docx`.

Si el archivo no se puede abrir (se movió, no hay ninguna app asociada), el aviso aparece en la barra de estado sin tocar el mensaje de éxito, que sigue siendo válido — el documento ya se generó.

### Modo sin conexión

Cada respuesta correcta se guarda en `%LOCALAPPDATA%\DocGenApp\cache.json`. Si el servidor no responde, la app muestra esa copia en modo solo lectura con un aviso y la fecha del caché. Los documentos se pueden seguir generando: la generación es completamente local.

## Despliegue (Docker / Dokploy)

```bash
docker build -f server/DocApi/Dockerfile -t docapi .
docker run -p 8080:8080 \
  -e Auth__MasterPassword="..." \
  -e GoogleSheets__SpreadsheetId="..." \
  -e GoogleSheets__ServiceAccountJson="$(cat service-account.json)" \
  docapi
```

Monta un volumen en `/app/cache` si quieres conservar el caché entre despliegues. `GET /health` sirve como healthcheck.

## Si algo no cuadra

### «Una fila que debería matchear sigue en errores de parseo»

Desde el cliente, «el patrón está mal» y «el servidor desplegado es de antes» se ven idénticos. Para distinguirlos:

```bash
curl -H "Authorization: Bearer <password>" https://tu-servidor/diagnostico
```

Si `patrones` trae **un solo** patrón, la instancia no tiene la lista: o no se ha redesplegado, o su configuración la está pisando. Con la lista cargada, `filasPorPatron` dice cuántas filas resolvió cada uno.

Dos cosas que pisan la lista silenciosamente:

- **`Parsing__Regex` (singular) como variable de entorno.** Solo se usa si `Regexes` está vacío, y `appsettings.json` ya trae `Regexes`, así que hoy se ignora. Si la tenías puesta en Dokploy, bórrala para no confundirte.
- **`Parsing__Regexes__0` en el entorno.** Reemplaza el patrón de esa posición. Si defines solo el índice 0, el de respaldo desaparece.

### «The Json value could not be converted to System.String. Path: $.resultado»

Servidor anterior a que los enums viajaran como texto: mandaba `{"resultado":0}` y el cliente esperaba `"Actualizado"`. El servidor ya manda texto y **el cliente aguanta las dos formas**, así que la combinación cliente nuevo + servidor viejo también funciona. Si lo ves, actualiza el cliente.

## Fuera de alcance (v1)

Sin escritura hacia Sheets, sin roles ni multiusuario, sin generación de documentos en el servidor, cliente solo para Windows.
