namespace RestauranteComandas.Api.Models
{
    public class Mesa
    {
        public int Id { get; set; }

        public int Numero { get; set; }

        public string Estado { get; set; } = "Disponible";

        public ICollection<Orden> Ordenes { get; set; } = new List<Orden>();
    }
}