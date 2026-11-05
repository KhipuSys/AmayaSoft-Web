using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniMarket.Web.Data;
using MiniMarket.Web.Services;
using MiniMarket.Web.Models;
using MiniMarket.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;

internal static class FactilizaQaSmoke
{
    // Invocación manual explícita; nunca forma parte de la batería automática.
    public static async Task Ejecutar(int ventaId)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.QA.json"))
            .AddEnvironmentVariables().AddInMemoryCollection(new Dictionary<string,string> {
                ["ASPNETCORE_ENVIRONMENT"]="QA", ["FactilizaSettings:FacturacionHabilitada"]="true"
            }).Build();
        if (string.IsNullOrWhiteSpace(config["FactilizaSettings:QA:Token"]) && string.IsNullOrWhiteSpace(config["FactilizaSettings:Token"]))
            throw new Exception("Configure FactilizaSettings__QA__Token en el entorno.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost\\SQLEXPRESS;Database=MiniMarketDB;Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new ApplicationDbContext(options);
        // Solo el upgrade fiscal aditivo; no ejecuta semillas ni ajustes comerciales.
        await ActualizacionFiscal.Aplicar(db);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false });
        var service = new FacturacionService(db, http, config);
        var fixture = ventaId == -1;
        if (fixture) ventaId = await PrepararFacturaPrueba(db);
        await service.Emitir(ventaId);
        var fiscal = await db.ComprobantesFiscales.AsNoTracking().SingleOrDefaultAsync(c => c.VentaId==ventaId);
        if (fiscal == null) { Console.WriteLine("Sin emisión: " + (await db.Ventas.FindAsync(ventaId)).EstadoEmision); return; }
        Console.WriteLine($"QA venta {ventaId}: {fiscal.Serie}-{fiscal.Correlativo}; {fiscal.Estado}; HTTP {fiscal.HttpStatus}; código {fiscal.Codigo}");
        Console.WriteLine(fiscal.Mensaje);
        if (fiscal.Estado != "Aceptada QA") return;
        foreach (var formato in new[] { "pdf", "xml", "cdr" }) {
            try { var file = await service.Descargar(fiscal, formato); Console.WriteLine($"{formato}: OK, {file.Bytes.Length} bytes"); }
            catch (Exception ex) { Console.WriteLine($"{formato}: {ex.Message}"); }
        }
        if (fixture) {
            await new OperacionVentaService(db).Anular(ventaId, "qa-facturacion@invalid.local", "Prueba técnica QA finalizada; no corresponde a una operación comercial real.");
            await service.Anular(ventaId,"Prueba técnica QA finalizada");
            var updated=await db.ComprobantesFiscales.AsNoTracking().SingleAsync(c=>c.VentaId==ventaId);
            Console.WriteLine($"ANULACION_QA: {updated.EstadoAnulacion}; {updated.MensajeAnulacion}");
            await db.AperturasCaja.Where(c=>c.UsuarioId=="qa-facturacion@invalid.local").ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Estado,false));
            await db.Productos.Where(p=>p.CodigoBarras=="QA-F079-TEST").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Estado,false));
            await db.Categorias.Where(c=>c.Nombre=="Pruebas técnicas QA").ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Estado,false));
            var clienteId = await db.Ventas.Where(v=>v.Id==ventaId).Select(v=>v.ClienteId).SingleAsync();
            await db.Clientes.Where(c=>c.Id==clienteId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Estado,false));
        }
    }

    private static async Task<int> PrepararFacturaPrueba(ApplicationDbContext db)
    {
        var operation = Guid.Parse("738b22ba-b88b-4b5d-b8da-f12f07900001");
        var existing = await db.Ventas.SingleOrDefaultAsync(v=>v.OperacionId==operation);
        if(existing != null) return existing.Id;
        const string user = "qa-facturacion@invalid.local";
        if (!await db.Users.AnyAsync(u=>u.Id=="qa-facturacion")) {
            db.Users.Add(new IdentityUser { Id="qa-facturacion",UserName=user,NormalizedUserName=user.ToUpperInvariant(),Email=user });
        }
        var categoria = new Categoria { Nombre="Pruebas técnicas QA", Estado=true };
        var producto = new Producto { Nombre="PRUEBA QA - Servicio de prueba", Precio=118m, Costo=0m, Stock=1, Estado=true, Categoria=categoria, CodigoBarras="QA-F079-TEST" };
        var cliente = new Cliente { Documento="20131312955", Nombre="SUPERINTENDENCIA NACIONAL DE ADUANAS Y DE ADMINISTRACION TRIBUTARIA - SUNAT", Direccion="AV. GARCILASO DE LA VEGA NRO. 1472, LIMA", Estado=true };
        db.Productos.Add(producto); db.Clientes.Add(cliente);
        db.AperturasCaja.Add(new AperturaCaja { UsuarioId=user, FechaApertura=HoraPeru.Ahora, Estado=true });
        await db.SaveChangesAsync();
        var request = new MiniMarket.Web.Controllers.VentaRequest {
            OperacionId=operation, ClienteId=cliente.Id, MetodoPago="Tarjeta", Descuento=18m, Total=105m,
            Detalles=[new() { ProductoId=producto.Id, Cantidad=1 }]
        };
        var saved = await new OperacionVentaService(db).Registrar(request,user);
        return saved.Venta.Id;
    }
}
