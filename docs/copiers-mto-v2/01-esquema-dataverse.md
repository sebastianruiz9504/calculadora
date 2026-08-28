# Especificación conceptual de esquema Dataverse

## 1. Convenciones obligatorias

- `signedMtoV2` es la propia fila ticket/MTO V2; no existe lookup ni sincronización con una tabla ticket anterior.
- Los `conceptId` son estables. `logicalName`, `schemaName`, `entitySetName`, navigation properties, solución, publisher/prefijo y valores numéricos Choice quedan `null` hasta inspeccionar metadata real.
- Este documento es de diseño: no aprovisiona tablas, columnas, relaciones, roles, Application Users ni flows.
- Ambas tablas son privadas, User/Team owned y auditadas.
- `signedMtoV2` debe tener `IsOptimisticConcurrencyEnabled=true`; todos los cambios de estado usan ETag/`If-Match`.
- `EmailState` inicia en `NotReady`; la app completa el staging en `Finalizing/NotReady` y publica con un PATCH mínimo `ReadyToSend/Pending`.
- Ubicación, `ServiceAddress`, notas internas, firma y archivos originales son internos; nunca forman parte del contrato de correo.

El catálogo legible por máquina está en [artifacts/dataverse-concept-catalog.v1.json](artifacts/dataverse-concept-catalog.v1.json).

## 2. Tabla signedMtoV2 — MTO Firmado V2

### Identidad, relaciones y estados

| conceptId / contrato | Tipo | Req. | Regla |
| --- | --- | --- | --- |
| name | Texto 160 | Sí | Nombre legible, no clave de integración. |
| operationKey | Texto 128 | Sí | Clave idempotente de 16–128 caracteres; alternate key única. |
| workflowState / WorkflowState | Choice local | Sí | Draft, Finalizing, ReadyToSend, Failed. |
| emailState / EmailState | Choice local | Sí | Default NotReady; Pending, Processing, Sent o Failed después de publicar. |
| client | Lookup configurable | Sí | Cliente validado por servidor; binding pendiente. |
| equipment | Lookup configurable | **No** | Opcional; si se informa, la app valida la relación. |
| technicianUserKey | Texto | Sí | Derivado de la identidad autenticada, nunca confiado desde navegador. |
| technicianNameSnapshot | Texto 200 | Sí | Snapshot para reporte/auditoría. |
| technicianEmailSnapshot | Texto 320 | Sí | Interno y protegido. |
| finalizationFingerprint | Texto 64 | Staging | Huella canónica para replay idempotente. |
| finalizationLeaseKey | Texto 64 | Staging | Lease único durante Finalizing/NotReady. |
| readyAtUtc | Fecha/hora | Publicación | Se escribe únicamente en el PATCH mínimo. |
| rowRevision | Row version nativa | N/A | ETag obligatorio; optimistic concurrency habilitada en metadata. |

El lookup de equipo puede quedar nulo. Esto no relaja el snapshot: `equipmentSerialSnapshot` siempre debe existir y ser no vacío, tanto si el serial proviene del equipo relacionado como si fue capturado/validado como serial externo.

### Snapshot del servicio

| conceptId | Tipo | Req. | Regla |
| --- | --- | --- | --- |
| clientNameSnapshot | Texto | Sí | Visible en PDF/correo según plantilla. |
| clientContactNameSnapshot | Texto | Sí | Visible permitido. |
| clientEmailSnapshot | Texto 320 | Sí | Destinatario congelado y validado. |
| equipmentSerialSnapshot | Texto 200 | **Sí** | Obligatorio aun con lookup equipment nulo. |
| title | Texto 250 | Sí | Título del MTO V2. |
| serviceDate | Solo fecha | Sí | Fecha del servicio. |
| maintenanceType | Choice local | Sí | Correctivo o Preventivo; valor físico resuelto desde metadata. |
| formVersion | Texto 80 | Finalización | Versión allowlisted del formulario/PDF. |
| answersJson | Texto multilínea | Finalización | Respuestas canónicas; rechaza claves internas. |
| workPerformed | Texto multilínea | Finalización | Contenido permitido en reporte. |
| customerObservations | Texto multilínea | No | Contenido permitido en reporte. |
| serviceAddressInternal / ServiceAddress | Texto 300 | No | Solo interno; no PDF/flow/correo. |
| internalNotes | Texto multilínea | No | Solo interno; no PDF/flow/correo. |

