using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ProveedoresController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FactilizaService _factilizaService;

        public ProveedoresController(ApplicationDbContext context, FactilizaService factilizaService)
        {
            _context = context;
            _factilizaService = factilizaService;
        }

        // --- LISTADO PRINCIPAL ---
        public async Task<IActionResult> Index()
        {
            return View(await _context.Proveedores.ToListAsync());
        }

        // --- CREAR ---
        public IActionResult Create()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarDocumento(string documento)
        {
            var resultado = await _factilizaService.ConsultarDocumentoAsync(documento);
            return Json(resultado);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Proveedor proveedor)
        {
            if (ModelState.IsValid)
            {
                _context.Add(proveedor);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(proveedor);
        }

        // --- EDITAR ---
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor == null) return NotFound();
            return View(proveedor);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Proveedor proveedor)
        {
            if (id != proveedor.Id) return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(proveedor);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(proveedor);
        }

        // --- ELIMINAR ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor == null) return NotFound();
            proveedor.Estado = !proveedor.Estado;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var proveedor = await _context.Proveedores.FirstOrDefaultAsync(m => m.Id == id);
            if (proveedor == null) return NotFound();
            return View(proveedor);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor != null)
            {
                _context.Proveedores.Remove(proveedor);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // ==========================================================
        //  MÉTODO NUEVO: BUSCADOR PARA EL SELECT2
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> BuscarProveedores(string term, int page = 1)
        {
            string busqueda = (term ?? "").Trim();
            page = Math.Clamp(page, 1, 10000);

            // Buscamos proveedores Activos que coincidan por Nombre o RUC
            var proveedores = await _context.Proveedores
                .AsNoTracking().Where(p => p.Estado &&
                           (busqueda == "" || p.RazonSocial.Contains(busqueda) || p.Ruc.Contains(busqueda)))
                .OrderBy(p => p.RazonSocial).ThenBy(p => p.Id).Skip((page - 1) * 20)
                .Select(p => new {
                    id = p.Id,
                    text = $"{p.RazonSocial} | RUC: {p.Ruc}" // Lo que se ve en la lista
                })
                .Take(21)
                .ToListAsync();

            return Json(new { results = proveedores.Take(20), pagination = new { more = proveedores.Count > 20 } });
        }
    }
}
