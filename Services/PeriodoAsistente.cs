using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniMarket.Web.Services;

public record PeriodoAsistente(DateTime Inicio, DateTime Fin)
{
    public string Etiqueta => Inicio == Fin.AddDays(-1)
        ? Inicio.ToString("dd/MM/yyyy") : $"{Inicio:dd/MM/yyyy} al {Fin.AddDays(-1):dd/MM/yyyy}";

    public static string Normalizar(string texto)
    {
        var limpio = new string((texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        limpio = Regex.Replace(limpio, @"\b(proovedores|provedores|proveedore)\b", "proveedores");
        limpio = Regex.Replace(limpio, @"\b(stok|stoc|stockk)\b", "stock");
        return Regex.Replace(limpio, @"\s+", " ").Trim();
    }

    public static PeriodoAsistente Interpretar(string pregunta, DateTime hoy)
    {
        var t = Normalizar(pregunta);
        var mes = new DateTime(hoy.Year, hoy.Month, 1);
        var lunes = hoy.AddDays(-((int)hoy.DayOfWeek + 6) % 7);
        if (t.Contains("entre ayer y hoy")) return new(hoy.AddDays(-1), hoy.AddDays(1));
        var ultimos = Regex.Match(t, @"ultimos?\s+(\d+)\s+dias?");
        if (ultimos.Success)
        {
            if (!int.TryParse(ultimos.Groups[1].Value, out var n) || n < 1 || n > 366)
                throw new ArgumentException("Indica entre 1 y 366 días para 'últimos días'.");
            return new(hoy.AddDays(1 - n), hoy.AddDays(1));
        }
        if (t.Contains("semana pasada")) return new(lunes.AddDays(-7), lunes);
        if (t.Contains("esta semana")) return new(lunes, hoy.AddDays(1));
        if (t.Contains("mes pasado")) return new(mes.AddMonths(-1), mes);
        if (Regex.IsMatch(t, @"\b(este mes|del mes|mes actual)\b")) return new(mes, hoy.AddDays(1));
        if (t.Contains("ano pasado")) return new(new(hoy.Year - 1, 1, 1), new(hoy.Year, 1, 1));
        if (t.Contains("este ano")) return new(new(hoy.Year, 1, 1), hoy.AddDays(1));
        const string nombres = "enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre";
        int NumeroMes(string s) => s == "setiembre" ? 9 : Array.IndexOf(nombres.Replace("|setiembre", "").Split('|'), s) + 1;
        DateTime Fecha(int ano, int m, int d)
        {
            if (ano < 1900 || ano > 9998 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(ano, m))
                throw new ArgumentException("La fecha no es válida. Usa, por ejemplo, 21/09/2026.");
            return new(ano, m, d);
        }
        int Anio(Group g) => g.Success ? int.Parse(g.Value) : hoy.Year;
        var corto = Regex.Match(t, $@"\b(?:del?|entre)\s+(\d{{1,2}})\s+(?:al|y)\s+(\d{{1,2}})\s+de\s+({nombres})(?:\s+(?:de\s+|del\s+)?(\d{{4}}))?\b");
        if (corto.Success)
        {
            var m = NumeroMes(corto.Groups[3].Value); var a = Anio(corto.Groups[4]);
            return Rango(Fecha(a, m, int.Parse(corto.Groups[1].Value)), Fecha(a, m, int.Parse(corto.Groups[2].Value)));
        }
        var fechas = Regex.Matches(t, $@"\b(?:(?<iso>\d{{4}})-(?<im>\d{{1,2}})-(?<id>\d{{1,2}})|(?<d>\d{{1,2}})[/-](?<m>\d{{1,2}})(?:[/-](?<a>\d{{4}}))?|(?<dt>\d{{1,2}})\s+(?:de\s+)?(?<mt>{nombres})(?:\s+(?:de\s+|del\s+)?(?<at>\d{{4}}))?)\b");
        if (fechas.Count > 2) throw new ArgumentException("Consulta una fecha o un rango a la vez.");
        if (fechas.Count > 0)
        {
            var dias = fechas.Select(f => f.Groups["iso"].Success
                ? Fecha(int.Parse(f.Groups["iso"].Value), int.Parse(f.Groups["im"].Value), int.Parse(f.Groups["id"].Value))
                : f.Groups["d"].Success ? Fecha(Anio(f.Groups["a"]), int.Parse(f.Groups["m"].Value), int.Parse(f.Groups["d"].Value))
                : Fecha(Anio(f.Groups["at"]), NumeroMes(f.Groups["mt"].Value), int.Parse(f.Groups["dt"].Value))).ToArray();
            if (dias.Length == 2 && !Regex.IsMatch(t, @"\b(al|hasta|entre)\b"))
                throw new ArgumentException("Para un rango escribe 'del 01/09/2026 al 21/09/2026'. Para comparar días, consulta cada día por separado.");
            return Rango(dias[0], dias[^1]);
        }
        var mensual = Regex.Match(t, $@"\b({nombres})(?:\s+(?:de\s+|del\s+)?(\d{{4}}))?\b");
        if (mensual.Success)
        {
            var inicio = Fecha(Anio(mensual.Groups[2]), NumeroMes(mensual.Groups[1].Value), 1);
            return new(inicio, inicio.AddMonths(1));
        }
        if (t.Contains("anteayer")) return new(hoy.AddDays(-2), hoy.AddDays(-1));
        if (Regex.IsMatch(t, @"\bayer\b")) return new(hoy.AddDays(-1), hoy);
        if (Regex.IsMatch(t, @"\b(\d{1,4}|fecha|rango|semana|mes|ano|lunes|martes|miercoles|jueves|viernes|sabado|domingo)\b") && !t.Contains('"'))
            throw new ArgumentException("No pude precisar el período. Indica una fecha (21/09/2026), un rango o 'últimos 7 días'.");
        return new(hoy, hoy.AddDays(1));
    }

    private static PeriodoAsistente Rango(DateTime inicio, DateTime ultimo)
    {
        if (ultimo < inicio) throw new ArgumentException("La fecha final debe ser igual o posterior a la inicial.");
        return new(inicio, ultimo.AddDays(1));
    }
}
