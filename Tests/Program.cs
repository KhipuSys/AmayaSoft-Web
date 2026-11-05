using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiniMarket.Web.Controllers;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using MiniMarket.Web.Infrastructure;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using VentaRequest = MiniMarket.Web.Controllers.VentaRequest;

if (args.Length == 2 && args[0] == "--factiliza-qa-venta") {
    await FactilizaQaSmoke.Ejecutar(int.Parse(args[1]));
    return;
}

var database = "MiniMarket_Test_" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
ApplicationDbContext Db() => new(options);
int productoId = 0, cajaId = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
VentaRequest Request(int quantity = 1, decimal total = 10) => new() {
    OperacionId = Guid.NewGuid(), MetodoPago = "Efectivo", Total = total,
    Detalles = [new() { ProductoId = productoId, Cantidad = quantity, Precio = 10, Total = total }]
};
async Task<(Venta Venta, bool Repetida)> Sell(VentaRequest r) {
    await using var db = Db(); return await new OperacionVentaService(db).Registrar(r, "tester");
}
async Task Reject(VentaRequest r) {
    try { await Sell(r); } catch (InvalidOperationException) { return; }
    throw new Exception("Se aceptó una solicitud inválida.");
}
async Task Stock(int value) { await using var db = Db(); await db.Productos.Where(p => p.Id == productoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, value)); }
ControllerContext UserContext() => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
    new Claim(ClaimTypes.NameIdentifier, "tester-id"), new Claim(ClaimTypes.Name, "tester"),
    new Claim(ClaimTypes.Role, "Administrador")], "test")) } };
SignInManager<IdentityUser> TestSignInManager(ApplicationDbContext db) {
    var options=Options.Create(new IdentityOptions());
    options.Value.Lockout.MaxFailedAccessAttempts=5;
    options.Value.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);
    var userManager=new UserManager<IdentityUser>(new UserStore<IdentityUser>(db),options,new PasswordHasher<IdentityUser>(),
        [],[],new UpperInvariantLookupNormalizer(),new IdentityErrorDescriber(),null,NullLogger<UserManager<IdentityUser>>.Instance);
    return new SignInManager<IdentityUser>(userManager,new HttpContextAccessor(),new UserClaimsPrincipalFactory<IdentityUser>(userManager,options),
        options,NullLogger<SignInManager<IdentityUser>>.Instance,new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())),
        new DefaultUserConfirmation<IdentityUser>());
}

