# Consistencia de ventas y caja

## Publicación y esquema

`ActualizacionComercial.Aplicar` ejecuta un cambio de esquema aditivo e idempotente para las instalaciones que ya usaban `EnsureCreated`. El cambio se ejecuta en una transacción protegida por un bloqueo SQL. No borra tablas ni ventas. Ante un fallo de actualización, el inicio se detiene en lugar de operar con un esquema incompatible.

Las ventas anteriores conservan costo histórico nulo y estado de emisión `Sin registro`. La asociación de ventas antiguas a caja se realiza por vendedor e intervalo de apertura/cierre; si existen intervalos superpuestos se elige la apertura más reciente. Las ventas sin turno compatible quedan sin asociación. Los cierres antiguos conservan sus importes originales; no se reconstruyen retroactivamente.

## Ventas

- El servidor valida productos activos, cantidades positivas, cliente, método de pago, descuento y total confirmado. Obtiene precios y costos de SQL.
- La venta, sus detalles, stock y movimientos se guardan en una sola transacción.
- Un bloqueo SQL compartido por ventas, anulaciones, apertura/cierre, gastos, compras y ajustes de Kardex ordena las operaciones; las filas de producto vendidas también se bloquean durante su lectura.
- La edición de producto no sobrescribe stock. Las correcciones se hacen por Kardex.
- `OperacionId` tiene índice único. Un reintento idéntico recupera la venta incluso si la caja ya cerró. Reutilizarlo con otra solicitud se rechaza.
- El navegador conserva la solicitud pendiente en sessionStorage hasta recibir una respuesta definitiva. No se debe cerrar la pestaña si todavía hay un resultado incierto; sessionStorage no sustituye un registro persistente entre dispositivos.

## Anulación y caja

La anulación es exclusiva del administrador, requiere antiforgery y motivo. Guarda fecha, usuario y motivo. Repetirla no devuelve stock dos veces. La caja abierta suma solo ventas activas de su turno y diferencia efectivo de tarjeta/QR.

Si se anula después del cierre, se acumula `AjustesAnulaciones` y se marca la venta como ajuste posterior. Esto es una anotación contable pendiente de conciliación: conserva el cierre original y no registra una devolución de efectivo automática ni una nota de crédito fiscal. El historial muestra el ajuste por separado.

## Comprobantes

Registrar una venta no contacta a Factiliza. La venta queda en estado `Pendiente` y solo un administrador puede solicitar manualmente la emisión desde el detalle fiscal. En Production se verifica otra vez la contraseña del administrador. Antes de enviar, se reserva y persiste el número fiscal y el payload asociado a la venta.

Una vez que se envió una solicitud, el sistema no vuelve a emitir automáticamente el mismo documento. Un timeout, respuesta incompleta o discrepancia entre el estado guardado en AmayaSoft y el portal del proveedor requiere conciliación manual antes de cualquier otro envío. La aceptación exige respuesta SUNAT exitosa y CDR con código `0`; un HTTP 200 por sí solo no basta. La anulación de la venta, la anulación fiscal y la devolución de dinero son procesos distintos.

## Costos

Los detalles nuevos guardan el costo unitario del producto en el instante de venta. Los reportes no usan el costo actual para ventas antiguas. Las ventas sin costo aparecen como N/D y se excluyen de los indicadores de costo, utilidad bruta y margen; sus ingresos sí se incluyen en ingresos totales.

## Verificación

Ejecutar `dotnet run --project Tests/Comercial.IntegrationTests.csproj -c Release` con una instancia de SQL Server para pruebas y permisos para crear bases. La suite crea una base aislada con nombre aleatorio `MiniMarket_Test_*` y la elimina al finalizar. No utiliza la base del producto ni realiza llamadas reales a Factiliza.
