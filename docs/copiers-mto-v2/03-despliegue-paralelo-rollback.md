# Plan de despliegue paralelo y rollback

## Estado y principios

Este archivo es un plan. **Dataverse ya fue aprovisionado y verificado en el ambiente confirmado; Power Automate/Graph aún no se han creado ni configurado y no se ha ejecutado un despliegue de la aplicación.** La identidad administrada y el Application User V2 existen, pero el Application User permanece sin roles hasta aprobar y verificar el rol mínimo.

- El flujo vigente permanece sin modificaciones durante el piloto.
- V2 usa tablas, identidad de aplicación, flow, connection references, variables y feature flags propios.
- La app crea/reutiliza `signedMtoV2`, guarda y verifica Evidence, hace read-back integral en Finalizing/NotReady y publica con el PATCH mínimo ETag ReadyToSend/Pending.
- El flow reclama la fila principal y usa exclusivamente ReportEvidenceKey y AttachmentManifestJson; solo lee SignedReport/CustomerAttachment+ScanPassed.
- `cr07a_mantenimiento`, otras tablas ticket y el flow antiguo quedan fuera de bindings, roles, trigger y acciones V2.
- Rollback detiene entradas/claims nuevos y conserva filas/evidencias para reconciliación; no borra datos.
- Un run `Succeeded` no sustituye read-back Dataverse ni reconciliación Graph.

## Artefactos y pendientes

Los nombres físicos ya resueltos y su read-back están congelados en [artifacts/dataverse-resolved-bindings.v1.json](artifacts/dataverse-resolved-bindings.v1.json). Permanecen pendientes los componentes operativos indicados explícitamente:

- tablas `signedMtoV2` y `evidenceV2`, columnas, Local Choices, relaciones y alternate keys: aprovisionados y verificados;
- `IsOptimisticConcurrencyEnabled=true` en signedMtoV2: aprovisionado; falta cerrar la prueba operativa de concurrencia;
- Application User exclusivo de la app: creado; falta aprobar, asignar y verificar el rol de mínimo privilegio, sin fallback a registros globales;
- flow Graph inicialmente apagado, connection references y buzón dedicado;
- extended property Graph de correlación, cuyo ID físico queda pendiente;
- configuración `PilotEnabled`, allowlist técnica no vacía, allowlist opcional de clientes, `processingEnabled` y `maxEmailBytes`;
- servicio/política AV/CDR capaz de acreditar `ScanPassed`;
- dashboard y alertas de WorkflowState/EmailState y resultados ambiguos.

## Fase 0 — baseline y decisiones

Acciones read-only:

1. Confirmar ambiente, solución/publisher, branch/artifact y flujo vigente.
2. Inspeccionar metadata de cliente/equipo, choices, relaciones, File limits, alternate keys y optimistic concurrency.
3. Inventariar legacy solo para demostrar ausencia de referencias V2.
4. Confirmar buzón/Graph, DLP, extended property, política de ImmutableId, privacidad y retención.
5. Definir `maxEmailBytes`, algoritmo/version de preflight y umbrales operativos.
6. Aprobar AV/CDR, eliminación de metadata y retención del OriginalAttachment.
7. Cerrar todos los bindings del catálogo desde metadata.

Gate: diseño aprobado, bindings físicos completos en el paquete de implementación y **ningún** cambio cloud ejecutado desde estos documentos.

## Fase 1 — construcción aislada en QA

