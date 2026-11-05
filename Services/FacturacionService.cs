using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MiniMarket.Web.Services;

public record ResultadoFiscal(string Estado, string Mensaje, int? Http = null, string Codigo = null, string Hash = null, string Cdr = null);
public record ArchivoFiscal(byte[] Bytes, string ContentType, string Nombre);

public class FacturacionService(ApplicationDbContext db, HttpClient http, IConfiguration config, IHostEnvironment environment = null)
{
    private const string QaBaseUrl = "https://apife-qa.factiliza.com/api/v1/";
    private const string ProduccionBaseUrl = "https://apife.factiliza.com/api/v1/";
    private const int MaxJsonBytes = 5 * 1024 * 1024;
    private const int MaxFileBytes = 20 * 1024 * 1024;
    public string Ambiente => string.Equals(config["FactilizaSettings:Ambiente"], "Produccion", StringComparison.OrdinalIgnoreCase)
        ? "Produccion" : "QA";
    public string EtiquetaAmbiente => Ambiente == "Produccion" ? "Producción" : "QA";
    public bool EsProduccion => Ambiente == "Produccion";
    public bool Habilitada => config.GetValue<bool>("FactilizaSettings:FacturacionHabilitada") && ConfiguracionCompleta &&
        (EsProduccion
            ? EntornoEs("Production") &&
              config.GetValue<bool>("FactilizaSettings:Produccion:SeriesConfirmadas")
            : EntornoEs("QA"));

