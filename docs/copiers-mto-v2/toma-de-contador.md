# Toma de contador

La quinta opción de Mto V2 registra una visita de toma de contador. Se seleccionan
cliente y equipo asignado, fecha y hora de ingreso y retiro (hora de Colombia), y
lecturas actuales de impresiones y escaneos. Se muestran las lecturas anteriores.
El retiro debe ser posterior al ingreso, no futuro y no anterior a la lectura
previa. Ambos horarios y los contadores se revisan antes de firmar; una edición
invalida la firma y conserva el retiro elegido, sin sustituirlo por la hora actual.

El acta usa `dtc_copiersactivityv2`, tipo local `dtc_maintenancetype=827270012`, y
`activity_kind=counter`. Las lecturas se guardan en `cr07a_contadores` (conjunto
`cr07a_contadoreses`), vinculadas al equipo y fechadas al retiro. Se reutilizan las
validaciones de historial y la clave determinista para reconciliar reintentos.
El acta solo queda lista para envío después de verificar el guardado del contador.
No crea un mantenimiento, movimiento o entrega de insumos.

El calendario del Dashboard muestra la actividad en mes y semana con su intervalo
real y el color del técnico. El detalle y PDF incluyen ingreso, retiro y lecturas.
Las visitas que cruzan un periodo siguen visibles; las tarjetas mensuales no se
duplican dentro del mismo mes. Los resúmenes de mantenimientos filtran
`activity_kind=maintenance` y excluyen la toma de contador.

## Esquema y despliegue

Entorno: `https://orgc79ca19c.crm2.dynamics.com/`.
Solución existente: `CopiersMtoFirmadoV2`, publisher `DigitalTechCopiers`.
`scripts/provision_copiers_counter_activity.py` consulta por defecto; `--apply`
agrega únicamente la opción y publica la tabla. Se conservan las opciones previas,
tablas, roles y flujos. `--export <ruta.zip>` exporta la solución por la identidad
del CLI de Dataverse sin cambiar el perfil global de PAC usado por otros chats.

Se cubren mediante pruebas el flujo de finalización, la persistencia previa a la
publicación, reintentos y rechazo de cambios firmados, fechas, lecturas, PDF y
exclusión de totales. La comprobación visual de producción no equivale al envío
de una visita real firmada por un cliente.
