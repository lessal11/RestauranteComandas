namespace RestauranteComandas.Api.DTOs
{
    public class CrearOrdenDetalleDto
    {
        public int MenuItemId { get; set; }

        public int Cantidad { get; set; }

        public string? DetallePersonalizado { get; set; }
    }
}