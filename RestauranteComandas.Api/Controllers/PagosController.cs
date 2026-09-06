using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.DTOs;
using RestauranteComandas.Api.Hubs;
using RestauranteComandas.Api.Models;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PagosController : ControllerBase
    {
        private readonly RestauranteDbContext _context;
        private readonly IHubContext<CocinaHub> _hubContext;

        public PagosController(RestauranteDbContext context, IHubContext<CocinaHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPagos()
        {
            var pagos = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .OrderByDescending(p => p.FechaPago)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    Mesa = p.Orden != null && p.Orden.Mesa != null ? p.Orden.Mesa.Numero : 0,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .ToListAsync();

            return Ok(pagos);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPago(int id)
        {
            var pago = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    Mesa = p.Orden != null && p.Orden.Mesa != null ? p.Orden.Mesa.Numero : 0,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .FirstOrDefaultAsync();

            if (pago == null)
            {
                return NotFound("Pago no encontrado");
            }

            return Ok(pago);
        }

        [HttpGet("orden/{ordenId}")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPagoPorOrden(int ordenId)
        {
            var pago = await _context.Pagos
                .Where(p => p.OrdenId == ordenId)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .FirstOrDefaultAsync();

            if (pago == null)
            {
                return NotFound("No existe pago registrado para esta orden");
            }

            return Ok(pago);
        }

        [HttpPost]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> RegistrarPago(RegistrarPagoDto dto)
        {
            if (dto.OrdenId <= 0)
            {
                return BadRequest("Debe seleccionar una orden");
            }

            if (string.IsNullOrWhiteSpace(dto.MetodoPago))
            {
                return BadRequest("Debe seleccionar un método de pago");
            }

            if (dto.Monto <= 0)
            {
                return BadRequest("El monto debe ser mayor a 0");
            }

            var metodosPermitidos = new List<string>
            {
                "Efectivo",
                "Transferencia",
                "D1"
            };

            if (!metodosPermitidos.Contains(dto.MetodoPago))
            {
                return BadRequest("Método de pago no permitido");
            }

            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Pago)
                .FirstOrDefaultAsync(o => o.Id == dto.OrdenId);

            if (orden == null)
            {
                return NotFound("La orden seleccionada no existe");
            }

            if (orden.Pago != null)
            {
                return BadRequest("Esta orden ya tiene un pago registrado");
            }

            if (orden.Estado == "Cancelado")
            {
                return BadRequest("No se puede pagar una orden cancelada");
            }

            if (dto.Monto < orden.Total)
            {
                return BadRequest($"El monto pagado no puede ser menor al total de la orden. Total: {orden.Total}");
            }

            var pago = new Pago
            {
                OrdenId = orden.Id,
                MetodoPago = dto.MetodoPago,
                Monto = dto.Monto,
                Referencia = dto.Referencia,
                FechaPago = DateTime.UtcNow,
                EstadoPago = "Confirmado"
            };

            _context.Pagos.Add(pago);

            orden.Estado = "Pagado";

            if (orden.Mesa != null)
            {
                orden.Mesa.Estado = "Disponible";
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("EstadoOrdenActualizado", new
            {
                id = orden.Id,
                estado = orden.Estado
            });

            var comprobanteUrl = $"{Request.Scheme}://{Request.Host}/api/Pagos/orden/{orden.Id}/comprobante";

            return Ok(new
            {
                mensaje = "Pago registrado correctamente",
                pagoId = pago.Id,
                ordenId = orden.Id,
                metodoPago = pago.MetodoPago,
                monto = pago.Monto,
                referencia = pago.Referencia,
                estadoOrden = orden.Estado,
                estadoPago = pago.EstadoPago,
                comprobanteUrl
            });
        }

        [HttpGet("orden/{ordenId}/comprobante")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GenerarComprobantePorOrden(int ordenId)
        {
            var pago = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Usuario)
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Detalles)
                        .ThenInclude(d => d.MenuItem)
                .FirstOrDefaultAsync(p => p.OrdenId == ordenId);

            if (pago == null)
            {
                return NotFound("No existe pago registrado para esta orden");
            }

            if (pago.Orden == null)
            {
                return NotFound("No se encontró la orden asociada al pago");
            }

            var orden = pago.Orden;

            // Ecuador continental UTC-5
            var ecuadorOffset = TimeSpan.FromHours(-5);

            var fechaOrdenEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(orden.Fecha, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var fechaPagoEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(pago.FechaPago, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var html = new StringBuilder();

            html.Append($@"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>

    <title>Pedido #{orden.Id:D3}</title>

    <style>
        * {{
            box-sizing: border-box;
        }}

        @page {{
            size: 58mm auto;
            margin: 2mm;
        }}

        html {{
            margin: 0;
            padding: 0;
            width: 58mm;
            background: white;
        }}

        body {{
            margin: 0;
            padding: 0;
            width: 54mm;
            background: white;
            color: #000;
            font-family: Arial, Helvetica, sans-serif;
            font-size: 10px;
            line-height: 1.25;
        }}

        .ticket {{
            width: 54mm;
            max-width: 54mm;
            margin: 0;
            padding: 1mm;
            background: white;
        }}

        .encabezado {{
            text-align: center;
            padding-bottom: 5px;
        }}

        .restaurante {{
            font-size: 15px;
            font-weight: 900;
            line-height: 1.1;
            margin: 0 0 3px 0;
        }}

        .tipo-documento {{
            font-size: 10px;
            font-weight: 700;
            margin: 0;
        }}

        .pedido {{
            font-size: 14px;
            font-weight: 900;
            margin-top: 3px;
        }}

        .linea {{
            width: 100%;
            border-top: 1px dashed #000;
            margin: 5px 0;
        }}

        .linea-fuerte {{
            width: 100%;
            border-top: 2px solid #000;
            margin: 5px 0;
        }}

        .datos {{
            width: 100%;
        }}

        .fila-dato {{
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            gap: 4px;
            margin: 2px 0;
        }}

        .fila-dato .etiqueta {{
            font-weight: 700;
            white-space: nowrap;
        }}

        .fila-dato .valor {{
            text-align: right;
            overflow-wrap: anywhere;
        }}

        .titulo-detalle {{
            font-size: 11px;
            font-weight: 900;
            text-align: center;
            margin: 5px 0;
        }}

        .cabecera-productos {{
            display: grid;
            grid-template-columns: 1fr 15mm;
            gap: 3px;
            font-size: 9px;
            font-weight: 700;
            padding-bottom: 3px;
            border-bottom: 1px solid #000;
        }}

        .cabecera-productos .precio {{
            text-align: right;
        }}

        .producto {{
            padding: 4px 0;
            border-bottom: 1px dashed #777;
            break-inside: avoid;
        }}

        .producto-principal {{
            display: grid;
            grid-template-columns: 1fr 15mm;
            gap: 3px;
            align-items: start;
        }}

        .producto-nombre {{
            font-size: 10px;
            font-weight: 700;
            line-height: 1.2;
            overflow-wrap: anywhere;
        }}

        .producto-total {{
            font-size: 10px;
            font-weight: 700;
            text-align: right;
            white-space: nowrap;
        }}

        .producto-calculo {{
            margin-top: 2px;
            font-size: 8.5px;
        }}

        .producto-nota {{
            margin-top: 2px;
            font-size: 8.5px;
            font-weight: 600;
            font-style: italic;
            overflow-wrap: anywhere;
        }}

        .total {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 5px;
            padding: 6px 0;
            font-size: 14px;
            font-weight: 900;
        }}

        .pago {{
            font-size: 9px;
        }}

        .estado {{
            font-weight: 700;
        }}

        .pie {{
            margin-top: 7px;
            text-align: center;
            font-size: 8.5px;
            line-height: 1.3;
        }}

        .botones {{
            margin-top: 12px;
            text-align: center;
        }}

        button {{
            padding: 9px 12px;
            border: none;
            border-radius: 5px;
            background: #111;
            color: white;
            font-weight: bold;
            cursor: pointer;
        }}

        @media print {{
            html {{
                width: 58mm !important;
                margin: 0 !important;
                padding: 0 !important;
            }}

            body {{
                width: 54mm !important;
                margin: 0 !important;
                padding: 0 !important;
                background: white !important;
            }}

            .ticket {{
                width: 54mm !important;
                max-width: 54mm !important;
                margin: 0 !important;
                padding: 1mm !important;
            }}

            .botones {{
                display: none !important;
            }}
        }}
    </style>
</head>

<body>

<div class='ticket'>

    <div class='encabezado'>
        <div class='restaurante'>LA SUPER CORVINA</div>
        <div class='tipo-documento'>COMPROBANTE DE PAGO</div>
        <div class='pedido'>PEDIDO #{orden.Id:D3}</div>
    </div>

    <div class='linea-fuerte'></div>

    <div class='datos'>

        <div class='fila-dato'>
            <span class='etiqueta'>Mesa:</span>
            <span class='valor'>{(orden.Mesa != null ? orden.Mesa.Numero : 0)}</span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Mesero:</span>
            <span class='valor'>{(orden.Usuario != null ? orden.Usuario.Nombre : "No registrado")}</span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Fecha:</span>
            <span class='valor'>{fechaOrdenEcuador:dd/MM/yyyy HH:mm}</span>
        </div>

    </div>

    <div class='linea'></div>

    <div class='titulo-detalle'>
        DETALLE DEL PEDIDO
    </div>

    <div class='cabecera-productos'>
        <div>Producto</div>
        <div class='precio'>Total</div>
    </div>
");

            foreach (var detalle in orden.Detalles)
            {
                var nombreProducto = detalle.MenuItem != null
                    ? detalle.MenuItem.Nombre
                    : "Producto";

                html.Append($@"
    <div class='producto'>

        <div class='producto-principal'>

            <div class='producto-nombre'>
                {detalle.Cantidad}x {nombreProducto}
            </div>

            <div class='producto-total'>
                ${detalle.Subtotal:F2}
            </div>

        </div>

        <div class='producto-calculo'>
            {detalle.Cantidad} x ${detalle.PrecioUnitario:F2}
        </div>

        {(string.IsNullOrWhiteSpace(detalle.DetallePersonalizado)
                    ? ""
                    : $"<div class='producto-nota'>Nota: {detalle.DetallePersonalizado}</div>")}

    </div>
");
            }

            html.Append($@"

    <div class='linea-fuerte'></div>

    <div class='total'>
        <span>TOTAL</span>
        <span>${pago.Monto:F2}</span>
    </div>

    <div class='linea-fuerte'></div>

    <div class='pago'>

        <div class='fila-dato'>
            <span class='etiqueta'>Forma de pago:</span>
            <span class='valor'>{pago.MetodoPago}</span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Referencia:</span>
            <span class='valor'>
                {(string.IsNullOrWhiteSpace(pago.Referencia)
                            ? "Sin referencia"
                            : pago.Referencia)}
            </span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Fecha pago:</span>
            <span class='valor'>{fechaPagoEcuador:dd/MM/yyyy HH:mm}</span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Estado:</span>
            <span class='valor estado'>{pago.EstadoPago}</span>
        </div>

        <div class='fila-dato'>
            <span class='etiqueta'>Comprobante:</span>
            <span class='valor'>#{pago.Id:D3}</span>
        </div>

    </div>

    <div class='linea'></div>

    <div class='pie'>
        ¡Gracias por su preferencia!<br>
        LA SUPER CORVINA
    </div>

    <div class='botones'>
        <button onclick='window.print()'>
            Imprimir comprobante
        </button>
    </div>

</div>

</body>
</html>
");

            return Content(html.ToString(), "text/html; charset=utf-8");
        }
    }
}