using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ConfiguracionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _hostEnvironment;

        public ConfiguracionController(ApplicationDbContext context, IWebHostEnvironment hostEnvironment)
        {
            _context = context;
            _hostEnvironment = hostEnvironment;
        }

        // GET: Muestra el formulario con los datos actuales
        [HttpGet]
        public async Task<IActionResult> HoraSistema()
        {
            var origen = await _context.Database.SqlQueryRaw<string>("SELECT ZonaOrigen AS Value FROM AjustesHorario WHERE Nombre='PeruV1'").SingleOrDefaultAsync();
            return Json(new { zona = "America/Lima", desfase = "UTC-05:00",
                ahoraPeru = HoraPeru.Ahora.ToString("yyyy-MM-dd HH:mm:ss"),
                utc = DateTimeOffset.UtcNow, zonaServidor = TimeZoneInfo.Local.Id,
                origenFechasAnteriores = origen });
        }

        public async Task<IActionResult> Index()
        {
            var configEntidad = await _context.Configuraciones.FirstOrDefaultAsync();
            var model = new ConfiguracionViewModel();

            if (configEntidad != null)
            {
                // Cargar datos de la BD al formulario
                model.NombreEmpresa = configEntidad.NombreEmpresa;
                model.Ruc = configEntidad.Ruc;
                model.Direccion = configEntidad.Direccion;
                model.Telefono = configEntidad.Telefono;
                model.EmailContacto = configEntidad.EmailContacto;
                model.IgvPorcentaje = configEntidad.IgvPorcentaje;
                model.MonedaSimbolo = configEntidad.MonedaSimbolo;
                model.CurrentLogoUrl = configEntidad.LogoUrl;
            }
            else
            {
                // Valores por defecto si es la primera vez
                model.NombreEmpresa = "AmayaSoft";
                model.Ruc = "00000000000";
                model.IgvPorcentaje = 18;
                model.MonedaSimbolo = "S/.";
            }

            return View(model);
        }

        // POST: Guarda los cambios
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(ConfiguracionViewModel model)
        {
            if (ModelState.IsValid)
            {
                var configEntidad = await _context.Configuraciones.FirstOrDefaultAsync();

                // Procesamiento del archivo de logo
                string uniqueFileName = configEntidad?.LogoUrl; // Mantener existente por defecto
                
                if (model.LogoUpload != null)
                {
                    string uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, "images", "logos");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    uniqueFileName = Guid.NewGuid().ToString() + "_" + model.LogoUpload.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.LogoUpload.CopyToAsync(fileStream);
                    }
                    uniqueFileName = "/images/logos/" + uniqueFileName;
                }

                if (configEntidad == null)
                {
                    // Si no existe, CREAMOS uno nuevo
                    configEntidad = new Configuracion
                    {
                        NombreEmpresa = model.NombreEmpresa,
                        Ruc = model.Ruc,
                        Direccion = model.Direccion,
                        Telefono = model.Telefono,
                        EmailContacto = model.EmailContacto,
                        IgvPorcentaje = model.IgvPorcentaje,
                        MonedaSimbolo = model.MonedaSimbolo,
                        LogoUrl = uniqueFileName
                    };
                    _context.Configuraciones.Add(configEntidad);
                }
                else
                {
                    // Si ya existe, ACTUALIZAMOS los datos
                    configEntidad.NombreEmpresa = model.NombreEmpresa;
                    configEntidad.Ruc = model.Ruc;
                    configEntidad.Direccion = model.Direccion;
                    configEntidad.Telefono = model.Telefono;
                    configEntidad.EmailContacto = model.EmailContacto;
                    configEntidad.IgvPorcentaje = model.IgvPorcentaje;
                    configEntidad.MonedaSimbolo = model.MonedaSimbolo;
                    if (model.LogoUpload != null)
                    {
                        configEntidad.LogoUrl = uniqueFileName;
                    }
                    
                    _context.Configuraciones.Update(configEntidad);
                }

                await _context.SaveChangesAsync();
                
                TempData["MensajeExito"] = "¡Configuración guardada correctamente en la Base de Datos!";
                // Actualizar la URL del logo visualizado
                model.CurrentLogoUrl = configEntidad.LogoUrl;
                return View("Index", model);
            }

            return View("Index", model);
        }
    }
}
