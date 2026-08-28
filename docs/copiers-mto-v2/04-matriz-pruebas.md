# Matriz de pruebas — Copiers MTO Firmado V2

## Reglas

- Estado de esta matriz: propuesta; no demuestra aprovisionamiento ni ejecución cloud.
- P0 bloquea piloto/producción; P1 bloquea ampliación.
- QA usa ambiente aislado y datos sintéticos.
- Cada mutación se verifica mediante read-back independiente. La app verifica cada File por longitud y SHA-256; el flow fija el ETag y compara byte a byte Dataverse contra Graph con `$content` Base64 canónico.
- Un run Succeeded, HTTP 202 o un health check no sustituye estados de negocio ni reconciliación Graph.

## Esquema, bindings y concurrencia

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| SCH-01 | P0 | Validar catálogo antes de metadata | logicalName, schemaName, entity sets, navigation, solution/publisher/prefix, IDs Graph y todos los optionValue siguen null/proposed. |
| SCH-02 | P0 | Resolver CustomerAccepted/SignaturePointCount | Boolean/WholeNumber correctos; nombres reales leídos de metadata. |
| SCH-03 | P0 | Equipo sin lookup y serial válido | Draft permitido; equipment nulo y equipmentSerialSnapshot obligatorio persistido. |
| SCH-04 | P0 | Equipo y serial ambos ausentes | Rechazo antes de crear/publicar. |
| SCH-05 | P0 | Metadata Decimal | latitude, longitude y accuracyMeters son Decimal con precision=7. |
| SCH-06 | P0 | Redondeo positivo/negativo >7 decimales | App normaliza a siete posiciones y read-back coincide exactamente; modo de redondeo queda evidenciado. |
| SCH-07 | P0 | Límites geográficos tras redondeo | -90/90 y -180/180 válidos; fuera de rango rechazado. |
| CHO-01 | P0 | WorkflowState metadata | Draft/Finalizing/ReadyToSend/Failed existen, valores reales y distintos dentro del Choice. |
| CHO-02 | P0 | EmailState metadata | NotReady/Pending/Processing/Sent/Failed existen, valores reales y distintos. |
| CHO-03 | P0 | EvidencePurpose metadata | Signature/SignedReport/OriginalAttachment/CustomerAttachment existen y son distintos. |
| CHO-04 | P0 | EvidenceSecurityState metadata | NotApplicable/Pending/ScanPassed/Rejected existen y son distintos. |
| CHO-05 | P0 | MaintenanceType metadata | Correctivo y Preventivo existen, sus valores físicos no son null/placeholder y son distintos. |
| CHO-06 | P0 | Comparar configuración vs metadata | Cada binding corresponde al miembro correcto; no se copiaron números legacy. |
| CON-01 | P0 | EntityMetadata signedMtoV2 | IsOptimisticConcurrencyEnabled=true por read-back. |
| CON-02 | P0 | Alternate keys | signedMtoByOperationKey y evidenceByContentKey están Active, no Pending. |
| CON-03 | P0 | Dos PATCH con mismo ETag | Primero aplica; segundo devuelve HTTP 412 Precondition Failed. |
| CON-04 | P0 | Read-back tras CON-03 | Solo persiste el primer cambio; ningún merge/update incondicional. |
| REL-01 | P0 | Inspeccionar relaciones | Cliente obligatorio, equipo opcional, padre Evidence obligatorio; cero relación legacy. |

## Identidad, autorización y piloto

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| SEC-01 | P0 | PilotEnabled=false | Denegado antes de escritura, aunque técnico/cliente estén allowlisted. |
| SEC-02 | P0 | Allowlist técnica vacía | Deniega a todos. |
| SEC-03 | P0 | Técnico fuera de allowlist | Denegado servidor-side; ocultar UI no es la única defensa. |
| SEC-04 | P0 | Cliente fuera de allowlist configurada | Denegado servidor-side. |
| SEC-05 | P0 | Allowlist clientes vacía + técnico permitido | No añade restricción por cliente. |
| SEC-06 | P0 | Técnico intenta create/update directo en V2 | Dataverse deniega signedMtoV2/evidenceV2 y File. |
| SEC-07 | P0 | Application User de app | Solo privilegios mínimos create/read/update/append/file V2 y read cliente/equipo; sin delete/admin/legacy. |
| SEC-08 | P0 | Técnico A accede MTO/Evidence de B por API app | Denegado según autorización y ownership. |
| SEC-09 | P0 | Inspeccionar identidad efectiva de escritura | createdby/telemetría corresponden al Application User; technicianUserKey conserva actor humano autenticado. |
| SEC-10 | P0 | Inspeccionar flow/roles V2 | Cero permisos, bindings, triggers o acciones sobre cr07a_mantenimiento, tickets/flow antiguos. |
| SEC-11 | P0 | Faltan `CopiersMtoV2:DataverseApp:*`, pero existen credenciales globales | Falla cerrado; cero fallback a `Dataverse:*` o `AzureAd:*`. |

