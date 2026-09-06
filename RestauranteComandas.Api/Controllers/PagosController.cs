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

            var ecuadorOffset = TimeSpan.FromHours(-5);

            var fechaOrdenEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(orden.Fecha, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var fechaPagoEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(pago.FechaPago, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var mesero = System.Net.WebUtility.HtmlEncode(
                orden.Usuario != null
                    ? orden.Usuario.Nombre
                    : "No registrado"
            );

            var metodoPago = System.Net.WebUtility.HtmlEncode(
                pago.MetodoPago ?? ""
            );

            var referencia = System.Net.WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(pago.Referencia)
                    ? "Sin referencia"
                    : pago.Referencia
            );

            var estadoPago = System.Net.WebUtility.HtmlEncode(
                pago.EstadoPago ?? ""
            );

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

        /*
         * El navegador usa el tamaño de papel seleccionado.
         * No se fija 58 mm dentro del HTML.
         * Así el contenido ocupa todo el ancho del PDF o de la impresora.
         */
        @page {{
            margin: 0;
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
            font-size: 22px;
            line-height: 1.08;
        }}

        /*
         * Márgenes laterales prácticamente nulos.
         */
        .ticket {{
            width: 100%;
            max-width: none;
            margin: 0;
            padding: 3px 5px;
            background: #fff;
            overflow: visible;
        }}

        /*
         * Encabezado único: solamente existe aquí,
         * por lo que no se repite al continuar hacia abajo.
         */
        .encabezado {{
            width: 100%;
            text-align: center;
            margin: 0;
            padding: 3px 0 8px 0;
        }}

        .restaurante {{
            margin: 0;
            font-size: 38px;
            line-height: 1;
            font-weight: 900;
        }}

        .tipo-documento {{
            margin-top: 6px;
            font-size: 27px;
            line-height: 1;
            font-weight: 900;
        }}

        .pedido {{
            margin-top: 6px;
            font-size: 34px;
            line-height: 1;
            font-weight: 900;
        }}

        .linea-fuerte {{
            width: 100%;
            margin: 9px 0;
            border-top: 5px solid #000;
        }}

        .datos {{
            width: 100%;
            font-size: 26px;
            line-height: 1.08;
        }}

        .fila-dato {{
            width: 100%;
            margin: 5px 0;
            overflow-wrap: anywhere;
        }}

        .fila-dato strong {{
            font-weight: 900;
        }}

        .cabecera-productos {{
            display: grid;
            grid-template-columns: minmax(0, 1fr) 24%;
            width: 100%;
            gap: 8px;
            padding: 5px 0 8px 0;
            border-bottom: 5px solid #000;
            font-size: 27px;
            line-height: 1;
            font-weight: 900;
        }}

        .cabecera-precio {{
            text-align: right;
        }}

        /*
         * Los platos están en flujo normal:
         * uno inmediatamente después del otro.
         * No hay alturas fijas ni saltos forzados.
         */
        .producto {{
            width: 100%;
            margin: 0;
            padding: 7px 0 6px 0;
            border-bottom: 2px dashed #000;
        }}

        .producto-principal {{
            display: grid;
            grid-template-columns: minmax(0, 1fr) 24%;
            width: 100%;
            gap: 8px;
            align-items: start;
        }}

        .producto-nombre {{
            min-width: 0;
            font-size: 29px;
            line-height: 1.05;
            font-weight: 900;
            overflow-wrap: anywhere;
        }}

        .producto-total {{
            font-size: 29px;
            line-height: 1.05;
            font-weight: 900;
            text-align: right;
            white-space: nowrap;
        }}

        .producto-nota {{
            width: 100%;
            margin-top: 3px;
            padding: 0;
            font-size: 21px;
            line-height: 1.08;
            font-weight: 700;
            font-style: italic;
            overflow-wrap: anywhere;
        }}

        .total {{
            display: flex;
            width: 100%;
            justify-content: space-between;
            align-items: center;
            gap: 8px;
            margin: 0;
            padding: 9px 0;
            font-size: 35px;
            line-height: 1;
            font-weight: 900;
        }}

        .datos-pago {{
            width: 100%;
            font-size: 24px;
            line-height: 1.08;
        }}

        .fila-pago {{
            display: flex;
            width: 100%;
            justify-content: space-between;
            align-items: flex-start;
            gap: 8px;
            margin: 4px 0;
        }}

        .etiqueta {{
            flex-shrink: 0;
            font-weight: 900;
        }}

        .valor {{
            flex: 1;
            text-align: right;
            font-weight: 600;
            overflow-wrap: anywhere;
        }}

        .pie {{
            width: 100%;
            margin-top: 7px;
            padding: 7px 0 3px 0;
            border-top: 2px dashed #000;
            text-align: center;
            font-size: 22px;
            line-height: 1.1;
            font-weight: 700;
        }}

        .botones {{
            width: 100%;
            margin: 12px 0 0 0;
            padding: 0 5px;
            text-align: center;
        }}

        button {{
            width: 100%;
            border: none;
            padding: 12px;
            background: #111;
            color: #fff;
            font-size: 18px;
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
                overflow: visible !important;
            }}

            .ticket {{
                width: 100% !important;
                max-width: none !important;
                margin: 0 !important;
                padding: 2px 3px !important;
                overflow: visible !important;
            }}

            .botones {{
                display: none !important;
            }}

            /*
             * No se aplican page-break ni break-inside.
             * Si el controlador de la térmica usa rollo continuo,
             * los productos continúan hacia abajo.
             */
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
            <strong>Mesa:</strong>
            {(orden.Mesa != null ? orden.Mesa.Numero : 0)}
        </div>

        <div class='fila-dato'>
            <strong>Mesero:</strong>
            {mesero}
        </div>

        <div class='fila-dato'>
            <strong>Fecha:</strong>
            {fechaOrdenEcuador:dd/MM/yyyy HH:mm}
        </div>
    </div>

    <div class='linea-fuerte'></div>

    <div class='cabecera-productos'>
        <div>Detalle del Producto</div>
        <div class='cabecera-precio'>Precio<br>Total</div>
    </div>
");

            foreach (var detalle in orden.Detalles)
            {
                var nombreProducto = System.Net.WebUtility.HtmlEncode(
                    detalle.MenuItem != null
                        ? detalle.MenuItem.Nombre
                        : "Producto"
                );

                var nota = System.Net.WebUtility.HtmlEncode(
                    detalle.DetallePersonalizado ?? ""
                );

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

        {(string.IsNullOrWhiteSpace(nota)
            ? ""
            : $@"<div class='producto-nota'>{nota}</div>")}
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

        <div class='fila-pago'>
            <span class='etiqueta'>Forma de pago:</span>
            <span class='valor'>{metodoPago}</span>
        </div>

        <div class='fila-pago'>
            <span class='etiqueta'>Referencia:</span>
            <span class='valor'>{referencia}</span>
        </div>

        <div class='fila-pago'>
            <span class='etiqueta'>Fecha pago:</span>
            <span class='valor'>{fechaPagoEcuador:dd/MM/yyyy HH:mm}</span>
        </div>

        <div class='fila-pago'>
            <span class='etiqueta'>Estado:</span>
            <span class='valor'>{estadoPago}</span>
        </div>

    </div>

    <div class='pie'>
        ¡Gracias por su preferencia! · LA SUPER CORVINA
    </div>

</div>

<div class='botones'>
    <button
        type='button'
        onclick='window.print()'
    >
        Imprimir comprobante
    </button>
</div>

</body>
</html>
");

            return Content(
                html.ToString(),
                "text/html; charset=utf-8"
            );
        }
    }
}