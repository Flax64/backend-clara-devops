namespace backend_CLARA.Models
{
    public class NuevaCompraRequest
    {
        public int IdProveedor { get; set; }
        public decimal TotalCompra { get; set; }
        public List<DetalleItem> Detalles { get; set; }
    }
}


