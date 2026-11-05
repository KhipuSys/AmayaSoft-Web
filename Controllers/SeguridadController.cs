using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class SeguridadController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public SeguridadController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FactoryReset()
        {
            try
            {
                if (_environment.IsEnvironment("QA") && await _context.ComprobantesFiscales.AnyAsync())
                {
                    TempData["Error"] = "Hay comprobantes fiscales y numeración reservada. No se permite reiniciar el historial fiscal con el reseteo de fábrica.";
                    return RedirectToAction(nameof(Index));
                }
                string sql = @"
                    EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';
                    DELETE FROM DetalleVentas;
                    DELETE FROM Ventas;
                    DELETE FROM DetalleCompras;
                    DELETE FROM Compras;
                    DELETE FROM MovimientosInventario;
                    DELETE FROM Gastos;
                    DELETE FROM AperturasCaja;
                    IF OBJECT_ID('CodigosProducto', 'U') IS NOT NULL DELETE FROM CodigosProducto;
                    DELETE FROM Productos;
                    DELETE FROM Categorias;
                    DELETE FROM Clientes;
                    DELETE FROM Proveedores;
                    
                    DBCC CHECKIDENT ('Ventas', RESEED, 0);
                    DBCC CHECKIDENT ('Compras', RESEED, 0);
                    DBCC CHECKIDENT ('DetalleVentas', RESEED, 0);
                    DBCC CHECKIDENT ('DetalleCompras', RESEED, 0);
                    DBCC CHECKIDENT ('MovimientosInventario', RESEED, 0);
                    DBCC CHECKIDENT ('Gastos', RESEED, 0);
                    DBCC CHECKIDENT ('AperturasCaja', RESEED, 0);
                    IF OBJECT_ID('CodigosProducto', 'U') IS NOT NULL DBCC CHECKIDENT ('CodigosProducto', RESEED, 0);
                    DBCC CHECKIDENT ('Productos', RESEED, 0);
                    DBCC CHECKIDENT ('Categorias', RESEED, 0);
                    DBCC CHECKIDENT ('Clientes', RESEED, 0);
                    DBCC CHECKIDENT ('Proveedores', RESEED, 0);
                    
                    EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';
                ";

                await _context.Database.ExecuteSqlRawAsync(sql);

                TempData["Mensaje"] = "¡Reseteo de fábrica exitoso! Se ha eliminado todo el historial pero se conservó la configuración visual y tu usuario Administrador.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al resetear base de datos: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
