using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Models;

namespace MiniMarket.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Producto> Productos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
        public DbSet<Venta> Ventas { get; set; }
        public DbSet<DetalleVenta> DetalleVentas { get; set; }
        public DbSet<Compra> Compras { get; set; }
        public DbSet<DetalleCompra> DetalleCompras { get; set; }
        
        // --- RESTAURADO: Tabla de Caja que faltaba ---
        public DbSet<AperturaCaja> AperturasCaja { get; set; }

        public DbSet<Gasto> Gastos { get; set; }

        // --- NUEVO: Tabla de Configuración ---
        public DbSet<Configuracion> Configuraciones { get; set; }
        public DbSet<UsuarioPerfil> UsuarioPerfiles { get; set; }
        public DbSet<ComprobanteFiscal> ComprobantesFiscales { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<ComprobanteFiscal>().Property(c => c.VentaId).ValueGeneratedNever();
            builder.Entity<ComprobanteFiscal>().HasOne(c => c.Venta).WithOne()
                .HasForeignKey<ComprobanteFiscal>(c => c.VentaId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ComprobanteFiscal>().HasIndex(c => new { c.Ambiente, c.EmpresaRuc, c.Serie, c.Correlativo }).IsUnique();
            builder.Entity<UsuarioPerfil>().HasIndex(p => p.Dni).IsUnique();
            builder.Entity<Venta>().HasIndex(v => v.OperacionId).IsUnique()
                .HasFilter("[OperacionId] IS NOT NULL");
        }
    }
}