## Aplicación, staging y publicación

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| APP-01 | P0 | Crear/reusar por operationKey | Una fila Draft/NotReady; read-back identidad/ETag. |
| APP-02 | P0 | Replay con snapshot base completo idéntico | Reutiliza; no duplica. |
| APP-03 | P0 | Replay Finalizing/Ready cambiando técnico, cliente, contacto/correo, equipo/serial, título, fecha o tipo | Conflicto; no devuelve como éxito el reporte anterior. |
| APP-04 | P0 | CustomerAccepted=false | Failed/NotReady; cero publicación. |
| APP-05 | P0 | SignaturePointCount insuficiente | Failed/NotReady; cero publicación. |
| APP-06 | P0 | Adquirir finalización | PATCH ETag a Finalizing/NotReady + lease confirmado. |
| APP-07 | P0 | Dos finalizadores | Solo uno adquiere; segundo 412/InProgress. |
| APP-08 | P0 | Mientras crea/escanea archivos | Permanece Finalizing/NotReady; trigger no elegible. |
| APP-09 | P0 | PDF profesional | Texto exacto visible `REPORTE CERRADO Y FIRMADO`. |
| APP-10 | P0 | Marcadores internos en ubicación/dirección/notas | Ausentes del PDF, correo, schemas, inputs/outputs y logs. |
| STG-01 | P0 | Guardar staging completo | Base, decimal normalizado, aceptación, claves/hashes/manifest/correo persisten en Finalizing/NotReady. |
| STG-02 | P0 | Alterar cualquier campo/hash/key/manifest/seguridad | Read-back integral falla y no publica. |
| PUB-01 | P0 | Inspeccionar PATCH final | Solo ReadyAtUtc, EmailState=Pending y WorkflowState=ReadyToSend con If-Match. |
| PUB-02 | P0 | Cambio entre read-back y publicación | HTTP 412; no publicación parcial. |
| PUB-03 | P0 | Read-back inmediato sin claim | Base íntegra y ReadyToSend/Pending aceptado. |
| PUB-04 | P0 | Flow reclama antes del read-back app | Base íntegra y ReadyToSend/Processing aceptado. |
| PUB-05 | P0 | Flow termina antes del read-back app | Base íntegra y ReadyToSend/Sent o ReadyToSend/Failed aceptado. |
| PUB-06 | P0 | Falla antes de publicación | Failed/NotReady, ReadyAtUtc nulo; trigger no elegible. |

