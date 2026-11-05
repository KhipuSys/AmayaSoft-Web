using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers;

[Authorize]
public class VentasController(ApplicationDbContext context, FactilizaService factilizaService,
    ILogger<VentasController> logger, SignInManager<IdentityUser> signInManager = null) : Controller
{
    public async Task<IActionResult> Index()
    {
        var ventas = context.Ventas.Include(v => v.Usuario).Include(v => v.Cliente).AsQueryable();
        if (!User.IsInRole("Administrador")) ventas = ventas.Where(v => v.Usuario.UserName == User.Identity.Name);
        var fiscal = factilizaService.Facturacion(context);
        ViewBag.FiscalHabilitado = fiscal.Habilitada;
        ViewBag.FiscalAmbiente = fiscal.EtiquetaAmbiente;
        return View(await ventas.OrderByDescending(v => v.Fecha).ToListAsync());
    }

    public IActionResult Create()
    {
        if (!context.AperturasCaja.Any(c => c.UsuarioId == User.Identity.Name && c.Estado))
        {
            TempData["MensajeError"] = "Debes ABRIR CAJA antes de poder vender.";
            return RedirectToAction("Index", "Caja");
        }
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> BuscarProductos(string term, int page = 1)
    {
        term = (term ?? "").Trim();
        page = Math.Clamp(page, 1, 10000);
        var productos = await context.Productos.AsNoTracking()
            .Where(p => p.Estado && (term == "" || p.Nombre.Contains(term) || p.CodigoBarras.Contains(term)))
            .OrderByDescending(p => term != "" && p.CodigoBarras == term).ThenBy(p => p.Nombre).ThenBy(p => p.Id)
            .Skip((page - 1) * 20).Take(21)
            .Select(p => new { id = p.Id, text = p.Nombre + " | Stock: " + p.Stock, nombre = p.Nombre,
                codigo = p.CodigoBarras, precio = p.Precio, stock = p.Stock, disabled = p.Stock <= 0 }).ToListAsync();
        return Json(new { results = productos.Take(20), pagination = new { more = productos.Count > 20 } });
    }

    [HttpGet]
    public async Task<IActionResult> BuscarClientes(string term, int page = 1)
    {
        term = (term ?? "").Trim();
        page = Math.Clamp(page, 1, 10000);
        var clientes = await context.Clientes.AsNoTracking()
            .Where(c => c.Estado && (term == "" || c.Nombre.Contains(term) || c.Documento.Contains(term)))
            .OrderBy(c => c.Nombre).ThenBy(c => c.Id).Skip((page - 1) * 20).Take(21)
            .Select(c => new { id = c.Id, text = c.Nombre + " | " + c.Documento }).ToListAsync();
        return Json(new { results = clientes.Take(20), pagination = new { more = clientes.Count > 20 } });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar([FromBody] VentaRequest request)
    {
        if (!ModelState.IsValid) return Json(new { exito = false, mensaje = "Datos inválidos." });
        Venta venta;
        bool repetida;
        try
        {
            (venta, repetida) = await new OperacionVentaService(context).Registrar(request, User.Identity.Name, User.IsInRole("Administrador"));
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { exito = false, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error procesando operación {Operacion}", request?.OperacionId);
            // Resultado incierto: el cliente conserva el mismo identificador para consultar/reintentar.
            return StatusCode(503, new { exito = false, mensaje = "No se pudo confirmar el resultado. Reintenta la misma operación." });
        }

        return Json(new { exito = true, estadoEmision = venta.EstadoEmision, mensaje = "Venta registrada. El administrador puede emitir el comprobante desde el historial.",
            idVenta = venta.Id, repetida });
    }

    public async Task<IActionResult> Comprobante(int id)
    {
        var venta = await context.Ventas.AsNoTracking().Include(v => v.Usuario).Include(v => v.Cliente)
            .Include(v => v.Detalles).ThenInclude(d => d.Producto).SingleOrDefaultAsync(v => v.Id == id);
        if (venta == null) return NotFound();
        if (!User.IsInRole("Administrador") && venta.Usuario?.UserName != User.Identity.Name) return Forbid();
        ViewBag.Venta = venta;
        var fiscal = factilizaService.Facturacion(context);
        ViewBag.FiscalHabilitado = fiscal.Habilitada;
        ViewBag.FiscalAmbiente = fiscal.EtiquetaAmbiente;
        ViewBag.FiscalProduccion = fiscal.EsProduccion;
        ViewBag.IgvPorcentaje = (await context.Configuraciones.AsNoTracking().FirstOrDefaultAsync())?.IgvPorcentaje ?? 18m;
        return View(await context.ComprobantesFiscales.AsNoTracking().SingleOrDefaultAsync(c => c.VentaId == id));
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirComprobante(int id, string claveAdministrador)
    {
        var fiscal = factilizaService.Facturacion(context);
        if (!fiscal.Habilitada) return NotFound();
        var venta = await context.Ventas.Include(v => v.Usuario).SingleOrDefaultAsync(v => v.Id == id);
        if (venta == null) return NotFound();
        if (!await ReautenticarProduccion(fiscal, claveAdministrador))
        {
            TempData["ErrorFiscal"] = "La contraseña del administrador no pudo validarse. No se realizó ningún envío.";
            return RedirectToAction(nameof(Comprobante), new { id });
        }
        // Las ventas anteriores con resultado desconocido requieren conciliación manual.
        if (venta.EstadoEmision is not ("Pendiente" or "No enviada (QA desactivado)" or "No enviada (configuración)" or "No enviada (datos)"))
            return BadRequest("Esta venta ya tiene un intento o resultado anterior. Consulte el comprobante; no se reenviará.");
        logger.LogInformation("El administrador {Usuario} solicitó emisión fiscal de venta {VentaId} en {Ambiente}", User.Identity?.Name, id, fiscal.Ambiente);
        await fiscal.Emitir(id);
        return RedirectToAction(nameof(Comprobante), new { id });
    }

    public async Task<IActionResult> ArchivoComprobante(int id, string formato)
    {
        var fiscal = await context.ComprobantesFiscales.AsNoTracking().Include(c => c.Venta).ThenInclude(v => v.Usuario).SingleOrDefaultAsync(c => c.VentaId == id);
        if (fiscal == null) return NotFound();
        if (!User.IsInRole("Administrador") && fiscal.Venta.Usuario?.UserName != User.Identity.Name) return Forbid();
        Response.Headers.CacheControl = "no-store";
        try {
            var archivo = await factilizaService.Facturacion(context).Descargar(fiscal, formato);
            return File(archivo.Bytes, archivo.ContentType, archivo.Nombre);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or FormatException or System.Xml.XmlException)
        {
            TempData["ErrorFiscal"] = ex is InvalidOperationException ? ex.Message : "No se pudo recuperar el archivo fiscal. Consulte el portal de Factiliza.";
            return RedirectToAction(nameof(Comprobante), new { id });
        }
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularComprobante(int id, string claveAdministrador)
    {
        var fiscal = factilizaService.Facturacion(context);
        if (!fiscal.Habilitada) return NotFound();
        var venta = await context.Ventas.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id);
        if (venta == null) return NotFound();
        if (venta.Estado != "Anulada") return BadRequest("Primero debe anular la venta local.");
        if (!await ReautenticarProduccion(fiscal, claveAdministrador))
        {
            TempData["ErrorFiscal"] = "La contraseña del administrador no pudo validarse. No se realizó ninguna anulación fiscal.";
            return RedirectToAction(nameof(Comprobante), new { id });
        }
        logger.LogInformation("El administrador {Usuario} solicitó anulación fiscal de venta {VentaId} en {Ambiente}", User.Identity?.Name, id, fiscal.Ambiente);
        await fiscal.Anular(id, venta.MotivoAnulacion);
        return RedirectToAction(nameof(Comprobante), new { id });
    }

    public async Task<IActionResult> Ticket(int id)
    {
        var venta = await context.Ventas.Include(v => v.Usuario).Include(v => v.Cliente)
            .Include(v => v.Detalles).ThenInclude(d => d.Producto).FirstOrDefaultAsync(v => v.Id == id);
        if (venta == null) return NotFound();
        if (!User.IsInRole("Administrador") && venta.Usuario?.UserName != User.Identity.Name) return Forbid();
        var perfilVendedor = await context.UsuarioPerfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == venta.UsuarioId);
        ViewBag.NombreCajero = perfilVendedor == null ? "Nombre pendiente de registrar" :
            string.Join(" ", new[] {
                perfilVendedor.Nombres.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                perfilVendedor.Apellidos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            }.Where(parte => !string.IsNullOrWhiteSpace(parte)));
        ViewBag.Configuracion = await context.Configuraciones.FirstOrDefaultAsync() ?? new Configuracion
            { NombreEmpresa = "AmayaSoft", Ruc = "00000000000", Direccion = "Configure el sistema", IgvPorcentaje = 18 };
        ViewBag.ComprobanteFiscal = await context.ComprobantesFiscales.AsNoTracking()
            .SingleOrDefaultAsync(c => c.VentaId == venta.Id);
        var fiscal = factilizaService.Facturacion(context);
        ViewBag.FiscalAmbiente = fiscal.Ambiente;
        return View(venta);
    }

    [HttpPost]
    [Authorize(Roles = "Administrador")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Anular(int id, string motivo)
    {
        try
        {
            await new OperacionVentaService(context).Anular(id, User.Identity.Name, motivo);
            var estadoFiscal = await context.Ventas.AsNoTracking().Where(v => v.Id == id).Select(v => v.EstadoEmision).SingleAsync();
            return Json(new { exito = true, mensaje = $"Venta anulada y stock restituido. Comprobante: {estadoFiscal}. El administrador debe gestionar por separado cualquier anulación fiscal. La devolución de dinero también se concilia por separado." });
        }
        catch (InvalidOperationException ex) { return Json(new { exito = false, mensaje = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error anulando venta {VentaId}", id);
            return StatusCode(503, new { exito = false, mensaje = "No se pudo confirmar la anulación. Reintenta." });
        }
    }

    private async Task<bool> ReautenticarProduccion(FacturacionService fiscal, string claveAdministrador)
    {
        if (!fiscal.EsProduccion) return true;
        if (signInManager == null || string.IsNullOrWhiteSpace(claveAdministrador)) return false;
        var usuario = await signInManager.UserManager.GetUserAsync(User);
        if (usuario == null) return false;
        var resultado = await signInManager.CheckPasswordSignInAsync(usuario, claveAdministrador, lockoutOnFailure: true);
        return resultado.Succeeded;
    }
}

public class VentaRequest
{
    public Guid OperacionId { get; set; }
    public int? ClienteId { get; set; }
    public decimal Total { get; set; }
    public decimal Descuento { get; set; }
    public string MetodoPago { get; set; }
    public List<DetalleRequest> Detalles { get; set; }
}
public class DetalleRequest
{
    public int ProductoId { get; set; }
    public string Nombre { get; set; }
    public decimal Precio { get; set; }
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}
