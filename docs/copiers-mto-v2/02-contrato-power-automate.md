# Contrato Power Automate — envío MTO Firmado V2

## 1. Estado, identidad y alcance

- Estado: `Propuesto / flow no creado ni configurado`.
- Nombre conceptual: `Copiers MTO V2 - Send Ready Report`.
- Trigger único: modificación de la fila principal `signedMtoV2`.
- Filtro obligatorio: `WorkflowState=ReadyToSend AND EmailState=Pending`.
- Responsabilidad: claim ETag, resolución exacta del PDF/adjuntos customer-safe, draft Graph, send sin retry automático, reconciliación y persistencia del resultado.
- No crea MTO, no genera PDF, no transforma adjuntos y no modifica Evidence.
- No usa una tabla intermediaria; `emailOutboxKey` es solo un campo de correlación en la fila principal.

El flow jamás lee, escribe o dispara sobre `cr07a_mantenimiento`, tablas ticket anteriores o el flujo antiguo.

## 2. Bindings resueltos y configuración pendiente

| Binding conceptual | Regla |
| --- | --- |
| mainTable | Tabla física de signedMtoV2, resuelta por metadata. |
| evidenceTable | Tabla física de evidenceV2, resuelta por metadata. |
| workflowReadyToSendValue | Choice físico de ReadyToSend. |
| emailPendingValue | Choice físico de Pending. |
| emailProcessingValue | Choice físico de Processing. |
| emailSentValue | Choice físico de Sent. |
| emailFailedValue | Choice físico de Failed. |
| reportPurposeValue | Choice físico de SignedReport. |
| customerAttachmentPurposeValue | Choice físico de CustomerAttachment. |
| scanPassedValue | Choice físico de ScanPassed. |
| senderMailbox | Buzón dedicado autorizado. |
| graphCorrelationExtendedPropertyId | ID real de la single-value extended property; queda null hasta configuración aprobada. |
| binaryEqualityContractVersion | Valor fijo `graph-dataverse-content-v1`. |
| dataverseDownloadBase64Expression | Ruta exacta del contenido del action Dataverse exportado; queda null hasta inspeccionar el flow. |
| maxEmailBytes | Límite conservador total; queda pendiente de aprobación operativa. |
| processingEnabled | Kill switch; en false no reclama. |

Los logical names, entity sets, relaciones, solución, publisher/prefijo y valores Choice de Dataverse ya están resueltos y verificados en [artifacts/dataverse-resolved-bindings.v1.json](artifacts/dataverse-resolved-bindings.v1.json). Permanecen pendientes exclusivamente los bindings operativos del flujo: mailbox/identidad, connection references, IDs Graph, expresión binaria exportada, límite aprobado y kill switch.

## 3. Trigger y claim

Conector Dataverse: `When a row is added, modified or deleted`.

- Change type: Modified.
- Scope: Organization.
- Table: binding `mainTable`.
- Filter rows: bindings físicos de `ReadyToSend` y `Pending`.
- Select/filter attributes: únicamente WorkflowState, EmailState y ReadyAtUtc.
- Trigger condition defensiva: ambos estados siguen exactamente ReadyToSend/Pending.
- Concurrency inicial: 1; el control efectivo sigue siendo el ETag.

Secuencia del claim:

1. Si `processingEnabled=false`, terminar sin mutar.
2. Releer la proyección allowlisted y capturar ETag.
3. Si no está ReadyToSend/Pending, terminar `NotEligible` sin efectos.
4. Validar [artifacts/ready-to-send.schema.json](artifacts/ready-to-send.schema.json).
5. PATCH `If-Match` con solo EmailState=Processing y limpieza de errores seguros.
6. Si responde 412, releer; nunca ejecutar update incondicional. Otra ejecución que ya reclamó o terminó se trata como duplicado suprimido.

WorkflowState permanece ReadyToSend durante todo el procesamiento.

## 4. Proyección allowlisted

La lectura de la fila principal se limita a:

- recordKey, operationKey, WorkflowState, EmailState, ReadyAtUtc y row version;
- ReportEvidenceKey y AttachmentManifestJson;
- emailOutboxKey, emailToSnapshot, emailSubjectSnapshot y emailHtmlBodySnapshot;
- providerDraftId, internetMessageId y campos seguros de error que el flow actualiza.

Está prohibido seleccionar, expandir, registrar o pasar a otra acción:

- latitude, longitude, accuracyMeters, locationCapturedAtUtc o locationSource;
- ServiceAddress/serviceAddressInternal e internalNotes;
- answersJson y campos operativos no necesarios;
- signerName, signerRole, SignaturePointCount, signatureEvidenceKey/signatureSha256;
- filas Evidence `Signature` u `OriginalAttachment`;
- cualquier lookup/tabla legacy.

## 5. Resolución exacta de archivos

### Reporte

