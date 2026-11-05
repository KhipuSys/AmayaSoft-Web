namespace MiniMarket.Web.Models
{
    public class ErrorViewModel
    {
        public string RequestId { get; set; } // Se quitó el ?

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}