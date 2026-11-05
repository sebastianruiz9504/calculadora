# Otras actividades

Base de producción: `8e7ca81eb248144d4f077fb6be55c233115685ed` (2026-11-04).

Sexta opción del formulario MTO V2. Mantiene el cliente, contacto, firma, evidencias y envío del reporte. Solicita notas de la actividad (obligatorias, hasta 1000 caracteres) y fecha/hora de ingreso y salida en Colombia. No requiere equipo ni consulta o registra contadores, movimientos o suministros. Toma de contador conserva su cliente y equipo.

Las notas se guardan en `dtc_workperformed` y en la instantánea firmada `activity_notes`; los horarios se conservan en las respuestas UTC existentes. El servidor comprueba la igualdad de notas y horarios con la captura firmada. Los reintentos conservan la misma identidad y rechazan cambios posteriores a la firma.

El acta utiliza `dtc_copiersactivityv2s`, tipo `827270013` (Otras actividades), y su tabla de evidencias existente. El PDF muestra cliente, notas y horarios; omite serial, contadores y campos de mantenimiento. El calendario mensual/semanal muestra la actividad, incluso si la visita cruza un mes. Los resúmenes por técnico, tipo y cliente siguen contando únicamente `activityKind=maintenance`.

## Esquema

`python scripts/provision_copiers_other_activity.py` verifica el entorno, solución, publicador y opciones existentes sin escribir. `--apply` agrega solo la opción y publica la tabla. La exportación de Dataverse se incorpora después de verificar el cambio; no se crean tablas, columnas, permisos ni flujos.

El lookup de equipo es opcional. El serial existente es `ApplicationRequired`: ese nivel lo exige la interfaz de aplicaciones de modelo, no el Web API ([Microsoft: niveles de obligatoriedad](https://learn.microsoft.com/power-apps/developer/data-platform/entity-attribute-metadata#column-requirement-level)). MTO V2 mantiene su validación para todos los servicios que usan equipo y permite vacío únicamente en Otras actividades.

## Verificación

Pruebas del controlador, ciclo firmado, reintentos, notas y horarios inválidos, PDF, exclusión de totales y visitas que cruzan meses. La revisión autenticada en producción consulta clientes y el calendario sin enviar capturas ficticias ni correos a clientes. El envío de una captura real firmada requiere una atención real.
