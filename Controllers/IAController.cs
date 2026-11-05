using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers
{
    [Authorize]
    public class IAController : Controller
    {
        private readonly IAAsistenteService _iaService;

        public IAController(IAAsistenteService iaService)
        {
            _iaService = iaService;
        }

        [HttpPost]
        public async Task<IActionResult> Consultar([FromBody] PreguntaDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Pregunta))
            {
                return BadRequest(new { respuesta = "Por favor ingresa una pregunta válida." });
            }

            if (dto.Pregunta.Length > 1000) return BadRequest(new { respuesta = "Escribe hasta 1.000 caracteres por consulta." });
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            espera.CancelAfter(TimeSpan.FromSeconds(15));
            string respuestaIa;
            try
            {
                respuestaIa = await _iaService.PreguntarAlAsistenteAsync(dto.Pregunta, User.Identity?.Name, User.IsInRole("Administrador"), espera.Token);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, new { respuesta = "La consulta tardó demasiado. Intenta un período más corto." });
            }
            return Json(new { respuesta = respuestaIa });
        }
    }

    public class PreguntaDto
    {
        public string Pregunta { get; set; }
    }
}
