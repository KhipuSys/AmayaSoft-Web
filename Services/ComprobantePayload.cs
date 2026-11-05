using MiniMarket.Web.Models;
using System.Globalization;
using System.Text.Json;

namespace MiniMarket.Web.Services;

public static class ComprobantePayload
{
    private static decimal Money(decimal n) => decimal.Round(n, 2, MidpointRounding.AwayFromZero);

    public static string Crear(Venta venta, ComprobanteFiscal fiscal, decimal tasa)
    {
        if (tasa <= 0 || tasa > 100 || venta.Total <= 0 || venta.Detalles.Count == 0 ||
            venta.Detalles.Any(d => d.Producto == null || d.Cantidad <= 0 || d.SubTotal < 0 || d.PrecioUnitario < 0 || Money(d.PrecioUnitario * d.Cantidad) != d.SubTotal))
            throw new InvalidOperationException("Revise los productos, cantidades e IGV de la venta.");
        var cliente = venta.Cliente;
        if (cliente != null && (string.IsNullOrWhiteSpace(cliente.Nombre) ||
            cliente.Documento?.Length is not (8 or 11) || cliente.Documento.Any(c => c < '0' || c > '9')))
            throw new InvalidOperationException("El documento o nombre del cliente no es válido.");
        if (fiscal.Tipo == "01" && (cliente?.Documento?.Length != 11 || string.IsNullOrWhiteSpace(cliente.Direccion)))
            throw new InvalidOperationException("La factura necesita RUC y dirección del cliente.");
        var bruto = venta.Detalles.Sum(d => d.SubTotal);
        if (bruto <= 0 || venta.Descuento < 0 || venta.Descuento >= bruto)
            throw new InvalidOperationException("Descuento fiscal inválido.");
        var neto = bruto - venta.Descuento;
        var recargo = venta.MetodoPago == "Tarjeta" ? Money(neto * .05m) : 0;
        if (neto + recargo != venta.Total) throw new InvalidOperationException("El total guardado no cuadra con productos, descuento y recargo.");

        // Distribución proporcional en céntimos por restos mayores: el descuento nunca
        // excede una línea y las líneas netas suman exactamente el total descontado.
        var exactos = venta.Detalles.Select(d => d.SubTotal * neto / bruto * 100).ToArray();
        var centimos = exactos.Select(decimal.Floor).ToArray();
        var faltantes = (int)(neto * 100 - centimos.Sum());
        foreach (var i in Enumerable.Range(0, centimos.Length).OrderByDescending(i => exactos[i] - centimos[i]).ThenBy(i => i).Take(faltantes)) centimos[i]++;
        var detalles = new List<object>();
        decimal baseTotal = 0, igvTotal = 0;
        void Linea(string codigo, string nombre, int cantidad, decimal importe)
        {
            if (importe <= 0) throw new InvalidOperationException("El descuento deja una línea gratuita. Revise la venta antes de emitir.");
            var baseIgv = Money(importe / (1 + tasa / 100m));
            var igv = importe - baseIgv;
            baseTotal += baseIgv; igvTotal += igv;
            detalles.Add(new {
                unidad = "NIU", cantidad, cod_Producto = codigo, descripcion = nombre,
                monto_Valor_Unitario = decimal.Round(baseIgv / cantidad, 10, MidpointRounding.AwayFromZero),
                monto_Base_Igv = baseIgv, porcentaje_Igv = tasa, igv, tip_Afe_Igv = "10",
                total_Impuestos = igv, monto_Precio_Unitario = decimal.Round(importe / cantidad, 10, MidpointRounding.AwayFromZero),
                monto_Valor_Venta = baseIgv, factor_Icbper = 0
            });
        }
        for (var i = 0; i < venta.Detalles.Count; i++)
        {
            var d = venta.Detalles[i];
            Linea(d.ProductoId.ToString(CultureInfo.InvariantCulture), d.Producto.Nombre +
                (venta.Descuento > 0 ? " (precio neto con descuento)" : ""), d.Cantidad, centimos[i] / 100m);
        }
        if (recargo > 0) Linea("RECARGO-TARJETA-05", "Recargo por pago con tarjeta 5%", 1, recargo);
        var fecha = new DateTimeOffset(DateTime.SpecifyKind(venta.Fecha, DateTimeKind.Unspecified), TimeSpan.FromHours(-5)).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(new {
            tipo_Operacion = "0101", tipo_Doc = fiscal.Tipo, serie = fiscal.Serie,
            correlativo = fiscal.Correlativo.ToString(CultureInfo.InvariantCulture), tipo_Moneda = "PEN", fecha_Emision = fecha,
            empresa_Ruc = fiscal.EmpresaRuc, cliente_Tipo_Doc = cliente?.Documento?.Length == 11 ? "6" : "1",
            cliente_Num_Doc = cliente?.Documento ?? "00000000", cliente_Razon_Social = cliente?.Nombre ?? "PUBLICO GENERAL",
            cliente_Direccion = string.IsNullOrWhiteSpace(cliente?.Direccion) ? "-" : cliente.Direccion,
            monto_Oper_Gravadas = baseTotal, monto_Igv = igvTotal, total_Impuestos = igvTotal, valor_Venta = baseTotal,
            sub_Total = venta.Total, monto_Imp_Venta = venta.Total, monto_Oper_Exoneradas = 0,
            estado_Documento = "0", manual = false, id_Base_Dato = venta.Id.ToString(CultureInfo.InvariantCulture),
            detalle = detalles,
            forma_pago = new[] { new { tipo = "Contado", monto = venta.Total, cuota = 0, fecha_Pago = fecha } },
            legend = new[] { new { legend_Code = "1000", legend_Value = ImporteEnLetras.Soles(venta.Total) } }
        });
    }
}
