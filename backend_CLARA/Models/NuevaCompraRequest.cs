namespace backend_CLARA.Models
{
    public class NuevaCompraRequest
    {
        public int IdProveedor { get; set;  }
        public decimal TotalCompra { get; set; }
        public List<DetalleNuevaCompra> Detalles { get; set; }
    }
    public class DetalleNuevaCompra
    {
        public int IdMedicamento { get; set;}
        public int Cantidad {  get; set;}

    }
}


