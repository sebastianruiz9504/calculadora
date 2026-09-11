# Gestión de equipos: candidato local, pendiente de publicación

## Base verificada

- Commit de producción: `12002bfa52344baa804133efe6e397c1f4c704ce`.
- Despliegue activo consultado el 11 de septiembre: `914bc93575654c7882f65b4d228fea46`, terminado 17:08:59 UTC.
- Rama de trabajo: `feat/copiers-operations-20260911`.
- No se modificó producción, configuración de Azure, esquema ni registros Dataverse durante esta implementación.

## Implementación

Nueva ruta `/CopiersEquipmentOperations`, accesible desde Movimiento de equipo en MTO V2 cuando se habilite el interruptor. Mantiene la ruta existente y los borradores MTO/tóner.

- Entrega: certificado del cliente y movimiento interno vinculado.
- Retiro: certificado exclusivo del cliente de origen; equipo en tránsito, no disponible en stock.
- Cambio: retiro y entrega del reemplazo en una transacción condicional, con un certificado del cliente. La llegada del retirado al destino interno se confirma aparte.
- Salida interna: sin firma ni correo al cliente; posterior recepción física.
- Recepción: consume una salida pendiente, sin poder cambiar el destino ni usarla dos veces. Si el destino es un cliente, firma y correo independientes; si es interno, confirmación del técnico.
- Calendario: actividades separadas, tipo de operación, datos internos, vínculos entre salida y recepción, fotos y certificado cuando corresponda.
- Firma y adjuntos conservados en IndexedDB; un solo editor por usuario; consulta de recibo antes de repetir POST. Los errores explícitos de validación previos a la recepción permiten corregir y volver a firmar.
- Se reutilizan recepción persistente, trabajador, diario de actas, tabla de movimientos, evidencias y flujo de correo existentes. No se cambiaron remitente ni copias del flujo.

## Configuración que falta confirmar

La clasificación interna se hace por ID, nunca por coincidencia de nombres:

- `Deposito`: `fd509c25-713e-f111-88b4-6045bd38ece5`.
- `DEMO - OFICINA DIGITAL`: `52abf9ea-5ba4-f011-bbd2-6045bd382f27`; **pendiente de confirmar con Sebastián que representa su oficina**.

Nueva sección `CopiersEquipmentOperations`: `Enabled` (por defecto false) e `InternalClientIds` (dos IDs confirmados). No habilitar con un cliente supuesto. La producción tiene `CopiersMtoV2__ActivitiesEnabled=true` como ajuste de App Service; conservar ese ajuste y los demás. `RequireLocation=false` en el archivo activo: captura automática oportunista, sin texto sobre ubicación en el formulario.

## Esquema aditivo pendiente

Plan de solo lectura verificado mediante `python scripts/provision_copiers_equipment_operations.py`:

- `cr07a_equipo.dtc_transitjson`, memo 4000: custodia, origen, destino previsto, operación y hora. Se limpia al recibir.
- `cr07a_movimientosequipos.dtc_operationjson`, memo 12000: instantánea y vínculo inmutable.

Reutilizar `CopiersMtoFirmadoV2`, editor `dtc`, entorno explícito `https://orgc79ca19c.crm2.dynamics.com`. El plan confirmó concurrencia optimista de Equipos. No se ejecutó `--apply`. Después de aplicar: verificar componentes, exportar y desempaquetar la solución en el repositorio. No requiere roles administrativos nuevos ni permisos de eliminación.

## Pruebas locales

- 803 pruebas .NET correctas, incluidas 16 nuevas de operación/calendario.
- 177 pruebas Node correctas del formulario, selector y calendario.
- Edge headless táctil 800×1100: nueve comprobaciones del nuevo formulario (selección, cambio, firma persistente con foto, recarga, respuesta perdida sin duplicado, recepción externa, movimiento interno, validación corregible y pestaña exclusiva).
- Pruebas de recuperación MTO, un equipo y múltiples equipos, correctas.
- Cuatro PDFs de prueba de una página, deterministas, con imágenes renderizadas y revisión visual. No muestran el otro cliente ni datos de movimiento interno.
- Muestras locales: `C:/cwt/artifacts-copiers-operations-20260911/pdf/`.
- Las pruebas no escribieron tickets, movimientos ni correos de producción. No constituyen validación en una Redmi física ni prueba autenticada del nuevo flujo en producción.

## Cierre pendiente

1. Confirmar el cliente de oficina.
2. Reconsultar despliegue/commit activos; integrar cualquier despliegue posterior antes de publicar.
3. Aplicar únicamente las dos columnas, verificar y extraer solución.
4. Preparar un paquete único contra los bytes activos: DLL/PDB, manifiesto de recursos, JS MTO/calendario/nuevo formulario, CSS nuevo y variantes comprimidas. Los scripts de la entrega anterior están fijados a otra base y **no se deben ejecutar sin adaptarlos**.
5. Configurar exclusivamente los IDs confirmados y el interruptor; preservar dependencias, resto de configuración y recursos ajenos.
6. Congelar commit, ejecutar gates, desplegar una vez, verificar hashes/healthz, rutas y catálogos autenticados. No inventar firmas ni hacer movimientos reales para probar.
7. Sincronizar main/production solo tras verificar el despliegue.
