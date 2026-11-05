using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Infrastructure;
using MiniMarket.Web.Services;
using System.Net;
using System.Text.Json;

internal static class FacturacionQaTests
{
    public static async Task Ejecutar(Func<ApplicationDbContext> createDb, IConfiguration config, int productoId)
    {
        void Check(bool ok,string error) { if(!ok) throw new Exception(error); }
        var venta = new Venta { Id=99, Fecha=HoraPeru.Ahora, MetodoPago="Tarjeta", Descuento=18m, Total=105m,
            Cliente=new Cliente { Documento="00000000001", Nombre="EMPRESA QA", Direccion="DIRECCION DE PRUEBA" },
            Detalles=[new() { ProductoId=1, Producto=new Producto { Nombre="Prueba" }, Cantidad=1, PrecioUnitario=118m, SubTotal=118m }] };
        var fiscal = new ComprobanteFiscal { EmpresaRuc="00000000000", Tipo="01", Serie="F079", Correlativo=1 };
        using(var json=JsonDocument.Parse(ComprobantePayload.Crear(venta,fiscal,18m))) {
            var p=json.RootElement; var d=p.GetProperty("detalle");
            Check(d.GetArrayLength()==2 && d[0].GetProperty("monto_Precio_Unitario").GetDecimal()==100m &&
                d[1].GetProperty("monto_Precio_Unitario").GetDecimal()==5m && p.GetProperty("monto_Imp_Venta").GetDecimal()==105m,"Descuento + recargo fiscal incorrectos");
            Check(d.EnumerateArray().Sum(x=>x.GetProperty("monto_Base_Igv").GetDecimal())==p.GetProperty("monto_Oper_Gravadas").GetDecimal(),"Bases no cuadran");
            Check(d.EnumerateArray().Sum(x=>x.GetProperty("igv").GetDecimal())==p.GetProperty("monto_Igv").GetDecimal(),"IGV no cuadra");
            Check(p.GetProperty("legend")[0].GetProperty("legend_Value").GetString()=="SON CIENTO CINCO CON 00/100 SOLES","Leyenda incorrecta");
        }
        venta.MetodoPago="Efectivo"; venta.Descuento=0; venta.Total=.03m;
        venta.Detalles=[new() { ProductoId=1, Producto=new Producto { Nombre="Céntimos" },Cantidad=3,PrecioUnitario=.01m,SubTotal=.03m }];
        using(var json=JsonDocument.Parse(ComprobantePayload.Crear(venta,fiscal,18m))) {
            var p=json.RootElement;
            Check(p.GetProperty("monto_Oper_Gravadas").GetDecimal()+p.GetProperty("monto_Igv").GetDecimal()==.03m,"Redondeo fiscal incorrecto");
        }
        var handler = new FiscalHandler();
        await using(var db=createDb()) {
            var api = new FacturacionService(db,new HttpClient(handler),config);
            handler.Response="""{"success":false,"message":"Documento rechazado","data":{"error":{"code":"3027","message":"Falta leyenda"}}}""";
            var error = await api.Enviar("{}");
            Check(error.Estado=="Rechazada QA" && error.Codigo=="3027" && error.Mensaje.Contains("Falta leyenda"),"Error fiscal oculto");
            handler.Response="""{"success":true}""";
            Check((await api.Enviar("{}")).Estado=="Resultado incierto QA","200 sin CDR aceptado");
        }
        handler.Response="""{"success":true,"data":{"hash":"hash-prueba","sunatResponse":{"success":true,"cdrResponse":{"code":"0"},"cdrZip":"UEs="}}}""";
        int id1,id2,id3;
        await using(var db=createDb()) {
            var producto=await db.Productos.FindAsync(productoId);
            Venta NewSale() => new() { UsuarioId="tester-id",Total=10,MetodoPago="Efectivo",Detalles=[new() { ProductoId=productoId,PrecioUnitario=10,SubTotal=10,Cantidad=1 }] };
            var a=NewSale(); var b=NewSale(); var c=NewSale(); db.AddRange(a,b,c); await db.SaveChangesAsync(); id1=a.Id;id2=b.Id;id3=c.Id;
        }
        await using(var db=createDb()) {
            var disabled = new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string> {
                ["ASPNETCORE_ENVIRONMENT"]="Production"
            }).Build();
            handler.Calls=0;
            await new FacturacionService(db,new HttpClient(handler),disabled).Emitir(id3);
            Check(handler.Calls==0 && !(await db.ComprobantesFiscales.AnyAsync(c=>c.VentaId==id3)) &&
                (await db.Ventas.AsNoTracking().SingleAsync(v=>v.Id==id3)).EstadoEmision=="Pendiente","Envío QA activado en producción");
            var invalid = new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string> {
                ["FactilizaSettings:QA:Token"]=""
            }).Build();
            await new FacturacionService(db,new HttpClient(handler),invalid).Emitir(id3);
            Check(handler.Calls==0 && (await db.Ventas.AsNoTracking().SingleAsync(v=>v.Id==id3)).EstadoEmision=="Pendiente",
                "La facturación quedó habilitada sin token");
            var cliente=new Cliente { Documento="00000000001",Nombre="CLIENTE QA",Estado=true };
            db.Clientes.Add(cliente);
            var pending=await db.Ventas.FindAsync(id3); pending.Cliente=cliente; await db.SaveChangesAsync();
            var api=new FacturacionService(db,new HttpClient(handler),config);
            await api.Emitir(id3);
            var reserved=await db.ComprobantesFiscales.FindAsync(id3);
            Check(handler.Calls==0 && reserved.Estado=="No enviada (datos)","Factura sin dirección enviada");
            var numero=reserved.Correlativo;
            cliente.Direccion="LIMA"; await db.SaveChangesAsync();
            await api.Emitir(id3);
            Check(handler.Calls==1 && reserved.Estado=="Aceptada QA" && reserved.Correlativo==numero,"Corrección de datos perdió el número reservado");
        }
        async Task Send(int id) { await using var db=createDb(); await new FacturacionService(db,new HttpClient(handler),config).Emitir(id); }
        handler.Calls=0;
        await Task.WhenAll(Send(id1),Send(id1),Send(id2));
        await using(var db=createDb()) {
            var records=await db.ComprobantesFiscales.Where(c=>c.VentaId==id1 || c.VentaId==id2).ToListAsync();
            Check(records.Count==2 && records.Select(c=>c.Correlativo).Distinct().Count()==2 && handler.Calls==2,"Emisión duplicada/numeración concurrente incorrecta");
            Check(records.All(c=>c.Estado=="Aceptada QA" && c.CdrZip=="UEs="),"Respuesta fiscal no persistida");
            await db.Ventas.Where(v=>v.Id==id1).ExecuteUpdateAsync(s=>s.SetProperty(v=>v.Estado,"Anulada"));
        }
        async Task Void() { await using var db=createDb(); await new FacturacionService(db,new HttpClient(handler),config).Anular(id1,"Prueba QA"); }
        handler.Calls=0; await Task.WhenAll(Void(),Void());
        Check(handler.Calls==1 && handler.LastPath.EndsWith("summary/cancel"),"Doble anulación externa");
        await using(var db=createDb()) {
            Check((await db.ComprobantesFiscales.FindAsync(id1)).EstadoAnulacion=="Anulada QA","Anulación no persistida");
            var controller=new MiniMarket.Web.Controllers.VentasController(db,new FactilizaService(new HttpClient(handler),config),Microsoft.Extensions.Logging.Abstractions.NullLogger<MiniMarket.Web.Controllers.VentasController>.Instance) {
                ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext() }
            };
            Check(await controller.Comprobante(id1) is Microsoft.AspNetCore.Mvc.ForbidResult,"Comprobante ajeno visible");
            Check(await controller.ArchivoComprobante(id1,"xml") is Microsoft.AspNetCore.Mvc.ForbidResult,"Archivo fiscal ajeno visible");
        }
        Console.WriteLine("PASS: fiscal QA persistente, importes con descuento/recargo, errores, numeración concurrente, permisos y anulación única");
    }
    private class FiscalHandler : HttpMessageHandler {
        public int Calls;
        public string LastPath;
        public string Response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Interlocked.Increment(ref Calls); LastPath=request.RequestUri.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(Response) });
        }
    }
}
