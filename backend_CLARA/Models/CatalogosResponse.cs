namespace backend_CLARA.Models
{
    public class CatalogosResponse
    {
        public List<CatalogoItem> Roles { get; set; } = new List<CatalogoItem>();
        public List<CatalogoItem> Generos { get; set; } = new List<CatalogoItem>();
        public List<CatalogoItem> Estatus { get; set; } = new List<CatalogoItem>();
    }
}