El PDF generado por la app incluye de forma visible el texto exacto **REPORTE CERRADO Y FIRMADO** y no recibe campos de ubicación, `ServiceAddress` ni notas internas.

### Aceptación, firma, reporte y manifiesto

| conceptId / contrato | Tipo | Req. | Regla |
| --- | --- | --- | --- |
| customerAccepted / CustomerAccepted | Sí/No | Finalización | Debe ser true antes de generar/publicar. |
| signaturePointCount / SignaturePointCount | Entero | Finalización | Debe cumplir el mínimo configurado. |
| signerName | Texto 200 | Finalización | Nombre declarado por quien acepta. |
| signerRole | Texto 150 | Finalización | Cargo/relación con el cliente. |
| deviceSignedAtUtc | Fecha/hora | Finalización | Contexto del dispositivo. |
| serverFinalizedAtUtc | Fecha/hora | Finalización | Hora autoritativa del servidor. |
| signatureEvidenceKey | Texto 64 | Publicación | Evidence `Signature`; solo uso interno de la app. |
| signatureSha256 | Texto 64 | Publicación | SHA-256 confirmado por descarga/read-back. |
| reportEvidenceKey / ReportEvidenceKey | Texto 64 | Publicación | Selector único del PDF `SignedReport` para el flow. |
| reportFileName | Texto 260 | Publicación | Nombre sanitizado del PDF. |
| reportSha256 | Texto 64 | Publicación | SHA-256 confirmado por descarga/read-back. |
| attachmentCount | Entero | Publicación | Cantidad de derivados customer-safe del manifiesto. |
| attachmentManifestJson / AttachmentManifestJson | Texto multilínea | Publicación | Array canónico que referencia solo `CustomerAttachment + ScanPassed`. |

Cada item del manifiesto contiene exclusivamente `sequence`, `evidenceKey`, `fileName`, `contentType`, `size`, `sha256`, `purpose=CustomerAttachment` y `securityState=ScanPassed`. `fileName` es genérico (`adjunto-001.jpg`, `adjunto-002.png`, etc.); nunca contiene el nombre original. No incluye firma, reporte, ubicación, dirección, notas ni campos del formulario.

### Ubicación interna

| conceptId | Tipo | Precisión | Regla |
| --- | --- | --- | --- |
| latitude | Decimal | 7 | -90 a 90. |
| longitude | Decimal | 7 | -180 a 180. |
| accuracyMeters | Decimal | 7 | No negativa y dentro del máximo configurado. |
| locationCapturedAtUtc | Fecha/hora | N/A | Vigencia validada por servidor. |
| locationSource | Texto | N/A | Fuente o not-captured. |

La app normaliza en decimal base 10 a siete posiciones con `MidpointRounding.AwayFromZero` antes del fingerprint y de persistir, convierte `-0` a `0`, y compara el mismo valor normalizado de forma exacta en el read-back. El gate P0 debe escribir valores positivos y negativos con más de siete decimales y confirmar el resultado exacto devuelto por Dataverse; no se acepta una comparación aproximada de `double`.

### Snapshot de correo y correlación Graph

| conceptId | Tipo | Escritor | Regla |
| --- | --- | --- | --- |
| emailOutboxKey | Texto 200 | App | Correlación estable e idempotente en la fila principal; no representa una tabla. |
| emailToSnapshot | Texto 1000 | App | Destinatarios validados y congelados. |
| emailSubjectSnapshot | Texto 500 | App | Plantilla segura. |
| emailHtmlBodySnapshot | Texto multilínea | App | Valores codificados; sin campos internos. |
| providerDraftId | Texto 512 | Flow | ID Graph solicitado/persistido con `Prefer: IdType="ImmutableId"`. |
| internetMessageId | Texto 998 | Flow | Identificador de mensaje persistido cuando Graph lo devuelve/reconcilia. |
| lastErrorCode | Texto 80 | App/Flow | Código seguro. |
| lastErrorSafeMessage | Texto 1500 | App/Flow | Sin binarios, coordenadas, dirección, firma, secretos ni HTML completo. |

El `emailOutboxKey` se guarda como valor de una extended property Graph cuyo identificador físico/configuración permanece pendiente. No hay tabla intermediaria de correo.

## 3. Tabla evidenceV2 — evidencia content-addressed

