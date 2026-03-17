namespace backend_CLARA.Models
{
    public class NuevaVentaRequest
    {
        public int IdUsuario { get; set; } // El vendedor (Ej. 1)
        public int IdMetodoPago { get; set; } // Ej. 1 para Efectivo
        public string NombreCliente { get; set; }
        public decimal TotalVenta { get; set; }
        public int? IdConsulta { get; set; }
        public List<DetalleNuevaVenta> Detalles { get; set; }
    }
}
