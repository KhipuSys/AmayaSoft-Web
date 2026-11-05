using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var hoyInicio = HoraPeru.Hoy;
            var hoyFin = hoyInicio.AddDays(1);

            // 1. TARJETAS SUPERIORES
            var ventasHoy = await _context.Ventas
                .Where(v => v.Fecha >= hoyInicio && v.Fecha < hoyFin && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            var totalProductos = await _context.Productos.CountAsync(p => p.Estado == true);
            var totalCategorias = await _context.Categorias.CountAsync();

            var capitalEstimado = await _context.Productos
                .Where(p => p.Estado == true)
                .SumAsync(p => p.Precio * p.Stock);

            // 2. GRÁFICO DE LÍNEAS (Últimos 7 días) — solo ventas activas
            var fechaInicioGrafico = hoyInicio.AddDays(-6);
            var ventasUltimos7Dias = await _context.Ventas
                .Where(v => v.Fecha >= fechaInicioGrafico && v.Estado == "Activa")
                .GroupBy(v => v.Fecha.Date)
                .Select(g => new { Fecha = g.Key, Total = g.Sum(v => v.Total) })
                .ToListAsync();

            var etiquetasFechas = new List<string>();
            var valoresVentas = new List<decimal>();

            for (int i = 0; i < 7; i++)
            {
                var fechaActual = fechaInicioGrafico.AddDays(i);
                var ventaDia = ventasUltimos7Dias.FirstOrDefault(v => v.Fecha == fechaActual);
                etiquetasFechas.Add(fechaActual.ToString("ddd dd", new CultureInfo("es-ES")));
                valoresVentas.Add(ventaDia?.Total ?? 0);
            }

            // 3. GRÁFICO CIRCULAR (TOP 6 PRODUCTOS) — solo ventas activas
            var topProductos = await _context.DetalleVentas
                .Include(d => d.Producto)
                .Include(d => d.Venta)
                .Where(d => d.Venta.Estado == "Activa")
                .GroupBy(d => d.Producto.Nombre)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(d => d.Cantidad) })
                .OrderByDescending(x => x.Cantidad)
                .Take(6)
                .ToListAsync();

            // 4. ALERTAS STOCK BAJO
            var productosStockBajo = await _context.Productos
                .Where(p => p.Estado == true && p.Stock <= p.StockMinimo)
                .OrderBy(p => p.Stock)
                .ToListAsync();

            // 5. VENTAS POR CAJERO Y COMPARATIVO MENSUAL
            var inicioMesActual = new DateTime(hoyInicio.Year, hoyInicio.Month, 1);
            var finMesActual = inicioMesActual.AddMonths(1).AddTicks(-1);
            
            var inicioMesAnterior = inicioMesActual.AddMonths(-1);
            var finMesAnterior = inicioMesActual.AddTicks(-1);

            var ventasActual = await _context.Ventas
                .Where(v => v.Fecha >= inicioMesActual && v.Fecha <= finMesActual && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            var ventasAnterior = await _context.Ventas
                .Where(v => v.Fecha >= inicioMesAnterior && v.Fecha <= finMesAnterior && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            decimal crecimiento = 0;
            if (ventasAnterior > 0)
                crecimiento = ((ventasActual - ventasAnterior) / ventasAnterior) * 100;
            else if (ventasActual > 0)
                crecimiento = 100; // Si antes era 0 y ahora hay ventas, 100% de crecimiento

            var ventasCajero = await _context.Ventas
                .Include(v => v.Usuario)
                .Where(v => v.Fecha >= inicioMesActual && v.Fecha <= finMesActual && v.Estado == "Activa")
                .GroupBy(v => v.Usuario.UserName)
                .Select(g => new { Cajero = g.Key ?? "Sistema", Total = g.Sum(x => x.Total) })
                .OrderByDescending(x => x.Total)
                .ToListAsync();

            var modelo = new DashboardViewModel
            {
                VentasHoy = ventasHoy,
                TotalProductos = totalProductos,
                TotalCategorias = totalCategorias,
                CapitalEstimado = capitalEstimado,
                FechasGrafico = etiquetasFechas,
                VentasGrafico = valoresVentas,
                TopProductosNombres = topProductos.Select(x => x.Producto).ToList(),
                TopProductosCantidades = topProductos.Select(x => x.Cantidad).ToList(),
                ProductosStockBajo = productosStockBajo,
                VentasMesActual = ventasActual,
                VentasMesAnterior = ventasAnterior,
                PorcentajeCrecimiento = crecimiento,
                VentasPorCajero = ventasCajero.ToDictionary(k => k.Cajero, v => v.Total)
            };

            return View(modelo);
        }
    }
}
