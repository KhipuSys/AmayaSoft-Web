using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;

namespace MiniMarket.Web.Services;

// Los importes se calculan en SQL con consultas predefinidas; el chat no ejecuta SQL generado.
public class IAAsistenteService(ApplicationDbContext db)
{
    private const string Collation = "Latin1_General_100_CI_AI";
    private static string Soles(decimal n) => "S/. " + n.ToString("N2", CultureInfo.GetCultureInfo("es-PE"));
    private static bool Tiene(string t, string patron) => Regex.IsMatch(t, patron);

    public async Task<string> PreguntarAlAsistenteAsync(string pregunta, string usuario, bool admin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pregunta) || pregunta.Length > 1000) return "Escribe una consulta de hasta 1.000 caracteres.";
        var t = PeriodoAsistente.Normalizar(pregunta);
        if (Tiene(t, @"^(hola|buenas|buenos dias|buenas tardes|buenas noches|gracias)[!¡.\s]*$"))
            return "¡Hola! Puedo consultar tus datos de negocio. Prueba 'resumen del día', 'ventas de ayer' o 'stock bajo'.";
        if (Tiene(t, @"\b(borra|elimina|modifica|anula|registra|resetea)\b"))
            return "El asistente consulta información. Para registrar o modificar datos utiliza el módulo correspondiente.";
        if (Tiene(t, @"\b(vencer|vencimiento|caduca|caducidad)\b"))
            return "Los productos todavía no tienen fecha de vencimiento registrada; no puedo calcular alertas de caducidad.";
        if (Tiene(t, @"\b(ayuda|opciones|ejemplos)\b|que puedes")) return Ayuda;

        var resumen = Tiene(t, @"\bresumen\b|como va (el|mi) negocio");
        var alertas = Tiene(t, @"\b(alerta\w*|recomienda\w*|recomendaciones)\b|que (debo|conviene) (comprar|reponer)");
        var ranking = Tiene(t, @"mas vendid|menos vendid|producto estrella|producto top|sin (movimiento|ventas)|lo que mas vendo");
        var pagos = Tiene(t, @"metodo.*pago|por metodo|\b(yape|plin|tarjeta|efectivo|qr)\b") && !Tiene(t, @"\bcaja\b");
        var margen = Tiene(t, @"\b(ganancia\w*|utilidad|utilidades|beneficio\w*|margen)\b");
        var compras = Tiene(t, @"\b(compra|compras|compre|comprado)\b");
        var gastos = Tiene(t, @"\b(gasto\w*|gaste|egreso\w*)\b");
        var comprobantes = Tiene(t, @"\b(comprobante\w*|boleta\w*|factura\w*|sunat)\b");
        var frecuentes = Tiene(t, @"cliente\w* (frecuente\w*|top)|mejores clientes");
        var ventas = Tiene(t, @"\b(venta\w*|vendi|vendio|vendimos|vendido|vendieron|vendiste)\b");
        var caja = Tiene(t, @"\bcaja\b");
        var stock = Tiene(t, @"\b(stock|existencia\w*|inventario|agotado\w*)\b");
        var valorizado = Tiene(t, @"valorizad|valor del (stock|inventario)|cuanto vale.*(stock|inventario)");
        var bajos = Tiene(t, @"bajo\w*|poc[oa]\w*|critic\w*|reponer|reposicion");
        var agotados = Tiene(t, @"sin stock|agotad|stock cero");
        var productoNombre = ExtraerFiltro(t, "producto");
        var clienteNombre = ExtraerFiltro(t, "cliente");
        var cajeroNombre = ExtraerFiltro(t, "cajero");
        var conteoProductos = Tiene(t, @"cuantos productos|productos (tengo|registrados|activos)|total de productos|total productos");
        var conteoClientes = !frecuentes && clienteNombre == null && Tiene(t, @"\bclientes\b");
        var proveedores = Tiene(t, @"\bproveedores\b");
        var porCajero = Tiene(t, @"por cajero") && cajeroNombre == null;
        var porCliente = Tiene(t, @"por cliente") && clienteNombre == null;
        var porProducto = Tiene(t, @"por producto") && productoNombre == null;
        if (!(resumen || alertas || ranking || pagos || margen || compras || gastos || comprobantes || frecuentes || ventas || caja || stock || valorizado || conteoProductos || conteoClientes || proveedores || productoNombre != null))
            return "No identifiqué una consulta disponible. " + Ayuda;

        // Nunca se amplían permisos mediante texto del usuario.
        if (!admin && (margen || compras || valorizado || proveedores || cajeroNombre != null || porCajero))
            return "Esa consulta está reservada al administrador. Puedes consultar tus propias ventas, caja, productos y clientes.";
        var uid = await db.Users.Where(u => u.UserName == usuario).Select(u => u.Id).SingleOrDefaultAsync(ct);
        if (uid == null) return "Tu sesión no corresponde a un usuario válido. Inicia sesión nuevamente.";
        PeriodoAsistente periodo;
        try { periodo = PeriodoAsistente.Interpretar(t, HoraPeru.Hoy); }
        catch (ArgumentException e) { return e.Message; }
        var inicio = periodo.Inicio; var fin = periodo.Fin;
        var saleQuery = db.Ventas.AsNoTracking().Where(v => v.Fecha >= inicio && v.Fecha < fin);
        if (!admin || Tiene(t, @"\b(mis ventas|yo vendi|vendi yo)\b")) saleQuery = saleQuery.Where(v => v.UsuarioId == uid);
        var filtros = new List<string>();
        int? productoId = null;
        if (productoNombre != null)
        {
            var encontrados = await db.Productos.Where(p => EF.Functions.Collate(p.Nombre, Collation).Contains(productoNombre) || p.CodigoBarras == productoNombre)
                .Select(p => new { p.Id, p.Nombre }).Take(6).ToListAsync(ct);
            if (encontrados.Count != 1) return Elegir("producto", encontrados.Select(p => p.Nombre));
            productoId = encontrados[0].Id; filtros.Add("Producto: " + encontrados[0].Nombre);
            saleQuery = saleQuery.Where(v => v.Detalles.Any(d => d.ProductoId == productoId));
        }
        if (clienteNombre != null)
        {
            var encontrados = await db.Clientes.Where(c => EF.Functions.Collate(c.Nombre, Collation).Contains(clienteNombre) || c.Documento == clienteNombre)
                .Select(c => new { c.Id, c.Nombre }).Take(6).ToListAsync(ct);
            if (encontrados.Count != 1) return Elegir("cliente", encontrados.Select(c => c.Nombre));
            var id = encontrados[0].Id; saleQuery = saleQuery.Where(v => v.ClienteId == id); filtros.Add("Cliente: " + encontrados[0].Nombre);
        }
        string cajeroUsuario = usuario;
        if (cajeroNombre != null)
        {
            var usuarios = from u in db.Users join p in db.UsuarioPerfiles on u.Id equals p.UsuarioId
                select new { u.Id, u.UserName, Nombre = p.Nombres + " " + p.Apellidos };
            foreach (var parte in cajeroNombre.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                usuarios = usuarios.Where(u => EF.Functions.Collate(u.Nombre, Collation).Contains(parte) || u.UserName == cajeroNombre);
            var encontrados = await usuarios.Take(6).ToListAsync(ct);
            if (encontrados.Count != 1) return Elegir("cajero", encontrados.Select(u => u.Nombre));
            uid = encontrados[0].Id; cajeroUsuario = encontrados[0].UserName;
            saleQuery = saleQuery.Where(v => v.UsuarioId == uid); filtros.Add("Cajero: " + encontrados[0].Nombre);
        }
        var anuladas = Tiene(t, @"\banulada\w*\b");
        var activas = saleQuery.Where(v => v.Estado == "Activa");
        var seleccion = anuladas ? saleQuery.Where(v => v.Estado == "Anulada") : activas;
        var metodo = Regex.Match(t, @"\b(yape|plin|tarjeta|efectivo|qr)\b");
        if (metodo.Success && pagos)
        {
            var valor = metodo.Value.ToUpperInvariant();
            seleccion = seleccion.Where(v => v.MetodoPago.ToUpper() == valor);
            filtros.Add("Pago: " + valor);
        }
        var r = new List<string> { $"Período: {periodo.Etiqueta}." + (admin ? "" : " Solo tus operaciones.") };
        r.AddRange(filtros);
        if (resumen || ventas || margen)
        {
            var total = await seleccion.SumAsync(v => (decimal?)v.Total, ct) ?? 0;
            var cantidad = await seleccion.CountAsync(ct);
            r.Add($"Ventas {(anuladas ? "anuladas (no son ingresos)" : "activas")}: {Soles(total)} en {cantidad} operaciones.");
            if (productoId != null) r.Add("El importe anterior corresponde a tickets completos que contienen el producto.");
        }
        if (resumen || pagos)
        {
            var grupos = await seleccion.GroupBy(v => v.MetodoPago).Select(g => new { Nombre = g.Key, Total = g.Sum(v => v.Total) }).OrderByDescending(x => x.Total).ToListAsync(ct);
            r.Add("Por método de pago: " + (grupos.Count == 0 ? "sin operaciones." : string.Join("; ", grupos.Select(x => $"{x.Nombre}: {Soles(x.Total)}"))));
        }
        if (resumen || ranking || porProducto || (ventas && productoId != null))
        {
            var detalles = seleccion.SelectMany(v => v.Detalles);
            if (productoId != null) detalles = detalles.Where(d => d.ProductoId == productoId);
            var top = detalles.GroupBy(d => new { d.ProductoId, d.Producto.Nombre })
                .Select(g => new { g.Key.ProductoId, g.Key.Nombre, Unidades = g.Sum(d => d.Cantidad), Bruto = g.Sum(d => d.SubTotal) });
            var lista = await (t.Contains("menos vendido") ? top.OrderBy(x => x.Unidades).ThenBy(x => x.ProductoId) : top.OrderByDescending(x => x.Unidades).ThenBy(x => x.ProductoId)).Take(5).ToListAsync(ct);
            r.Add("Productos vendidos (hasta 5; importes antes del descuento global):\n" + (lista.Count == 0 ? "Sin ventas." : string.Join("\n", lista.Select(x => $"- {x.Nombre}: {x.Unidades} unidades; {Soles(x.Bruto)}."))));
        }
        if (alertas || Tiene(t, "sin movimiento|sin ventas|menos vendido"))
        {
            var sin = await db.Productos.Where(p => p.Estado && !activas.Any(v => v.Detalles.Any(d => d.ProductoId == p.Id)))
                .OrderBy(p => p.Nombre).Select(p => p.Nombre).Take(5).ToListAsync(ct);
            r.Add("Sin ventas en el período" + (admin ? "" : " en tus operaciones") + ": " + (sin.Count == 0 ? "ninguno." : string.Join(", ", sin) + " (hasta 5)."));
        }
        if (frecuentes || porCliente)
        {
            var grupos = await seleccion.GroupBy(v => new { v.ClienteId, Nombre = v.Cliente == null ? "Público general" : v.Cliente.Nombre })
                .Select(g => new { g.Key.Nombre, Veces = g.Count(), Total = g.Sum(v => v.Total) }).OrderByDescending(x => x.Veces).ThenByDescending(x => x.Total).Take(5).ToListAsync(ct);
            r.Add("Clientes del período (hasta 5):\n" + (grupos.Count == 0 ? "Sin operaciones." : string.Join("\n", grupos.Select(x => $"- {x.Nombre}: {x.Veces} ventas; {Soles(x.Total)}."))));
        }
        if (porCajero)
        {
            var grupos = await seleccion.GroupBy(v => v.Usuario.UserName).Select(g => new { Nombre = g.Key, Total = g.Sum(v => v.Total) }).OrderByDescending(x => x.Total).Take(10).ToListAsync(ct);
            r.Add("Ventas por cajero (hasta 10):\n" + string.Join("\n", grupos.Select(x => $"- {x.Nombre}: {Soles(x.Total)}")));
        }
        if (margen)
        {
            var incompletas = await activas.CountAsync(v => !v.Detalles.Any() || v.Detalles.Any(d => d.CostoUnitarioHistorico == null), ct);
            var completas = activas.Where(v => v.Detalles.Any() && v.Detalles.All(d => d.CostoUnitarioHistorico != null));
            var ingreso = await completas.SumAsync(v => (decimal?)v.Total, ct) ?? 0;
            var costo = await completas.SelectMany(v => v.Detalles).SumAsync(d => d.CostoUnitarioHistorico * d.Cantidad, ct) ?? 0;
            r.Add($"Margen comercial estimado de tickets con costo histórico: {Soles(ingreso - costo)} = cobrado {Soles(ingreso)} − costo {Soles(costo)}. Incluye descuentos; no es utilidad neta contable ni descuenta gastos o impuestos. Tickets excluidos por falta de costo/detalle: {incompletas}.");
        }
        if (compras)
        {
            var q = db.Compras.Where(c => c.Fecha >= inicio && c.Fecha < fin);
            r.Add($"Compras: {Soles(await q.SumAsync(c => (decimal?)c.Total, ct) ?? 0)} en {await q.CountAsync(ct)} registros.");
        }
        if (resumen || gastos)
        {
            var q = db.Gastos.Where(g => g.Fecha >= inicio && g.Fecha < fin);
            if (!admin || cajeroNombre != null) q = q.Where(g => g.UsuarioId == uid);
            r.Add($"Gastos: {Soles(await q.SumAsync(g => (decimal?)g.Monto, ct) ?? 0)}.");
            if (t.Contains("concepto"))
            {
                var grupos = await q.GroupBy(g => g.Concepto).Select(g => new { Nombre = g.Key, Total = g.Sum(x => x.Monto) }).OrderByDescending(x => x.Total).Take(10).ToListAsync(ct);
                r.Add("Por concepto (hasta 10):\n" + string.Join("\n", grupos.Select(x => $"- {x.Nombre}: {Soles(x.Total)}")));
            }
        }
        if (resumen || stock || alertas || (productoId != null && !ventas))
        {
            var q = db.Productos.Where(p => p.Estado);
            if (productoId != null) q = q.Where(p => p.Id == productoId);
            else if (agotados) q = q.Where(p => p.Stock <= 0);
            else if (bajos || resumen || alertas) q = q.Where(p => p.Stock <= p.StockMinimo);
            var count = await q.CountAsync(ct);
            var lista = await q.OrderBy(p => p.Stock).ThenBy(p => p.Nombre).Take(8).Select(p => new { p.Nombre, p.Stock, p.StockMinimo, p.Precio }).ToListAsync(ct);
            r.Add($"Stock actual ({(agotados ? "agotados" : bajos || resumen || alertas ? "bajo o crítico" : "productos activos")}): {count} productos.\n" + string.Join("\n", lista.Select(p => $"- {p.Nombre}: {p.Stock} unidades; mínimo {p.StockMinimo}; precio {Soles(p.Precio)}.")) + (count > 8 ? "\nMostrando 8; consulta Inventario para el resto." : ""));
            if (alertas && count > 0) r.Add("Revisa la reposición de estos productos según demanda y plazo de entrega del proveedor.");
        }
        if (valorizado)
        {
            var q = db.Productos.Where(p => p.Estado && p.Stock > 0);
            r.Add($"Inventario actual al costo configurado: {Soles(await q.SumAsync(p => (decimal?)(p.Stock * p.Costo), ct) ?? 0)}. A precio de venta: {Soles(await q.SumAsync(p => (decimal?)(p.Stock * p.Precio), ct) ?? 0)}. No representa dinero cobrado.");
        }
        if (conteoProductos) r.Add($"Productos registrados: {await db.Productos.CountAsync(ct)}; activos: {await db.Productos.CountAsync(p => p.Estado, ct)}. Son referencias, no unidades.");
        if (conteoClientes) r.Add($"Clientes registrados: {await db.Clientes.CountAsync(ct)}; activos: {await db.Clientes.CountAsync(c => c.Estado, ct)}.");
        if (proveedores || (alertas && admin)) r.Add($"Proveedores registrados: {await db.Proveedores.CountAsync(ct)}; activos: {await db.Proveedores.CountAsync(p => p.Estado, ct)}.");
        if (caja || resumen || alertas)
        {
            var abierta = await db.AperturasCaja.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == cajeroUsuario && c.Estado, ct);
            if (abierta == null) r.Add("Caja actual del usuario consultado: cerrada.");
            else
            {
                var ef = await db.Ventas.Where(v => v.AperturaCajaId == abierta.Id && v.Estado == "Activa" && v.MetodoPago == "Efectivo").SumAsync(v => (decimal?)v.Total, ct) ?? 0;
                var salidas = await db.Gastos.Where(g => g.AperturaCajaId == abierta.Id).SumAsync(g => (decimal?)g.Monto, ct) ?? 0;
                r.Add($"Caja actual: abierta desde {abierta.FechaApertura:dd/MM/yyyy HH:mm}. Efectivo esperado: {Soles(abierta.MontoInicial + ef - salidas)} (inicial + ventas en efectivo − gastos). Yape, Plin y tarjeta no suman efectivo físico.");
                if (alertas && HoraPeru.Ahora - abierta.FechaApertura > TimeSpan.FromHours(12)) r.Add("La caja lleva más de 12 horas abierta; revisa si corresponde cerrar el turno.");
            }
        }
        if (comprobantes || alertas)
        {
            var estados = await saleQuery.GroupBy(v => v.EstadoEmision).Select(g => new { Estado = g.Key, Cantidad = g.Count() }).ToListAsync(ct);
            r.Add("Estados de emisión registrados: " + (estados.Count == 0 ? "sin registros." : string.Join("; ", estados.Select(e => $"{e.Estado}: {e.Cantidad}"))) + "\nEstos estados de API no acreditan aceptación de SUNAT. Para confirmarla se necesita verificar el CDR o consultar al proveedor.");
        }
        if (alertas && admin)
        {
            var descuentosAltos = await activas.CountAsync(v => v.Descuento * 100 > (v.Total + v.Descuento) * 30, ct);
            var debajoCosto = await activas.CountAsync(v => v.Detalles.Any() && v.Detalles.All(d => d.CostoUnitarioHistorico != null) &&
                v.Total < v.Detalles.Sum(d => d.CostoUnitarioHistorico * d.Cantidad), ct);
            r.Add($"Revisión comercial del período: {descuentosAltos} ventas con descuento superior al 30%; {debajoCosto} ventas por debajo del costo histórico. Revisa promociones y márgenes si aparecen casos.");
        }
        if (Tiene(t, @"\b(compara|comparar|versus|vs)\b")) r.Add("No realicé una comparación automática; consulta cada período por separado.");
        return string.Join("\n\n", r);
    }

    private static string ExtraerFiltro(string t, string tipo)
    {
        var citado = Regex.Match(t, $"\\b{tipo}\\s+\"([^\"]+)\"");
        if (citado.Success) return citado.Groups[1].Value.Trim();
        var m = Regex.Match(t, $@"\b(?:del?|al|para el)\s+{tipo}\s+(.+?)(?=\s+(?:hoy|ayer|anteayer|este|esta|el\s+\d|del?\s+\d|entre\s+\d|ultimos|en\s+\w+|por\s+|y\s+)|[?¿!]|$)");
        if (!m.Success) return null;
        var nombre = m.Groups[1].Value.Trim();
        return nombre.Length == 0 || Tiene(nombre, @"^(mas|menos|top|estrella|frecuente|dia|mes)\b") ? null : nombre;
    }
    private static string Elegir(string tipo, IEnumerable<string> nombres)
    {
        var lista = nombres.ToArray();
        return lista.Length == 0 ? $"No encontré ese {tipo}. Revisa el nombre o documento/código."
            : $"Hay varias coincidencias de {tipo}. Usa un nombre más completo entre comillas:\n- " + string.Join("\n- ", lista);
    }
    private const string Ayuda = "Puedes consultar:\n" +
        "• Resumen del día / alertas del negocio\n• Ventas de ayer / esta semana / mes pasado / últimos 7 días\n" +
        "• Ventas del 1 al 15 de septiembre / ventas de agosto 2026\n• Ventas por cajero / por cliente / por producto\n" +
        "• Ventas del producto \"Inca Kola\" / del cliente \"Carmen\"\n• Ventas por Yape / métodos de pago\n" +
        "• Producto más vendido / menos vendido / sin movimiento\n• Ganancia estimada / compras / gastos por concepto\n" +
        "• Stock bajo / agotados / inventario valorizado\n• Cuántos clientes, productos o proveedores tengo\n" +
        "• Mi caja / caja del cajero \"nombre completo\"\n• Comprobantes pendientes\n" +
        "Sin fecha, los reportes usan hoy (hora Perú). Stock y caja abierta reflejan el estado actual. Algunas consultas son solo para administrador. No conservo contexto: incluye fecha y filtros en cada pregunta.";
}
