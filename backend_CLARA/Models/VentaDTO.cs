namespace backend_CLARA.Models
{
    public class VentaDTO
    {
        public int Id { get; set; }
        public string Fecha { get; set; } // Usaremos esta para el 17/03/2026
        public string Hora { get; set; }  // Usaremos esta para las 02:57 PM
        public string Cliente { get; set; }
        public string Vendedor { get; set; }
        public decimal Total { get; set; }
        public string Metodo { get; set; }
        public string Estatus { get; set; }
    }
}
