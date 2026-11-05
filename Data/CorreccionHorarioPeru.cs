using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MiniMarket.Web.Data;

public static class CorreccionHorarioPeru
{
    // Llamar dentro de la transacción/bloqueo de actualización del esquema, antes de sembrar datos.
    public static async Task Aplicar(ApplicationDbContext db, string zonaAnterior = null)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Se requiere una transacción para convertir el historial.");
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('AjustesHorario', 'U') IS NULL
                CREATE TABLE AjustesHorario (Nombre nvarchar(50) NOT NULL PRIMARY KEY, ZonaOrigen nvarchar(150) NOT NULL, AplicadoUtc datetime2 NOT NULL);
            IF OBJECT_ID('RespaldoFechasPeru', 'U') IS NULL
                CREATE TABLE RespaldoFechasPeru (
                    Tabla nvarchar(64) NOT NULL, RegistroId int NOT NULL, Campo nvarchar(64) NOT NULL,
                    FechaOriginal datetime2 NOT NULL, FechaPeru datetime2 NOT NULL,
                    CONSTRAINT PK_RespaldoFechasPeru PRIMARY KEY (Tabla, RegistroId, Campo));
            """);
        if (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM AjustesHorario WHERE Nombre='PeruV1'").SingleAsync() > 0) return;

        var origen = zonaAnterior ?? TimeZoneInfo.Local.Id;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(origen, out var windowsId)) origen = windowsId;
        if (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.time_zone_info WHERE name={origen}").SingleAsync() != 1)
            throw new InvalidOperationException("La zona anterior del servidor no es reconocida por SQL Server. No se modificaron las fechas.");

        // Lista cerrada de identificadores internos; solo la zona horaria se recibe como parámetro SQL.
        var campos = new[] { ("Ventas", "Fecha"), ("Ventas", "FechaAnulacion"),
            ("Compras", "Fecha"), ("Gastos", "Fecha"), ("MovimientosInventario", "Fecha"),
            ("AperturasCaja", "FechaApertura"), ("AperturasCaja", "FechaCierre") };
        foreach (var (tabla, campo) in campos)
        {
            // EF1002: los identificadores provienen exclusivamente de la lista constante anterior;
            // no pueden parametrizarse como valores SQL. La zona sí se envía como parámetro.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO RespaldoFechasPeru(Tabla, RegistroId, Campo, FechaOriginal, FechaPeru)
                SELECT '{tabla}', Id, '{campo}', [{campo}],
                    CAST(([{campo}] AT TIME ZONE @origen) AT TIME ZONE 'SA Pacific Standard Time' AS datetime2)
                FROM [{tabla}] WHERE [{campo}] IS NOT NULL;
                UPDATE [{tabla}] SET [{campo}]=CAST(([{campo}] AT TIME ZONE @origen) AT TIME ZONE 'SA Pacific Standard Time' AS datetime2)
                WHERE [{campo}] IS NOT NULL;
                """, new SqlParameter("@origen", origen));
#pragma warning restore EF1002
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AjustesHorario(Nombre,ZonaOrigen,AplicadoUtc) VALUES ('PeruV1',{origen},SYSUTCDATETIME())");
    }
}
