namespace RestauranteComandas.Api.Models
{
    public class MenuItem
    {
        public int Id { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string Categoria { get; set; } = string.Empty;

        public decimal Precio { get; set; }

        public bool Disponible { get; set; } = true;

        public ICollection<OrdenDetalle> OrdenDetalles { get; set; } = new List<OrdenDetalle>();
    }
}