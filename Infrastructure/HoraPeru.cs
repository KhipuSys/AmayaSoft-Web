namespace MiniMarket.Web.Infrastructure;

/// <summary>Fechas comerciales almacenadas como hora civil de Perú (sin zona del servidor).</summary>
public static class HoraPeru
{
    public static DateTime Ahora => DesdeInstante(DateTimeOffset.UtcNow);
    public static DateTime Hoy => Ahora.Date;
    public static DateTime DesdeInstante(DateTimeOffset instante) => instante.ToOffset(TimeSpan.FromHours(-5)).DateTime;
}