| conceptId | Tipo | Req. | Regla |
| --- | --- | --- | --- |
| name | Texto 160 | Sí | Nombre legible interno. |
| evidenceKey | Texto 64 | Sí | Clave content-addressed; alternate key única. |
| signedMto | Lookup signedMtoV2 | Sí | Padre obligatorio. |
| purpose | Choice local | Sí | Signature, SignedReport, OriginalAttachment o CustomerAttachment. |
| sequence | Entero | Sí | 0 para firma/reporte; adjuntos desde 1. |
| fileContent | File | Tras carga | Binario privado. |
| originalFileName | Texto 260 | Sí | Interno; en CustomerAttachment contiene solo el nombre genérico. |
| contentType | Texto 160 | Sí | Validado por firma binaria/decoder, no solo extensión. |
| byteLength | Entero largo | Sí | Coincide con descarga/read-back. |
| sha256 | Texto 64 | Sí | SHA-256 del binario descargado. |
| derivedFromEvidenceKey | Texto 64 | Derivado | En CustomerAttachment apunta a OriginalAttachment de la misma secuencia. |
| securityState | Choice local | Adjuntos | NotApplicable, Pending, ScanPassed o Rejected. |
| securityCheckedAtUtc | Fecha/hora | ScanPassed/Rejected | Hora del control. |
| securityProvider | Texto 200 | ScanPassed/Rejected | Motor/política/versión, sin secretos. |

La clave se calcula sobre valores canónicos:

`evidenceKey = SHA-256(operationKey + "|" + slot + "|" + sequence + "|" + contentSha256Lower)`

Slots: `signature`, `signed-report`, `attachment-original`, `attachment-customer`. Un replay recupera la misma fila; si la key existe con otro padre/hash/propósito, se detiene por conflicto. Cada File se descarga de inmediato para comprobar longitud y SHA-256.

### Política de adjuntos del piloto

1. Solo se aceptan extensiones `.jpg`, `.jpeg`, `.png`, MIME `image/jpeg` o `image/png` y contenido decodificable del mismo tipo.
2. El upload crudo nunca se persiste: la app lo decodifica y reconstruye de inmediato, eliminando EXIF/XMP/IPTC/perfiles y cualquier metadata. La copia reconstruida conserva el nombre de origen solo en `OriginalAttachment`, privado y fuera del manifiesto.
3. La app crea desde esa copia saneada un `CustomerAttachment` con nombre genérico; no vuelve a usar bytes crudos.
4. El derivado se descarga/verifica y queda asociado al original saneado mediante secuencia y `derivedFromEvidenceKey`.
5. Original saneado y derivado deben quedar `ScanPassed`, con proveedor y hora de control releídos, conforme a la política CDR/AV aprobada antes de publicar. Si la política no está acreditada, el gate operativo queda pendiente y `PilotEnabled=false`.
6. El PDF puede listar únicamente nombres/hashes customer-safe. El flow nunca consulta `OriginalAttachment`.

## 4. Choices físicos y gate de metadata

Los conjuntos conceptuales son:

- WorkflowState: Draft, Finalizing, ReadyToSend, Failed.
- EmailState: NotReady, Pending, Processing, Sent, Failed.
- EvidencePurpose: Signature, SignedReport, OriginalAttachment, CustomerAttachment.
- EvidenceSecurityState: NotApplicable, Pending, ScanPassed, Rejected.
- MaintenanceType: Correctivo, Preventivo.

Todos los `optionValue` físicos, incluidos Correctivo/Preventivo, permanecen `null` en el catálogo. Antes de habilitar el esquema, un read-back de metadata debe probar para **cada Local Choice** que todos los miembros esperados existen, que cada binding es un entero real/no placeholder y que sus valores son distintos dentro de ese conjunto. En particular, `Correctivo != Preventivo`; no se reutilizan números copiados de tablas legacy.

## 5. Claves, relaciones y optimistic concurrency

| Tabla | Alternate key | Columnas | Gate |
| --- | --- | --- | --- |
| signedMtoV2 | signedMtoByOperationKey | operationKey | Estado Active. |
| evidenceV2 | evidenceByContentKey | evidenceKey | Estado Active. |

| Origen | Destino | Cardinalidad | Obligatorio | Delete |
| --- | --- | --- | --- | --- |
| Cliente configurable | signedMtoV2 | 1:N | Sí | Restrict |
| Equipo configurable | signedMtoV2 | 1:N | **No** | Restrict |
| signedMtoV2 | evidenceV2 | 1:N | Sí en Evidence | Restrict |

Gates P0 de concurrencia:

