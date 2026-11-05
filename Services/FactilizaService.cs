using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration; 

namespace MiniMarket.Web.Services
{
    public class FactilizaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _consultaToken;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public FactilizaService(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _environment = environment;
            _consultaToken = configuration["FactilizaSettings:ConsultaToken"] ?? string.Empty;

            _httpClient.BaseAddress = new Uri("https://api.factiliza.com/pe/v1/");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public FacturacionService Facturacion(MiniMarket.Web.Data.ApplicationDbContext db) => new(db, _httpClient, _configuration, _environment);

        public async Task<ConsultaDocumentoResult> ConsultarDocumentoAsync(string documento)
        {
            documento = documento?.Trim() ?? string.Empty;

            if ((documento.Length != 8 && documento.Length != 11) || documento.Any(c => c < '0' || c > '9'))
                return ConsultaDocumentoResult.Fallo("Ingrese un DNI de 8 dígitos o RUC de 11 dígitos.");

            if (string.IsNullOrWhiteSpace(_consultaToken))
                return ConsultaDocumentoResult.Fallo("No se configuró el token de consulta DNI/RUC de Factiliza.");

            try
            {
                var tipo = documento.Length == 8 ? "dni" : "ruc";
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{tipo}/info/{documento}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _consultaToken);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    return ConsultaDocumentoResult.Fallo("Factiliza rechazó el token de consulta DNI/RUC. Verifica el acceso a la API de consultas.");

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return ConsultaDocumentoResult.Fallo("No se encontró información para el documento ingresado.");

                if (!response.IsSuccessStatusCode)
                    return ConsultaDocumentoResult.Fallo("La consulta DNI/RUC no está disponible en este momento.");

                using var json = JsonDocument.Parse(await LeerLimitado(response.Content, 1024 * 1024, timeout.Token));

                var root = json.RootElement;
                if (TryGetProperty(root, "success", out var success) && success.ValueKind == JsonValueKind.False)
                    return ConsultaDocumentoResult.Fallo("Factiliza no devolvió información para este documento.");
                var data = TryGetProperty(root, "data", out var dataElement) ? dataElement : root;

                var nombre = documento.Length == 8
                    ? FirstString(data, "nombre_completo", "nombre", "nombres")
                    : FirstString(data, "nombre_o_razon_social", "razon_social", "nombre", "nombre_completo");

                if (string.IsNullOrWhiteSpace(nombre))
                    return ConsultaDocumentoResult.Fallo("La API respondió, pero no devolvió el nombre del cliente.");

                var direccion = FirstString(data, "direccion_completa", "direccion", "domicilio_fiscal");
                var nombres = documento.Length == 8 ? FirstString(data, "nombres", "prenombres") : "";
                var apellidos = documento.Length == 8 ? string.Join(" ", new[] {
                    FirstString(data, "apellido_paterno", "apellidoPaterno"),
                    FirstString(data, "apellido_materno", "apellidoMaterno")
                }.Where(a => !string.IsNullOrWhiteSpace(a))) : "";
                if (documento.Length == 8 && !string.IsNullOrWhiteSpace(nombres) && !string.IsNullOrWhiteSpace(apellidos))
                    nombre = $"{nombres} {apellidos}";

                return new ConsultaDocumentoResult
                {
                    Exito = true,
                    Documento = documento,
                    Nombre = nombre,
                    Nombres = nombres,
                    Apellidos = apellidos,
                    Direccion = direccion
                };
            }
            catch
            {
                return ConsultaDocumentoResult.Fallo("No se pudo consultar la API de documentos.");
            }
        }

        private static string FirstString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
                    return property.GetString();
            }

            return string.Empty;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
                return true;

            property = default;
            return false;
        }

        private static async Task<byte[]> LeerLimitado(HttpContent content, int limite, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > limite) throw new InvalidDataException();
            await using var input = await content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > limite) throw new InvalidDataException();
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return output.ToArray();
        }
    }

    public class ConsultaDocumentoResult
    {
        public bool Exito { get; set; }
        public string Documento { get; set; }
        public string Nombre { get; set; }
        public string Nombres { get; set; }
        public string Apellidos { get; set; }
        public string Direccion { get; set; }
        public string Mensaje { get; set; }

        public static ConsultaDocumentoResult Fallo(string mensaje)
        {
            return new ConsultaDocumentoResult { Exito = false, Mensaje = mensaje };
        }
    }
}
