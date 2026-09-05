namespace RestauranteComandas.Api.DTOs
{
    public class CrearUsuarioDto
    {
        public string Nombre { get; set; } = string.Empty;

        public string NombreUsuario { get; set; } = string.Empty;

        public string Clave { get; set; } = string.Empty;

        public string Rol { get; set; } = "Mesero";
    }
}