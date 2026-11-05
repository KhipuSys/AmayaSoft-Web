using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMarket.Web.Models.ViewModels;
using System.Collections.Generic;
using System.Text.Json;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class EtiquetasController : Controller
    {
        // GET: Etiquetas
        public IActionResult Index()
        {
            return View();
        }

        // POST: Etiquetas/Imprimir
        [HttpPost]
        public IActionResult Imprimir(string jsonEtiquetas)
        {
            if (string.IsNullOrEmpty(jsonEtiquetas))
            {
                // Si mandan un array vacío, regresamos al formulario.
                TempData["MensajeError"] = "Para imprimir debes añadir al menos un producto a la lista.";
                return RedirectToAction(nameof(Index));
            }

            var items = JsonSerializer.Deserialize<List<EtiquetaItemViewModel>>(jsonEtiquetas);
            
            if (items == null || items.Count == 0)
            {
                TempData["MensajeError"] = "Error procesando las etiquetas. Intente otra vez.";
                return RedirectToAction(nameof(Index));
            }

            // Enviamos el modelo des-serializado a la plantilla HTML especial de impresión.
            return View(items);
        }
    }
}
