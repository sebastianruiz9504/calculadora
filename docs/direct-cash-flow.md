# Flujo de caja directo

Implementado sobre `origin/production` (`89d2ce6`) el 17 de noviembre de 2026.

## Uso

- Abrir **Conciliación → Flujo de caja directo**. El piloto muestra octubre–diciembre de 2026; el selector permite cualquier período del año.
- Abrir un movimiento desde Conciliación 2 o desde una celda del reporte y guardar su número y concepto. El catálogo filtra ingresos y salidas, con validación adicional en el servidor.
- **Registro manual** permite incorporar movimientos bancarios faltantes, identificados en morado y con estado **Solo banco**. Se pueden editar o anular, conservando el registro. No generan comprobantes ni llamadas a Siigo.
- Completar el saldo inicial consolidado y los cuatro saldos de cierre seleccionando sus celdas. Cero es un valor válido; vacío significa pendiente. CHECK compara el cierre calculado con Cloud + Copiers + Bolsillos + Fidurenta únicamente cuando todos están informados.
- Descargar Excel conserva el formato, orden, códigos, estilos y hojas ocultas del archivo entregado. Las columnas corresponden al período seleccionado. Los datos pendientes no se inventan.

## Piloto de octubre

Lectura verificada de Dataverse el 17/11/2026: 171 movimientos, 26 partidas de traslados internos con neto cero y **145 movimientos externos sugeridos**. Ingresos externos: **272.025.140,20 COP**. Salidas externas: **299.920.841,00 COP**. Variación neta: **−27.895.700,80 COP**.

Las 145 clasificaciones están guardadas como `Sugerido`, pendientes de revisión, sin alterar importes ni estados de conciliación existentes. Noviembre y diciembre no reciben sugerencias automáticas. El botón de octubre conserva las clasificaciones existentes; puede sugerir nuevos registros del mismo mes.

No había saldo inicial de octubre registrado. El usuario debe completar saldos y movimientos bancarios faltantes. Estos importes son un punto de partida, no un mes cerrado ni una validación contra extractos.

## Cálculo y plantilla

- 52 conceptos de detalle. El código impreso 903 aparece dos veces en el modelo: claves estables `903:51` (viáticos) y `903:52` (representación).
- El flujo usa el importe efectivamente registrado en banco, incluyendo registros manuales. Los movimientos sin concepto aparecen en los indicadores bancarios y en pendientes, pero no se distribuyen en filas del reporte.
- Las sugerencias sí suman; la pantalla y la descarga indican que requieren revisión.
- Los traslados explícitos se excluyen del consolidado. Para registros omitidos con descripción CONCILIO, se reconoce una contrapartida única del mismo mes, igual importe, sentido opuesto, otra cuenta y diferencia máxima de cuatro días. Los registros manuales no se infieren como contrapartidas. Los traslados sin contrapartida generan advertencia.
- El cierre anterior se arrastra únicamente dentro del período seleccionado, cuando no quedan conceptos pendientes ni traslados con diferencia. También se pueden usar los saldos iniciales ya registrados para ambas cuentas de conciliación; el saldo consolidado ingresado explícitamente tiene prioridad.
- Se corrigieron las sumas del modelo que omitían socios, apertura de CDT o contenían totales fijos en diciembre. Las mismas fórmulas se aplican a todos los meses. La hoja visible no conserva importes de ejemplo.

## Persistencia y rendimiento

Tablas aditivas de la solución existente `CotizadorInternoCRM`:

| Tabla | Propósito |
|---|---|
| `cr07a_clasificacionflujodirecto` | Concepto, origen, fecha, estado de revisión y motivo; GUID del movimiento original |
| `cr07a_movimientomanualbanco` | Movimientos exclusivamente bancarios; `EstadoSiigo=NoEnviar`; anulación mediante estado inactivo |
| `cr07a_saldoflujodirecto` | Saldo inicial consolidado y saldos de cierre por período |

Las escrituras usan ETag y creación condicional. Los reintentos manuales conservan el UUID del cliente y se comprueba duplicidad de fecha, cuenta, importe, descripción y referencia. El servidor valida dirección del concepto, fechas y precisión decimal. Los endpoints requieren acceso al módulo Conciliación y antifalsificación para POST.

La vista del reporte evita construir el tablero completo o consultar Siigo. Las consultas están limitadas al período y usan selección de columnas y paginación Dataverse. El detalle responde en páginas de 50, con máximo de 100 por petición. Se comparte una caché de 20 segundos, invalidada al escribir, con exclusión de lecturas simultáneas repetidas. La pantalla refresca inmediatamente después de guardar y cada 25 segundos mientras está visible y sin formularios abiertos.

La consulta inicial de octubre–diciembre mediante el servicio real tomó aproximadamente 3,4 segundos en la prueba local y la repetida desde caché menos de 1 ms; no es una garantía de latencia de cada cliente.

## Instalación y validación

1. Verificar que el App Service productivo `dtc-calculadora-linux-7018`, dueño de `calculadora.digitaltechcolombia.com`, utiliza `https://orgc79ca19c.crm2.dynamics.com`. El servicio Windows `calculadoradt` contiene únicamente la redirección de la migración; el script y el workflow quedan alineados al destino Linux.
2. `python scripts/provision_direct_cash_flow.py` muestra el plan; `--apply` agrega únicamente tablas/columnas faltantes al entorno verificado.
3. Agregar cada tabla completa con `pac solution add-solution-component --solutionUniqueName CotizadorInternoCRM --component <logical name> --componentType 1 --AddRequiredComponents`, conectado al mismo entorno. Exportar y desempaquetar la solución. Las tres raíces deben tener `behavior="0"` y ninguna dependencia faltante consigo mismas.
4. Ejecutar las pruebas .NET y comprobar sintaxis de ambos JavaScript de conciliación. Desplegar con `scripts/Deploy-AppService.ps1 -VerifyPath /healthz -KeepPackage`; el proyecto excluye configuraciones locales, credenciales y `tmp` de la publicación.
5. Verificar reporte autenticado, detalle paginado, conceptos por dirección, descarga y hash de los archivos publicados. Las pruebas de creación/edición de movimientos se hicieron en un servidor local con persistencia en memoria para no incorporar cifras ficticias en producción.

El paquete y la carpeta de la solución contienen la exportación actual del entorno, incluyendo cambios de metadatos que ya existían en producción antes de esta funcionalidad.

## Fase posterior

La lectura y comparación de extractos PDF y el envío de registros manuales a Siigo quedan fuera de esta entrega. Se reserva `LoteExtracto` y el origen del movimiento para una futura importación con trazabilidad y detección de duplicados; no hay carga de PDF ni procesos automáticos de envío habilitados.