## Evidence, adjuntos y seguridad

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| EVD-01 | P0 | Repetir operationKey/slot/sequence/SHA | Misma evidenceKey/fila. |
| EVD-02 | P0 | Key existente con padre/hash/propósito diferente | Conflicto; no sobrescribe. |
| EVD-03 | P0 | Firma y reporte | Signature/SignedReport sequence 0; descarga, longitud y SHA correctos. |
| EVD-04 | P0 | JPG/JPEG/PNG válido | Upload crudo descartado; OriginalAttachment interno ya saneado y CustomerAttachment derivado/verificado. |
| EVD-05 | P0 | PDF/HEIC/GIF/SVG u otro adjunto | Rechazo en piloto antes de Evidence publicable. |
| EVD-06 | P0 | Extensión/MIME falsos o imagen corrupta | Rechazo por validación de contenido/decoder. |
| EVD-07 | P0 | Upload con EXIF GPS/XMP/IPTC/perfil/nombre sensible | Ningún binario persistido conserva metadata; solo el nombre permanece en OriginalAttachment interno y nunca llega a derivado/correo/PDF/log. |
| EVD-08 | P0 | Nombre de derivado | Formato genérico adjunto-NNN.jpg/png, secuencia estable. |
| EVD-09 | P0 | AV/CDR no disponible | Gate pendiente, PilotEnabled=false y cero publicación. |
| EVD-10 | P0 | Pending/Rejected en original o derivado | Cero ReadyToSend; fail-closed. |
| EVD-11 | P0 | ScanPassed válido | Estado/proveedor/hora se releen; solo entonces puede publicarse. |
| EVD-12 | P0 | Manifest | Solo CustomerAttachment+ScanPassed; campos exactos, orden estable, sin originales/firma/reporte/datos internos. |
| EVD-13 | P0 | Evidence adicional no listada | Flow no la consulta ni envía. |
| EVD-14 | P0 | Flow intenta Signature/OriginalAttachment | Prueba/inspección falla P0; cero acceso permitido por contrato. |
| EVD-15 | P0 | Validación de adjunto falla | UI y error HTTP muestran alias/posición genérica; el nombre original queda solo en payload/metadata interna restringida. |

## Trigger, maxEmailBytes y Graph

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| TRG-01 | P0 | ReadyToSend/Pending | Una ejecución elegible en fila principal. |
| TRG-02 | P0 | Otros pares de estado | Cero ejecuciones elegibles. |
| TRG-03 | P0 | Dos runs | Un claim Pending→Processing; segundo 412/duplicado suprimido. |
| TRG-04 | P0 | Inspeccionar select | Solo control/correo/IDs Graph, ReportEvidenceKey y AttachmentManifestJson; cero campos internos. |
| SIZ-01 | P0 | Estimado justo bajo maxEmailBytes | App/flow calculan igual; elegible. |
| SIZ-02 | P0 | Estimado igual al límite | Resultado conforme a regla documentada, idéntico en app/flow. |
| SIZ-03 | P0 | Estimado sobre límite | Failed antes de draft; cero envío parcial/omisión. |
| SIZ-04 | P0 | Overhead/base64/cuerpo cambia | Algoritmo versionado lo incorpora; no usa solo suma de binarios. |
| SIZ-05 | P0 | Cambiar destinatario/asunto/HTML o versión | App/flow recalculan con `graph-json-base64-v1`; versión distinta falla configuración. |
| SIZ-06 | P0 | Conteo UTF-8 en Power Automate | La fórmula Base64 WDL coincide con `Encoding.UTF8.GetByteCount` para ASCII, á/ñ y emoji; nunca usa `length(value)` como bytes. |
| SIZ-07 | P0 | Vectores de borde con texto multibyte | App/flow coinciden exactamente en `maxEmailBytes-1`, igual y `+1`. |
| GRF-01 | P0 | Inspeccionar definición | No existe acción Send an email; send Graph tiene retry policy None. |
| GRF-02 | P0 | Crear draft | Extended property contiene emailOutboxKey y requests usan Prefer IdType=ImmutableId. |
| GRF-03 | P0 | Persistencia inicial | providerDraftId e internetMessageId disponible quedan en principal y se releen. |
| GRF-04 | P0 | Replay con draft correlacionado | Reutiliza único draft; no crea segundo. |
| GRF-05 | P0 | Mensaje ya en Sent Items | Reconcilia por emailOutboxKey, persiste IDs y marca Sent sin reenviar. |
| GRF-06 | P0 | Draft con adjuntos | PDF + CustomerAttachment exactos; nombres/MIME/tamaños coinciden. |
| GRF-06A | P0 | Verificar binario Graph | Evidence conserva el mismo ETag alrededor de la descarga; Base64 se canonicaliza decodificando/recodificando y longitud+`contains` case-sensitive prueban igualdad exacta; Base64 no se registra. |
| GRF-06B | P0 | Inspeccionar output del action Dataverse exportado | Una sola ruta real (`body.$content` o `base64(body)`) queda fijada; no existe `string(body)` ni doble codificación. |
| GRF-06C | P0 | Inmutabilidad Evidence pospublicación | Flow/técnicos sin update/delete, app sin ruta de mutación y auditoría/read-back activos. |
| GRF-07 | P0 | HTTP 202 de send | No marca Sent hasta encontrar el mensaje en Sent Items y hacer read-back. |
| GRF-08 | P0 | Timeout/5xx durante send | EmailState=Processing, alerta, cero retry automático y cero cambio automático a Pending/Failed. |
| GRF-09 | P0 | Antes de actuación sobre ambiguo | Reconciliación Drafts/Sent Items por providerDraftId/emailOutboxKey obligatoria. |
| GRF-10 | P0 | Dos drafts/mensajes correlacionados | Processing + alerta/conflicto; nunca nuevo send. |
| GRF-11 | P0 | Error terminal inequívoco | Failed seguro; MTO/Evidence intactos. |
| GRF-12 | P0 | Read-back final | ReadyToSend + EmailState/IDs/error exactos; Sent requiere providerDraftId e internetMessageId. |
| GRF-13 | P0 | Archivo <3 MiB | POST simple; respuesta ambigua obliga a relistar, descargar y comparar bytes contra Dataverse antes de repetir. |
| GRF-14 | P0 | Archivo desde 3 MiB dentro del límite por tipo (CustomerAttachment <=8 MiB; SignedReport <=12 MiB) | Upload session, nextExpectedRanges y reanudación desde offset confirmado; uploadUrl no persiste ni aparece en logs. |
| GRF-15 | P0 | Caída entre adjuntos o último chunk ambiguo | Reutiliza exactos, completa ausentes y no duplica; conjunto final exacto antes de send. |
| GRF-16 | P0 | Attachment duplicado/extra/mismatch | Cero send; eliminación+read-back inequívocos o Processing+alerta. |
| GRF-17 | P0 | Matriz binaria | 1 byte, 2 bytes/padding, cambio de un byte al inicio/medio/final, mismo largo con cambio solo de case Base64, 3 MiB, 8 MiB y 12 MiB se clasifican correctamente. |
| GRF-18 | P0 | Inspeccionar secretos de acciones | Descarga DV, GET Graph, Compose y Condition tienen Secure Inputs/Outputs; Base64/uploadUrl no aparecen en historial/logs. |

