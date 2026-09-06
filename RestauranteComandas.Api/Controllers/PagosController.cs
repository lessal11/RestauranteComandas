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
        size: auto;
        margin: 6mm;
    }}

    html,
    body {{
        width: 100%;
        margin: 0;
        padding: 0;
        background: #fff;
        color: #000;
        font-family: Arial, Helvetica, sans-serif;
    }}

    body {{
        font-size: 18px;
        line-height: 1.25;
    }}

    .ticket {{
        width: 100%;
        max-width: none;
        margin: 0;
        padding: 0;
        background: #fff;
    }}

    .encabezado {{
        width: 100%;
        text-align: center;
        padding: 0 0 14px 0;
    }}

    .restaurante {{
        margin: 0;
        font-size: 34px;
        line-height: 1.05;
        font-weight: 900;
    }}

    .tipo-documento {{
        margin-top: 7px;
        font-size: 22px;
        font-weight: 900;
    }}

    .pedido {{
        margin-top: 6px;
        font-size: 30px;
        line-height: 1.05;
        font-weight: 900;
    }}

    .linea-fuerte {{
        width: 100%;
        border-top: 4px solid #000;
        margin: 14px 0;
    }}

    .linea {{
        width: 100%;
        border-top: 2px dashed #000;
        margin: 10px 0;
    }}

    .datos {{
        width: 100%;
        font-size: 20px;
    }}

    .fila-dato {{
        display: flex;
        width: 100%;
        justify-content: space-between;
        align-items: flex-start;
        gap: 15px;
        margin: 7px 0;
    }}

    .fila-dato .etiqueta {{
        font-weight: 900;
        white-space: nowrap;
    }}

    .fila-dato .valor {{
        flex: 1;
        text-align: right;
        font-weight: 600;
        overflow-wrap: anywhere;
    }}

    .cabecera-productos {{
        display: grid;
        grid-template-columns: minmax(0, 1fr) 25%;
        width: 100%;
        gap: 15px;
        padding: 8px 0 10px 0;
        font-size: 20px;
        line-height: 1.05;
        font-weight: 900;
        border-bottom: 4px solid #000;
    }}

    .cabecera-productos .precio {{
        text-align: right;
    }}

    .producto {{
        width: 100%;
        padding: 13px 0;
        border-bottom: 2px dashed #666;

        break-inside: avoid;
        page-break-inside: avoid;
    }}

    .producto-principal {{
        display: grid;
        grid-template-columns: minmax(0, 1fr) 25%;
        gap: 15px;
        width: 100%;
        align-items: start;
    }}

    .producto-nombre {{
        min-width: 0;
        font-size: 22px;
        line-height: 1.15;
        font-weight: 900;
        overflow-wrap: anywhere;
    }}

    .producto-total {{
        font-size: 22px;
        line-height: 1.15;
        font-weight: 900;
        text-align: right;
        white-space: nowrap;
    }}

    .producto-calculo {{
        margin-top: 5px;
        font-size: 17px;
        font-weight: 500;
    }}

    .producto-nota {{
        margin-top: 7px;
        padding: 6px 0;
        font-size: 17px;
        line-height: 1.2;
        font-weight: 700;
        font-style: italic;
        overflow-wrap: anywhere;
    }}

    .total {{
        display: flex;
        justify-content: space-between;
        align-items: center;
        width: 100%;
        gap: 15px;
        padding: 14px 0;
        font-size: 30px;
        line-height: 1.1;
        font-weight: 900;

        break-inside: avoid;
        page-break-inside: avoid;
    }}

    .pago {{
        width: 100%;
        font-size: 18px;

        break-inside: avoid;
        page-break-inside: avoid;
    }}

    .estado {{
        font-weight: 900;
    }}

    .pie {{
        width: 100%;
        margin-top: 15px;
        padding-top: 12px;
        border-top: 2px dashed #000;
        text-align: center;
        font-size: 18px;
        line-height: 1.3;
        font-weight: 700;

        break-inside: avoid;
        page-break-inside: avoid;
    }}

    .botones {{
        margin-top: 20px;
        text-align: center;
    }}

    button {{
        padding: 12px 20px;
        border: none;
        background: #000;
        color: #fff;
        font-size: 16px;
        font-weight: bold;
        cursor: pointer;
    }}

    @media print {{

        html,
        body {{
            width: 100% !important;
            margin: 0 !important;
            padding: 0 !important;
            background: #fff !important;
        }}

        .ticket {{
            width: 100% !important;
            max-width: none !important;
            margin: 0 !important;
            padding: 0 !important;
        }}

        .botones {{
            display: none !important;
        }}

        .producto,
        .producto-principal,
        .total,
        .pago,
        .pie {{
            break-inside: avoid !important;
            page-break-inside: avoid !important;
        }}
    }}

    @media print and (max-width: 80mm) {{

        @page {{
            margin: 2mm;
        }}

        body {{
            font-size: 10px;
        }}

        .restaurante {{
            font-size: 17px;
        }}

        .tipo-documento {{
            font-size: 11px;
        }}

        .pedido {{
            font-size: 15px;
        }}

        .linea-fuerte {{
            border-top-width: 2px;
            margin: 6px 0;
        }}

        .linea {{
            border-top-width: 1px;
            margin: 5px 0;
        }}

        .datos {{
            font-size: 10px;
        }}

        .fila-dato {{
            gap: 4px;
            margin: 3px 0;
        }}

        .cabecera-productos {{
            grid-template-columns: minmax(0, 1fr) 18mm;
            gap: 3px;
            font-size: 10px;
            padding: 4px 0;
            border-bottom-width: 2px;
        }}

        .producto {{
            padding: 5px 0;
            border-bottom-width: 1px;
        }}

        .producto-principal {{
            grid-template-columns: minmax(0, 1fr) 18mm;
            gap: 3px;
        }}

        .producto-nombre {{
            font-size: 11px;
        }}

        .producto-total {{
            font-size: 11px;
        }}

        .producto-calculo {{
            margin-top: 2px;
            font-size: 9px;
        }}

        .producto-nota {{
            margin-top: 3px;
            padding: 0;
            font-size: 9px;
        }}

        .total {{
            padding: 6px 0;
            font-size: 15px;
        }}

        .pago {{
            font-size: 10px;
        }}

        .pie {{
            margin-top: 7px;
            padding-top: 5px;
            font-size: 9px;
        }}
    }}
