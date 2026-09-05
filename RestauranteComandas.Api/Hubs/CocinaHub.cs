using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RestauranteComandas.Api.Hubs
{
    [Authorize(Roles = "Administrador,Cocina")]
    public class CocinaHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("ConexionExitosa", "Conectado a cocina en tiempo real");
            await base.OnConnectedAsync();
        }
    }
}