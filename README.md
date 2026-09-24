# DocumentosAbogados

Genera documentos Word a partir de un registro que vive en Google Sheets.

- **`server/DocApi`** — ASP.NET Core Minimal API. Es el único componente que conoce las credenciales de Google. Lee las columnas configuradas de la hoja, tal cual, y las sirve cacheadas. Solo lectura: nunca escribe en Sheets ni genera documentos.
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
dotnet test           # 76 tests
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
| `GoogleSheets:Hoja` | `GoogleSheets__Hoja` | Nombre de la pestaña, tal como aparece abajo en Google Sheets. Por defecto `Hoja1`. |
| `GoogleSheets:Columnas` | `GoogleSheets__Columnas__0__Columna` | Qué columnas se leen y con qué nombre. Ver abajo. |
| `GoogleSheets:TieneEncabezado` | `GoogleSheets__TieneEncabezado` | Si la primera fila de la hoja es encabezado. Por defecto `true`. |
| `Sync:CacheFilePath` | `Sync__CacheFilePath` | Dónde persistir el caché. Vacío = solo memoria. |
| `Sync:ImpresosFilePath` | `Sync__ImpresosFilePath` | Dónde persistir las marcas de impreso. Vacío = solo memoria. |

La service account necesita permiso de **lectura** sobre la hoja: compártela con el email de la service account. Los scopes que se piden son `spreadsheets.readonly` y `drive.readonly`.

### Las columnas

Cada columna configurada se copia **tal cual**, sin interpretarla, y queda disponible en la plantilla como `{{Nombre}}`. Por defecto:

```json
"GoogleSheets": {
  "Hoja": "Hoja1",
  "Columnas": [
    { "Nombre": "consecutivo", "Columna": "B" },
    { "Nombre": "nombre", "Columna": "D" },
    { "Nombre": "circuito", "Columna": "E" }
  ]
}
```

| Columna en la hoja | Marcador | Ejemplo |
|---|---|---|
| B — Amparo número | `{{consecutivo}}` | `644/2026` |
| D — Nombre | `{{nombre}}` | `JUAN PEREZ LOPEZ` |
| E — Circuito | `{{circuito}}` | `Quinto Tribunal Colegiado en Materia Administrativa del Tercer Circuito` |

Detalles que importan:

- **Una sola llamada a Sheets por sync**: todas las columnas se piden juntas con `values.batchGet`.
- Una fila entra en la lista si **cualquiera** de sus columnas tiene algo. Una celda vacía no la descarta: el campo queda vacío y el marcador se reporta como faltante al generar.
- Cada `Nombre` debe ser único; si no, el servidor falla al arrancar con un mensaje explícito.
- Por env var, `Columnas` es una **lista**: lleva el índice (`GoogleSheets__Columnas__2__Columna=F`). Definir un índice pisa solo esa posición.

### Cambiar de hoja reinicia los datos

El caché y las marcas de impreso se guardan junto con el **origen** del que salieron: spreadsheet, pestaña y columnas. Si al arrancar el servidor el origen configurado no coincide, lo guardado se descarta y se arranca vacío hasta la primera sincronización. Las marcas se indexan por número de fila, y con otra hoja la fila 5 es otro expediente.

No hace falta borrar el volumen a mano: cambiar `GoogleSheets__SpreadsheetId` (o la pestaña, o las columnas) y redesplegar basta.

## Endpoints

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| `GET` | `/health` | No | Estado y última sincronización. Para Traefik/Dokploy. |
| `GET` | `/diagnostico` | Sí | Qué configuración tiene cargada **esta instancia**: spreadsheet, pestaña y columnas con su rango. |
| `GET` | `/registros` | Sí | Registros + marcas de impreso + `ultimaSync`. |
| `GET` | `/registros/{fila}` | Sí | Un registro. |
| `POST` | `/registros/{fila}/impreso` | Sí | Marca la fila como generada/impresa. |
| `DELETE` | `/registros/{fila}/impreso` | Sí | Quita la marca. |
| `DELETE` | `/registros/impresos` | Sí | Quita todas las marcas. |
| `POST` | `/sync` | Sí | Sincroniza con la hoja. `?forzar=true` salta el chequeo de cambios. |

## Cómo se sincroniza

**Solo a mano**: con el botón «Sincronizar» del cliente (`POST /sync?forzar=true`). El servidor no consulta la hoja por su cuenta, ni siquiera al arrancar; hasta la primera sincronización la lista está vacía.

Cada `POST /sync` pasa por el mismo coordinador:

1. **Chequeo barato** — se pregunta a Drive solo por `modifiedTime` de la hoja. Sin `forzar`, si no cambió, **se corta ahí**.
2. **Lectura** — una única llamada a Sheets (`values.batchGet`) que trae todas las columnas a la vez.
3. **Reemplazo** — los valores se copian a los registros y se reemplaza el caché. Una fila marcada como impresa pierde la marca si cualquiera de sus columnas cambió o si desapareció de la hoja.

Un `SemaphoreSlim` garantiza que varias solicitudes simultáneas no disparen varias lecturas: se coalescen en una.

## La plantilla Word

`client/DocGenApp/Templates/plantilla.docx` es la plantilla que usa la app. Los marcadores se escriben como `{{nombre}}`:

| Marcador | Contenido |
|---|---|
| `{{consecutivo}}`, `{{nombre}}`, `{{circuito}}` | Las columnas de la hoja (o las que configures) |
| `{{abogadoNombre}}`, `{{abogadoFirel}}`, `{{abogadoCedula}}` | El **primer** abogado capturado en la app |
| `{{abogado1Nombre}}`, `{{abogado2Nombre}}`, ... | Cada abogado capturado, numerado por su posición en la lista — ver [Datos de los abogados](#datos-de-los-abogados) |
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

- El cuadro de filtro busca en todas las columnas (amparo, nombre, circuito) y en el número de fila.
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
  -e GoogleSheets__Hoja="..." \
  -e GoogleSheets__ServiceAccountJson="$(cat service-account.json)" \
  docapi
```

Monta un volumen en `/app/cache` si quieres conservar el caché entre despliegues. `GET /health` sirve como healthcheck.

## Si algo no cuadra

### «La lista sale vacía» o «faltan columnas»

Desde el cliente, «la pestaña o las columnas están mal» y «el servidor desplegado es de antes» se ven idénticos. Para distinguirlos:

```bash
curl -H "Authorization: Bearer <password>" https://tu-servidor/diagnostico
```

Muestra el `spreadsheetId`, la `hoja` y cada columna con el rango que se pide a Sheets (`'Hoja1'!B:B`). Si la pestaña no existe, `POST /sync` devuelve 502 con el error de Google. Recuerda que la lista está vacía hasta pulsar «Sincronizar»: el servidor no sincroniza solo.

### «The Json value could not be converted to System.String. Path: $.resultado»

Servidor anterior a que los enums viajaran como texto: mandaba `{"resultado":0}` y el cliente esperaba `"Actualizado"`. El servidor ya manda texto y **el cliente aguanta las dos formas**, así que la combinación cliente nuevo + servidor viejo también funciona. Si lo ves, actualiza el cliente.

## Fuera de alcance (v1)

Sin escritura hacia Sheets, sin roles ni multiusuario, sin generación de documentos en el servidor, cliente solo para Windows.
