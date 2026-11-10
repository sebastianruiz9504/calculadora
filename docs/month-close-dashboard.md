# Panel de cierre mensual

El panel usa capturas persistidas. La primera consulta lee solamente resúmenes; los documentos se paginan al abrir un popup y el YTD se carga al entrar en su sección. El proceso de fondo obtiene una observación completa y vuelve a intentarlo cinco minutos después de terminar cada lectura. Una lectura fallida conserva la captura anterior y su fecha visible.

- Cloud cruza facturación neta sin IVA y licenciamiento por **fecha de factura del mismo mes**, usando Account ID y grupos explícitos. No desplaza el costo por mes de consumo ni aproxima nombres. Los costos sin ingresos aparecen negativos; los cruces ambiguos y costos desconocidos quedan pendientes.
- Copiers cuenta una visita completada, sin multiplicarla por equipos, a COP 100.000. El flete usa la ciudad registrada del cliente: Bogotá 40.000; municipios aprobados y Cartagena, Santa Marta y Barranquilla 100.000; otras ciudades 500.000. Una ciudad vacía deja el costo pendiente. Se trata de costos estándar de gestión, sin gastos generales.
- Los suministros entregados usan cantidad por el último costo unitario aprobado registrado antes de la entrega. La fuente de compras disponible no tiene fecha de factura; se utiliza su fecha de registro y se informa el criterio en cada movimiento. Sin costo anterior no se inventa un cero.
- La política está en `MonthClose:Costs`: `Maintenance`, `BogotaFreight`, `RegionalFreight`, `OtherFreight` y `RegionalCities`. Cada captura conserva la política utilizada. El listado inicial aprobado incluye Chía, Cajicá, Cota, Funza, Mosquera, Madrid, Soacha, Sibaté, La Calera, Sopó, Tocancipá, Gachancipá, Tabio, Tenjo, Zipaquirá y Facatativá, además de las tres ciudades de la costa.
- El valor de negocios activos usa la misma fórmula anual USD de Todos los negocios: cantidad por valor unitario redondeado por doce. En cierres anteriores se anualiza exclusivamente la base mensual de negocios ya guardada, cuando existe.
- El YTD reutiliza el componente, filtros y distribución de gastos XCB existentes. Su detalle dentro del cierre no permite edición. La utilidad de la tabla por cliente usa el costo cargado sin redistribuir el desfase XCB.

El borrador mensual tiene un ID separado y actualizaciones condicionales por versión. Al comenzar el mes siguiente se copia su última observación completa a un cierre creado una sola vez; conserva la fecha real de observación. No se recalculan los cierres nuevos con fuentes actuales. Si no existe ninguna observación anterior, el cierre se identifica como reconstruido.

El YTD original también usa límites UTC explícitos al consultar gastos para respetar la fecha de calendario almacenada, igual que el cierre. Se verificó contra Dataverse que el filtro anterior omitía cuatro gastos de `2026-01-01T00:00:00Z`, por COP 21.150.000. La corrección no cambia registros ni capturas cerradas.

La ampliación inicial del histórico agrega una nueva versión con utilidad y YTD reconstruidos, conservando cada reporte financiero previo y su versión original. `DashboardApply` es idempotente y rechaza fuentes de prueba (`ReplayPath`). No corrige automáticamente el maestro de Account ID.

## Validación y operación

Se verificaron 1.100 pruebas .NET, seis pruebas de interacción y los popups, filtros YTD, detalle de solo lectura, mes cerrado y tamaño móvil en navegador. También se bloquea la ruta administrativa antigua para cierres congelados. `scripts/verify_month_close_upgrade.py` verifica con el SDK oficial que los originales conservan su hash y sus reportes.

1. Compilar Release y ejecutar `scripts/Run-MonthClose.ps1 -Mode DashboardPreview`.
2. Ejecutar `scripts/verify_month_close_upgrade.py` para comprobar que los reportes financieros se conservan.
3. Publicar la ampliación con `scripts/Run-MonthClose.ps1 -Mode DashboardApply` y verificar con `scripts/verify_month_close_upgrade.py --after`.
4. Desplegar el paquete en **dtc-calculadora-linux-7018**, suscripción **7018b9b6-5dfc-4d91-bc4d-5f29f27553bd**, grupo **DigitalTechAppAI**. El workflow histórico apunta a otro App Service; esta publicación se realiza de forma controlada con `[skip ci]`.

Los archivos de evidencia y datos de prueba se guardan bajo `tmp/`, excluido del repositorio y del paquete web. Los informes de cartera mantienen las limitaciones históricas de trazabilidad; las nuevas tablas identifican expresamente datos pendientes y facturación sin vertical.
