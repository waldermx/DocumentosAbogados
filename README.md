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
dotnet test           # 20 tests
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
| `GoogleSheets:Range` | `GoogleSheets__Range` | Rango A1 de la columna a leer. Por defecto `Hoja1!C:C`. |
| `GoogleSheets:TieneEncabezado` | `GoogleSheets__TieneEncabezado` | Si la primera fila del rango es encabezado. Por defecto `true`. |
| `Parsing:Regex` | `Parsing__Regex` | Patrón .NET con grupos con nombre. Ver abajo. |
| `Sync:PollingIntervalMinutes` | `Sync__PollingIntervalMinutes` | Cada cuánto se consulta si la hoja cambió. Por defecto `5`. |
| `Sync:CacheFilePath` | `Sync__CacheFilePath` | Dónde persistir el caché. Vacío = solo memoria. |

La service account necesita permiso de **lectura** sobre la hoja: compártela con el email de la service account. Los scopes que se piden son `spreadsheets.readonly` y `drive.readonly`.

### El regex

Por defecto:

```
^(?<consecutivo>\d+/\d{4})\s+(?<colegio>.+?)\s+DEL\s+(?<circuito>.+)$
```

Sobre `644/2026 TERCER COLEGIADO DEL DECIMOPRIMER CIRCUITO` extrae:

| Grupo | Valor |
|---|---|
| `consecutivo` | `644/2026` |
| `colegio` | `TERCER COLEGIADO` |
| `circuito` | `DECIMOPRIMER CIRCUITO` |

Está en configuración, no en el código. **Los nombres de los grupos son libres**: cada grupo con nombre se convierte automáticamente en un marcador `{{nombre}}` disponible en la plantilla, sin tocar código. Una fila que no coincide con el patrón no interrumpe la sincronización: se lista aparte en la app para que se corrija en la hoja.

## Endpoints

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| `GET` | `/health` | No | Estado y última sincronización. Para Traefik/Dokploy. |
| `GET` | `/registros` | Sí | Registros parseados + errores de parseo + `ultimaSync`. |
| `GET` | `/registros/{fila}` | Sí | Un registro. |
| `POST` | `/sync` | Sí | Fuerza un refresco. `?forzar=true` salta el chequeo de cambios y reparsea todo. |

## Cómo se sincroniza (y por qué es barato)

Cada ciclo de polling y cada `POST /sync` pasan por el mismo coordinador, en tres pasos:

1. **Chequeo barato** — se pregunta a Drive solo por `modifiedTime` de la hoja. Si no cambió, **se corta ahí**: no se lee la columna ni se reparsea nada.
2. **Lectura** — solo si la hoja cambió, una única llamada a Sheets por el rango configurado.
3. **Diff por fila** — cada valor se compara contra el de la sync anterior. Solo las filas cuyo texto cambió vuelven a pasar por el regex; el resto conserva su resultado.

Un `SemaphoreSlim` garantiza que varias solicitudes simultáneas (polling + botón «Actualizar») no disparen varias sincronizaciones: se coalescen en una.

Los logs reportan `Filas reparseadas` / `reutilizadas` en cada sync, que es la forma de comprobar que el diff funciona.

## La plantilla Word

`client/DocGenApp/Templates/plantilla.docx` es un **ejemplo**: reemplázalo por la plantilla real. Los marcadores se escriben como `{{nombre}}`:

| Marcador | Contenido |
|---|---|
| `{{consecutivo}}`, `{{colegio}}`, `{{circuito}}` | Los grupos del regex (o los que definas) |
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

## Fuera de alcance (v1)

Sin escritura hacia Sheets, sin roles ni multiusuario, sin generación de documentos en el servidor, cliente solo para Windows.
