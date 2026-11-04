# Calendario interno Mantenimiento V2

## Alcance

Dashboard > Soporte > Copiers > Mantenimiento V2 consulta las tablas V2 existentes. No escribe tickets, no modifica el mantenimiento anterior ni consulta los flujos antiguos. Requiere sesión autenticada y acceso efectivo a Dashboard y Copiers antes de utilizar la identidad de aplicación.

El calendario abre en el mes actual y permite alternar Mes/Semana, navegar al período anterior/siguiente y regresar a Hoy. El mes muestra semanas completas de lunes a domingo, con los días ajenos atenuados y sin incluir sus visitas en los conteos. Cada registro mensual se coloca una sola vez en la fecha del servicio; las horas y la vista semanal usan Colombia. Los días con más de tres registros permiten desplegar los adicionales.

Cada técnico tiene un color asignado usando el catálogo completo de técnicos actuales e históricos, estable al navegar y filtrar. El mismo color aparece en ambas vistas, la leyenda y la tarjeta por técnico; el nombre también identifica al técnico. Los tipos de actividad siguen visibles como texto. Los reportes Failed con PDF ya guardado se muestran como **Pendiente de finalizar**, nunca como enviados. Los borradores y la finalización en curso no se presentan como mantenimientos terminados.

El detalle muestra el formulario guardado, notas internas, destinatario, estados, firma, PDF y copias seguras de los adjuntos. La ubicación solo aparece aquí, no en la captura del cliente ni en el PDF. OpenStreetMap recibe las coordenadas únicamente después de pulsar Cargar mapa; el diálogo informa ese intercambio.

Las tres tarjetas muestran cantidades por técnico, tipo de mantenimiento y cliente del período y técnico seleccionados. Cuentan registros de mantenimiento ReadyToSend e históricos una vez por ID, no equipos ni segmentos horarios. Movimientos, entregas de tóner y reportes pendientes permanecen visibles pero no se suman. Los clientes se agrupan por ID, con nombre como respaldo para registros sin vínculo. Al cargar o fallar una consulta no se conservan cifras del período anterior.

## Fechas y documentos

El inicio histórico procede de la respuesta `service_started_at`, interpretada explícitamente en hora de Bogotá; el cierre procede de la firma registrada. Cuando esos datos no permiten calcular una duración válida, se presenta una franja visual de 30 minutos con una advertencia explícita. Las franjas muy cortas reservan altura legible sin modificar las horas guardadas.

Las rutas Bootstrap, Week, Month, Detail y Evidence son GET sin caché. Los documentos se recuperan por padre y clave, validando manifiesto, propósito, MIME, tamaño, SHA-256 y ETag estable. No se exponen URLs de Dataverse ni archivos originales no saneados.

Enlace directo: `/Dashboard?tab=copiers&copiersTab=maintenance-v2&maintenanceId={id}`.

## Clave del formulario

La interfaz no restaura los datos ni los archivos al recargar. Por eso una captura nueva genera una clave nueva, en vez de restaurar una clave aislada de sessionStorage. Si existe un envío anterior sin confirmar, se muestra una advertencia y acceso al calendario para revisarlo antes de repetir el mantenimiento.

Los reintentos dentro del mismo formulario y para el mismo cliente/equipo conservan la clave y la captura original. Cambiar cliente o equipo después de un intento crea una nueva operación y limpia la identidad de fila y ubicación anteriores. La protección del servidor contra reutilizar una clave con otro cliente/equipo permanece intacta.

## Verificación

Pruebas locales: `scripts/Test-CopiersMtoV2Ui.js`, `scripts/Test-CopiersMtoV2CalendarUi.js` y `CopiersMtoV2CalendarTests`. El gate completo y la verificación autenticada del artefacto desplegado se registran por separado: las pruebas simuladas no acreditan envío de correo ni recepción del cliente.

No requiere columnas, roles ni configuración de aplicación nuevos. Las copias fijas del correo se actualizan por separado mediante el procedimiento CC-only documentado en `05-envio-operativo-outlook.md`. El remitente dinámico de otros técnicos no forma parte de esa actualización de CC.
