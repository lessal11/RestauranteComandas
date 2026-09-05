namespace RestauranteComandas.Api.Models
{
    public class Usuario
    {
        public int Id { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string NombreUsuario { get; set; } = string.Empty;

        public string ClaveHash { get; set; } = string.Empty;

        public string Rol { get; set; } = "Mesero";

        public bool Activo { get; set; } = true;

        public ICollection<Orden> Ordenes { get; set; } = new List<Orden>();
    }
}