try {
    await using (var db=Db()) {
        var seeder=new SeedDataController(db,null!,new TestHostEnvironment("Production"));
        Assert(seeder.Index() is NotFoundResult && await seeder.Generar() is NotFoundResult,
            "La herramienta de datos de demostración está disponible fuera de Development");
    }
    Console.WriteLine("PASS: el sembrador de datos de demostración está bloqueado en Production");

    await using (var db = Db()) {
        await ActualizacionComercial.Aplicar(db);
        db.Users.Add(new IdentityUser { Id="tester-id", UserName="tester", NormalizedUserName="TESTER" });
        var p = new Producto { Nombre="Prueba", Precio=10, Costo=4, Stock=10, Estado=true, Categoria=new Categoria { Nombre="Test", Estado=true } };
        db.Productos.Add(p);
        var caja = new AperturaCaja { UsuarioId="tester", Estado=true, FechaApertura=HoraPeru.Ahora.AddHours(-1) };
        db.AperturasCaja.Add(caja);
        await db.SaveChangesAsync(); productoId=p.Id; cajaId=caja.Id;
        // Simula columnas ausentes de una instalación anterior sin perder sus filas.
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX IX_Ventas_OperacionId ON Ventas;
            ALTER TABLE Ventas DROP COLUMN OperacionId, HuellaSolicitud, AperturaCajaId, EstadoEmision, FechaAnulacion, AnuladaPor, MotivoAnulacion, AjustePosteriorCierre;
            ALTER TABLE DetalleVentas DROP COLUMN CostoUnitarioHistorico;
            ALTER TABLE AperturasCaja DROP COLUMN VentasEfectivo, AjustesAnulaciones;
            DROP TABLE UsuarioPerfiles;
            """);
        await ActualizacionComercial.Aplicar(db);
        await ActualizacionComercial.Aplicar(db);
        Assert(await db.Productos.CountAsync() == 1, "Upgrade perdió datos");
    }
    Console.WriteLine("PASS: esquema antiguo actualizado de forma repetible sin perder datos");

    await Reject(Request(-1, -10)); await Reject(Request(0,0)); await Reject(Request(1,1));
    var discount = Request(); discount.Descuento=-1; await Reject(discount);
    discount = Request(); discount.Descuento=11; await Reject(discount);
    discount = Request(total: 6); discount.Descuento=4; await Reject(discount);
    var adminDiscount = Request(total: 6); adminDiscount.Descuento=4; await using (var db = Db()) await new OperacionVentaService(db).Registrar(adminDiscount, "tester", true);
    discount = Request(total: 0); discount.Descuento=10; await Reject(discount);
    Console.WriteLine("PASS: rechazo de cantidades, descuentos e importes inválidos");

    var forged = Request(); forged.Detalles[0].Precio=0.01m; forged.Detalles[0].Total=0.01m;
    var saved = await Sell(forged);
    await using (var db = Db()) {
        var d = await db.DetalleVentas.SingleAsync(d => d.VentaId == saved.Venta.Id);
        Assert(d.PrecioUnitario == 10 && d.SubTotal == 10 && d.CostoUnitarioHistorico == 4, "Importes no autoritativos");
        await db.Productos.Where(p => p.Id == productoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Costo, 8));
        var report = new ReportesController(db);
        var view = (ViewResult)await report.Ventas(null,null);
        var model = (IEnumerable<MiniMarket.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(model.Single(v => v.VentaId == saved.Venta.Id).CostoTotal == 4, "Costo histórico cambió");
    }
    Console.WriteLine("PASS: precio del servidor y costo histórico estable al cambiar producto");

    await Stock(10);
    var duplicate = Request();
    var two = await Task.WhenAll(Sell(duplicate), Sell(duplicate));
    Assert(two[0].Venta.Id == two[1].Venta.Id && two.Count(v=>v.Repetida)==1, "Duplicación de venta");
    await using (var db = Db()) Assert((await db.Productos.FindAsync(productoId)).Stock == 9, "Descuento doble");
    duplicate.Total=9; await Reject(duplicate);
    Console.WriteLine("PASS: solicitudes simultáneas con mismo ID registran una sola venta; se rechaza reutilización distinta");

    await Stock(1);
    async Task<bool> TrySell() { try { await Sell(Request()); return true; } catch (InvalidOperationException) { return false; } }
    var lastUnit = await Task.WhenAll(TrySell(), TrySell());
    Assert(lastUnit.Count(x=>x)==1, "Dos ventas de última unidad");
    await using (var db = Db()) Assert((await db.Productos.FindAsync(productoId)).Stock == 0, "Stock final incorrecto");
    Console.WriteLine("PASS: dos operaciones concurrentes compiten por última unidad; solo una se confirma");

    await Stock(5);
    int count;
    await using (var db = Db()) {
        count=await db.Ventas.CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER Test_FailMovement ON MovimientosInventario AFTER INSERT AS THROW 51010, 'Fallo simulado', 1;");
    }
    bool rolledBack=false;
    try { await Sell(Request()); } catch { rolledBack=true; }
    await using (var db = Db()) {
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER Test_FailMovement");
        Assert(rolledBack && await db.Ventas.CountAsync()==count && (await db.Productos.FindAsync(productoId)).Stock==5, "Rollback incompleto");
    }
    Console.WriteLine("PASS: fallo después de guardar cabecera revierte venta, detalles y stock");

    var anulable = await Sell(Request());
    async Task VoidSale() { await using var db=Db(); await new OperacionVentaService(db).Anular(anulable.Venta.Id,"tester","Prueba concurrente"); }
    await Task.WhenAll(VoidSale(),VoidSale());
    await using (var db=Db()) {
        Assert((await db.Productos.FindAsync(productoId)).Stock==5,"Doble reposición");
        Assert(await db.MovimientosInventario.CountAsync(m=>m.Referencia==$"Anulación Venta #{anulable.Venta.Id}")==1,"Doble movimiento");
    }
    Console.WriteLine("PASS: anulación concurrente restaura stock una vez");

    var closeSale = await Sell(Request());
    var cardRequest = Request(total: 10.50m); cardRequest.MetodoPago="Tarjeta"; await Sell(cardRequest);
    decimal closingTotal;
    await using(var db=Db()) {
        var controller=new CajaController(db) { ControllerContext=UserContext() };
        var view=(ViewResult)await controller.Index();
        var box=(AperturaCaja)view.Model;
        Assert(box.TotalVentas==await db.Ventas.Where(v=>v.AperturaCajaId==cajaId && v.Estado=="Activa").SumAsync(v=>v.Total), "Caja suma anuladas");
        Assert(box.VentasEfectivo==box.TotalVentas-10.50m,"Caja incluye tarjeta como efectivo");
        await controller.Cerrar(20); closingTotal=box.TotalVentas;
    }
    await using(var db=Db()) await new OperacionVentaService(db).Anular(closeSale.Venta.Id,"tester","Después del cierre");
    await using(var db=Db()) {
        var box=await db.AperturasCaja.FindAsync(cajaId);
        Assert(box.TotalVentas==closingTotal && box.MontoCierre==20 && box.AjustesAnulaciones==10,"Se alteró cierre o falta ajuste");
    }
    var replay=await Sell(forged);
    Assert(replay.Repetida && replay.Venta.Id==saved.Venta.Id,"Reintento tras cierre no recuperó venta");
    await Reject(Request());
    Console.WriteLine("PASS: caja excluye anuladas, distingue efectivo y conserva cierres con ajuste posterior; reintentos recuperables tras cierre");

    await using(var db=Db()) {
        var box = await db.AperturasCaja.FindAsync(cajaId); box.Estado=true; box.FechaCierre=null; await db.SaveChangesAsync();
    }
    var handler=new RejectingHandler();
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> {
        ["ASPNETCORE_ENVIRONMENT"]="QA",
        ["FactilizaSettings:Ambiente"]="QA", ["FactilizaSettings:QA:Token"]="test", ["FactilizaSettings:ConsultaToken"]="consulta-test",
        ["FactilizaSettings:FacturacionHabilitada"]="true",
        ["FactilizaSettings:QA:EmpresaRuc"]="00000000000", ["FactilizaSettings:QA:BoletaSerie"]="B079",
        ["FactilizaSettings:QA:FacturaSerie"]="F079", ["FactilizaSettings:QA:BoletaVentaIdInicial"]="1",
        ["FactilizaSettings:QA:BoletaCorrelativoInicial"]="1", ["FactilizaSettings:QA:FacturaVentaIdInicial"]="1",
        ["FactilizaSettings:QA:FacturaCorrelativoInicial"]="1"
    }).Build();
    await using(var db=Db()) {
        var external=new FactilizaService(new HttpClient(handler),configuration);
        var controller=new VentasController(db,external,NullLogger<VentasController>.Instance) { ControllerContext=UserContext() };
        var request=Request();
        await controller.Registrar(request); await controller.Registrar(request);
        var v=await db.Ventas.AsNoTracking().SingleAsync(v=>v.OperacionId==request.OperacionId);
        Assert(v.EstadoEmision=="Pendiente" && handler.Calls==0 &&
            !await db.ComprobantesFiscales.AnyAsync(c=>c.VentaId==v.Id),
            $"Registrar emitió automáticamente: {v.EstadoEmision}, llamadas {handler.Calls}");
    }
    var emitirAuth=typeof(VentasController).GetMethod(nameof(VentasController.EmitirComprobante))!
        .GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>();
    Assert(emitirAuth.Any(a=>a.Roles?.Split(',').Select(r=>r.Trim()).Contains("Administrador")==true),
        "EmitirComprobante no exige el rol Administrador");
    Console.WriteLine("PASS: registrar deja pendiente sin llamada externa y emitir exige administrador");

    var productionHandler=new RejectingHandler();
    var blockedProductionConfiguration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> {
        ["ASPNETCORE_ENVIRONMENT"]="Production",
        ["FactilizaSettings:Ambiente"]="Produccion",
        ["FactilizaSettings:FacturacionHabilitada"]="true",
        ["FactilizaSettings:Produccion:SeriesConfirmadas"]="false"
    }).Build();
    await using(var db=Db()) {
        Assert(!new FacturacionService(db,new HttpClient(productionHandler),blockedProductionConfiguration).Habilitada,
            "Producción quedó activa sin confirmar las series");
    }
    var missingProductionToken=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> {
        ["ASPNETCORE_ENVIRONMENT"]="Production", ["FactilizaSettings:Ambiente"]="Produccion",
        ["FactilizaSettings:FacturacionHabilitada"]="true", ["FactilizaSettings:Produccion:SeriesConfirmadas"]="true",
        ["FactilizaSettings:Token"]="qa-legacy-must-not-be-used",
        ["FactilizaSettings:Produccion:EmpresaRuc"]="00000000000", ["FactilizaSettings:Produccion:BoletaSerie"]="B002",
        ["FactilizaSettings:Produccion:FacturaSerie"]="F002", ["FactilizaSettings:Produccion:BoletaCorrelativoInicial"]="1",
        ["FactilizaSettings:Produccion:FacturaCorrelativoInicial"]="1"
    }).Build();
    await using(var db=Db()) {
        Assert(!new FacturacionService(db,new HttpClient(productionHandler),missingProductionToken).Habilitada,
            "Producción reutilizó un token QA/legado o quedó activa sin token propio");
    }
    var productionConfiguration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> {
        ["ASPNETCORE_ENVIRONMENT"]="Production",
        ["FactilizaSettings:Ambiente"]="Produccion",
        ["FactilizaSettings:FacturacionHabilitada"]="true",
        ["FactilizaSettings:Produccion:SeriesConfirmadas"]="true",
        ["FactilizaSettings:Produccion:Token"]="production-test",
        ["FactilizaSettings:Produccion:EmpresaRuc"]="00000000000",
        ["FactilizaSettings:Produccion:BoletaSerie"]="B002",
        ["FactilizaSettings:Produccion:FacturaSerie"]="F002",
        ["FactilizaSettings:Produccion:BoletaCorrelativoInicial"]="1",
        ["FactilizaSettings:Produccion:FacturaCorrelativoInicial"]="1"
    }).Build();
    await using(var db=Db()) {
        var venta=await new OperacionVentaService(db).Registrar(Request(),"tester");
        var productionService=new FacturacionService(db,new HttpClient(productionHandler),productionConfiguration);
        await productionService.Emitir(venta.Venta.Id);
        var fiscal=await db.ComprobantesFiscales.AsNoTracking().SingleAsync(c=>c.VentaId==venta.Venta.Id);
        Assert(productionHandler.Calls==1 && productionHandler.Url=="https://apife.factiliza.com/api/v1/invoice/send" &&
            fiscal.Ambiente=="Produccion" && fiscal.Serie=="B002" && fiscal.Correlativo==1,
            "La emisión de producción no usa el ambiente, URL o numeración configurados");
        await productionService.Emitir(venta.Venta.Id);
        Assert(productionHandler.Calls==1,"Se reenvió un resultado incierto de producción");

        var cliente=new Cliente { Documento="00000000001",Nombre="EMPRESA DE PRUEBA",Direccion="DIRECCION DE PRUEBA",Estado=true };
        db.Clientes.Add(cliente); await db.SaveChangesAsync();
        var facturaRequest=Request(); facturaRequest.ClienteId=cliente.Id;
        var factura=await new OperacionVentaService(db).Registrar(facturaRequest,"tester");
        await new FacturacionService(db,new HttpClient(productionHandler),productionConfiguration).Emitir(factura.Venta.Id);
        var fiscalFactura=await db.ComprobantesFiscales.AsNoTracking().SingleAsync(c=>c.VentaId==factura.Venta.Id);
        Assert(productionHandler.Calls==2 && fiscalFactura.Serie=="F002" && fiscalFactura.Correlativo==1,
            "La primera factura de producción no inició en F002-00000001");

        var admin=await db.Users.FindAsync("tester-id");
        admin.PasswordHash=new PasswordHasher<IdentityUser>().HashPassword(admin,"Clave-Segura-2026!");
        admin.LockoutEnabled=true; await db.SaveChangesAsync();
        await db.Productos.Where(p=>p.Id==productoId).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Stock,10));
        db.ChangeTracker.Clear();
        var signIn=TestSignInManager(db);
        var ventaAutorizada=await new OperacionVentaService(db).Registrar(Request(),"tester");
        var controller=new VentasController(db,new FactilizaService(new HttpClient(productionHandler),productionConfiguration),
            NullLogger<VentasController>.Instance,signIn) { ControllerContext=UserContext() };
        controller.TempData=new TempDataDictionary(controller.HttpContext,new MemoryTempDataProvider());
        var llamadasAntes=productionHandler.Calls;
        await controller.EmitirComprobante(ventaAutorizada.Venta.Id,"incorrecta");
        Assert(productionHandler.Calls==llamadasAntes,"Una contraseña incorrecta permitió emitir en producción");
        await controller.EmitirComprobante(ventaAutorizada.Venta.Id,"Clave-Segura-2026!");
        Assert(productionHandler.Calls==llamadasAntes+1,"La reautenticación correcta no permitió emitir en producción");

        var ventaBloqueada=await new OperacionVentaService(db).Registrar(Request(),"tester");
        for(var intento=0;intento<5;intento++) await controller.EmitirComprobante(ventaBloqueada.Venta.Id,"incorrecta");
        admin=await signIn.UserManager.FindByIdAsync("tester-id");
        Assert(await signIn.UserManager.IsLockedOutAsync(admin),"Los intentos fallidos no bloquearon al administrador");
        await controller.EmitirComprobante(ventaBloqueada.Venta.Id,"Clave-Segura-2026!");
        Assert(productionHandler.Calls==llamadasAntes+1,"Un administrador bloqueado pudo emitir");
        await signIn.UserManager.SetLockoutEndDateAsync(admin,null); await signIn.UserManager.ResetAccessFailedCountAsync(admin);
    }
    Console.WriteLine("PASS: producción usa URL oficial y numeración independiente desde 1");
    var dniHandler = new DniHandler();
    var dniService = new FactilizaService(new HttpClient(dniHandler), configuration);
    var dniResult = await dniService.ConsultarDocumentoAsync("82000001");
    Assert(dniResult.Exito && dniResult.Nombres == "ANA MARIA" && dniResult.Apellidos == "PRUEBA DEMO" && dniResult.Nombre == "ANA MARIA PRUEBA DEMO" &&
        dniHandler.Url == "https://api.factiliza.com/pe/v1/dni/info/82000001" && dniHandler.Bearer == "consulta-test", "Ruta, token o datos estructurados de DNI incorrectos");
    var unauthorized = new FactilizaService(new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized)), configuration);
    var unauthorizedResult = await unauthorized.ConsultarDocumentoAsync("82000001");
    Assert(!unauthorizedResult.Exito && unauthorizedResult.Mensaje.Contains("token de consulta"), "Error 401 de consulta mal diagnosticado");
    var missingDni = new FactilizaService(new HttpClient(new StatusHandler(HttpStatusCode.NotFound)), configuration);
    var missingResult = await missingDni.ConsultarDocumentoAsync("82000001");
    Assert(!missingResult.Exito && missingResult.Mensaje.Contains("No se encontró"), "Error 404 de documento mal diagnosticado");
    var rejectedDni = new FactilizaService(new HttpClient(new JsonStatusHandler(HttpStatusCode.OK,
        """{"success":false,"data":{"nombre_completo":"NO CONFIABLE"}}""")), configuration);
    Assert(!(await rejectedDni.ConsultarDocumentoAsync("82000001")).Exito, "Se aceptó una consulta rechazada con HTTP 200");
    var onlyBilling = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> {
        ["FactilizaSettings:Token"]="facturacion-prueba"
    }).Build();
    var noConsultaHandler = new DniHandler();
    var noConsulta = await new FactilizaService(new HttpClient(noConsultaHandler), onlyBilling)
        .ConsultarDocumentoAsync("82000001");
    Assert(!noConsulta.Exito && noConsulta.Mensaje.Contains("No se configuró el token de consulta") &&
        noConsultaHandler.Url == null, "Se usó el token de facturación para consultar DNI");
    Console.WriteLine("PASS: proveedor DNI simulado devuelve nombres y apellidos separados");
    Assert(HoraPeru.DesdeInstante(new DateTimeOffset(2026,9,18,1,29,0,TimeSpan.Zero)) == new DateTime(2026,9,17,20,29,0), "Cambio de día UTC a Perú incorrecto");
    Assert(HoraPeru.DesdeInstante(new DateTimeOffset(2026,9,18,5,0,0,TimeSpan.Zero)) == new DateTime(2026,9,18,0,0,0), "Medianoche peruana incorrecta");
    await using(var db=Db()) {
        // Fechas de servidor europeo (UTC+2 en septiembre), como las del hosting antes de corregirlo.
        var legacy = new Venta { UsuarioId="tester-id", Fecha=new DateTime(2026,9,18,1,29,0), Total=10, FechaAnulacion=new DateTime(2026,9,18,2,0,0) };
        var historicalBox=new AperturaCaja { UsuarioId="tester", FechaApertura=new DateTime(2026,9,18,1,0,0), FechaCierre=null, Estado=false };
        db.Ventas.Add(legacy); db.AperturasCaja.Add(historicalBox); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AjustesHorario WHERE Nombre='PeruV1'; DELETE FROM RespaldoFechasPeru;");
        await ActualizacionComercial.Aplicar(db,"W. Europe Standard Time");
        await db.Entry(legacy).ReloadAsync(); await db.Entry(historicalBox).ReloadAsync();
        Assert(legacy.Fecha==new DateTime(2026,9,17,18,29,0) && legacy.FechaAnulacion==new DateTime(2026,9,17,19,0,0), "No se convirtió historial a Perú");
        Assert(historicalBox.FechaApertura==new DateTime(2026,9,17,18,0,0) && historicalBox.FechaCierre==null, "Conversión de caja incorrecta");
        Assert(await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM RespaldoFechasPeru WHERE Tabla='Ventas' AND RegistroId={legacy.Id} AND Campo='Fecha'").SingleAsync()==1,"Falta respaldo de fecha original");
        await ActualizacionComercial.Aplicar(db,"W. Europe Standard Time");
        await db.Entry(legacy).ReloadAsync(); Assert(legacy.Fecha==new DateTime(2026,9,17,18,29,0),"La fecha se convirtió dos veces");
        var report=new ReportesController(db);
        var view=(ViewResult)await report.Ventas(new DateTime(2026,9,17),new DateTime(2026,9,17));
        var list=(IEnumerable<MiniMarket.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(list.Any(v=>v.VentaId==legacy.Id),"El filtro diario de Perú excluyó una venta convertida");
        view=(ViewResult)await report.Ventas(new DateTime(2026,9,18),new DateTime(2026,9,18));
        list=(IEnumerable<MiniMarket.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(!list.Any(v=>v.VentaId==legacy.Id),"El filtro del día siguiente incluyó la venta anterior");
    }
    Console.WriteLine("PASS: hora Perú, medianoche, conversión histórica con respaldo, idempotencia y filtros diarios");
    var fechaReferencia = new DateTime(2026, 9, 21);
    Assert(PeriodoAsistente.Interpretar("ventas del 1 al 15 de septiembre", fechaReferencia) == new PeriodoAsistente(new(2026,9,1), new(2026,9,16)), "Rango español");
    Assert(PeriodoAsistente.Interpretar("ventas de agosto 2026", fechaReferencia) == new PeriodoAsistente(new(2026,8,1), new(2026,9,1)), "Mes explícito");
    Assert(PeriodoAsistente.Interpretar("ventas de la semana pasada", fechaReferencia) == new PeriodoAsistente(new(2026,9,14), new(2026,9,21)), "Semana lunes-domingo");
    Assert(PeriodoAsistente.Interpretar("ventas últimos 7 días", fechaReferencia).Inicio == new DateTime(2026,9,15), "Últimos días inclusivos");
    Assert(PeriodoAsistente.Interpretar("ventas 2024-02-29", fechaReferencia).Inicio == new DateTime(2024,2,29), "Año bisiesto ISO");
    foreach(var invalida in new[] { "ventas 31/02/2026", "ventas del 21/09/2026 al 01/09/2026", "ventas últimos 0 días" }) {
        var rechazo = false;
        try { PeriodoAsistente.Interpretar(invalida, fechaReferencia); } catch(ArgumentException) { rechazo = true; }
        Assert(rechazo, "Se aceptó período inválido: " + invalida);
    }
    await using(var db=Db()) {
        db.Users.Add(new IdentityUser { Id="otro-id", UserName="otro", NormalizedUserName="OTRO" });
        db.UsuarioPerfiles.Add(new UsuarioPerfil { UsuarioId="otro-id", Dni="12345678", Nombres="Jesus Angel", Apellidos="Campos Landa" });
        var fecha=new DateTime(2024,2,29,12,0,0);
        var p=new Producto { Nombre="Producto IA", CodigoBarras="TESTIA", Precio=10, Costo=4, Stock=0, Estado=true, CategoriaId=(await db.Categorias.FirstAsync()).Id };
        db.Productos.Add(p);
        db.Ventas.AddRange(
            new Venta { UsuarioId="tester-id", Fecha=fecha, Total=18, Descuento=2, MetodoPago="Yape", Detalles=[new DetalleVenta { Producto=p, Cantidad=2, PrecioUnitario=10, SubTotal=20, CostoUnitarioHistorico=4 }] },
            new Venta { UsuarioId="otro-id", Fecha=fecha, Total=40 },
            new Venta { UsuarioId="tester-id", Fecha=fecha, Total=100, Estado="Anulada" },
            new Venta { UsuarioId="tester-id", Fecha=fecha.Date.AddDays(1), Total=500 });
        await db.SaveChangesAsync();
        var ia=new IAAsistenteService(db);
        var propia=await ia.PreguntarAlAsistenteAsync("cuanto se vendio el 29 de febrero de 2024", "tester", false);
        Assert(propia.Contains("18.00") && !propia.Contains("58.00"), "IA filtrado cajero/fecha/anuladas");
        var global=await ia.PreguntarAlAsistenteAsync("ventas 29/02/2024", "tester", true);
        Assert(global.Contains("58.00") && global.Contains("2 operaciones"), "IA ventas globales/límite exclusivo");
        var ganancia=await ia.PreguntarAlAsistenteAsync("ganancia estimada 29/02/2024", "tester", true);
        Assert(ganancia.Contains("10.00") && ganancia.Contains("Tickets excluidos por falta de costo/detalle: 1"), "IA margen descuenta descuento, usa costo histórico y detecta faltantes");
        Assert((await ia.PreguntarAlAsistenteAsync("ganancia estimada", "tester", false)).Contains("reservada"), "IA no revela márgenes a cajero");
        Assert((await ia.PreguntarAlAsistenteAsync("ventas del cajero \"otro\"", "tester", false)).Contains("reservada"), "IA no amplía rol por filtro");
        var filtroCajero=await ia.PreguntarAlAsistenteAsync("ventas del cajero \"Jesus Campos\" 29/02/2024", "tester", true);
        Assert(filtroCajero.Contains("40.00") && filtroCajero.Contains("Jesus Angel Campos Landa"), "IA primer nombre/apellido filtra cajero");
        var top=await ia.PreguntarAlAsistenteAsync("producto mas vendido 29/02/2024", "tester", true);
        Assert(top.Contains("Producto IA") && top.Contains("2 unidades"), "IA ranking respeta período");
        Assert((await ia.PreguntarAlAsistenteAsync("cuantos proovedores tengo", "tester", true)).Contains("Proveedores registrados"), "IA tolera error conocido");
        var compuesto=await ia.PreguntarAlAsistenteAsync("cuantos productos tengo? hay alguno bajo en stock?", "tester", true);
        Assert(compuesto.Contains("Productos registrados") && compuesto.Contains("Stock actual"), "IA multiintención");
        var resumen=await ia.PreguntarAlAsistenteAsync("resumen del 29/02/2024", "tester", true);
        Assert(resumen.Contains("58.00") && resumen.Contains("Yape") && resumen.Contains("Gastos:"), "IA resumen SQL");
        Assert((await ia.PreguntarAlAsistenteAsync("productos sin movimiento 29/02/2024", "tester", true)).Contains("Sin ventas en el período"), "IA productos sin movimiento");
        Assert((await ia.PreguntarAlAsistenteAsync("inventario valorizado", "tester", true)).Contains("Inventario actual al costo"), "IA valorización SQL");
        Assert((await ia.PreguntarAlAsistenteAsync("compras 29/02/2024", "tester", true)).Contains("Compras:"), "IA compras SQL");
        Assert((await ia.PreguntarAlAsistenteAsync("comprobantes 29/02/2024", "tester", true)).Contains("no acreditan aceptación"), "IA no confunde estado API con SUNAT");
        Assert((await ia.PreguntarAlAsistenteAsync("alertas del negocio 29/02/2024", "tester", true)).Contains("Revisión comercial"), "IA alertas comerciales SQL");
        Assert((await ia.PreguntarAlAsistenteAsync("hola", "tester", true)).StartsWith("¡Hola!"), "IA saludo local");
    }
    Console.WriteLine("PASS: asistente: períodos, permisos, SQL de reportes, margen histórico, multiintención y datos ausentes");
    await FacturacionQaTests.Ejecutar(Db, configuration, productoId);
    Console.WriteLine("TODAS LAS PRUEBAS PASARON");
} finally {
    await using var db=Db(); await db.Database.EnsureDeletedAsync();
}

class RejectingHandler : HttpMessageHandler {
 public int Calls;
 public string Url;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
    Calls++; Url=request.RequestUri?.ToString(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
 }
}
class DniHandler : HttpMessageHandler {
 public string Url;
 public string Bearer;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
     Url=request.RequestUri.ToString(); Bearer=request.Headers.Authorization?.Parameter;
     return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"nombres":"ANA MARIA","apellido_paterno":"PRUEBA","apellido_materno":"DEMO","nombre_completo":"ANA MARIA PRUEBA DEMO"}}""") });
 }
}
class StatusHandler(HttpStatusCode status) : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
     Task.FromResult(new HttpResponseMessage(status));
}
class JsonStatusHandler(HttpStatusCode status, string json) : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
     Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
}
class MemoryTempDataProvider : ITempDataProvider {
    private Dictionary<string,object> data=[];
    public IDictionary<string,object> LoadTempData(HttpContext context) => new Dictionary<string,object>(data);
    public void SaveTempData(HttpContext context, IDictionary<string,object> values) => data=new(values);
}
class TestHostEnvironment(string environmentName) : IHostEnvironment {
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "MiniMarket.IntegrationTests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