1. Leer `EntityMetadata` y confirmar `IsOptimisticConcurrencyEnabled=true` en signedMtoV2.
2. Leer alternate keys y esperar `Active`; no basta con haber enviado la creación.
3. Sobre una fila sintética, capturar un ETag, ejecutar dos PATCH diferentes con exactamente el mismo `If-Match`: el primero debe aplicar y el segundo debe responder HTTP `412 Precondition Failed`.
4. Releer la fila y demostrar que solo quedó el primer cambio. Si el segundo PATCH aplica o no devuelve 412, el piloto queda bloqueado.

No existe relación con `cr07a_mantenimiento` ni con una tabla ticket anterior.

## 6. Estados y finalización

### WorkflowState

| Estado | Actor | Regla |
| --- | --- | --- |
| Draft | App | EmailState=NotReady; editable con ETag. |
| Finalizing | App | EmailState=NotReady; el flow no dispara. |
| ReadyToSend | App | Base y artefactos verificados; el flow solo cambia EmailState/campos Graph/error. |
| Failed | App | Falló antes de publicar; EmailState=NotReady. |

### EmailState

| Estado | Actor | Regla |
| --- | --- | --- |
| NotReady | App | Default y único durante Draft/Finalizing/Failed. |
| Pending | App | Solo por PATCH mínimo de publicación. |
| Processing | Flow | Claim ETag y también resultado ambiguo pendiente de reconciliación. |
| Sent | Flow | Mensaje reconciliado en Graph; no prueba entrega/lectura. |
| Failed | Flow | Fallo terminal confirmado; no resultado ambiguo. |

### Secuencia

1. El servidor comprueba `PilotEnabled`, allowlist técnica y allowlist opcional de cliente.
2. La app crea/reutiliza por `operationKey` una fila Draft/NotReady mediante su Application User y hace read-back. Si la fila ya está Finalizing/ReadyToSend, técnico, cliente, contacto/correo, equipo/serial, título, fecha y tipo deben coincidir exactamente con el snapshot inmutable; cualquier diferencia es conflicto, no replay exitoso.
3. Con ETag adquiere Finalizing/NotReady y lease.
4. Valida snapshots —incluido serial obligatorio—, CustomerAccepted, firma, ubicación, choices, adjuntos y política de seguridad.
5. Guarda/verifica Evidence Signature, OriginalAttachment, CustomerAttachment y SignedReport; el reporte contiene **REPORTE CERRADO Y FIRMADO**.
6. Construye `AttachmentManifestJson` solo con CustomerAttachment/ScanPassed y ejecuta el preflight `maxEmailBytes`. Si falla, no publica.
7. Persiste base completa, claves/hashes/manifest y snapshot de correo mientras sigue Finalizing/NotReady.
8. Hace read-back integral de base, ubicación decimal normalizada, aceptación, Evidence, seguridad, manifiesto y correo.
9. Con el ETag leído ejecuta solo `ReadyAtUtc`, `EmailState=Pending`, `WorkflowState=ReadyToSend`.
10. El read-back posterior compara integralmente la base inmutable. Por una carrera válida con el flow, acepta EmailState Pending, Processing, Sent o Failed; WorkflowState debe seguir ReadyToSend.

## 7. Identidades y mínimo privilegio

- **Application User de la app:** create/read/update/append/append-to necesarios sobre signedMtoV2/evidenceV2 y carga/lectura File; read mínimo sobre cliente/equipo. Sin delete, assign/share, metadata, tablas legacy ni administración.
- **Técnico:** accede solo por endpoints autorizados de la aplicación. No tiene create/update/delete directo en signedMtoV2/evidenceV2 ni permiso para descargar Evidence mediante Dataverse.
- **Identidad del flow:** read allowlisted de signedMtoV2, update solo de EmailState/campos Graph/error, y read File para SignedReport/CustomerAttachment seleccionados. La definición debe impedir queries a Signature/OriginalAttachment.
- **Supervisión:** lectura restringida y auditada; acceso a originales/firma requiere rol interno explícito.

La autorización de la app se evalúa servidor-side. `PilotEnabled=false` o técnico fuera de allowlist deniega antes de escribir. Si la allowlist de clientes está configurada, un cliente fuera de ella también se deniega.

Todos los nombres lógicos, SchemaNames, entity sets, navigation properties, IDs Graph y valores Choice permanecen `null/proposed` hasta metadata/configuración confirmada.

