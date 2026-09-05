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
                FechaPago = DateTime.Now,
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

            var html = new StringBuilder();

            html.Append($@"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'>
    <title>Comprobante Orden #{orden.Id}</title>

    <style>
        body {{
            font-family: Arial, sans-serif;
            background: #f3f4f6;
            margin: 0;
            padding: 30px;
            color: #111827;
        }}

        .comprobante {{
            max-width: 760px;
            margin: auto;
            background: white;
            padding: 30px;
            border-radius: 14px;
            box-shadow: 0 8px 22px rgba(0,0,0,0.15);
        }}

        .encabezado {{
            text-align: center;
            border-bottom: 2px solid #dc2626;
            padding-bottom: 15px;
            margin-bottom: 20px;
        }}

        .encabezado h1 {{
            margin: 0;
            color: #dc2626;
            font-size: 30px;
        }}

        .encabezado p {{
            margin: 5px 0;
            color: #4b5563;
        }}

        .datos {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px 25px;
            margin-bottom: 25px;
            font-size: 15px;
        }}

        .dato {{
            background: #f9fafb;
            padding: 10px;
            border-radius: 8px;
        }}

        .dato strong {{
            color: #374151;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
            margin-top: 10px;
        }}

        th {{
            background: #dc2626;
            color: white;
            padding: 10px;
            text-align: left;
        }}

        td {{
            padding: 10px;
            border-bottom: 1px solid #e5e7eb;
        }}

        .total {{
            text-align: right;
            margin-top: 25px;
            font-size: 22px;
            font-weight: bold;
            color: #111827;
        }}

        .estado {{
            display: inline-block;
            padding: 7px 14px;
            border-radius: 20px;
            background: #22c55e;
            color: white;
            font-weight: bold;
        }}

        .botones {{
            margin-top: 25px;
            text-align: center;
        }}

        button {{
            background: #dc2626;
            color: white;
            border: none;
            padding: 12px 18px;
            border-radius: 8px;
            cursor: pointer;
            font-weight: bold;
        }}

        .pie {{
            margin-top: 30px;
            text-align: center;
            color: #6b7280;
            font-size: 13px;
        }}

        @media print {{
            body {{
                background: white;
                padding: 0;
            }}

            .comprobante {{
                box-shadow: none;
                border-radius: 0;
            }}

            .botones {{
                display: none;
            }}
        }}
    </style>
</head>

<body>
    <div class='comprobante'>
        <div class='encabezado'>
            <h1>Restaurante Comandas</h1>
            <p>Comprobante digital de pago</p>
            <p><strong>Comprobante N.º:</strong> {pago.Id}</p>
        </div>

        <div class='datos'>
            <div class='dato'>
                <strong>Orden:</strong> #{orden.Id}
            </div>

            <div class='dato'>
                <strong>Mesa:</strong> {(orden.Mesa != null ? orden.Mesa.Numero : 0)}
            </div>

            <div class='dato'>
                <strong>Mesero:</strong> {(orden.Usuario != null ? orden.Usuario.Nombre : "No registrado")}
            </div>

            <div class='dato'>
                <strong>Fecha orden:</strong> {orden.Fecha:dd/MM/yyyy HH:mm}
            </div>

            <div class='dato'>
                <strong>Fecha pago:</strong> {pago.FechaPago:dd/MM/yyyy HH:mm}
            </div>

            <div class='dato'>
                <strong>Método de pago:</strong> {pago.MetodoPago}
            </div>

            <div class='dato'>
                <strong>Referencia:</strong> {(string.IsNullOrWhiteSpace(pago.Referencia) ? "Sin referencia" : pago.Referencia)}
            </div>

            <div class='dato'>
                <strong>Estado pago:</strong> <span class='estado'>{pago.EstadoPago}</span>
            </div>
        </div>

        <h2>Detalle de consumo</h2>

        <table>
            <thead>
                <tr>
                    <th>Producto</th>
                    <th>Cantidad</th>
                    <th>Precio unitario</th>
                    <th>Subtotal</th>
                    <th>Detalle</th>
                </tr>
            </thead>
            <tbody>
");

            foreach (var detalle in orden.Detalles)
            {
                html.Append($@"
                <tr>
                    <td>{(detalle.MenuItem != null ? detalle.MenuItem.Nombre : "Producto no encontrado")}</td>
                    <td>{detalle.Cantidad}</td>
                    <td>${detalle.PrecioUnitario:F2}</td>
                    <td>${detalle.Subtotal:F2}</td>
                    <td>{(string.IsNullOrWhiteSpace(detalle.DetallePersonalizado) ? "-" : detalle.DetallePersonalizado)}</td>
                </tr>
");
            }

            html.Append($@"
            </tbody>
        </table>

        <div class='total'>
            Total pagado: ${pago.Monto:F2}
        </div>

        <div class='botones'>
            <button onclick='window.print()'>Imprimir / Guardar PDF</button>
        </div>

        <div class='pie'>
            Gracias por su compra. Este comprobante fue generado digitalmente por el sistema.
        </div>
    </div>
</body>
</html>
");

            return Content(html.ToString(), "text/html; charset=utf-8");
        }
    }
}