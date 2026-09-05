using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.DTOs;
using RestauranteComandas.Api.Helpers;
using RestauranteComandas.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuariosController : ControllerBase
    {
        private readonly RestauranteDbContext _context;
        private readonly IConfiguration _configuration;

        public UsuariosController(RestauranteDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NombreUsuario))
            {
                return BadRequest("Debe ingresar el usuario");
            }

            if (string.IsNullOrWhiteSpace(dto.Clave))
            {
                return BadRequest("Debe ingresar la contraseña");
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.NombreUsuario == dto.NombreUsuario && u.Activo);

            if (usuario == null)
            {
                return Unauthorized("Usuario o contraseña incorrectos");
            }

            var claveCorrecta = PasswordHelper.VerifyPassword(dto.Clave, usuario.ClaveHash);

            if (!claveCorrecta)
            {
                return Unauthorized("Usuario o contraseña incorrectos");
            }

            var token = GenerarToken(usuario);

            return Ok(new
            {
                mensaje = "Login correcto",
                usuario = new
                {
                    usuario.Id,
                    usuario.Nombre,
                    usuario.NombreUsuario,
                    usuario.Rol
                },
                token
            });
        }

        [HttpGet]
        [Authorize(Roles = "Administrador")]
        public async Task<ActionResult<IEnumerable<object>>> GetUsuarios()
        {
            var usuarios = await _context.Usuarios
                .OrderBy(u => u.Nombre)
                .Select(u => new
                {
                    u.Id,
                    u.Nombre,
                    u.NombreUsuario,
                    u.Rol,
                    u.Activo
                })
                .ToListAsync();

            return Ok(usuarios);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador")]
        public async Task<ActionResult<object>> GetUsuario(int id)
        {
            var usuario = await _context.Usuarios
                .Where(u => u.Id == id)
                .Select(u => new
                {
                    u.Id,
                    u.Nombre,
                    u.NombreUsuario,
                    u.Rol,
                    u.Activo
                })
                .FirstOrDefaultAsync();

            if (usuario == null)
            {
                return NotFound("Usuario no encontrado");
            }

            return Ok(usuario);
        }

        [HttpPost]
        public async Task<IActionResult> CrearUsuario(CrearUsuarioDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombre))
            {
                return BadRequest("El nombre es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(dto.NombreUsuario))
            {
                return BadRequest("El nombre de usuario es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(dto.Clave))
            {
                return BadRequest("La contraseña es obligatoria");
            }

            var rolesPermitidos = new List<string>
            {
                "Administrador",
                "Mesero",
                "Cocina",
                "Caja"
            };

            if (!rolesPermitidos.Contains(dto.Rol))
            {
                return BadRequest("Rol no permitido");
            }

            var existeUsuario = await _context.Usuarios
                .AnyAsync(u => u.NombreUsuario == dto.NombreUsuario);

            if (existeUsuario)
            {
                return BadRequest("Ya existe un usuario con ese nombre de usuario");
            }

            var cantidadUsuarios = await _context.Usuarios.CountAsync();

            if (cantidadUsuarios > 0)
            {
                var usuarioActualRol = User.FindFirst(ClaimTypes.Role)?.Value;

                if (usuarioActualRol != "Administrador")
                {
                    return Unauthorized("Solo un administrador puede crear nuevos usuarios");
                }
            }

            var usuario = new Usuario
            {
                Nombre = dto.Nombre,
                NombreUsuario = dto.NombreUsuario,
                ClaveHash = PasswordHelper.HashPassword(dto.Clave),
                Rol = dto.Rol,
                Activo = true
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Usuario creado correctamente",
                usuario = new
                {
                    usuario.Id,
                    usuario.Nombre,
                    usuario.NombreUsuario,
                    usuario.Rol,
                    usuario.Activo
                }
            });
        }

        [HttpPatch("{id}/estado")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> CambiarEstadoUsuario(int id, [FromBody] bool activo)
        {
            var usuario = await _context.Usuarios.FindAsync(id);

            if (usuario == null)
            {
                return NotFound("Usuario no encontrado");
            }

            usuario.Activo = activo;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Estado de usuario actualizado correctamente",
                usuario.Id,
                usuario.Nombre,
                usuario.Activo
            });
        }

        private string GenerarToken(Usuario usuario)
        {
            var jwtKey = _configuration["Jwt:Key"];

            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new Exception("La clave JWT no está configurada");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreUsuario),
                new Claim(ClaimTypes.Role, usuario.Rol),
                new Claim("nombre", usuario.Nombre)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expireMinutes = Convert.ToDouble(_configuration["Jwt:ExpireMinutes"] ?? "120");

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(expireMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}