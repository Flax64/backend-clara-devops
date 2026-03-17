namespace backend_CLARA.Models
{
    public class VentaCompletaDTO
    {
        public int IdVenta { get; set; }
        public string NombreCliente { get; set; }
        public int IdMetodoPago { get; set; }
        public List<FilaCarritoDTO> Detalles { get; set; } = new List<FilaCarritoDTO>();
    }
}
