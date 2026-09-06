using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.DTOs;
using RestauranteComandas.Api.Hubs;
using RestauranteComandas.Api.Models;
using System.Security.Claims;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdenesController : ControllerBase
    {
        private static readonly string[] EstadosOrdenActiva =
        {
            "Pendiente",
            "En preparación",
            "Listo"
        };

        private readonly RestauranteDbContext _context;
        private readonly IHubContext<CocinaHub> _hubContext;

        public OrdenesController(
            RestauranteDbContext context,
            IHubContext<CocinaHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetOrdenes()
        {
            var ordenes = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .OrderByDescending(o => o.Fecha)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null ? o.Usuario.Nombre : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null ? d.MenuItem.Nombre : "",
                        d.Cantidad,
                        d.PrecioUnitario,
                        d.Subtotal,
                        d.DetallePersonalizado
                    })
                })
                .ToListAsync();

            return Ok(ordenes);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador,Mesero,Caja,Cocina")]
        public async Task<IActionResult> GetOrden(int id)
        {
            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .Where(o => o.Id == id)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null ? o.Usuario.Nombre : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null ? d.MenuItem.Nombre : "",
                        d.Cantidad,
                        d.PrecioUnitario,
                        d.Subtotal,
                        d.DetallePersonalizado
                    })
                })
                .FirstOrDefaultAsync();

            if (orden == null)
            {
                return NotFound("Orden no encontrada");
            }

            return Ok(orden);
        }

        // FASE 2:
        // Permite al mesero saber si una mesa ya tiene una orden abierta.
        [HttpGet("mesa/{mesaId}/activa")]
        [Authorize(Roles = "Administrador,Mesero,Caja")]
        public async Task<IActionResult> GetOrdenActivaPorMesa(int mesaId)
        {
            if (mesaId <= 0)
            {
                return BadRequest("Mesa no válida");
            }

            var mesaExiste = await _context.Mesas
                .AnyAsync(m => m.Id == mesaId);

            if (!mesaExiste)
            {
                return NotFound("La mesa seleccionada no existe");
            }

            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .Where(o =>
                    o.MesaId == mesaId &&
                    EstadosOrdenActiva.Contains(o.Estado))
                .OrderByDescending(o => o.Fecha)
                .Select(o => new
                {
                    o.Id,
                    o.MesaId,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null ? o.Usuario.Nombre : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles
                        .OrderBy(d => d.Id)
                        .Select(d => new
                        {
                            d.Id,
                            d.MenuItemId,
                            Plato = d.MenuItem != null
                                ? d.MenuItem.Nombre
                                : "Producto",
                            d.Cantidad,
                            d.PrecioUnitario,
                            d.Subtotal,
                            d.DetallePersonalizado
                        })
                })
                .FirstOrDefaultAsync();

            if (orden == null)
            {
                // No existe una orden activa para esa mesa.
                // El frontend puede crear una nueva normalmente.
                return NoContent();
            }

            return Ok(orden);
        }

        [HttpGet("pendientes")]
        [Authorize(Roles = "Administrador,Cocina,Caja")]
        public async Task<IActionResult> GetOrdenesPendientes()
        {
            var ordenes = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .Where(o =>
                    o.Estado == "Pendiente" ||
                    o.Estado == "En preparación")
                .OrderBy(o => o.Fecha)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null ? d.MenuItem.Nombre : "",
                        d.Cantidad,
                        d.DetallePersonalizado
                    })
                })
                .ToListAsync();

            return Ok(ordenes);
        }

        [HttpPost]
        [Authorize(Roles = "Administrador,Mesero")]
        public async Task<IActionResult> CrearOrden(CrearOrdenDto dto)
        {
            if (dto.MesaId <= 0)
            {
                return BadRequest("Debe seleccionar una mesa");
            }

            if (dto.Detalles == null || !dto.Detalles.Any())
            {
                return BadRequest("La orden debe tener al menos un plato");
            }

            var mesa = await _context.Mesas.FindAsync(dto.MesaId);

            if (mesa == null)
            {
                return NotFound("La mesa seleccionada no existe");
            }

            // Evita crear dos órdenes abiertas para la misma mesa.
            // Si ya existe una, se deben agregar productos a esa orden.
            var existeOrdenActiva = await _context.Ordenes
                .AnyAsync(o =>
                    o.MesaId == dto.MesaId &&
                    EstadosOrdenActiva.Contains(o.Estado));

            if (existeOrdenActiva)
            {
                return Conflict(
                    "La mesa ya tiene una orden activa. Agregue los nuevos productos al pedido existente."
                );
            }

            var usuarioIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(usuarioIdClaim, out var usuarioId))
            {
                return Unauthorized(
                    "No se pudo identificar al usuario autenticado"
                );
            }

            var usuario = await _context.Usuarios.FindAsync(usuarioId);

            if (usuario == null || !usuario.Activo)
            {
                return Unauthorized(
                    "El usuario autenticado no existe o está inactivo"
                );
            }

            var orden = new Orden
            {
                MesaId = dto.MesaId,
                UsuarioId = usuarioId,
                Fecha = DateTime.UtcNow,
                Estado = "Pendiente",
                Total = 0
            };

            decimal total = 0;

            foreach (var detalleDto in dto.Detalles)
            {
                if (detalleDto.Cantidad <= 0)
                {
                    return BadRequest(
                        "La cantidad debe ser mayor a 0"
                    );
                }

                var menuItem = await _context.MenuItems
                    .FindAsync(detalleDto.MenuItemId);

                if (menuItem == null)
                {
                    return NotFound(
                        $"El plato con ID {detalleDto.MenuItemId} no existe"
                    );
                }

                if (!menuItem.Disponible)
                {
                    return BadRequest(
                        $"El plato {menuItem.Nombre} no está disponible"
                    );
                }

                var subtotal =
                    menuItem.Precio * detalleDto.Cantidad;

                var detalle = new OrdenDetalle
                {
                    MenuItemId = menuItem.Id,
                    Cantidad = detalleDto.Cantidad,
                    PrecioUnitario = menuItem.Precio,
                    Subtotal = subtotal,
                    DetallePersonalizado =
                        detalleDto.DetallePersonalizado
                };

                orden.Detalles.Add(detalle);
                total += subtotal;
            }

            orden.Total = total;

            _context.Ordenes.Add(orden);
            mesa.Estado = "Ocupada";

            await _context.SaveChangesAsync();

            var ordenNotificacion = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .Where(o => o.Id == orden.Id)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null
                        ? o.Usuario.Nombre
                        : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null
                            ? d.MenuItem.Nombre
                            : "",
                        d.Cantidad,
                        d.DetallePersonalizado
                    })
                })
                .FirstOrDefaultAsync();

            await _hubContext.Clients.All.SendAsync(
                "NuevaOrden",
                ordenNotificacion
            );

            return CreatedAtAction(
                nameof(GetOrden),
                new { id = orden.Id },
                new
                {
                    mensaje = "Orden creada correctamente",
                    ordenId = orden.Id,
                    total = orden.Total,
                    estado = orden.Estado
                }
            );
        }

        // FASE 2:
        // Agrega únicamente los nuevos productos a una orden ya existente.
        [HttpPost("{id}/detalles")]
        [Authorize(Roles = "Administrador,Mesero")]
        public async Task<IActionResult> AgregarDetallesOrden(
            int id,
            AgregarDetallesOrdenDto dto)
        {
            if (dto.Detalles == null || !dto.Detalles.Any())
            {
                return BadRequest(
                    "Debe agregar al menos un producto"
                );
            }

            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (orden == null)
            {
                return NotFound("Orden no encontrada");
            }

            if (!EstadosOrdenActiva.Contains(orden.Estado))
            {
                return BadRequest(
                    $"No se pueden agregar productos a una orden en estado {orden.Estado}"
                );
            }

            decimal totalAgregado = 0;
            var detallesNotificacion = new List<object>();

            foreach (var detalleDto in dto.Detalles)
            {
                if (detalleDto.Cantidad <= 0)
                {
                    return BadRequest(
                        "La cantidad debe ser mayor a 0"
                    );
                }

                var menuItem = await _context.MenuItems
                    .FindAsync(detalleDto.MenuItemId);

                if (menuItem == null)
                {
                    return NotFound(
                        $"El plato con ID {detalleDto.MenuItemId} no existe"
                    );
                }

                if (!menuItem.Disponible)
                {
                    return BadRequest(
                        $"El plato {menuItem.Nombre} no está disponible"
                    );
                }

                var subtotal =
                    menuItem.Precio * detalleDto.Cantidad;

                var detalle = new OrdenDetalle
                {
                    OrdenId = orden.Id,
                    MenuItemId = menuItem.Id,
                    Cantidad = detalleDto.Cantidad,
                    PrecioUnitario = menuItem.Precio,
                    Subtotal = subtotal,
                    DetallePersonalizado =
                        detalleDto.DetallePersonalizado
                };

                _context.OrdenDetalles.Add(detalle);

                totalAgregado += subtotal;

                detallesNotificacion.Add(new
                {
                    plato = menuItem.Nombre,
                    cantidad = detalleDto.Cantidad,
                    precioUnitario = menuItem.Precio,
                    subtotal,
                    detallePersonalizado =
                        detalleDto.DetallePersonalizado
                });
            }

            var estadoAnterior = orden.Estado;

            orden.Total += totalAgregado;

            if (orden.Mesa != null)
            {
                orden.Mesa.Estado = "Ocupada";
            }

            // Si cocina ya había marcado la orden como lista,
            // los nuevos productos necesitan preparación otra vez.
            if (orden.Estado == "Listo")
            {
                orden.Estado = "Pendiente";
            }

            var fechaAgregado = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (estadoAnterior != orden.Estado)
            {
                await _hubContext.Clients.All.SendAsync(
                    "EstadoOrdenActualizado",
                    new
                    {
                        id = orden.Id,
                        estado = orden.Estado
                    }
                );
            }

            // Esta notificación contiene SOLO los productos nuevos.
            await _hubContext.Clients.All.SendAsync(
                "ProductosAgregados",
                new
                {
                    id = orden.Id,
                    mesa = orden.Mesa != null
                        ? orden.Mesa.Numero
                        : 0,
                    estado = orden.Estado,
                    fecha = fechaAgregado,
                    total = orden.Total,
                    detalles = detallesNotificacion
                }
            );

            return Ok(new
            {
                mensaje =
                    "Productos agregados correctamente a la orden",
                ordenId = orden.Id,
                totalAgregado,
                totalOrden = orden.Total,
                estado = orden.Estado
            });
        }

        [HttpPut("{id}/estado")]
        [Authorize(Roles = "Administrador,Cocina,Caja")]
        public async Task<IActionResult> ActualizarEstadoOrden(
            int id,
            [FromBody] string nuevoEstado)
        {
            var estadosPermitidos = new List<string>
            {
                "Pendiente",
                "En preparación",
                "Listo",
                "Pagado",
                "Cancelado"
            };

            if (!estadosPermitidos.Contains(nuevoEstado))
            {
                return BadRequest("Estado no permitido");
            }

            var rolUsuario =
                User.FindFirst(ClaimTypes.Role)?.Value;

            if (rolUsuario == "Cocina")
            {
                var estadosPermitidosCocina = new List<string>
                {
                    "En preparación",
                    "Listo"
                };

                if (!estadosPermitidosCocina.Contains(nuevoEstado))
                {
                    return Unauthorized(
                        "Cocina solo puede cambiar la orden a En preparación o Listo"
                    );
                }
            }

            if (rolUsuario == "Caja")
            {
                var estadosPermitidosCaja = new List<string>
                {
                    "Pagado"
                };

                if (!estadosPermitidosCaja.Contains(nuevoEstado))
                {
                    return Unauthorized(
                        "Caja solo puede cambiar la orden a Pagado"
                    );
                }
            }

            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (orden == null)
            {
                return NotFound("Orden no encontrada");
            }

            orden.Estado = nuevoEstado;

            if (
                (nuevoEstado == "Pagado" ||
                 nuevoEstado == "Cancelado") &&
                orden.Mesa != null)
            {
                orden.Mesa.Estado = "Disponible";
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync(
                "EstadoOrdenActualizado",
                new
                {
                    id = orden.Id,
                    estado = orden.Estado
                }
            );

            return Ok(new
            {
                mensaje = "Estado actualizado correctamente",
                orden.Id,
                orden.Estado
            });
        }

        [HttpGet("por-cobrar")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetOrdenesPorCobrar()
        {
            var ordenes = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .Where(o =>
                    o.Estado != "Pagado" &&
                    o.Estado != "Cancelado")
                .OrderByDescending(o => o.Fecha)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null ? o.Usuario.Nombre : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null ? d.MenuItem.Nombre : "",
                        d.Cantidad,
                        d.PrecioUnitario,
                        d.Subtotal,
                        d.DetallePersonalizado
                    })
                })
                .ToListAsync();

            return Ok(ordenes);
        }

        [HttpGet("historial")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetHistorialOrdenes(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] string? estado,
            [FromQuery] int? mesaId)
        {
            var query = _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Usuario)
                .Include(o => o.Pago)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .AsQueryable();

            // Ecuador UTC-5
            var ecuadorOffset = TimeSpan.FromHours(-5);

            if (desde.HasValue)
            {
                var inicioDesdeUtc = new DateTimeOffset(
                    desde.Value.Year,
                    desde.Value.Month,
                    desde.Value.Day,
                    0, 0, 0,
                    ecuadorOffset
                ).UtcDateTime;

                query = query.Where(
                    o => o.Fecha >= inicioDesdeUtc
                );
            }

            if (hasta.HasValue)
            {
                var finHastaUtc = new DateTimeOffset(
                    hasta.Value.Year,
                    hasta.Value.Month,
                    hasta.Value.Day,
                    0, 0, 0,
                    ecuadorOffset
                )
                .AddDays(1)
                .UtcDateTime;

                query = query.Where(
                    o => o.Fecha < finHastaUtc
                );
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                query = query.Where(
                    o => o.Estado == estado
                );
            }

            if (mesaId.HasValue && mesaId.Value > 0)
            {
                query = query.Where(
                    o => o.MesaId == mesaId.Value
                );
            }

            var ordenes = await query
                .OrderByDescending(o => o.Fecha)
                .Select(o => new
                {
                    o.Id,
                    Mesa = o.Mesa != null ? o.Mesa.Numero : 0,
                    Mesero = o.Usuario != null ? o.Usuario.Nombre : "",
                    o.Fecha,
                    o.Estado,
                    o.Total,
                    Pago = o.Pago == null ? null : new
                    {
                        o.Pago.Id,
                        o.Pago.MetodoPago,
                        o.Pago.Monto,
                        o.Pago.Referencia,
                        o.Pago.FechaPago,
                        o.Pago.EstadoPago
                    },
                    Detalles = o.Detalles.Select(d => new
                    {
                        d.Id,
                        d.MenuItemId,
                        Plato = d.MenuItem != null ? d.MenuItem.Nombre : "",
                        d.Cantidad,
                        d.PrecioUnitario,
                        d.Subtotal,
                        d.DetallePersonalizado
                    })
                })
                .ToListAsync();

            return Ok(ordenes);
        }

        [HttpGet("resumen")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> GetResumenAdministrativo()
        {
            var ecuadorOffset = TimeSpan.FromHours(-5);

            var ahoraEcuador =
                DateTimeOffset.UtcNow.ToOffset(ecuadorOffset);

            var inicioHoyUtc = new DateTimeOffset(
                ahoraEcuador.Year,
                ahoraEcuador.Month,
                ahoraEcuador.Day,
                0, 0, 0,
                ecuadorOffset
            ).UtcDateTime;

            var finHoyUtc = new DateTimeOffset(
                ahoraEcuador.Year,
                ahoraEcuador.Month,
                ahoraEcuador.Day,
                0, 0, 0,
                ecuadorOffset
            )
            .AddDays(1)
            .UtcDateTime;

            var ordenesHoy = await _context.Ordenes
                .Include(o => o.Pago)
                .Where(o =>
                    o.Fecha >= inicioHoyUtc &&
                    o.Fecha < finHoyUtc)
                .ToListAsync();

            var totalOrdenes = ordenesHoy.Count;

            var ordenesPagadas = ordenesHoy.Count(
                o => o.Estado == "Pagado"
            );

            var ordenesPendientes = ordenesHoy.Count(
                o =>
                    o.Estado != "Pagado" &&
                    o.Estado != "Cancelado"
            );

            var ordenesCanceladas = ordenesHoy.Count(
                o => o.Estado == "Cancelado"
            );

            var ventasHoy = ordenesHoy
                .Where(o => o.Estado == "Pagado")
                .Sum(o => o.Total);

            var pagosEfectivo = ordenesHoy
                .Where(o =>
                    o.Pago != null &&
                    o.Pago.MetodoPago == "Efectivo")
                .Sum(o => o.Pago!.Monto);

            var pagosTransferencia = ordenesHoy
                .Where(o =>
                    o.Pago != null &&
                    o.Pago.MetodoPago == "Transferencia")
                .Sum(o => o.Pago!.Monto);

            var pagosD1 = ordenesHoy
                .Where(o =>
                    o.Pago != null &&
                    o.Pago.MetodoPago == "D1")
                .Sum(o => o.Pago!.Monto);

            return Ok(new
            {
                fecha = ahoraEcuador.ToString("yyyy-MM-dd"),
                totalOrdenes,
                ordenesPagadas,
                ordenesPendientes,
                ordenesCanceladas,
                ventasHoy,
                pagos = new
                {
                    efectivo = pagosEfectivo,
                    transferencia = pagosTransferencia,
                    d1 = pagosD1
                }
            });
        }
    }
}
