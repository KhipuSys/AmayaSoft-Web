using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class SeedDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IHostEnvironment _environment;

        public SeedDataController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        public IActionResult Index()
        {
            if (!_environment.IsDevelopment()) return NotFound();
            return Content("La generación de datos de prueba requiere una solicitud POST de administrador con token antifalsificación.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generar()
        {
            if (!_environment.IsDevelopment()) return NotFound();
            try
            {
                // 0. Obtener Usuario Administrador (Obligatorio para Ventas)
                var adminUser = await _userManager.FindByEmailAsync("admin@amayasoft.com") ?? await _context.Users.FirstOrDefaultAsync();
                if (adminUser == null) return Content("Error: No se encontró ningún usuario en el sistema.");

                // 1. GENERAR 20 CATEGORÍAS
                string[] categoriasNombres = { 
                    "Lácteos", "Bebidas", "Snacks", "Abarrotes", "Limpieza", 
                    "Higiene Personal", "Panadería", "Embutidos", "Frutas", "Verduras",
                    "Carnes", "Pescados", "Congelados", "Mascotas", "Librería",
                    "Ferretería", "Hogar", "Cuidado Bebé", "Licores", "Dulces"
                };

                var listaCategorias = new List<Categoria>();
                foreach (var nombre in categoriasNombres)
                {
                    var cat = new Categoria { Nombre = nombre, Descripcion = "Categoría de prueba para " + nombre };
                    listaCategorias.Add(cat);
                }
                _context.Categorias.AddRange(listaCategorias);
                await _context.SaveChangesAsync();

                // 2. GENERAR 20 PRODUCTOS
                var random = new Random();
                var listaProductos = new List<Producto>();
                for (int i = 1; i <= 20; i++)
                {
                    var catAleatoria = listaCategorias[random.Next(listaCategorias.Count)];
                    var prod = new Producto
                    {
                        Nombre = "Producto de Prueba " + i,
                        Descripcion = "Descripción del producto " + i,
                        Precio = (decimal)(random.NextDouble() * 50 + 1), // 1.00 a 51.00
                        Costo = (decimal)(random.NextDouble() * 30 + 0.5),
                        Stock = random.Next(10, 100),
                        StockMinimo = 5,
                        Estado = true,
                        CategoriaId = catAleatoria.Id,
                        CodigoBarras = HoraPeru.Ahora.Ticks.ToString().Substring(10, 8) + i
                    };
                    listaProductos.Add(prod);
                }
                _context.Productos.AddRange(listaProductos);
                await _context.SaveChangesAsync();

                // 3. GENERAR 20 VENTAS (Últimos 7 días)
                var listaVentas = new List<Venta>();
                for (int i = 0; i < 20; i++)
                {
                    var diaAtras = random.Next(0, 7); // 0 = hoy, 1 = ayer, etc.
                    var venta = new Venta
                    {
                        Fecha = HoraPeru.Ahora.AddDays(-diaAtras).AddHours(random.Next(-5, 0)),
                        UsuarioId = adminUser.Id,
                        Total = 0, // Se calculará según los detalles
                    };
                    
                    // Detalle de venta para esta venta
                    int numItems = random.Next(1, 4);
                    decimal totalVenta = 0;
                    for (int j = 0; j < numItems; j++)
                    {
                        var prod = listaProductos[random.Next(listaProductos.Count)];
                        var cant = random.Next(1, 5);
                        var subtotal = prod.Precio * cant;
                        totalVenta += subtotal;

                        venta.Detalles.Add(new DetalleVenta 
                        { 
                            ProductoId = prod.Id, 
                            Cantidad = cant, 
                            PrecioUnitario = prod.Precio,
                            SubTotal = subtotal
                        });
                    }
                    venta.Total = totalVenta;
                    listaVentas.Add(venta);
                }
                _context.Ventas.AddRange(listaVentas);
                await _context.SaveChangesAsync();

                return Content("¡Éxito! Se han generado 20 categorías, 20 productos y 20 ventas de prueba para tu Dashboard. Puedes ir al Dashboard a ver los resultados.");
            }
            catch (Exception ex)
            {
                return Content("Error al generar datos: " + ex.Message + (ex.InnerException != null ? " -> " + ex.InnerException.Message : ""));
            }
        }
    }
}
