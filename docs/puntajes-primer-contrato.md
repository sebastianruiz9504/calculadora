# Primer contrato por cliente

La columna `cr07a_esprimercontratoconelcliente` de `cr07a_contractrecord1`
pertenece a la regla de Dataverse. Se agrupa por `cr07a_cliente`, incluyendo
todo el historial, registros inactivos, renovaciones y todos los periodos.
El orden es `createdon`, `cr07a_contractstartdate` y GUID en formato D ordinal,
ascendentes. El primero recibe `1` (Si); los demas, `2` (No).

## Componentes

- Ensamblado firmado y aislado `DigitalTech.Puntajes.FirstContract`.
- Seis pasos sincronos: PreOperation y PostOperation de Create, Update y Delete.
- Update PreOperation escucha cliente, fecha de creacion, inicio y la propia
  columna. Update PostOperation excluye la bandera para impedir reentradas.
- Preimagenes con cliente para Update/Delete, en ambos pasos.
- Tabla tecnica `cr07a_scoreclientlock`, propiedad de la organizacion. La clave
  primaria coincide con el GUID del cliente. Un Upsert con nonce serializa
  operaciones del mismo cliente dentro de la transaccion original, antes de
  escribir el registro de puntaje. Los bloqueos de dos clientes se adquieren
  en orden estable al cambiar un registro de cliente.
- El PostOperation consulta todas las paginas y corrige solo las banderas que
  difieren, desmarcando antes de marcar, dentro de la misma transaccion.
- Una edicion de la bandera se corrige directamente en Target en PreOperation,
  sin otra escritura. PostOperation solo reconcilia altas, eliminaciones o
  cambios de cliente/cronologia. Un error revierte la operacion original.
- El servicio se ejecuta con acceso al historial completo para que los permisos
  de lectura del usuario que dispara el evento no alteren quien es el primero.
- Los registros existentes sin cliente se excluyen de la migracion. Si se
  desvincula expresamente un registro, pasa a No y se reevalua el cliente anterior.

El aplicativo muestra el campo deshabilitado, utiliza el valor persistido y
no acepta reemplazarlo con el valor enviado por el navegador al verificar.
No se cambia la formula del calculador, el tipo de negocio, el puntaje ni
la comision. Una escritura directa o desde Power Automate queda cubierta
por la regla sincrona de Dataverse.

## Entorno y solucion

Entorno: `https://orgc79ca19c.crm2.dynamics.com` (Digital Tech Copiers).
Solucion existente: `CotizadorInternoCRM`; editor existente `Cr44b61`, prefijo
`cr07a`. No se crea otro editor ni se modifican roles de usuarios.

`scripts/score_first_contract.py` implementa respaldo, registro desactivado,
activacion comprobada, migracion con If-Match y lectura final. Requiere el
directorio `scripts` del plugin Dataverse mediante `--auth-dir`. La migracion
rechaza cambios de cronologia y comprueba cada cliente antes de continuar.
El respaldo completo contiene datos empresariales y se guarda en `artifacts`,
excluido de Git y de la publicacion web.

La API de metadatos se usa para la tabla tecnica porque el SDK Python 1.0.0
fija LCID 1033 y no expone propiedad OrganizationOwned. El entorno usa 3082.
Los pasos se crean con configuracion `disabled` y luego se desactivan: Dataverse
no acepta crear estos registros directamente con statuscode Disabled.

## Compilacion y comprobacion

```powershell
dotnet build plugins/FirstContract/FirstContract.csproj -c Release -p:AssemblyOriginatorKeyFile=<ruta-local-snk>
dotnet test tests/FirstContract.Plugin.Tests/FirstContract.Plugin.Tests.csproj -c Release
dotnet test tests/CotizadorInterno.Web.Tests/CotizadorInterno.Web.Tests.csproj -c Release
```

La clave de firma se conserva fuera del repositorio y del paquete web. El
proyecto web excluye plugins y soluciones de compilacion y publicacion.

Las pruebas cubren orden cronologico, empates, valores antiguos, idempotencia,
registros historicos inactivos, sucesion tras eliminacion, bloqueos al cambiar
de cliente, contexto de ejecucion, preimagenes, recursion y desactivacion.
La validacion de migracion compara todos los campos de negocio con el respaldo,
permitiendo solo la bandera y los campos de auditoria de sistema.

## Recuperacion

Para una incidencia de la regla, ejecutar primero el modo `disable` con el
manifiesto `registration.json` de esta publicacion. Desactiva PostOperation
antes de PreOperation. No eliminar tabla, registros ni ensamblado como medida
de recuperacion. Restaurar banderas solo desde el respaldo y con revision de
concurrencia; reactivar la regla volveria a normalizarlas.

Fuentes del diseno transaccional:
- https://learn.microsoft.com/power-apps/developer/data-platform/scalable-customization-design/database-transactions
- https://learn.microsoft.com/power-apps/developer/data-platform/register-plug-in
