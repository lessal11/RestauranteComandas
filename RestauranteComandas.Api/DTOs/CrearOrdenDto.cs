namespace RestauranteComandas.Api.DTOs
{
    public class CrearOrdenDto
    {
        public int MesaId { get; set; }

        public List<CrearOrdenDetalleDto> Detalles { get; set; } = new();
    }
}