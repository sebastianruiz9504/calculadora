# Copiers MTO Firmado V2

Especificación de diseño para un formato móvil de mantenimiento u orden de trabajo (`MTO`) con firma manuscrita, ubicación interna, evidencias, PDF profesional y envío al cliente.

## Estado y límites

- Actualización del 8 de septiembre de 2026: catálogos de Clientes/Equipos, correo de encargado editable, consecutivo nativo, captura interna automática y PDF implementados sobre la producción `e5509fcc`. Esquema, identidad y persistencia comprobados; flujo independiente de Outlook creado y Started. La entrega de un correo real al cliente no fue probada durante la instalación.
- Ambiente confirmado: `Digital Tech Copiers (default)` (`https://orgc79ca19c.crm2.dynamics.com/`). Solución no administrada: `CopiersMtoFirmadoV2`; publisher: `DigitalTechCopiers` (`dtc`).
- Las tablas `dtc_copiersmtov2` y `dtc_copiersmtoevidenciav2`, sus relaciones y alternate keys activas están aisladas del modelo legacy. Los bindings resueltos están en [artifacts/dataverse-resolved-bindings.v1.json](artifacts/dataverse-resolved-bindings.v1.json).
- Identidad administrada dedicada verificada desde Azure; rol con 16 privilegios y perfil de 27 campos asignados y leídos de vuelta. Prueba temporal Draft/NotReady: consecutivo, campos protegidos, ETag/412 y archivo con SHA-256 idéntico. No se enviaron correos de prueba ni se modificó Graph.
- El catálogo conceptual conserva sus bindings `null` deliberadamente como contrato portable; el artefacto resuelto es específico del ambiente confirmado y no contiene secretos.
- La fila nueva `signedMtoV2` es el ticket/MTO V2. No crea, actualiza ni enlaza un ticket legacy.

## Arquitectura final

1. `signedMtoV2` guarda el snapshot, consecutivo, estados, aceptación, ubicación interna, claves/hashes exactos y `AttachmentManifestJson`. La interfaz y el servidor exigen seleccionar un equipo del cliente desde Equipos. Véase [captura actual](05-captura-septiembre-2026.md).
2. `evidenceV2` guarda archivos content-addressed con propósitos separados: `Signature`, `SignedReport`, `OriginalAttachment` y `CustomerAttachment`. El upload crudo se descarta tras decode/re-encode; `OriginalAttachment` es la copia saneada interna que conserva únicamente el nombre de origen, y el flow solo puede enviar el derivado customer-safe con nombre genérico.
3. La aplicación usa un **Application User V2 de mínimo privilegio** para escribir. En Azure emplea la identidad administrada dedicada configurada en `CopiersMtoV2:DataverseApp:ManagedIdentityClientId`; el bloque de client secret solo es un fallback explícito para ejecución local y requiere el conjunto completo `{TenantId,ClientId,ClientSecret}`. No reutiliza ni hace fallback a `Dataverse:*` o `AzureAd:*`. Los técnicos se autentican ante la app, pero no reciben privilegios Dataverse directos de create/update sobre tablas V2.
4. `EmailState` inicia en `NotReady`. La app adquiere `Finalizing/NotReady` con ETag, completa y relee integralmente el staging, y publica mediante un PATCH mínimo `ReadyAtUtc + ReadyToSend + Pending` con `If-Match`.
5. El flow reclama directamente la fila principal `ReadyToSend/Pending`. Resuelve el PDF solo por `ReportEvidenceKey` y los adjuntos solo por `AttachmentManifestJson`; cada elemento del manifiesto debe ser `CustomerAttachment + ScanPassed`.
6. El flujo separado usa Outlook `SendEmailV2` sin reintento automático. Serializa sus ejecuciones, relee Pending antes de cambiar a Processing, verifica el PDF y todos los adjuntos del manifiesto, y marca Sent cuando Outlook confirma éxito. Un resultado ambiguo permanece Processing y exige revisión del historial y del buzón antes de cualquier reintento. No se implementó reconciliación automática de borradores Graph.

El PDF debe mostrar de forma visible el texto exacto **REPORTE CERRADO Y FIRMADO**. Ubicación, `ServiceAddress` y notas internas no pertenecen al modelo PDF ni al correo.

## Fronteras no negociables