`ReportEvidenceKey` es el único selector del PDF:

1. Consultar por evidenceKey exacta, mismo padre y purpose=SignedReport.
2. Exigir exactamente una fila, capturar su ID y ETag, comprobar `application/pdf`, longitud y SHA-256 declarados contra la principal y descargar su File.
3. Releer la misma Evidence después de la descarga y exigir el mismo ETag; un cambio concurrente queda `Processing + alerta`.
4. Extraer el Base64 mediante la expresión resuelta del action Dataverse y canonicalizarlo con `base64(base64ToBinary(<dvBase64>))` como fuente de la comparación Graph.
5. Rechazar ausencia, duplicado, padre/propósito diferente o mismatch.

El flow no usa una File column de la principal y no reemplaza el reporte.

### Adjuntos customer-safe

`AttachmentManifestJson` es el único selector de adjuntos. Por cada item en sequence ascendente:

1. Validar `purpose=CustomerAttachment` y `securityState=ScanPassed` en el manifiesto.
2. Consultar una sola Evidence por evidenceKey exacta, mismo padre, purpose=CustomerAttachment y securityState=ScanPassed.
3. Capturar ID/ETag y verificar sequence, nombre genérico, MIME permitido, tamaño y SHA-256 declarados contra manifiesto/Evidence.
4. Descargar exactamente ese File, extraer/canonicalizar su Base64 y releer la Evidence; el ETag debe seguir intacto.

Reglas obligatorias:

- solo `image/jpeg` o `image/png` y nombre `adjunto-NNN.jpg|png`;
- no enumerar hijos para descubrir archivos;
- no enviar una Evidence ausente del manifiesto ni omitir una listada;
- rechazar claves/sequence duplicadas;
- no consultar/descargar `Signature` ni `OriginalAttachment`;
- no usar nombres originales en Graph, logs o correo.

Si AV/CDR/ScanPassed no está operativo y acreditado, la app no debe publicar y el piloto permanece apagado. El flow además aplica fail-closed: una Evidence sin ScanPassed termina en Failed sin crear/enviar correo.

La app es quien calcula SHA-256 sobre cada File descargado, lo compara con el binario de origen y relee todo antes de publicar `ReadyToSend`. El flow **no intenta recalcular SHA-256**, porque las expresiones estándar de Power Automate no ofrecen esa función. Su control independiente y realizable es: metadata/hash declarado iguales al snapshot inmutable, ETag estable alrededor de la descarga y comparación byte a byte entre Dataverse y Graph mediante Base64 canónico. Evidence queda técnicamente inmutable después de `ReadyToSend`: identidad del flow y técnicos sin update/delete, aplicación sin ruta de mutación pospublicación y auditoría/read-back activos.

Al construir el flow se exporta su definición y se fija **una sola** expresión real para la descarga Dataverse: si el action entrega envelope binario, se toma `body('Download_Evidence')?['$content']`; si entrega binario nativo, se usa `base64(body('Download_Evidence'))`. No se dejan ambas ramas activas y nunca se usa `string(body(...))`. El resultado se canonicaliza con `base64(base64ToBinary(<dvBase64>))`. En Graph se hace GET individual de cada attachment y se canonicaliza `body('Get_Graph_Attachment')?['contentBytes']` del mismo modo.

La igualdad exacta y sensible a mayúsculas se evalúa con `and(equals(length(dvCanon), length(graphCanon)), contains(dvCanon, graphCanon))`; `contains()` sobre strings es case-sensitive. Cualquier error de decodificación, output ambiguo o diferencia deja `Processing + alerta`, sin send. Las acciones de descarga Dataverse, GET Graph, Compose y Condition llevan Secure Inputs/Outputs; Base64 y `uploadUrl` nunca se registran.

Esta implementación usa solo acciones Dataverse/HTTP y expresiones estándar; no presupone Azure Function ni custom connector. Si el conector seleccionado no expone el contenido binario como `$content` de forma estable, `PowerAutomateDraftReconciliationVerified` permanece false hasta provisionar y documentar un verificador aprobado, con identidad, permisos y DLP propios.

## 6. Preflight maxEmailBytes

La app ya debió ejecutar el preflight antes de ReadyToSend; el flow lo repite antes de crear el draft con la fórmula canónica `graph-json-base64-v1`:

```text
estimated = 65536
          + utf8Bytes("graph-json-base64-v1")
          + utf8Bytes(subject)
          + utf8Bytes(htmlBody)
          + sum(utf8Bytes(recipient) + 256 for each recipient)
          + sum(4 * floor((fileBytes + 2) / 3)
                + utf8Bytes(fileName)
                + utf8Bytes(contentType)
                + 1024 for SignedReport and each CustomerAttachment)
```

