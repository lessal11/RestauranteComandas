namespace RestauranteComandas.Api.DTOs
{
    public class AgregarDetallesOrdenDto
    {
        public List<CrearOrdenDetalleDto> Detalles { get; set; } = new();
    }
}