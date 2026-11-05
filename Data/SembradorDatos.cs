using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMarket.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Data
{
    public static class SembradorDatos
    {
        public static async Task Inicializar(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // 1. Asegurar base de datos
            // El esquema se prepara en Program antes de ejecutar el sembrador.

            // 2. Crear Roles
            string[] roles = { "Administrador", "Cajero" };
            foreach (var rol in roles)
            {
                if (!await roleManager.RoleExistsAsync(rol))
                {
                    await roleManager.CreateAsync(new IdentityRole(rol));
                }
            }

            // 3. Usuario Administrador por defecto
            var emailAdmin = "admin@amayasoft.com";
            var usuarioAdmin = (await userManager.GetUsersInRoleAsync("Administrador")).FirstOrDefault();

            if (usuarioAdmin != null)
            {
                // Mantener la cuenta existente aunque el administrador cambie su correo.
                emailAdmin = usuarioAdmin.UserName;
            }
            else
            {
                if (await context.Users.AnyAsync())
                    throw new InvalidOperationException("Hay usuarios existentes sin administrador. No se creará otra cuenta predeterminada automáticamente.");
                var adminInitialPassword = serviceProvider.GetRequiredService<IConfiguration>()["SeedData:InitialAdminPassword"];
                if (string.IsNullOrWhiteSpace(adminInitialPassword))
                    throw new InvalidOperationException("Configure SeedData__InitialAdminPassword como secreto de desarrollo para crear el administrador inicial.");
                var nuevoAdmin = new IdentityUser { UserName = emailAdmin, Email = emailAdmin, EmailConfirmed = true };
                var resultado = await userManager.CreateAsync(nuevoAdmin, adminInitialPassword);
                if (!resultado.Succeeded)
                    throw new InvalidOperationException("No se pudo crear el administrador inicial. Revise la política de contraseñas y el secreto de desarrollo configurado.");
                var roleResult = await userManager.AddToRoleAsync(nuevoAdmin, "Administrador");
                if (!roleResult.Succeeded)
                    throw new InvalidOperationException("No se pudo asignar el rol de administrador a la cuenta inicial.");
            }

            // 4. Categorías
            if (!context.Categorias.Any())
            {
                context.Categorias.AddRange(
                    new Categoria { Nombre = "Bebidas y Licores", Descripcion = "Gaseosas, aguas, energizantes y cervezas" },
                    new Categoria { Nombre = "Snacks y Golosinas", Descripcion = "Papas fritas, galletas, chocolates" },
                    new Categoria { Nombre = "Lácteos y Huevos", Descripcion = "Leche, quesos, yogures" },
                    new Categoria { Nombre = "Abarrotes", Descripcion = "Arroz, azúcar, fideos, aceites y conservas" },
                    new Categoria { Nombre = "Limpieza y Hogar", Descripcion = "Artículos de aseo y limpieza" }
                );
                await context.SaveChangesAsync();
            }

            // 5. Catálogo de Productos
            if (!context.Productos.Any())
            {
                var catBebidas = context.Categorias.First(c => c.Nombre == "Bebidas y Licores");
                var catSnacks = context.Categorias.First(c => c.Nombre == "Snacks y Golosinas");
                var catLacteos = context.Categorias.First(c => c.Nombre == "Lácteos y Huevos");
                var catAbarrotes = context.Categorias.First(c => c.Nombre == "Abarrotes");

                context.Productos.AddRange(
                    new Producto { CodigoBarras = "77501", Nombre = "Coca Cola 500ml", CategoriaId = catBebidas.Id, Costo = 2.20m, Precio = 3.50m, Stock = 50, StockMinimo = 10, Estado = true },
                    new Producto { CodigoBarras = "77502", Nombre = "Inca Kola 500ml", CategoriaId = catBebidas.Id, Costo = 2.20m, Precio = 3.50m, Stock = 45, StockMinimo = 10, Estado = true },
                    new Producto { CodigoBarras = "77503", Nombre = "Agua San Mateo 600ml", CategoriaId = catBebidas.Id, Costo = 1.20m, Precio = 2.00m, Stock = 30, StockMinimo = 10, Estado = true },
                    new Producto { CodigoBarras = "77504", Nombre = "Cerveza Cusqueña 330ml", CategoriaId = catBebidas.Id, Costo = 4.80m, Precio = 7.00m, Stock = 24, StockMinimo = 6, Estado = true },
                    new Producto { CodigoBarras = "77505", Nombre = "Papas Lays Clásicas 150g", CategoriaId = catSnacks.Id, Costo = 4.00m, Precio = 5.80m, Stock = 20, StockMinimo = 5, Estado = true },
                    new Producto { CodigoBarras = "77506", Nombre = "Galletas Oreo x6", CategoriaId = catSnacks.Id, Costo = 2.50m, Precio = 3.80m, Stock = 35, StockMinimo = 8, Estado = true },
                    new Producto { CodigoBarras = "77507", Nombre = "Leche Gloria Azul 400g", CategoriaId = catLacteos.Id, Costo = 3.60m, Precio = 4.50m, Stock = 40, StockMinimo = 12, Estado = true },
                    new Producto { CodigoBarras = "77508", Nombre = "Yogurt Gloria 1L", CategoriaId = catLacteos.Id, Costo = 5.20m, Precio = 7.00m, Stock = 15, StockMinimo = 4, Estado = true },
                    new Producto { CodigoBarras = "77509", Nombre = "Arroz Costeño 1kg", CategoriaId = catAbarrotes.Id, Costo = 4.10m, Precio = 5.20m, Stock = 60, StockMinimo = 15, Estado = true },
                    new Producto { CodigoBarras = "77510", Nombre = "Aceite Primor 1L", CategoriaId = catAbarrotes.Id, Costo = 7.80m, Precio = 9.90m, Stock = 25, StockMinimo = 5, Estado = true }
                );
                await context.SaveChangesAsync();
            }

            // 6. Historial de Ventas Simuladas (Últimos 30 días)
            if (!context.Ventas.Any())
            {
                var random = new Random();
                var productosLista = await context.Productos.ToListAsync();
                var usuarioVendedor = await userManager.FindByEmailAsync(emailAdmin);
                var listaVentas = new List<Venta>();

                DateTime fechaInicio = HoraPeru.Hoy.AddDays(-30);

                for (int dia = 0; dia <= 30; dia++)
                {
                    DateTime fechaActual = fechaInicio.AddDays(dia);
                    int transaccionesDelDia = random.Next(3, 7);

                    for (int t = 0; t < transaccionesDelDia; t++)
                    {
                        var venta = new Venta
                        {
                            Fecha = fechaActual.AddHours(random.Next(9, 20)).AddMinutes(random.Next(0, 59)),
                            UsuarioId = usuarioVendedor.Id,
                            MetodoPago = random.Next(0, 2) == 0 ? "Efectivo" : "QR",
                            Estado = "Activa",
                            Descuento = 0,
                            Detalles = new List<DetalleVenta>()
                        };

                        decimal totalVenta = 0;
                        int cantidadItems = random.Next(1, 4);

                        for (int i = 0; i < cantidadItems; i++)
                        {
                            var prod = productosLista[random.Next(productosLista.Count)];
                            int unidades = random.Next(1, 3);
                            decimal subtotal = unidades * prod.Precio;

                            venta.Detalles.Add(new DetalleVenta
                            {
                                ProductoId = prod.Id,
                                Cantidad = unidades,
                                PrecioUnitario = prod.Precio,
                                SubTotal = subtotal
                            });

                            totalVenta += subtotal;
                        }

                        venta.Total = totalVenta;
                        listaVentas.Add(venta);
                    }
                }

                context.Ventas.AddRange(listaVentas);
                await context.SaveChangesAsync();
            }
        }
    }
}
