using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class GastosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public GastosController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Gastos (Historial)
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Index()
        {
            var gastos = await _context.Gastos
                .Include(g => g.Usuario)
                .OrderByDescending(g => g.Fecha)
                .ToListAsync();

            return View(gastos);
        }

        // GET: Gastos/Create
        public async Task<IActionResult> Create()
        {
            var usuario = User.Identity.Name;

            // Verificamos si hay caja abierta, si no, no se pueden registrar gastos en caja
            var cajaAbierta = await _context.AperturasCaja
                .FirstOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado == true);

            if (cajaAbierta == null)
            {
                TempData["ErrorCaja"] = "Debe abrir una caja antes de registrar un gasto operativo.";
                return RedirectToAction("Index", "Caja");
            }

            return View();
        }

        // POST: Gastos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Concepto,Monto")] Gasto gasto)
        {
            if (gasto.Monto <= 0) ModelState.AddModelError("Monto", "El gasto debe ser positivo.");
            if (ModelState.IsValid)
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                var usuario = User.Identity.Name;
                var userObj = await _userManager.FindByNameAsync(usuario);

                var cajaAbierta = await _context.AperturasCaja
                    .FirstOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado == true);

                if (cajaAbierta == null)
                {
                    TempData["ErrorCaja"] = "Debe tener una caja abierta para registrar gastos.";
                    return RedirectToAction("Index", "Caja");
                }

                gasto.UsuarioId = userObj.Id;
                gasto.AperturaCajaId = cajaAbierta.Id;

                _context.Add(gasto);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["MensajeExito"] = "Gasto registrado correctamente descontado de la caja actual.";
                return RedirectToAction("Index", "Caja");
            }
            return View(gasto);
        }
    }
}