1. Crear tablas dentro de la solución confirmada; no escribir XML a mano.
2. Habilitar optimistic concurrency en signedMtoV2 y verificarlo por read-back de EntityMetadata.
3. Crear alternate keys después de propagación y esperar estado Active.
4. Crear lookups: cliente obligatorio, equipo opcional y padre Evidence obligatorio; verificar Navigation Property Names.
5. Crear Local Choices y resolver valores desde metadata. Todos los miembros deben existir y ser distintos dentro de su conjunto, incluidos Correctivo/Preventivo.
6. Crear columnas Decimal de precisión 7 y verificar metadata/read-back de redondeo.
7. Configurar Application User de la app con mínimo privilegio y secretos `CopiersMtoV2:DataverseApp:*`; probar que la ausencia de esa sección falla cerrado aunque existan `Dataverse:*`/`AzureAd:*`. Técnicos sin create/update directo en V2.
8. Integrar AV/CDR y generación de CustomerAttachment sin metadata/nombre original.
9. Configurar flow apagado con Graph draft/correlación/ImmutableId/send retry None; prohibir la acción Send an email.
10. Exportar/unpack de la solución producida por Dataverse al repositorio después de la sesión real.

Gate P0 de esquema:

- `IsOptimisticConcurrencyEnabled=true` confirmado;
- alternate keys Active;
- dos PATCH con el mismo ETag: primero exitoso, segundo HTTP 412, y read-back solo del primero;
- todos los Choice bindings reales y distintos por conjunto;
- Decimal precision=7 y redondeo/read-back exactos;
- equipo nulo aceptado con serial snapshot obligatorio;
- técnico directo denegado y Application User mínimo aprobado.

## Fase 2 — seguridad de archivos y correo en QA

1. Aceptar únicamente JPG/JPEG/PNG por extensión, MIME y decodificación.
2. Descartar el upload crudo tras decode/re-encode; persistir OriginalAttachment interno ya saneado y producir CustomerAttachment sin metadata y con nombre genérico.
3. Acreditar ScanPassed en original y derivado; verificar fail-closed.
4. Generar el PDF con el texto visible `REPORTE CERRADO Y FIRMADO` y sin ubicación/ServiceAddress/notas.
5. Ejecutar maxEmailBytes preflight en app y flow con los mismos vectores de prueba.
6. Probar Graph: draft con extended correlation=emailOutboxKey, `Prefer: IdType="ImmutableId"`, providerDraftId/internetMessageId persistidos y adjuntos exactos.
7. Simular timeout de send: EmailState debe quedar Processing, generar alerta y exigir reconciliación Drafts/Sent Items; cero retry automático.
8. Inspeccionar definición/telemetría para confirmar cero acceso a Signature/OriginalAttachment/legacy.

Si no existe un servicio AV/CDR verificable o no puede producirse `ScanPassed`, este gate queda **pendiente**: no se pasa a preproducción y `PilotEnabled=false`.

## Fase 3 — preproducción en paralelo

1. Importar solución administrada V2 sin activar routing ni flow.
2. Resolver variables/connection references y verificar identidad/roles con read-back.
3. Ejecutar ciclo sintético completo: Draft/NotReady → Finalizing/NotReady → Evidence/read-back → PATCH mínimo ReadyToSend/Pending → Graph.
4. Confirmar que el read-back de app acepta Pending/Processing/Sent/Failed sin relajar la comparación integral de la base.
5. Confirmar que vistas/endpoints/flow legacy no cambiaron.
6. Activar el flow únicamente con `processingEnabled=true`; mantener `PilotEnabled=false`.

Gate: trigger listo sin productores, reconciliación/alertas operativas y todos los P0 aprobados.

## Fase 4 — piloto limitado

Habilitación servidor-side:

`PilotEnabled=true AND technician IN AllowedTechnicians AND (AllowedClients vacío OR client IN AllowedClients)`

- La allowlist técnica debe ser no vacía; vacía deniega a todos.
- La allowlist de clientes es opcional; si se configura, es restrictiva.
- La interfaz oculta no es control de seguridad.
- Un servicio iniciado en V2 termina en V2; no se duplica manualmente en legacy.
- Revisar diariamente Finalizing estancados, Pending sin claim, Processing ambiguos, Failed, hashes, ScanPassed, tamaño y reconciliación Graph.