</style>
</head>

<body>

<div class='ticket'>

    <div class='encabezado'>
        <div class='restaurante'>LA SUPER CORVINA</div>

        <div class='tipo-documento'>
            COMPROBANTE DE PAGO
        </div>

        <div class='pedido'>
            PEDIDO #{orden.Id:D3}
        </div>
    </div>

    <div class='linea-fuerte'></div>

    <div class='datos'>

        <div class='fila-dato'>
            <strong>Mesa:</strong>
            {(orden.Mesa != null ? orden.Mesa.Numero : 0)}
        </div>

        <div class='fila-dato'>
            <strong>Mesero:</strong>
            {(orden.Usuario != null ? orden.Usuario.Nombre : "No registrado")}
        </div>

        <div class='fila-dato'>
            <strong>Fecha:</strong>
            {fechaOrdenEcuador:dd/MM/yyyy HH:mm}
        </div>

    </div>

    <div class='linea-fuerte'></div>

    <div class='cabecera-productos'>
        <div>Detalle del Producto</div>
        <div class='precio'>Precio<br>Total</div>
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
                {detalle.Cantidad}× {nombreProducto}
            </div>

            <div class='producto-total'>
                ${detalle.Subtotal:F2}
            </div>

        </div>

        <div class='producto-calculo'>
            {detalle.Cantidad} × ${detalle.PrecioUnitario:F2}
        </div>

        {(string.IsNullOrWhiteSpace(detalle.DetallePersonalizado)
                        ? ""
                        : $@"
                <div class='producto-nota'>
                    {detalle.DetallePersonalizado}
                </div>
            ")}

    </div>
");
            }

            html.Append($@"

    <div class='linea-fuerte'></div>

    <div class='total'>
        <span>TOTAL:</span>
        <span>${pago.Monto:F2}</span>
    </div>

    <div class='linea-fuerte'></div>

    <div class='datos-pago'>

        <div class='fila-dato'>
            <strong>Forma de pago:</strong>
            {pago.MetodoPago}
        </div>

        <div class='fila-dato'>
            <strong>Referencia:</strong>
            {(string.IsNullOrWhiteSpace(pago.Referencia)
                ? "Sin referencia"
                : pago.Referencia)}
        </div>

        <div class='fila-dato'>
            <strong>Fecha pago:</strong>
            {fechaPagoEcuador:dd/MM/yyyy HH:mm}
        </div>

        <div class='fila-dato'>
            <strong>Estado:</strong>
            {pago.EstadoPago}
        </div>

    </div>

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