    public async Task Emitir(int ventaId)
    {
        if (!Habilitada) return;
        // Un solo número por venta; nunca se reenvía un intento incierto o rechazado.
        ComprobanteFiscal fiscal;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await ActualizacionComercial.BloquearOperaciones(db);
            var anterior = await db.ComprobantesFiscales.SingleOrDefaultAsync(c => c.VentaId == ventaId);
            // Solo se permite corregir datos cuando jamás salió una solicitud HTTP.
            if (anterior != null && anterior.Estado != "No enviada (datos)") return;
            var venta = await db.Ventas.Include(v => v.Cliente).Include(v => v.Detalles).ThenInclude(d => d.Producto).SingleAsync(v => v.Id == ventaId);
            if (venta.Estado != "Activa") return;
            var tipo = venta.Cliente?.Documento?.Length == 11 ? "01" : "03";
            var prefijo = tipo == "01" ? "Factura" : "Boleta";
            var serie = Valor($"{prefijo}Serie");
            var ruc = Valor("EmpresaRuc");
            if (string.IsNullOrWhiteSpace(Token) || ruc?.Length != 11 || ruc.Any(c => !char.IsAsciiDigit(c)) ||
                serie?.Length != 4 || serie[0] != (tipo == "01" ? 'F' : 'B') || serie.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c)))
            {
                venta.EstadoEmision = "No enviada (configuración)";
                await db.SaveChangesAsync(); await tx.CommitAsync(); return;
            }
            var ultimo = await db.ComprobantesFiscales.Where(c => c.Ambiente == Ambiente && c.EmpresaRuc == ruc && c.Serie == serie)
                .MaxAsync(c => (int?)c.Correlativo) ?? 0;
            var inicio = int.TryParse(Valor($"{prefijo}CorrelativoInicial"), out var configurado) ? configurado : 1;
            fiscal = anterior ?? new ComprobanteFiscal { VentaId = venta.Id, Ambiente = Ambiente, EmpresaRuc = ruc, Tipo = tipo, Serie = serie,
                Correlativo = Math.Max(Math.Max(1, inicio), checked(ultimo + 1)), Estado = Estado("Enviando"), Actualizado = HoraPeru.Ahora };
            if (anterior != null && (fiscal.Tipo != tipo || fiscal.EmpresaRuc != ruc || fiscal.Serie != serie))
                throw new InvalidOperationException("La corrección no puede cambiar el tipo, RUC o serie ya reservados.");
            fiscal.Estado = Estado("Enviando"); fiscal.Mensaje = null; fiscal.Actualizado = HoraPeru.Ahora;
            var tasa = (await db.Configuraciones.FirstOrDefaultAsync())?.IgvPorcentaje ?? 18m;
            try { fiscal.SolicitudJson = ComprobantePayload.Crear(venta, fiscal, tasa); }
            catch (InvalidOperationException ex) {
                fiscal.Estado = "No enviada (datos)"; fiscal.Mensaje = ex.Message; fiscal.SolicitudJson = "{}";
            }
            if (anterior == null) db.ComprobantesFiscales.Add(fiscal);
            venta.EstadoEmision = fiscal.Estado;
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        if (fiscal.Estado != Estado("Enviando")) return;
        var resultado = await Enviar(fiscal.SolicitudJson);
        fiscal.Estado = resultado.Estado; fiscal.Mensaje = resultado.Mensaje; fiscal.HttpStatus = resultado.Http;
        fiscal.Codigo = resultado.Codigo; fiscal.Hash = resultado.Hash; fiscal.CdrZip = resultado.Cdr;
        fiscal.Actualizado = HoraPeru.Ahora;
        await using (var tx = await db.Database.BeginTransactionAsync()) {
            await db.SaveChangesAsync();
            await db.Ventas.Where(v => v.Id == ventaId && v.Estado == "Activa")
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EstadoEmision, resultado.Estado));
            await tx.CommitAsync();
        }
    }

    public async Task Anular(int ventaId, string motivo)
    {
        if (!Habilitada) return;
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 300) throw new InvalidOperationException("Indique un motivo válido de anulación.");
        // Claim atómico: dos solicitudes o respuestas inciertas nunca duplican la baja.
        var estadoAceptado = Estado("Aceptada");
        var estadoEnviandoAnulacion = Estado("Enviando anulación");
        var claimed = await db.ComprobantesFiscales.Where(c => c.VentaId == ventaId && c.Ambiente == Ambiente && c.Estado == estadoAceptado &&
            c.EstadoAnulacion == null && c.Venta.Estado == "Anulada")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EstadoAnulacion, estadoEnviandoAnulacion)
                .SetProperty(c => c.MotivoAnulacion,motivo));
        if (claimed == 0) return;
        var fiscal = await db.ComprobantesFiscales.AsNoTracking().SingleAsync(c => c.VentaId == ventaId);
        var body = JsonSerializer.Serialize(new { empresa_Ruc=fiscal.EmpresaRuc, tipo_Doc=fiscal.Tipo, serie=fiscal.Serie,
            correlativo=fiscal.Correlativo.ToString(System.Globalization.CultureInfo.InvariantCulture), anulacion_Motivo=motivo, estado_Documento="0" });
        var result = await Enviar(body, fiscal.Tipo == "01" ? "voided/cancel" : "summary/cancel");
        var estado = result.Estado == Estado("Aceptada") ? Estado("Anulada") :
            result.Estado == Estado("Rechazada") ? Estado("Anulación rechazada") : Estado("Anulación incierta");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.ComprobantesFiscales.Where(c => c.VentaId == ventaId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.EstadoAnulacion, estado).SetProperty(c => c.MensajeAnulacion,result.Mensaje)
            .SetProperty(c => c.CdrAnulacionZip,result.Cdr).SetProperty(c => c.Actualizado,HoraPeru.Ahora));
        await db.Ventas.Where(v => v.Id == ventaId).ExecuteUpdateAsync(s => s.SetProperty(v => v.EstadoEmision,estado));
        await tx.CommitAsync();
    }

    public async Task<ResultadoFiscal> Enviar(string json, string ruta = "invoice/send")
    {
        try
        {
            using var request = Request(ruta, json);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            string text;
            try { text = Encoding.UTF8.GetString(await LeerLimitado(response.Content, MaxJsonBytes, timeout.Token)); }
            catch (InvalidDataException) { return new(Estado("Resultado incierto"), $"HTTP {status}: la respuesta excedió el límite permitido. Consulte el portal antes de reenviar.", status); }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch (JsonException) { return new(Estado("Resultado incierto"), $"HTTP {status}: respuesta no interpretable. Consulte el portal antes de reenviar.", status); }
            using (doc)
            {
                var root = doc.RootElement;
                var data = Get(root, "data"); var sunat = Get(data, "sunatResponse"); var cdr = Get(sunat, "cdrResponse");
                var code = Str(cdr, "code");
                var mensaje = Str(root, "message") ?? Str(root,"title");
                var errors = Get(root,"errors");
                if (errors.ValueKind == JsonValueKind.Object) mensaje += " · Factiliza reportó errores de validación.";
                var error = Get(data, "error");
                if (Str(error, "message") is string detail && detail.Length > 0) mensaje += " · " + detail;
                if (Str(cdr, "description") is string description && description.Length > 0) mensaje += " · " + description;
                mensaje = Safe(mensaje, 2000) ?? $"Respuesta HTTP {status}";
                var accepted = response.IsSuccessStatusCode && Get(root, "success").ValueKind == JsonValueKind.True &&
                    Get(sunat, "success").ValueKind == JsonValueKind.True && code == "0";
                var estado = accepted ? Estado("Aceptada") : status >= 500 ? Estado("Resultado incierto") :
                    Get(root, "success").ValueKind == JsonValueKind.False || !response.IsSuccessStatusCode ? Estado("Rechazada") : Estado("Resultado incierto");
                return new(estado, mensaje, status, Safe(code ?? Str(error,"code"),40), Safe(Str(data,"hash"),200), Str(sunat,"cdrZip"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { return new(Estado("Resultado incierto"), "No se obtuvo respuesta completa. Consulte PDF/XML y el portal antes de cualquier reenvío."); }
    }

    public async Task<ArchivoFiscal> Descargar(ComprobanteFiscal fiscal, string formato)
    {
        if (formato is not ("pdf" or "xml" or "cdr" or "cdr-anulacion")) throw new InvalidOperationException("Formato fiscal inválido.");
        if (formato is "cdr" or "cdr-anulacion")
        {
            var cdr = formato == "cdr" ? fiscal.CdrZip : fiscal.CdrAnulacionZip;
            if (string.IsNullOrWhiteSpace(cdr)) throw new InvalidOperationException("Todavía no existe CDR guardado.");
            return new(Convert.FromBase64String(cdr), "application/zip", $"{fiscal.Serie}-{fiscal.Correlativo:D8}-{formato}.zip");
        }
        if (!Habilitada || fiscal.Ambiente != Ambiente) throw new InvalidOperationException("La consulta externa no está habilitada para el ambiente de este comprobante.");
        var body = JsonSerializer.Serialize(new { empresa_Ruc = fiscal.EmpresaRuc, tipo_Doc = fiscal.Tipo,
            serie = fiscal.Serie, correlativo = fiscal.Correlativo.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var request = Request("invoice/" + formato, body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        byte[] bytes;
        try { bytes = await LeerLimitado(response.Content, MaxFileBytes, timeout.Token); }
        catch (InvalidDataException) { throw new InvalidOperationException("Factiliza devolvió un archivo que excede el límite permitido."); }
        if (!response.IsSuccessStatusCode || bytes.Length == 0) throw new InvalidOperationException($"Factiliza no entregó el {formato.ToUpperInvariant()} (HTTP {(int)response.StatusCode}). Revise el portal de {EtiquetaAmbiente}.");
        if (formato == "pdf" && (bytes.Length < 5 || Encoding.ASCII.GetString(bytes,0,5) != "%PDF-"))
            throw new InvalidOperationException("Factiliza respondió sin un PDF válido.");
        if (formato == "xml")
        {
            using var reader = System.Xml.XmlReader.Create(new MemoryStream(bytes), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            if (reader.LocalName != "Invoice") throw new InvalidOperationException("Factiliza respondió sin un XML de comprobante.");
        }
        return new(bytes, formato == "pdf" ? "application/pdf" : "application/xml", $"{fiscal.Serie}-{fiscal.Correlativo:D8}.{formato}");
    }
    private HttpRequestMessage Request(string path, string body)
    {
        if (string.IsNullOrWhiteSpace(Token)) throw new InvalidOperationException($"Falta token de facturación de {EtiquetaAmbiente}.");
        var baseUrl = EsProduccion ? ProduccionBaseUrl : QaBaseUrl;
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }
    private string Safe(string value, int max)
    {
        if (value == null) return null;
        if (!string.IsNullOrEmpty(Token)) value = value.Replace(Token, "[credencial omitida]");
        return value[..Math.Min(value.Length,max)];
    }
    private string Token => Valor("Token");
    private bool ConfiguracionCompleta => !string.IsNullOrWhiteSpace(Token) && RucValido(Valor("EmpresaRuc")) &&
        SerieValida(Valor("BoletaSerie"), 'B') && SerieValida(Valor("FacturaSerie"), 'F') &&
        CorrelativoValido(Valor("BoletaCorrelativoInicial")) && CorrelativoValido(Valor("FacturaCorrelativoInicial"));
    private bool EntornoEs(string nombre) => environment?.IsEnvironment(nombre) ??
        string.Equals(config["ASPNETCORE_ENVIRONMENT"], nombre, StringComparison.OrdinalIgnoreCase);
    private string Valor(string nombre)
    {
        var valor = config[$"FactilizaSettings:{Ambiente}:{nombre}"];
        // Conserva instalaciones QA existentes; producción nunca reutiliza claves antiguas.
        return !string.IsNullOrWhiteSpace(valor) || EsProduccion ? valor : config[$"FactilizaSettings:{nombre}"];
    }
    private string Estado(string nombre) => $"{nombre} {EtiquetaAmbiente}";
    private static bool RucValido(string ruc) => ruc?.Length == 11 && ruc.All(char.IsAsciiDigit);
    private static bool SerieValida(string serie, char prefijo) => serie?.Length == 4 && serie[0] == prefijo && serie.Skip(1).All(char.IsAsciiLetterOrDigit);
    private static bool CorrelativoValido(string valor) => int.TryParse(valor, out var numero) && numero >= 1;
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
    private static JsonElement Get(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    private static string Str(JsonElement e, string key) { var v = Get(e,key); return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.ToString() : null; }
}
