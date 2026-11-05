using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class KardexController : Controller
    {
        private readonly ApplicationDbContext _context;

        public KardexController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Kardex/Index
        public async Task<IActionResult> Index(int? productoId, DateTime? desde, DateTime? hasta)
        {
            ViewBag.Productos = new SelectList(
                _context.Productos.Where(p => p.Estado == true).OrderBy(p => p.Nombre),
                "Id", "Nombre");
            ViewBag.ProductoSeleccionado = productoId;
            ViewBag.Desde = desde?.ToString("yyyy-MM-dd");
            ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd");

            if (productoId == null)
                return View(new System.Collections.Generic.List<MovimientoInventario>());

            var producto = await _context.Productos.FindAsync(productoId);
            ViewBag.ProductoObjeto = producto;

            var query = _context.MovimientosInventario
                .Include(m => m.Producto)
                .Where(m => m.ProductoId == productoId);

            if (desde.HasValue)
                query = query.Where(m => m.Fecha >= desde.Value.Date);

            if (hasta.HasValue)
                query = query.Where(m => m.Fecha < hasta.Value.Date.AddDays(1));

            var movimientos = await query.OrderByDescending(m => m.Fecha).ToListAsync();
            return View(movimientos);
        }

        // GET: Kardex/Ajuste
        [Authorize(Roles = "Administrador")]
        public IActionResult Ajuste()
        {
            ViewBag.Productos = new SelectList(
                _context.Productos.Where(p => p.Estado == true).OrderBy(p => p.Nombre),
                "Id", "Nombre");
            return View();
        }

        // POST: Kardex/Ajuste
        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ajuste(int productoId, string tipoAjuste, int cantidad, string motivo)
        {
            var recargarSelect = new SelectList(
                _context.Productos.Where(p => p.Estado == true).OrderBy(p => p.Nombre),
                "Id", "Nombre");

            if (cantidad <= 0 || (tipoAjuste != "ENTRADA" && tipoAjuste != "SALIDA"))
            {
                TempData["Error"] = "La cantidad debe ser mayor a cero.";
                ViewBag.Productos = recargarSelect;
                return View();
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            var producto = await _context.Productos.FindAsync(productoId);
            if (producto == null)
            {
                TempData["Error"] = "Producto no encontrado.";
                ViewBag.Productos = recargarSelect;
                return View();
            }

            if (tipoAjuste == "SALIDA" && producto.Stock < cantidad)
            {
                TempData["Error"] = $"Stock insuficiente. Disponible: {producto.Stock} unidades.";
                ViewBag.Productos = recargarSelect;
                return View();
            }

            if (tipoAjuste == "ENTRADA")
                producto.Stock += cantidad;
            else
                producto.Stock -= cantidad;

            _context.MovimientosInventario.Add(new MovimientoInventario
            {
                Fecha = HoraPeru.Ahora,
                ProductoId = productoId,
                TipoMovimiento = "AJUSTE",
                Cantidad = cantidad,
                Usuario = User.Identity.Name,
                Referencia = $"Ajuste Manual ({tipoAjuste})",
                Motivo = string.IsNullOrEmpty(motivo) ? "Sin motivo especificado" : motivo
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Exito"] = $"✅ Ajuste registrado. Stock de '{producto.Nombre}' actualizado a {producto.Stock} unidades.";
            return RedirectToAction(nameof(Index), new { productoId });
        }

        // API: Obtener stock de un producto (para el formulario de ajuste)
        [HttpGet]
        public IActionResult GetStock(int id)
        {
            var producto = _context.Productos.Find(id);
            if (producto == null) return Json(new { stock = 0, nombre = "" });
            return Json(new { stock = producto.Stock, nombre = producto.Nombre });
        }
    }
}
