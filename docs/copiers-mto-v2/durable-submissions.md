# Recepción y recuperación de MTO V2

Base de producción: `1b2b64956d5588857487c0b0da1984182ad48c53`, despliegue `994f95c1629949d1bce1e3713f900c8e`.

## Contrato

- IndexedDB conserva campos, trazos de firma y archivos en el dispositivo, separados por tenant/usuario. No guarda tokens ni antiforgery. Web Locks evita dos editores simultáneos del mismo usuario. El borrador se escribe tras las interacciones y antes de subir el contenido firmado.
- Antes del primer POST se congela el multipart original (firma binaria incluida). Los reintentos consultan `SubmissionStatus` por la misma clave antes de subirlo. La consulta exige módulo autorizado y el mismo tenant/usuario.
- `Finalize` vuelve a autorizar técnico/cliente/equipo y recibe todos los bytes antes de confirmar HTTP 202. No se considera enviado por recibir 202: la interfaz distingue recepción, registro creado y correo confirmado.
- La recepción es un documento protegido mediante ASP.NET Data Protection en `HOME/data/CotizadorInterno/copiers-submissions-v1`, fuera de wwwroot y del despliegue. Se rechaza Local Cache. La escritura es atómica y los procesos comparten leases de archivo. Los archivos `.lock` no se borran.
- El worker reconstruye el contenido desde esa recepción, sin cookies/tokens del técnico, y usa la identidad aislada de Copiers. Conserva el técnico verificado como autor de negocio. Usa la lógica existente de firma/PDF/finalización y el flujo de correo existente, sin modificarlo.
- Contadores: ID determinista compatible con la ruta anterior; POST con primary key explícita y comprobación posterior exacta. Nunca PATCH/DELETE sobre lecturas existentes. Permisos adicionales: Read Global; Create/Append Basic en `cr07a_contadores`. No se cambia el propietario de otras filas ni se conceden permisos de escritura/eliminación de contadores.
- PDF y evidencias siguen siendo verificados por el repositorio antes de ReadyToSend. Los contadores/movimientos/entregas conservan su idempotencia y el correo mantiene su outbox/send-once actual.

## Recuperación y límites

Después de recibir, cerrar Edge no cancela el trabajo. Un reinicio recupera el documento; un lease de finalización interrumpido puede demorar hasta 16 minutos antes del siguiente intento. Se aplican reintentos espaciados; después de 12 fallos, o ante una validación inválida, queda `needs_review` con el original conservado y sin una segunda clave. No se debe recrear manualmente la firma ni reenviar un correo cuyo estado sea incierto.

Para soporte: consultar el recibo protegido por su ID en los logs `Recepción Copiers`, el estado de Dataverse y su operación original. `SubmissionStatus` nunca devuelve el contenido de la firma/fotos. No editar directamente el archivo protegido. Un caso `needs_review` requiere diagnóstico interno; no hay una ruta pública para forzar su envío.

Antes de recibir, sigue siendo necesaria una conexión para autenticarse/cargar catálogos inicialmente. Una captura existente puede sobrevivir sin conexión y recuperar cuando se abre la interfaz. El navegador debe permitir IndexedDB; si lo bloquea o no hay espacio se muestra una advertencia y no se afirma que esté guardado. Borrar datos del navegador elimina su copia local. No se promete recuperar una captura cerrada antes de instalar esta versión. La firma pendiente no recibida conserva la validación existente de antigüedad máxima de un día; la cola no envejece una firma ya recibida. Nunca se reemplazan hora/firma/coordenadas originales para un reintento.

Después de finalizar, el servidor elimina de su recibo la copia adicional del payload, conservando el resultado y la huella; Dataverse mantiene los originales verificados. En el navegador queda un estado liviano hasta que se confirma el correo y el técnico elige «Crear otro registro».

## Verificación

### Ampliación de varios equipos (2026-09-11)

Base verificada: `5dd13452dc9a20d8587f707156d43f8a93259f7c`, despliegue `9e70bf8222b8454ab1466d1fd1b75e84`.
Mantenimiento permite un equipo principal y hasta nueve adicionales del mismo cliente. Movimientos, tóner y el mantenimiento de equipo ajeno conservan su comportamiento. Un ticket, una recepción y un correo; todos los detalles quedan en las respuestas inmutables de la fila existente. Los lookup de cliente/equipo principal no cambian, ni se crean nuevas tablas o permisos.

Cada equipo adicional exige su trabajo y contadores, valida asignación/serial/referencia antes de recibir y vuelve a validar asignación/historia al procesar. Su contador tiene una identidad determinista por mantenimiento + equipo; la identidad anterior del contador principal sigue intacta. Primero se valida el conjunto, después se guardan las lecturas, y solo entonces queda ReadyToSend. Un fallo intermedio se reconcilia sin repetir las lecturas ya verificadas. El calendario muestra los detalles legibles por serial, no el JSON interno.

El PDF conserva el membrete: una página por equipo dentro del mismo documento, numeración de serial y una sola conformidad de visita reproducida con su firma original. Antes de firmar se puede revisar el trabajo de todos los equipos. Fotos de cámara/galería agregadas o retiradas no borran firma ni horarios, pero obligan a confirmar nuevamente la revisión final. Cambiar los hechos del servicio o los equipos sí invalida la firma. Después de pulsar Enviar, el payload completo sigue siendo inmutable.

Pruebas específicas: autorización del conjunto, duplicados, contadores por equipo, fallo parcial/reintento y único ReadyToSend; PDF normal/denso de dos equipos; Edge táctil con dos trabajos, captura posterior a la firma, recarga, desconexión y ausencia de doble POST. No se fabrican tickets ni correos en producción.

- `CopiersSubmissionStoreTests`: persistencia protegida, reinicio, propietarios, replays, concurrencia, límites, reintentos y revisión.
- `CopiersWorkerCounterTests`: creación una vez, sin Write, conflictos, asignación, historia y read-back.
- `Test-CopiersRecoveryBrowser.cjs`: Edge headless 800x1100/touch contra servidor localhost; fotos y firma de prueba, recarga, pestañas concurrentes, desconexión, cierre/reapertura, acuse perdido, consulta previa y nuevo registro. No hace escrituras en producción ni envía correos.
- Mantener los tests completos existentes y la comparación independiente de artefacto/configuración/despliegue. La prueba física en Redmi/Edge y el siguiente correo real se informan aparte.
