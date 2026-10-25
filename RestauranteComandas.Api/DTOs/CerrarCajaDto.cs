namespace RestauranteComandas.Api.DTOs
{
    public class CerrarCajaDto
    {
        public decimal EfectivoContado { get; set; }

        public string? ObservacionCierre { get; set; }
        public List<ConteoDenominacionDto> Denominaciones { get; set; } =
                 new();
    }
}