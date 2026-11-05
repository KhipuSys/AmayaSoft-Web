using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador,Cajero")]
    public class ClientesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FactilizaService _factilizaService;

        public ClientesController(ApplicationDbContext context, FactilizaService factilizaService)
        {
            _context = context;
            _factilizaService = factilizaService;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.Clientes.ToListAsync());
        }

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
        public async Task<IActionResult> Create(Cliente cliente)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                // Evitar duplicados por Documento
                if (await _context.Clientes.AnyAsync(c => c.Documento == cliente.Documento))
                {
                    ModelState.AddModelError("Documento", "Ya existe un cliente con este documento.");
                    return View(cliente);
                }

                cliente.Estado = true;
                _context.Add(cliente);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                TempData["Titulo"] = "¡Registrado!";
                TempData["Mensaje"] = "Cliente guardado exitosamente.";
                TempData["Tipo"] = "success";
                
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int id, Cliente cliente)
        {
            if (id != cliente.Id) return NotFound();

            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                var existente = await _context.Clientes.FindAsync(id);
                if (existente == null) return NotFound();
                if (await _context.Clientes.AnyAsync(c => c.Id != id && c.Documento == cliente.Documento))
                {
                    ModelState.AddModelError("Documento", "Ya existe otro cliente con este documento.");
                    return View(cliente);
                }
                try
                {
                    // Editar datos no activa/desactiva al cliente ni cambia su identidad interna.
                    existente.Documento = cliente.Documento;
                    existente.Nombre = cliente.Nombre;
                    existente.Telefono = cliente.Telefono;
                    existente.Correo = cliente.Correo;
                    existente.Direccion = cliente.Direccion;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    TempData["Titulo"] = "¡Actualizado!";
                    TempData["Mensaje"] = "Datos del cliente actualizados.";
                    TempData["Tipo"] = "success";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Clientes.Any(e => e.Id == cliente.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> CambiarEstado(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();

            cliente.Estado = !cliente.Estado;
            await _context.SaveChangesAsync();

            TempData["Titulo"] = cliente.Estado ? "¡Habilitado!" : "¡Inhabilitado!";
            TempData["Mensaje"] = "El estado del cliente ha cambiado.";
            TempData["Tipo"] = cliente.Estado ? "success" : "warning";

            return RedirectToAction(nameof(Index));
        }

        // GET: Historial de compras de un cliente
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Compras(int? id)
        {
            if (id == null) return NotFound();

            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();

            var ventas = await _context.Ventas
                .Include(v => v.Usuario)
                .Include(v => v.Detalles)
                .Where(v => v.ClienteId == id)
                .OrderByDescending(v => v.Fecha)
                .ToListAsync();

            ViewBag.Cliente = cliente;
            return View(ventas);
        }
    }
}
