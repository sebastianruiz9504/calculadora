# Ajustes de captura MTO Firmado V2 (2026-09-08)

Entorno confirmado: `https://orgc79ca19c.crm2.dynamics.com/`.
Solucion existente: `CopiersMtoFirmadoV2`, publisher `DigitalTechCopiers`, prefijo `dtc`.

## Columnas nuevas

| Tabla | Nombre visible | Nombre logico | Contrato |
|---|---|---|---|
| Clientes (`cr07a_cliente`) | Persona encargada de copiers | `dtc_personaencargadacopiers` | Texto con formato Email, maximo 320, sin carga inicial ni reemplazo del correo existente. Opcional en Dataverse para no romper otros modulos; obligatorio al tramitar el MTO en la aplicacion. |
| MTO Firmado V2 (`dtc_copiersmtov2`) | Consecutivo MTO | `dtc_reference` | Autonumeracion `MTO-{SEQNUM:6}`, maximo 100. La aplicacion no envia ni modifica el valor: Dataverse lo asigna al crear la fila y la aplicacion lee el consecutivo persistido. |

El consecutivo usa la secuencia nativa, no `max + 1`. Conserva la semilla nativa inicial (1000); la primera referencia se presenta como `MTO-001000`. Reintentos idempotentes recuperan la misma fila mediante `dtc_operationkey`. Como toda secuencia de Dataverse, puede tener saltos por reservas o operaciones no completadas. No se reseedearon ni actualizaron filas existentes y se conservaron `dtc_name` y `dtc_title`.

Provisionamiento especifico: `scripts/provision_copiers_mto_v2_capture_fields.py`.
Sin `--apply` solo consulta y muestra el plan. Reutiliza columnas compatibles y se detiene ante divergencia; no elimina ni repara destructivamente componentes. Verifica que la columna este incluida explicitamente en la solucion o que la tabla este incluida con todos sus componentes (`rootcomponentbehavior = 0`).

## Catalogos frente a snapshots del ticket

| Uso | Tabla y columna |
|---|---|
| Buscar cliente | `cr07a_clientes`: `cr07a_clienteid`, `cr07a_nombre` |
| Correo encargado | `cr07a_clientes`: `dtc_personaencargadacopiers` |
| Direccion cliente | `cr07a_clientes`: `cr07a_direccion` |
| Buscar serial | `cr07a_equipos`: `cr07a_equipoid`, `cr07a_nombredelequipo` |
| Cliente del equipo | `_cr07a_cliente_value`; navegacion `cr07a_Cliente` |
| Nombre cliente guardado en MTO | `dtc_clientnamesnapshot` |
| Persona guardada en MTO | `dtc_clientcontactnamesnapshot` |
| Correo guardado en MTO | `dtc_clientemailsnapshot` (protegido por seguridad de campo) |
| Serial guardado en MTO | `dtc_equipmentserialsnapshot` |

Las columnas de los catalogos `cr07a_*` no se pueden usar como nombres de columnas en el payload de `dtc_copiersmtov2`. Las vinculaciones del ticket son `dtc_Client` y `dtc_Equipment`; la vinculacion de Equipo con Cliente no es el campo polimorfico `cr07a_clienteasignado`.

## Seguridad y entrega

El perfil dedicado conserva los 27 campos internos existentes, incluidas coordenadas, firma y evidencias. El correo nuevo en Clientes no exige ampliar los permisos de la identidad de V2: se edita por la ruta autorizada del servicio de Clientes, con validacion de la aplicacion.

La precision interna `dtc_accuracymeters` admite ahora hasta 20.000.000 metros para conservar el valor real que entrega el navegador aun cuando la ubicacion es aproximada. Se preservaron minimo 0, precision decimal 7, seguridad de campo, auditoria y etiquetas. No se reducen artificialmente los valores reportados. El limite anterior de 250 metros no era compatible con captura automatica opcional.

`scripts/Provision-CopiersMtoV2Security.ps1` implementa los 16 privilegios aprobados del rol y los 27 permisos de campo. Incluye comprobacion de pertenencia del rol (componente 20) y perfil (componente 70) a la solucion. Las asignaciones de usuarios se verifican en el entorno y no se sustituyen por una exportacion.

La presencia de las tablas, columnas y permisos no demuestra entrega de correo. El flujo Power Automate V2 y su reconciliacion deben verificarse por separado. No se modifican los flujos antiguos ni se envia correo de prueba desde el provisionador de esquema.

Referencia del formato nativo: [Microsoft Learn - Create autonumber columns](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/create-auto-number-attributes).

## Verificacion de este cambio

- 7 pruebas offline de la migracion: modo consulta sin escrituras, drift incompatible, pertenencia a solucion y reuso idempotente.
- `verify_copiers_mto_v2_bindings.py`: 65 columnas, 19 valores de opciones, 3 navegaciones, ambas claves activas, concurrencia optimista, archivo protegido de 12 MiB y precision GPS ampliada: PASS contra el entorno vivo.
- Identidad de V2: un rol de 16 privilegios exactos y un perfil de 27 campos, ambos asignados solo a la identidad dedicada, sin Delete/Assign/Share.
- Prueba desde la identidad administrada real: borrador `Draft/NotReady`, autonumero `MTO-001000`, escritura condicional y rechazo de ETag obsoleto con HTTP 412, datos protegidos y archivo de 58 bytes verificado por SHA-256.
- Se eliminaron exclusivamente la evidencia de prueba `9099436e-f6f0-41a2-b75b-5770e8f79be5` y el borrador `98e3e748-7f17-4aa8-a211-9d776015caf5`, en ese orden, previa comprobacion de sus marcadores. Lectura independiente: cero filas para ambos identificadores. No se enviaron correos de prueba ni se reinicio la secuencia.