- V2 jamás lee, crea, actualiza, enlaza ni dispara sobre `cr07a_mantenimiento`, otra tabla ticket anterior o el flujo antiguo.
- El flow nunca selecciona ubicación, dirección de servicio, notas internas, firma ni `OriginalAttachment`.
- Para el piloto solo se aceptan originales JPG/JPEG/PNG validados por contenido. La app produce un derivado customer-safe sin metadatos y con nombre genérico; el nombre original no sale al cliente.
- Antes de publicar, los adjuntos requieren `ScanPassed` y el control de seguridad/AV/CDR aprobado. Si ese servicio no existe o no se ha validado, el gate operativo queda **pendiente** y `PilotEnabled` debe permanecer en `false`.
- La app y el flow ejecutan la misma fórmula versionada `graph-json-base64-v1` contra `maxEmailBytes`, incluyendo destinatarios, asunto, HTML y base64; si excede el límite, no se publica ni se envía parcialmente.
- Una falla de correo no recrea la fila ni altera PDF, manifest o Evidence verificados.

## Piloto

La ruta V2 queda habilitada únicamente si se cumplen las tres condiciones servidor-side:

1. `PilotEnabled=true`;
2. técnico autenticado autorizado para el módulo Copiers; si se configura una allowlist técnica, también debe estar incluido;
3. si existe una allowlist de clientes, el cliente está incluido.

Ocultar la interfaz no sustituye estas comprobaciones. Una allowlist vacía no agrega restricciones sobre la autorización del módulo. Los catálogos y la edición del correo no dependen de `PilotEnabled`; esa bandera permite detener nuevas finalizaciones sin bloquear la consulta. El flujo operativo vigente es [outlook-send-once-v1](05-envio-operativo-outlook.md); los contratos Graph anteriores se conservan únicamente como historia del diseño.

## Contenido

| Archivo | Propósito |
| --- | --- |
| [01-esquema-dataverse.md](01-esquema-dataverse.md) | Tablas, columnas, relaciones, Choice, concurrencia, seguridad y ubicación. |
| [02-contrato-power-automate.md](02-contrato-power-automate.md) | Claim de fila principal, Graph draft/send, reconciliación y errores. |
| [03-despliegue-paralelo-rollback.md](03-despliegue-paralelo-rollback.md) | Fases, gates, piloto, observabilidad y rollback. |
| [04-matriz-pruebas.md](04-matriz-pruebas.md) | Controles P0/P1 funcionales, técnicos, de seguridad y recuperación. |
| [artifacts/dataverse-concept-catalog.v1.json](artifacts/dataverse-concept-catalog.v1.json) | Catálogo conceptual portable; sus bindings permanecen en `null`. |
| [artifacts/dataverse-resolved-bindings.v1.json](artifacts/dataverse-resolved-bindings.v1.json) | Read-back de nombres físicos, relaciones, keys, Choice, identidad y gates del ambiente confirmado. |
| [artifacts/ready-to-send.schema.json](artifacts/ready-to-send.schema.json) | Proyección allowlisted `ReadyToSend/Pending` que consume el flow. |
| [artifacts/flow-result.schema.json](artifacts/flow-result.schema.json) | Estado posterior que acepta `Pending/Processing/Sent/Failed`. |
| [artifacts/power-automate-deployment-settings.template.json](artifacts/power-automate-deployment-settings.template.json) | Plantilla sin secretos, desactivada y con bindings físicos; deja conexiones, buzón y gates operativos explícitamente pendientes. |

## Gates P0 antes del piloto

1. Confirmar ambiente, solución y publisher; completar bindings desde metadata sin inferir prefijo.
2. Confirmar todos los valores Choice físicos, incluidos `Correctivo` y `Preventivo`, y demostrar que cada conjunto no tiene valores duplicados.
3. Confirmar `IsOptimisticConcurrencyEnabled=true`, alternate keys en `Active` y la prueba real de dos PATCH con el mismo ETag: el segundo responde `412`.
4. Confirmar Decimal precisión `7` en latitud/longitud/accuracy y read-back del redondeo canónico.
5. Validar rol mínimo del Application User y demostrar que un técnico no puede crear/actualizar V2 directamente.
6. Aprobar privacidad, retención, DLP, buzón, extended property de correlación Graph y reconciliación.
7. Aprobar AV/CDR, demostrar `ScanPassed`, eliminación de metadatos/nombres originales y cálculo `maxEmailBytes` en límites.
8. Aprobar todos los P0 de [04-matriz-pruebas.md](04-matriz-pruebas.md) antes de cambiar `PilotEnabled`.

## Criterio de terminado

Después del PATCH de publicación, la app relee y compara integralmente los campos base inmutables; por carrera legítima con el flow, acepta `EmailState` en `Pending`, `Processing`, `Sent` o `Failed`. `Sent` solo se declara después de reconciliar el mensaje en Graph y persistir `providerDraftId`/`internetMessageId`; no prueba entrega ni lectura por el destinatario.
