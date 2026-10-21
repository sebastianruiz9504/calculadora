# Cruce de licenciamiento

El periodo del costo se toma de `cr07a_mesconsumo`. Si no se puede interpretar,
se usa el mes anterior a `cr07a_factura`. Las ventas corresponden al mes
siguiente del consumo. La fecha del proveedor se conserva al mover un consumo
prepagado. La misma regla alimenta matriz, graficos, exportacion y Dashboard.

La ficha abre desde el nombre del cliente, con costo y venta visibles incluso
cuando falta uno de los lados. Permite elegir un consumo del periodo/tipo
cargado y buscar el cliente operativo por nombre o NIT completo sin digito de
verificacion. Los importes siguen abriendo el detalle de su mes.

Las relaciones usan la tabla existente `cr07a_licenciamientoaccountmap`:

- Solo este consumo: `sourceaccountname=consumption:<record GUID>`; sin cuenta
  origen. Tiene precedencia sobre reglas de cuenta y no afecta otros registros.
- Permanente: cuenta origen por GUID y `sourceaccountname=account:<account GUID>`.
  Afecta periodos anteriores y futuras cargas de esa cuenta, respetando excepciones.
- Ambas guardan `targetclientid` y `targetclientname`, dejando el destino Account
  ID vacio. No cambian la cuenta original, la factura ni los importes del consumo.
- Se mantienen las equivalencias antiguas con Account ID destino y sus reglas.
  Las reglas nuevas no hacen fallback por nombre. Clientes con IDs distintos
  tampoco se cruzan automaticamente por similitud del nombre.

El servidor deriva el origen del registro real, valida la version del consumo
y de la relacion, usa escrituras condicionales y verifica la lectura posterior.
Los IDs deterministas impiden crear duplicados al repetir una solicitud.
La aplicacion conserva la autorizacion del modulo y los permisos delegados de
Dataverse. El despliegue no asigna relaciones empresariales automaticamente.

Validacion: suite .NET y `scripts/Test-LicenciamientoRelations.js` con jsdom
solo para pruebas. Los scripts Prepare/Deploy de CruceLicenciamiento conservan
configuracion, dependencias y activos de otros modulos, y verifican el paquete
contra la base productiva antes y despues del despliegue.
