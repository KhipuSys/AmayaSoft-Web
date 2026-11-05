namespace MiniMarket.Web.Services;

public static class ImporteEnLetras
{
    public static string Soles(decimal importe)
    {
        if (importe < 0 || importe >= 1000000000m) throw new InvalidOperationException("Importe fiscal fuera de rango.");
        return $"SON {Entero((int)decimal.Truncate(importe))} CON {(int)((importe % 1) * 100):00}/100 SOLES";
    }

    private static string Entero(int n)
    {
        string[] unidades = ["CERO", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
        if (n < 30) return unidades[n];
        if (n < 100) return new[] { "", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" }[n / 10] + (n % 10 == 0 ? "" : " Y " + Entero(n % 10));
        if (n == 100) return "CIEN";
        if (n < 1000) return new[] { "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" }[n / 100] + (n % 100 == 0 ? "" : " " + Entero(n % 100));
        if (n < 1000000) return (n < 2000 ? "MIL" : Apocopar(Entero(n / 1000)) + " MIL") + (n % 1000 == 0 ? "" : " " + Entero(n % 1000));
        return (n < 2000000 ? "UN MILLÓN" : Apocopar(Entero(n / 1000000)) + " MILLONES") + (n % 1000000 == 0 ? "" : " " + Entero(n % 1000000));
    }
    private static string Apocopar(string n) => n.EndsWith("VEINTIUNO") ? n[..^9] + "VEINTIÚN" : n.EndsWith("UNO") ? n[..^3] + "UN" : n;
}