## Paralelo y rollback

| ID | Pri. | Escenario | Resultado esperado |
| --- | --- | --- | --- |
| LEG-01 | P0 | Ejecutar operación legacy | No despierta ni muta V2. |
| LEG-02 | P0 | Ejecutar ciclo V2 | Cero lecturas/escrituras/acciones legacy. |
| RBK-01 | P0 | PilotEnabled=false + processingEnabled=false | No nuevas filas publicables/claims; Evidence intacta. |
| RBK-02 | P0 | Hay Processing ambiguo | Reconciliar; no reenviar ni volver a Pending. |
| RBK-03 | P1 | Reanudar Failed inequívoco aprobado | ETag, mismas keys/manifest/emailOutboxKey; no duplica correo. |

## Evidencia mínima

- ambiente, solución, publisher y artefacto exactos;
- metadata/read-back de choices, precision, optimistic concurrency y keys Active;
- respuesta HTTP 412 y read-back de la prueba de doble PATCH;
- identidad/rol efectivo del Application User y denegación directa al técnico;
- estado de PilotEnabled/allowlists;
- recordKey, operationKey, ETag y estados antes/después;
- evidenceKey, purpose, securityState, provider/hora, longitud y hashes;
- prueba de metadata eliminada y nombres genéricos;
- AttachmentManifestJson y preflight maxEmailBytes;
- PATCH final sin campos adicionales;
- definición Graph, extended correlation, retry policy None, providerDraftId/internetMessageId y reconciliación;
- contrato `graph-dataverse-content-v1`, ETag estable y comparación binaria exacta Dataverse/Graph;
- read-back final.

No anexar firmas, coordenadas, direcciones, originales ni datos personales reales al repositorio de pruebas.

## Salida del piloto

- todos los P0 aprobados;
- AV/CDR/ScanPassed operativo, no pendiente;
- cero duplicados, acceso directo técnico, exposición interna o referencias legacy;
- 100% de staging/publicación/read-back correcto;
- 100% de ambigüedades Graph en Processing con alerta y reconciliación, nunca reenvío ciego.

