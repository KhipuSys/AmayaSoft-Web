using System.ComponentModel.DataAnnotations;

namespace MiniMarket.Web.Models;

public class ComprobanteFiscal
{
    [Key] public int VentaId { get; set; }
    public Venta Venta { get; set; }
    [MaxLength(12)] public string Ambiente { get; set; } = "QA";
    [MaxLength(11)] public string EmpresaRuc { get; set; }
    [MaxLength(2)] public string Tipo { get; set; }
    [MaxLength(4)] public string Serie { get; set; }
    public int Correlativo { get; set; }
    [MaxLength(40)] public string Estado { get; set; }
    [MaxLength(2000)] public string Mensaje { get; set; }
    public int? HttpStatus { get; set; }
    [MaxLength(40)] public string Codigo { get; set; }
    [MaxLength(200)] public string Hash { get; set; }
    public string CdrZip { get; set; }
    [MaxLength(40)] public string EstadoAnulacion { get; set; }
    [MaxLength(2000)] public string MensajeAnulacion { get; set; }
    [MaxLength(300)] public string MotivoAnulacion { get; set; }
    public string CdrAnulacionZip { get; set; }
    // Snapshot fiscal: no se reconstruye con productos/clientes editados posteriormente.
    public string SolicitudJson { get; set; }
    public DateTime Actualizado { get; set; }
}
