# Upgrades por producto

Cada línea de catálogo puede conservar un `ProductUpgrade` opcional. La interfaz
usa el check **Es upgrade** y un único diálogo de búsqueda de la licencia base.
El destino sigue siendo el producto de la línea; al desmarcar se restauran su
costo y precio sugerido originales. Hardware conserva su flujo habitual.

## Cálculo

- `CostUnit` es el costo diferencial: destino menos base, con hasta cuatro decimales.
- Margen y acelerador del destino se aplican sobre ese diferencial.
- Cantidad, meses, IVA, tipo de negocio y prorrateo conservan sus reglas actuales.
- Con prorrateo, el controlador conserva CrossSale y aplica días/365 una sola vez
  a la utilidad. Los totales de venta mantienen la semántica actual de la calculadora.
- Se validan la selección, la diferencia positiva y los términos de facturación
  expresados en las descripciones. El catálogo de la calculadora utiliza USD.
- No se consultan precios al recalcular o reabrir: los precios seleccionados
  forman parte del escenario, aunque posteriormente cambie el catálogo.

## Persistencia y compatibilidad

No se necesitan columnas, tablas ni migraciones nuevas. En los registros de tipo
línea de `cr07a_negocioscomerciales`, `cr07a_linesjson` conserva el objeto opcional
`{ "Upgrade": { ... } }` junto a los campos económicos ya existentes. Se lee y se
escribe dentro de las operaciones actuales. Los registros sin upgrade mantienen
sus hashes y su comportamiento anterior. Cambiar la licencia base invalida los
resultados y exportaciones guardados mediante el hash económico.

El snapshot acompaña la duplicación, el guardado, la propuesta, Excel y la
solicitud de aprovisionamiento. La descripción completa de solicitudes que
contienen upgrades utiliza el formato JSON que Puntajes ya admite; las
notificaciones mantienen tablas legibles. Los campos enteros del contrato de
Power Automate permanecen compatibles y `lineItemsJson` conserva los decimales.

Para no perder líneas ni metadatos, los snapshots de Puntajes con upgrades que
superan 4.000 caracteres se conservan íntegros como gzip/base64 dentro del mismo
campo, con marcador `Encoding: gzip-base64-v1` y su `Version` original. La lectura
expande ese formato antes de deserializar. Los registros existentes no cambian.
La lectura de comisiones de Nómina utiliza la misma expansión del snapshot.
Si incluso la representación comprimida excede la capacidad existente, la
operación devuelve un error explícito en lugar de eliminar información.

## Consultas y comprobaciones

Ambos buscadores comparten una caché de 40 búsquedas durante dos minutos y una
promesa por búsqueda en curso. Hay un mínimo de dos caracteres, debounce y
descarte de resultados obsoletos. Abrir, editar, confirmar o quitar un upgrade,
calcular y reabrir un escenario no agrega consultas de catálogo.

Las pruebas cubren cálculo mixto, prorrateo, multiplicadores, precisión,
validaciones, hashes, persistencia estructurada, IVA, Excel, propuesta, conversión
de aprovisionamiento a Puntajes, snapshots grandes y caché de búsquedas.
`scripts/Test-CalculatorUpgrades.js` verifica el contrato del módulo de interfaz.