`utf8Bytes` mide bytes UTF-8, no caracteres. La división es entera. `estimated <= maxEmailBytes` es aceptado; `estimated > maxEmailBytes` se marca `EMAIL_TOO_LARGE/Failed`, no crea draft y nunca omite adjuntos. La versión configurada debe ser exactamente `graph-json-base64-v1` en app y flow; otra versión falla configuración. Las pruebas P0 cubren justo debajo, igual y un byte/artefacto por encima del límite, además de cambios en To/Subject/Body.

Power Automate expande `utf8Bytes(value)` con expresiones WDL estándar; no usa `length(value)`, que cuenta caracteres:

```text
b64 = base64(coalesce(value, ''))
utf8Bytes(value) = sub(
    div(mul(length(b64), 3), 4),
    if(endsWith(b64, '=='), 2,
       if(endsWith(b64, '='), 1, 0)))

base64Bytes(fileBytes) = mul(4, div(add(fileBytes, 2), 3))
```

Estas expresiones se reutilizan sin variantes para destinatario, asunto, HTML, nombre y content type. Antes de habilitar el flow se comparan sus resultados con `Encoding.UTF8.GetByteCount` de la app usando ASCII, `á/ñ`, emoji y los puntos exactos `maxEmailBytes - 1`, `maxEmailBytes` y `maxEmailBytes + 1`.

## 7. Contrato Graph: draft, correlación y send

Está prohibida la acción de Power Automate **Send an email**. El flow usa llamadas Graph autorizadas y mantiene la correlación durable.

### 7.1 Correlación y reconciliación previa

1. `emailOutboxKey` es inmutable y se guarda como valor de la extended property configurada por `graphCorrelationExtendedPropertyId`.
2. Todas las operaciones que devuelven IDs solicitan `Prefer: IdType="ImmutableId"`.
3. Antes de crear un draft o intentar send, buscar primero por `providerDraftId` si existe y después reconciliar Drafts y Sent Items por la extended property=`emailOutboxKey`.
4. Si ya existe en Sent Items, persistir/confirmar `providerDraftId` e `internetMessageId`, marcar Sent con ETag y terminar.
5. Si existe un único draft correlacionado, reutilizarlo. Cero o más de uno fuera del caso esperado requieren clasificación/alerta; nunca crear otro sin resolver.

El identificador físico de la extended property no se inventa aquí; debe configurarse y probarse en el buzón confirmado.

### 7.2 Creación/verificación del draft

Si la reconciliación demuestra que no existe draft ni mensaje enviado:

1. Crear un draft Graph con destinatarios/asunto/HTML congelados y la extended property=`emailOutboxKey`.
2. Persistir inmediatamente `providerDraftId` y el `internetMessageId` que Graph exponga, mediante PATCH ETag sobre la principal.
3. Construir el conjunto esperado inmutable: SignedReport más todos los CustomerAttachment, cada uno identificado por `name + contentType + size + sha256`; los nombres son únicos y cada elemento conserva en memoria del run su Base64 canónico de Dataverse.
4. Adjuntar o reconciliar cada archivo según 7.2.1.
5. Releer el draft con `Prefer: IdType="ImmutableId"` y verificar correlación, destinatarios, asunto y conjunto exacto de adjuntos antes de send.

Un timeout/5xx durante creación es ambiguo: reconciliar Drafts/Sent Items por correlación antes de repetir. No se permite retry automático de POST con efectos.

#### 7.2.1 Carga y recuperación exacta de adjuntos

Al reutilizar un draft, el flow primero lista sus attachments y después hace GET individual de cada candidato; no presupone que la lista incluya `contentBytes`. Para cada archivo exige `@odata.type=#microsoft.graph.fileAttachment`, ID presente, `isInline=false`, nombre exacto, MIME normalizado, tamaño esperado y Base64 canónico exactamente igual al origen Dataverse mediante la expresión case-sensitive anterior. Un archivo exacto se reutiliza; uno ausente se carga. Un attachment con nombre esperado pero bytes/metadata distintos se elimina y se relee antes de reemplazarlo. Duplicados, nombres inesperados o una eliminación ambigua dejan `Processing + alerta` hasta reconciliación; nunca se envía un conjunto incierto. `lastModifiedDateTime` o `changeKey` son solo señales auxiliares, no prueba de contenido ni precondición. No se convierten los binarios a texto ni se registran sus Base64.

La rama se decide por tamaño de cada archivo, conforme al contrato oficial de Graph:

- `< 3 MiB`: `POST /messages/{id}/attachments`. Si el resultado es ambiguo, relistar, descargar y comparar bytes con la fuente Dataverse; solo repetir después de demostrar que el archivo exacto está ausente.
- `>= 3 MiB` y `<= 150 MiB`: `createUploadSession` y `PUT` secuenciales. Tras timeout/5xx se consulta `nextExpectedRanges` y se reanuda desde el offset confirmado. El `uploadUrl` es secreto efímero: solo vive en memoria del run y nunca se persiste ni registra. Si expira o el último PUT es ambiguo, se relista, descarga y compara byte a byte el draft antes de crear otra sesión.
- `> 150 MiB`: `EMAIL_TOO_LARGE/Failed`; no aplica al piloto: cada CustomerAttachment admite máximo 8 MiB y SignedReport máximo 12 MiB.

