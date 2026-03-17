namespace backend_CLARA.Models
{
    public class FilaCarritoDTO
    {
        public int IdProducto { get; set; }
        public string Producto { get; set; }
        public int Cant { get; set; }
        public decimal P_Unit { get; set; }
        public decimal Subtotal { get; set; }
    }
}
