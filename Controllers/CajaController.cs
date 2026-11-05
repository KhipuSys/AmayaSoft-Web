using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class CajaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CajaController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Muestra el estado de la caja del usuario actual
        public async Task<IActionResult> Index()
        {
            var usuario = User.Identity.Name;

            // Buscamos si hay una caja abierta para este usuario
            var cajaAbierta = await _context.AperturasCaja
                .FirstOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado == true);

            if (cajaAbierta != null)
            {
                // SI YA ESTÁ ABIERTA: Calculamos cuánto ha vendido hasta ahora
                // Sumamos todas las ventas de este usuario desde que abrió la caja
                var ventas = await _context.Ventas
                    .Where(v => v.AperturaCajaId == cajaAbierta.Id && v.Estado == "Activa")
                    .SumAsync(v => v.Total);

                // Calculamos los gastos registrados en la caja activa
                var gastos = await _context.Gastos
                    .Where(g => g.AperturaCajaId == cajaAbierta.Id)
                    .SumAsync(g => g.Monto);

                // Actualizamos el modelo visualmente (no en BD aún)
                cajaAbierta.TotalVentas = ventas;
                cajaAbierta.VentasEfectivo = await _context.Ventas
                    .Where(v => v.AperturaCajaId == cajaAbierta.Id && v.Estado == "Activa" && v.MetodoPago == "Efectivo")
                    .SumAsync(v => v.Total);
                cajaAbierta.TotalGastos = gastos;
                
                ViewBag.Estado = "Abierto";
                return View(cajaAbierta);
            }
            else
            {
                // SI ESTÁ CERRADA: Mandamos modelo vacío para abrir nueva
                ViewBag.Estado = "Cerrado";
                return View(new AperturaCaja { MontoInicial = 0 });
            }
        }

        // POST: Abrir Caja
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Abrir(decimal montoInicial)
        {
            if (!ModelState.IsValid || montoInicial < 0 || decimal.Round(montoInicial, 2) != montoInicial)
                return BadRequest("Monto inicial inválido.");
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            if (await _context.AperturasCaja.AnyAsync(c => c.UsuarioId == User.Identity.Name && c.Estado))
                return RedirectToAction(nameof(Index));
            var nuevaCaja = new AperturaCaja
            {
                UsuarioId = User.Identity.Name,
                FechaApertura = HoraPeru.Ahora,
                MontoInicial = montoInicial,
                Estado = true
            };

            _context.AperturasCaja.Add(nuevaCaja);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return RedirectToAction(nameof(Index));
        }

        // POST: Cerrar Caja (Arqueo)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cerrar(decimal montoCierre)
        {
            if (!ModelState.IsValid || montoCierre < 0 || decimal.Round(montoCierre, 2) != montoCierre)
                return BadRequest("Monto de cierre inválido.");
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            var usuario = User.Identity.Name;
            var caja = await _context.AperturasCaja
                .FirstOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado == true);

            if (caja == null) return RedirectToAction(nameof(Index));

            // 1. Calcular total vendido real
            var ventasTotal = await _context.Ventas
                .Where(v => v.AperturaCajaId == caja.Id && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            // 2. Calcular gastos totales
            var gastos = await _context.Gastos
                .Where(g => g.AperturaCajaId == caja.Id)
                .SumAsync(g => g.Monto);

            // 3. Guardar cierre
            caja.FechaCierre = HoraPeru.Ahora;
            caja.TotalVentas = ventasTotal;
            caja.VentasEfectivo = await _context.Ventas
                .Where(v => v.AperturaCajaId == caja.Id && v.Estado == "Activa" && v.MetodoPago == "Efectivo")
                .SumAsync(v => v.Total);
            caja.TotalGastos = gastos;
            caja.MontoCierre = montoCierre;
            caja.Estado = false; // Cerramos la caja

            _context.Update(caja);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Historial de Arqueos
        [Authorize(Roles = "Administrador,Cajero")]
        public async Task<IActionResult> Historial()
        {
            var consulta = _context.AperturasCaja.Where(c => c.Estado == false);
            if (!User.IsInRole("Administrador"))
                consulta = consulta.Where(c => c.UsuarioId == User.Identity.Name);
            var arqueos = await consulta.OrderByDescending(c => c.FechaCierre).ToListAsync();

            return View(arqueos);
        }
    }
}
