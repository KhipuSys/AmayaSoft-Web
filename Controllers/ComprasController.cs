using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ComprasController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public ComprasController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(int? productoId, int? cantidad)
        {
            if (productoId.HasValue && productoId.Value > 0)
            {
                var prod = await _context.Productos.FindAsync(productoId.Value);
                if (prod != null)
                {
                    ViewBag.PreloadProductoId = prod.Id;
                    ViewBag.PreloadNombre = prod.Nombre;
                    ViewBag.PreloadCosto = prod.Costo;
                    ViewBag.PreloadCantidad = (cantidad.HasValue && cantidad.Value > 0) ? cantidad.Value : 1;
                }
            }

            var compras = await _context.Compras
                .Include(c => c.Proveedor)
                .Include(c => c.Usuario)
                .OrderByDescending(c => c.Fecha)
                .ToListAsync();
            return View(compras);
        }

        public IActionResult Create(int? productoId, int? cantidad)
        {
            return RedirectToAction(nameof(Index), new { productoId, cantidad });
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var compra = await _context.Compras
                .Include(c => c.Proveedor)
                .Include(c => c.Usuario)
                .Include(c => c.Detalles)
                .ThenInclude(d => d.Producto)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (compra == null) return NotFound();

            return View(compra);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registrar([FromBody] CompraRequestDTO modelo)
        {
            if (modelo != null)
                modelo.NumeroDocumento = modelo.NumeroDocumento?.Trim();
            if (!ModelState.IsValid || modelo == null || string.IsNullOrWhiteSpace(modelo.NumeroDocumento) || modelo.NumeroDocumento.Length > 50 ||
                modelo.Detalles == null || modelo.Detalles.Count is < 1 or > 200 ||
                modelo.Detalles.Any(d => d == null || d.Cantidad is < 1 or > 100000 || d.Precio <= 0 || d.Precio > 100000000 || decimal.Round(d.Precio, 2) != d.Precio))
                return Json(new { exito = false, mensaje = "Detalle, cantidades o costos inválidos." });
            var totalCalculado = modelo.Detalles.Sum(d => decimal.Round(d.Cantidad * d.Precio, 2, MidpointRounding.AwayFromZero));
            if (modelo.Total != totalCalculado || modelo.Detalles.Any(d => d.Total != decimal.Round(d.Cantidad * d.Precio, 2, MidpointRounding.AwayFromZero)))
                return Json(new { exito = false, mensaje = "Los subtotales o el total no coinciden con cantidades y costos." });

            using var tran = await _context.Database.BeginTransactionAsync();
            try
            {
                await ActualizacionComercial.BloquearOperaciones(_context);
                if (!await _context.Proveedores.AnyAsync(p => p.Id == modelo.ProveedorId && p.Estado))
                    throw new InvalidOperationException("Proveedor inexistente o inactivo.");
                if (await _context.Compras.AnyAsync(c => c.ProveedorId == modelo.ProveedorId && c.NumeroDocumento == modelo.NumeroDocumento))
                    throw new InvalidOperationException("Ya existe una compra registrada con este proveedor y número de documento.");
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId) && User.Identity.IsAuthenticated)
                {
                    var user = await _userManager.FindByNameAsync(User.Identity.Name);
                    userId = user?.Id;
                }

                var compra = new Compra
                {
                    ProveedorId = modelo.ProveedorId,
                    Fecha = HoraPeru.Ahora,
                    NumeroDocumento = modelo.NumeroDocumento,
                    Total = totalCalculado,
                    UsuarioId = userId
                };

                _context.Compras.Add(compra);
                await _context.SaveChangesAsync();

                foreach (var item in modelo.Detalles)
                {
                    if (item.Cantidad <= 0 || item.Precio < 0) throw new InvalidOperationException("Cantidad o costo inválidos.");
                    var producto = await _context.Productos.FindAsync(item.ProductoId);
                    if (producto == null || !producto.Estado) throw new InvalidOperationException("Producto inexistente o inactivo.");

                    // A) AUMENTAR STOCK Y ACTUALIZAR COSTO
                    producto.Stock = checked(producto.Stock + item.Cantidad);
                    producto.Costo = item.Precio;

                    // B) Guardar Detalle
                    var detalle = new DetalleCompra
                    {
                        CompraId = compra.Id,
                        ProductoId = item.ProductoId,
                        Cantidad = item.Cantidad,
                        PrecioUnitario = item.Precio,
                        Total = decimal.Round(item.Cantidad * item.Precio, 2, MidpointRounding.AwayFromZero)
                    };
                    _context.DetalleCompras.Add(detalle);

                    // C) REGISTRAR EN KARDEX
                    var kardex = new MovimientoInventario
                    {
                        Fecha = HoraPeru.Ahora,
                        ProductoId = item.ProductoId,
                        TipoMovimiento = "ENTRADA", 
                        Cantidad = item.Cantidad,
                        Referencia = $"Compra {compra.NumeroDocumento}",
                        Usuario = User.Identity.Name ?? "Sistema"
                    };
                    _context.MovimientosInventario.Add(kardex);
                }

                await _context.SaveChangesAsync();
                await tran.CommitAsync();

                return Json(new { exito = true, mensaje = "Compra registrada correctamente" });
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                var errorMsg = ex is InvalidOperationException ? ex.Message : "No se pudo registrar la compra. Verifica los datos e inténtalo de nuevo.";
                return Json(new { exito = false, mensaje = "Error al guardar: " + errorMsg });
            }
        }

        // ==========================================================
        //  MÉTODO NUEVO: BUSCADOR PARA COMPRAS (Muestra Stock 0)
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> BuscarProductos(string term, int page = 1)
        {
            string busqueda = (term ?? "").Trim();
            page = Math.Clamp(page, 1, 10000);

            // NOTA: Aquí quitamos la restricción de "p.Stock > 0"
            var productos = await _context.Productos
                .AsNoTracking().Where(p => p.Estado &&
                           (busqueda == "" || p.Nombre.Contains(busqueda) || p.CodigoBarras.Contains(busqueda) || p.Id.ToString() == busqueda))
                .OrderByDescending(p => busqueda != "" && p.CodigoBarras == busqueda).ThenBy(p => p.Nombre).ThenBy(p => p.Id)
                .Skip((page - 1) * 20)
                .Select(p => new {
                    id = p.Id,
                    // Mostramos el stock actual para referencia
                    text = $"{p.Nombre} | Stock Actual: {p.Stock}", 
                    precio = p.Costo,
                    stock = p.Stock,
                    nombre = p.Nombre
                })
                .Take(21)
                .ToListAsync();

            return Json(new { results = productos.Take(20), pagination = new { more = productos.Count > 20 } });
        }
    }

    public class CompraRequestDTO
    {
        public int ProveedorId { get; set; }
        [Required]
        [StringLength(50)]
        public string NumeroDocumento { get; set; }
        public decimal Total { get; set; }
        public List<DetalleCompraDTO> Detalles { get; set; }
    }

    public class DetalleCompraDTO
    {
        public int ProductoId { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Total { get; set; }
    }
}
