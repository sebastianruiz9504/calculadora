# Centros de costo compartidos

Produccion: Digital Tech Copiers (default), `https://orgc79ca19c.crm2.dynamics.com`.
Aplicacion Gerencia: `c2c05c79-1385-f011-b4cc-000d3ac071b1`.
Solucion: `CentrosCostoCompartidos`, editor existente `Cr44b61`, prefijo `cr07a`.

La tabla organizacional `cr07a_centrodecosto` contiene los cinco centros de
Ventas verificados en Siigo. Codigo tiene una clave alternativa activa.
`cr07a_identificadorsiigo` guarda el identificador numerico de Siigo; el codigo
visible no es ese identificador. Los registros se incluyen en `catalog-seed.json`
para reproducir la configuracion en otro entorno mediante una migracion revisada.

| Tabla | Registros migrados | Valor inicial para nuevos formularios |
|---|---:|---|
| `cr07a_salesperformancerecord` (Productos CLOUD) | 364 | 101-00 |
| `cr07a_contratosboris` | 8 | 103-00 |
| `cr07a_productoscopiers` | 42 | 102-00 |

Las tres tablas usan el lookup obligatorio `cr07a_centrocostoref`, cuya propiedad
de navegacion es `cr07a_CentroCostoRef`. Las relaciones restringen eliminar un
centro referenciado. La columna anterior `cr07a_centrocosto` queda oculta, opcional
y sin valor inicial; sus datos se conservan como respaldo, no como fuente activa.
El catalogo `cr07a_precioscloud` no participa en esta migracion.

Los formularios y vistas muestran el lookup. Una biblioteca comun aplica el
valor inicial exclusivamente a formularios nuevos vacios, consultando el catalogo
por codigo; no reemplaza selecciones existentes ni las hechas mientras carga.
Clientes API deben enviar el lookup: las facturas rechazan centros inexistentes,
inactivos o sin identificador positivo, sin recurrir a la columna anterior.

Cloud agrupa por cliente y GUID del centro, y obtiene codigo/ID mediante `$expand`.
Boris toma el ID del lookup de cada contrato. Se mantienen comprobante FV 1
(ID 7481), horarios, conexiones, TRM, precios, IVA, periodos y controles de reintento.
Copiers no tiene una nueva automatizacion.

## Ejecucion y evidencia

Scripts limitados al tenant autorizado. Requieren los snapshots revisados de esta
migracion en `tmp/shared-cost-centers-20261127` (ignorados por Git). No ejecutar
la migracion de datos despues de empezar a editar los nuevos lookups: las opciones
anteriores son solamente un respaldo historico. Los cambios de negocio concurrentes
se protegen con ETags por fila y lectura posterior; nunca se escriben importes.

1. `manage.py schema`: crea catalogo, clave, cinco filas y relaciones.
2. `manage.py migrate`: migra cada seleccion actual con control de concurrencia.
3. `validate_platform.py`: ocho pruebas nativas sinteticas; elimina sus flujos temporales.
4. `manage.py ui` y `manage.py app`: publica formularios, vistas y entrada de Gerencia.
5. `flows.py`: despliega solo el cambio de origen del centro, comprobando la definicion base.
6. `verify.py`: lectura independiente de registros, controles Boris, UI y flujos activos.
7. `export.py`: exporta XML generado por Dataverse y actualiza los paquetes existentes;
   las credenciales de flujos se censuran en memoria antes de escribir los archivos.

Las pruebas validan los cinco centros actuales y un sexto centro sintetico para
comprobar que no hay una equivalencia fija dentro de los flujos. Tambien validan
agrupacion sin duplicar lineas, meses/anualidades, rechazo de centros invalidos y
protecciones de reintento. No emiten facturas reales ni envian correos.

Los scripts anteriores de opciones locales tienen deshabilitados sus comandos
de escritura para evitar reinstalar la configuracion reemplazada.
