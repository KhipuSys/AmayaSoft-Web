using Microsoft.EntityFrameworkCore;

namespace MiniMarket.Web.Data;

public static class ActualizacionFiscal
{
    // Aditivo y explícito: también sirve para las instalaciones EnsureCreated existentes.
    public static Task Aplicar(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('ComprobantesFiscales', 'U') IS NULL
        BEGIN
            CREATE TABLE ComprobantesFiscales (
                VentaId int NOT NULL PRIMARY KEY REFERENCES Ventas(Id),
                Ambiente nvarchar(12) NOT NULL,
                EmpresaRuc nvarchar(11) NOT NULL,
                Tipo nvarchar(2) NOT NULL,
                Serie nvarchar(4) NOT NULL,
                Correlativo int NOT NULL,
                Estado nvarchar(40) NOT NULL,
                Mensaje nvarchar(2000) NULL,
                HttpStatus int NULL,
                Codigo nvarchar(40) NULL,
                Hash nvarchar(200) NULL,
                CdrZip nvarchar(max) NULL,
                SolicitudJson nvarchar(max) NOT NULL,
                Actualizado datetime2 NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ComprobantesFiscales_Numero' AND object_id=OBJECT_ID('ComprobantesFiscales'))
            CREATE UNIQUE INDEX IX_ComprobantesFiscales_Numero ON ComprobantesFiscales(Ambiente, EmpresaRuc, Serie, Correlativo);
        IF COL_LENGTH('ComprobantesFiscales','EstadoAnulacion') IS NULL ALTER TABLE ComprobantesFiscales ADD EstadoAnulacion nvarchar(40) NULL;
        IF COL_LENGTH('ComprobantesFiscales','MensajeAnulacion') IS NULL ALTER TABLE ComprobantesFiscales ADD MensajeAnulacion nvarchar(2000) NULL;
        IF COL_LENGTH('ComprobantesFiscales','MotivoAnulacion') IS NULL ALTER TABLE ComprobantesFiscales ADD MotivoAnulacion nvarchar(300) NULL;
        IF COL_LENGTH('ComprobantesFiscales','CdrAnulacionZip') IS NULL ALTER TABLE ComprobantesFiscales ADD CdrAnulacionZip nvarchar(max) NULL;
        """);
}
