namespace backend_CLARA.Models
{
    public class VentaDTO
    {
        public int Id { get; set; }
        public string FechaHora { get; set; }
        public string Cliente { get; set; }
        public string Vendedor { get; set; }
        public decimal Total { get; set; }
        public string Metodo { get; set; }
    }
}