Una sesión parcial que todavía no materializó un attachment no cuenta como archivo. Inmediatamente antes de `send`, el flow relista, hace GET individual y demuestra correspondencia 1:1 para cada elemento esperado mediante `graph-dataverse-content-v1`, con cero extras. Véase [recurso fileAttachment/contentBytes](https://learn.microsoft.com/en-us/graph/api/resources/fileattachment?view=graph-rest-1.0), [POST para archivos menores de 3 MB](https://learn.microsoft.com/en-us/graph/api/message-post-attachments?view=graph-rest-1.0), [upload session para 3-150 MB](https://learn.microsoft.com/en-us/graph/api/attachment-createuploadsession?view=graph-rest-1.0) y [funciones de expresión/Base64 aplicables a Power Automate](https://learn.microsoft.com/en-us/azure/logic-apps/expression-functions-reference).

### 7.3 Send sin retry automático

1. Configurar la acción Graph `send` con retry policy `None`.
2. Ejecutar send una sola vez sobre el `providerDraftId` reconciliado.
3. No marcar Sent solo por HTTP 202/run Succeeded; reconciliar Sent Items por `emailOutboxKey`, recuperar `internetMessageId` y hacer read-back Dataverse.
4. Error terminal explícito antes de aceptación puede terminar Failed con detalle seguro.
5. Timeout, desconexión, 5xx o resultado no concluyente queda **EmailState=Processing + alerta**. Nunca cambia a Failed ni vuelve a Pending automáticamente.
6. Antes de cualquier actuación posterior, reconciliar primero Sent Items y Drafts. Está prohibido el reenvío ciego.

## 8. Estados, retry y read-back

- GET de Dataverse/Graph y descargas pueden usar backoff acotado.
- Claim ETag, creación de draft y send no tienen retry ciego; siempre requieren read-back/reconciliación.
- Hash/schema/ScanPassed/preflight incompatibles: Failed sin correo.
- Resultado Graph ambiguo: Processing y alerta hasta resolución.
- Sent: mensaje encontrado de forma inequívoca en Sent Items y IDs persistidos.
- Failed: fallo terminal inequívoco; conserva MTO, manifest y Evidence.

El read-back de la app posterior a publicación acepta `EmailState` Pending, Processing, Sent o Failed porque el flow puede reclamar inmediatamente. Los campos base, ReportEvidenceKey, AttachmentManifestJson y hashes deben seguir coincidiendo integralmente.

## 9. Errores mínimos

| Código | Estado/resultado |
| --- | --- |
| CONFIGURATION_MISSING | Failed; cero correo. |
| ROW_NOT_ELIGIBLE | Control; sin mutación. |
| CONCURRENT_CLAIM | Control; sin efectos. |
| READY_CONTRACT_INVALID | Failed. |
| REPORT_EVIDENCE_NOT_FOUND | Failed. |
| ATTACHMENT_MANIFEST_INVALID | Failed. |
| CUSTOMER_ATTACHMENT_NOT_SCAN_PASSED | Failed. |
| EVIDENCE_MISMATCH | Failed. |
| EMAIL_TOO_LARGE | Failed; cero draft/correo. |
| GRAPH_DRAFT_AMBIGUOUS | Processing + alerta/reconciliación. |
| GRAPH_SEND_REJECTED | Failed solo si el rechazo es terminal e inequívoco. |
| GRAPH_SEND_AMBIGUOUS | Processing + alerta; nunca reenvío automático. |
| GRAPH_RECONCILIATION_CONFLICT | Processing + alerta/manual. |
| FINAL_READBACK_FAILED | Alerta; no declarar Sent. |

Los mensajes persistidos/logs no contienen ubicación, dirección, notas internas, firma, OriginalAttachment, binarios, tokens ni respuestas completas.

## 10. Aceptación del flow

- un solo claim ETag cambia Pending→Processing;
- el reporte proviene solo de ReportEvidenceKey;
- los adjuntos provienen solo de AttachmentManifestJson y son CustomerAttachment+ScanPassed;
- preflight maxEmailBytes aprobado sin omisiones;
- draft correlacionado por emailOutboxKey, IDs inmutables persistidos y send sin retry automático;
- toda ambigüedad queda Processing/alerta y se reconcilia antes de otra acción;
- EmailState/IDs se confirman por read-back;
- cero lecturas de firma, originales, datos internos o legacy;
- cero creación/configuración cloud se deriva de este documento.
