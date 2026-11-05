using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ReportesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Ventas(DateTime? fechaInicio, DateTime? fechaFin)
        {
            // Rango por defecto: últimos 30 días hasta hoy
            DateTime inicio = (fechaInicio ?? HoraPeru.Hoy.AddDays(-30)).Date;
            DateTime fin = (fechaFin ?? HoraPeru.Hoy).Date.AddDays(1);

            var ventas = await _context.Ventas
                .Include(v => v.Cliente)
                .Include(v => v.Usuario)
                .Include(v => v.Detalles)
                    .ThenInclude(d => d.Producto)
                .Where(v => v.Fecha >= inicio && v.Fecha < fin && v.Estado == "Activa")
                .OrderByDescending(v => v.Fecha)
                .ToListAsync();

            var modelo = ventas.Select(v => new ReporteVentasViewModel
            {
                VentaId = v.Id,
                Fecha = v.Fecha,
                Cliente = v.Cliente != null ? v.Cliente.Nombre : "Público General",
                Cajero = v.Usuario != null ? v.Usuario.UserName : "Sistema",
                MetodoPago = v.MetodoPago,
                CantidadArticulos = v.Detalles.Sum(d => d.Cantidad),
                TotalVenta = v.Total,
                CostoTotal = v.Detalles.All(d => d.CostoUnitarioHistorico.HasValue)
                    ? v.Detalles.Sum(d => d.CostoUnitarioHistorico.Value * d.Cantidad) : null
            }).ToList();

            ViewBag.FechaInicio = inicio.ToString("yyyy-MM-dd");
            ViewBag.FechaFin = (fechaFin ?? HoraPeru.Hoy).ToString("yyyy-MM-dd");

            return View(modelo);
        }
    }
}
