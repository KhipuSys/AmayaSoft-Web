using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting; // Necesario para manejar archivos
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ProductosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment; // Para guardar imágenes

        public ProductosController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Productos
        public async Task<IActionResult> Index()
        {
            var productos = await _context.Productos.Include(p => p.Categoria).ToListAsync();
            return View(productos);
        }

        // GET: Productos/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos
                .Include(p => p.Categoria)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (producto == null) return NotFound();

            return View(producto);
        }

        // GET: Productos/Create
        public IActionResult Create()
        {
            ViewData["CategoriaId"] = new SelectList(_context.Categorias, "Id", "Nombre");
            return View();
        }

        // POST: Productos/Create
        // Aceptamos "imagenArchivo" desde el formulario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Producto producto, IFormFile imagenArchivo)
        {
            if (ModelState.IsValid)
            {
                // LÓGICA PARA GUARDAR IMAGEN
                if (imagenArchivo != null)
                {
                    // 1. Preparar carpeta
                    string carpetaImagenes = Path.Combine(_webHostEnvironment.WebRootPath, "imagenes", "productos");
                    if (!Directory.Exists(carpetaImagenes)) Directory.CreateDirectory(carpetaImagenes);

                    // 2. Generar nombre único (para que no se reemplacen fotos con el mismo nombre)
                    string nombreArchivo = Guid.NewGuid().ToString() + Path.GetExtension(imagenArchivo.FileName);
                    string rutaCompleta = Path.Combine(carpetaImagenes, nombreArchivo);

                    // 3. Guardar archivo en el servidor
                    using (var fileStream = new FileStream(rutaCompleta, FileMode.Create))
                    {
                        await imagenArchivo.CopyToAsync(fileStream);
                    }

                    // 4. Guardar la ruta en la base de datos
                    producto.ImagenUrl = "/imagenes/productos/" + nombreArchivo;
                }

                _context.Add(producto);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["CategoriaId"] = new SelectList(_context.Categorias, "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // GET: Productos/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();
            
            ViewData["CategoriaId"] = new SelectList(_context.Categorias, "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // POST: Productos/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Producto producto, IFormFile imagenArchivo)
        {
            if (id != producto.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // LÓGICA PARA ACTUALIZAR IMAGEN
                    if (imagenArchivo != null)
                    {
                        string carpetaImagenes = Path.Combine(_webHostEnvironment.WebRootPath, "imagenes", "productos");
                        if (!Directory.Exists(carpetaImagenes)) Directory.CreateDirectory(carpetaImagenes);

                        string nombreArchivo = Guid.NewGuid().ToString() + Path.GetExtension(imagenArchivo.FileName);
                        string rutaCompleta = Path.Combine(carpetaImagenes, nombreArchivo);

                        using (var fileStream = new FileStream(rutaCompleta, FileMode.Create))
                        {
                            await imagenArchivo.CopyToAsync(fileStream);
                        }

                        // Borrar imagen anterior si existía (Opcional, para no llenar el disco)
                        // ...

                        producto.ImagenUrl = "/imagenes/productos/" + nombreArchivo;
                    }
                    else
                    {
                        // Si no suben nueva foto, mantenemos la que ya tenía (truco para no perderla)
                        // Buscamos el producto original en la BD sin rastrearlo (AsNoTracking) para leer su URL
                        var productoOriginal = await _context.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
                        if (productoOriginal != null)
                        {
                            producto.ImagenUrl = productoOriginal.ImagenUrl;
                        }
                    }

                    _context.Update(producto);
                    // El inventario se modifica por compras, ventas o ajustes auditados de Kardex.
                    _context.Entry(producto).Property(p => p.Stock).IsModified = false;
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductoExists(producto.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["CategoriaId"] = new SelectList(_context.Categorias, "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // GET: Productos/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            producto.Estado = !producto.Estado;
            await _context.SaveChangesAsync();

            TempData["Exito"] = producto.Estado ? "Producto activado correctamente." : "Producto desactivado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private bool ProductoExists(int id)
        {
            return _context.Productos.Any(e => e.Id == id);
        }
    }
}
