using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VentaRequest = MiniMarket.Web.Controllers.VentaRequest;

namespace MiniMarket.Web.Services;

public class OperacionVentaService(ApplicationDbContext db)
{
    private const decimal DescuentoMaximoCajeroPct = 30m;
    private const decimal RecargoTarjetaPct = 5m;

    public async Task<(Venta Venta, bool Repetida)> Registrar(VentaRequest request, string usuario, bool permiteDescuentoMayor = false)
    {
        if (request == null || request.OperacionId == Guid.Empty || request.Detalles == null ||
            request.Detalles.Count is < 1 or > 200 || request.Detalles.Any(d => d == null || d.ProductoId <= 0 || d.Cantidad is < 1 or > 100000))
            throw new InvalidOperationException("Solicitud inválida: cantidades positivas e identificador de operación obligatorios.");
        if (request.Descuento < 0 || decimal.Round(request.Descuento, 2) != request.Descuento ||
            !new[] { "Efectivo", "Tarjeta", "Yape", "Plin", "QR" }.Contains(request.MetodoPago))
            throw new InvalidOperationException("Descuento o método de pago inválido.");

        var lineas = request.Detalles.GroupBy(d => d.ProductoId).OrderBy(g => g.Key)
            .Select(g => new { ProductoId = g.Key, Cantidad = checked(g.Sum(d => d.Cantidad)) }).ToList();
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            request.ClienteId, request.Descuento, request.MetodoPago, request.Total, Lineas = lineas
        }))));

        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var anterior = await db.Ventas.SingleOrDefaultAsync(v => v.OperacionId == request.OperacionId);
        if (anterior != null)
        {
            var propietario = await db.Users.AnyAsync(u => u.Id == anterior.UsuarioId && u.UserName == usuario);
            if (!propietario || anterior.HuellaSolicitud != huella)
                throw new InvalidOperationException("El identificador de operación ya corresponde a otra solicitud.");
            await tx.CommitAsync();
            return (anterior, true);
        }

        var caja = await db.AperturasCaja.SingleOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado);
        if (caja == null) throw new InvalidOperationException("Debes abrir caja antes de vender.");
        var userId = await db.Users.Where(u => u.UserName == usuario).Select(u => u.Id).SingleAsync();
        if (request.ClienteId.HasValue && !await db.Clientes.AnyAsync(c => c.Id == request.ClienteId && c.Estado))
            throw new InvalidOperationException("El cliente no existe o está inactivo.");

        var venta = new Venta { OperacionId = request.OperacionId, HuellaSolicitud = huella,
            AperturaCajaId = caja.Id, UsuarioId = userId, ClienteId = request.ClienteId,
            Descuento = request.Descuento, MetodoPago = request.MetodoPago, Fecha = HoraPeru.Ahora };
        foreach (var linea in lineas)
        {
            // Bloquea la fila hasta el commit: lectura de precio/costo y descuento de stock consistentes.
            var p = await db.Productos.FromSqlInterpolated($"SELECT * FROM Productos WITH (UPDLOCK, HOLDLOCK) WHERE Id={linea.ProductoId}").SingleOrDefaultAsync();
            if (p == null || !p.Estado) throw new InvalidOperationException("Producto inexistente o inactivo.");
            if (p.Stock < linea.Cantidad) throw new InvalidOperationException($"Stock insuficiente para {p.Nombre}.");
            if (p.Precio < 0 || p.Costo < 0) throw new InvalidOperationException("El producto tiene un precio o costo inválido.");
            var subtotal = decimal.Round(p.Precio * linea.Cantidad, 2, MidpointRounding.AwayFromZero);
            venta.Detalles.Add(new DetalleVenta { ProductoId = p.Id, Cantidad = linea.Cantidad,
                PrecioUnitario = p.Precio, CostoUnitarioHistorico = p.Costo, SubTotal = subtotal });
            p.Stock -= linea.Cantidad;
        }
        var bruto = venta.Detalles.Sum(d => d.SubTotal);
        if (request.Descuento > bruto) throw new InvalidOperationException("El descuento excede el subtotal.");
        var descuentoMaximo = decimal.Round(bruto * DescuentoMaximoCajeroPct / 100m, 2, MidpointRounding.AwayFromZero);
        if (!permiteDescuentoMayor && request.Descuento > descuentoMaximo)
            throw new InvalidOperationException($"El descuento máximo para cajero es {DescuentoMaximoCajeroPct:N0}%: S/. {descuentoMaximo:N2}.");
        var totalBase = bruto - request.Descuento;
        var recargoTarjeta = request.MetodoPago == "Tarjeta"
            ? decimal.Round(totalBase * RecargoTarjetaPct / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
        venta.Total = totalBase + recargoTarjeta;
        if (venta.Total <= 0) throw new InvalidOperationException("El total de la venta debe ser mayor a S/. 0.00.");
        // La confirmación del cliente debe coincidir; nunca guardamos importes calculados por el navegador.
        if (request.Total != venta.Total) throw new InvalidOperationException("Los precios cambiaron o el total no coincide. Actualiza el carrito antes de confirmar.");
        if (venta.Total > 9999999999999999.99m) throw new InvalidOperationException("El total excede el límite permitido.");
        db.Ventas.Add(venta);
        await db.SaveChangesAsync();
        foreach (var d in venta.Detalles)
            db.MovimientosInventario.Add(new MovimientoInventario { Fecha = venta.Fecha, ProductoId = d.ProductoId,
                TipoMovimiento = "SALIDA", Cantidad = d.Cantidad, Usuario = usuario, Referencia = $"Venta #{venta.Id}" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (venta, false);
    }

    public async Task Anular(int id, string usuario, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 300)
            throw new InvalidOperationException("Indica un motivo de anulación (máximo 300 caracteres).");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var venta = await db.Ventas.Include(v => v.Detalles).SingleOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("Venta no encontrada.");
        if (venta.Estado == "Anulada") { await tx.CommitAsync(); return; }
        venta.Estado = "Anulada";
        venta.FechaAnulacion = HoraPeru.Ahora;
        venta.AnuladaPor = usuario;
        venta.MotivoAnulacion = motivo.Trim();
        if (venta.EstadoEmision.StartsWith("No enviada", StringComparison.Ordinal)) venta.EstadoEmision = "Cancelada sin emisión";
        else if (venta.EstadoEmision != "Pendiente") venta.EstadoEmision = "Revisar anulación externa";
        else venta.EstadoEmision = "Cancelada";
        var caja = venta.AperturaCajaId.HasValue ? await db.AperturasCaja.FindAsync(venta.AperturaCajaId.Value) : null;
        if (caja != null && !caja.Estado)
        {
            caja.AjustesAnulaciones += venta.Total;
            venta.AjustePosteriorCierre = true;
        }
        foreach (var d in venta.Detalles.OrderBy(d => d.ProductoId))
        {
            // Incremento atómico: tampoco se pierde stock ante otra actualización concurrente.
            var filas = await db.Productos.Where(p => p.Id == d.ProductoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + d.Cantidad));
            if (filas != 1) throw new InvalidOperationException("No se puede restituir un producto eliminado.");
            db.MovimientosInventario.Add(new MovimientoInventario { Fecha = HoraPeru.Ahora,
                ProductoId = d.ProductoId, TipoMovimiento = "AJUSTE-ENTRADA", Cantidad = d.Cantidad,
                Usuario = usuario, Referencia = $"Anulación Venta #{id}", Motivo = motivo.Trim() });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