Gate recomendado: volumen acordado, 100% read-backs, cero 412 ignorados, cero duplicados, cero exposición interna y cero send ambiguo sin alerta.

## Fase 5 — ampliación gradual

Ampliar por allowlists de técnicos/clientes. No retirar legacy hasta completar observación, entrenamiento, reconciliación y aprobación funcional.

## Observabilidad P0

| Métrica/alerta | Umbral |
| --- | --- |
| Finalizing/NotReady estancado | > timeout de lease |
| ReadyToSend/Pending sin claim | > 5 min |
| ReadyToSend/Processing sin resolución | > 15 min; alerta, nunca retry ciego |
| EmailState=Failed | Cualquier fila |
| Duplicados operationKey/evidenceKey | 0 |
| Segundo PATCH con ETag consumido no devuelve 412 | 0; bloquea piloto |
| Base/Evidence sin read-back o hash distinto | 0 |
| CustomerAttachment sin ScanPassed en manifest | 0 |
| OriginalAttachment/firma consultados por flow | 0 |
| Metadata/nombre original en derivado/correo/log | 0 |
| maxEmailBytes excedido o adjunto omitido | 0 |
| Graph send con retry automático | 0 |
| Sent sin reconciliación/IDs/read-back | 0 |
| Ambigüedad que sale de Processing sin reconciliación | 0 |
| Referencia V2 a legacy | 0 |

Dashboard/logs usan datos sanitizados; no muestran firma, originales, coordenadas ni contenido binario.

## Rollback operacional

Ejecutar si hay duplicados, integridad inválida, acceso cruzado, exposición interna, ScanPassed no confiable, send ambiguo sin reconciliación, violación maxEmailBytes o artefacto activo no identificable.

1. Cambiar `PilotEnabled=false`; esto bloquea nuevos borradores V2 aun si las allowlists permanecen.
2. Cambiar `processingEnabled=false` o apagar trigger y registrar hora exacta.
3. Inventariar Finalizing/NotReady, ReadyToSend/Pending, ReadyToSend/Processing y Failed.
4. Para Processing, consultar primero Sent Items/Drafts por providerDraftId y emailOutboxKey. No reenviar ni cambiar a Pending.
5. Restaurar routing a la versión legacy previamente verificada sin modificar `cr07a_mantenimiento` ni el flow antiguo.
6. Validar un caso legacy controlado con read-back.
7. Conservar tablas, copias internas saneadas, derivados, PDF, auditoría e IDs Graph V2 en lectura restringida.
8. Resolver cada fila pendiente mediante procedimiento aprobado.

No se eliminan soluciones, tablas, Application Users ni filas firmadas durante rollback. Una desinstalación es un cambio destructivo separado.

## Reanudación

1. Corregir en QA y repetir todos los P0.
2. Confirmar mismas operationKey, ReportEvidenceKey, AttachmentManifestJson, hashes e emailOutboxKey.
3. Reconciliar providerDraftId/internetMessageId y Drafts/Sent Items antes de cualquier acción.
4. Un Failed inequívoco solo vuelve a Pending mediante ETag y procedimiento aprobado; un Processing ambiguo nunca se reencola hasta resolver.
5. Rehabilitar un único canario y observar el ciclo completo.

## Responsables mínimos

| Rol | Responsabilidad |
| --- | --- |
| Dueño funcional | Formato, texto del PDF, piloto y aceptación. |
| Privacidad/seguridad | Firma, ubicación, originales, retención, AV/CDR y DLP. |
| Admin Dataverse | Metadata, choices, concurrency, keys, roles y Application User. |
| Dueño Graph/Power Automate | Buzón, correlation property, draft/send, reconciliación y alertas. |
| Release owner | Artefacto, flags, gates, rollback y read-back. |
| Soporte piloto | Monitoreo y resolución de Processing/Failed. |

Despliegue, validación independiente y aprobación funcional deben quedar evidenciados por responsables distintos.
