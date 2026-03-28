namespace backend_CLARA.Models
{
    public class MedicamentoRequest
    {
        public int IdEstatus { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public decimal Precio { get; set; }
        public decimal ConcentracionValor { get; set; }
        public string ConcentracionUnidad { get; set; }
    }
}
