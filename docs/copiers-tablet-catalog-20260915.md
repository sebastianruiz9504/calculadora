# Copiers: recuperación del catálogo en tablet

## Base y alcance

Base activa comprobada: commit `0c3806cab56304ee7865353a9d0e6693de4edd5e`, despliegue `d0a493bedfcb4234ab679d8ea8222dc9` del 15 de septiembre. El JavaScript público coincide con esa base, salvo finales de línea. No hay cambios de esquema, permisos, configuración ni correo.

El fallo se reprodujo con el código anterior: un borrador con `equipmentCatalog.loaded=false` recibe un catálogo válido en `refreshRecoveryCatalog`; seleccionar el serial muestra «Equipo seleccionado», pero la validación sigue mostrando «Espera a que termine la consulta de equipos». No se inspeccionó el borrador de la Redmi, por lo que no se atribuye a una incompatibilidad exclusiva de Edge.

## Corrección

- Carga inicial y recuperación validan y actualizan conjuntamente la lista y el estado del catálogo.
- La recuperación vuelve a sincronizar el serial por ID y persiste el estado corregido.
- Respuestas tardías se ignoran tras cambiar cliente, tipo, petición o comenzar un envío inmutable.
- Un fallo de recuperación conserva la captura y ofrece reintento; reintentar ya no reinicia ni borra la selección o los equipos adicionales.
- Una respuesta mal formada nunca habilita captura manual ni sustituye la lista guardada.

## Validación y entrega

185 pruebas Node del formulario, selector y calendario, incluidas ocho nuevas. Pruebas locales con Edge headless táctil 800×1100: borrador con catálogo incompleto, uno y varios equipos, continuación al siguiente paso, firma y evidencias conservadas, respuesta perdida, recarga, un único editor y ausencia de cargas duplicadas. Nueve comprobaciones del formulario de operaciones de equipos también pasan. No se crearon tickets, movimientos ni correos de producción. La prueba de tablet física queda con el técnico.

Los scripts `Prepare-CopiersTabletCatalogRelease.ps1` y `Deploy-CopiersTabletCatalogRelease.ps1` fijan la base activa y generan un único paquete de seis archivos (DLL/PDB identificados por commit, manifiesto y JS con sus variantes comprimidas). Conservan configuración, dependencias y catorce archivos por lectura SHA-256; mantienen todos los endpoints estáticos ajenos. Se conserva ZIP de reversión. Evidencia final de pruebas .NET y publicación: `C:/cwt/artifacts-copiers-tablet-catalog-20260915/`.

Tras desplegar, recargar la misma página en Edge cuando indique que el borrador está guardado. No borrar caché/datos del sitio ni usar «Crear otro registro» para recuperar el registro actual.
