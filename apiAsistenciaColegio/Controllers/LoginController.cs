using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net; 

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly string _connectionString;

        private readonly string _secretKey = "MiClaveSuperSecretaParaElColegioSagradoCorazonDeJesus2026!";

        public LoginController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConexionColegio");
        }

        [HttpPost("entrar")]
        public IActionResult Autenticar([FromBody] UsuarioRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { mensaje = "El usuario y contraseña son obligatorios." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);

                using var cmd = new SqlCommand("SELECT nombreCompleto, rol, passwordHash FROM tblUsuarios WHERE usuario = @user AND estado = 1", conn);
                cmd.Parameters.AddWithValue("@user", request.Usuario);

                conn.Open();
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    string nombre = reader["nombreCompleto"].ToString();
                    string rol = reader["rol"].ToString();
                    string hashGuardadoEnBd = reader["passwordHash"].ToString();

                    bool passwordCorrecto = BCrypt.Net.BCrypt.Verify(request.Password, hashGuardadoEnBd);

                    if (passwordCorrecto)
                    {
                        var tokenHandler = new JwtSecurityTokenHandler();
                        var llave = Encoding.ASCII.GetBytes(_secretKey);
                        var tokenDescriptor = new SecurityTokenDescriptor
                        {
                            Subject = new ClaimsIdentity(new[]
                            {
                                new Claim(ClaimTypes.Name, nombre),
                                new Claim(ClaimTypes.Role, rol)
                            }),
                            Expires = DateTime.UtcNow.AddHours(8),
                            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(llave), SecurityAlgorithms.HmacSha256Signature)
                        };

                        var token = tokenHandler.CreateToken(tokenDescriptor);
                        var tokenString = tokenHandler.WriteToken(token);

                        return Ok(new { token = tokenString, nombre, rol });
                    }
                }

                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        public class UsuarioRequest
        {
            public string Usuario { get; set; }
            public string Password { get; set; }
        }

        [HttpPost("registrar")]
        public IActionResult Registrar([FromBody] RegistroRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Usuario) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.NombreCompleto) ||
                string.IsNullOrWhiteSpace(request.Rol))
            {
                return BadRequest(new { mensaje = "Todos los campos, incluyendo el rol, son obligatorios." });
            }

            // VALIDACIÓN DE SEGURIDAD: Evita que registren roles extraños
            var rolesPermitidos = new List<string> { "Administrador", "Maestro", "Padre", "Alumno" };
            if (!rolesPermitidos.Contains(request.Rol))
            {
                return BadRequest(new { mensaje = "El tipo de usuario seleccionado no es válido." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                // VALIDACIÓN: Verificar si el nombre de usuario ya existe
                string queryCheck = "SELECT COUNT(1) FROM tblUsuarios WHERE usuario = @userCheck";
                using (var cmdCheck = new SqlCommand(queryCheck, conn))
                {
                    cmdCheck.Parameters.AddWithValue("@userCheck", request.Usuario.Trim());
                    int usuarioExiste = (int)cmdCheck.ExecuteScalar();

                    if (usuarioExiste > 0)
                    {
                        return BadRequest(new { mensaje = "El nombre de usuario ya está en uso. Por favor, intenta con otro." });
                    }
                }

                // PROCESO DE INSERCIÓN (Guardando el Hash criptográfico)
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                string queryInsert = @"INSERT INTO tblUsuarios (nombreCompleto, usuario, passwordHash, rol, estado) 
                                       VALUES (@nombre, @user, @pass, @rol, 1)";

                using var cmdInsert = new SqlCommand(queryInsert, conn);
                cmdInsert.Parameters.AddWithValue("@nombre", request.NombreCompleto.Trim());
                cmdInsert.Parameters.AddWithValue("@user", request.Usuario.Trim());
                cmdInsert.Parameters.AddWithValue("@pass", passwordHash);
                cmdInsert.Parameters.AddWithValue("@rol", request.Rol);

                int filasAfectadas = cmdInsert.ExecuteNonQuery();

                if (filasAfectadas > 0)
                {
                    return Ok(new { mensaje = "Usuario registrado exitosamente con sus permisos correspondientes." });
                }
                else
                {
                    return StatusCode(500, new { mensaje = "No se pudo registrar el usuario." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        public class RegistroRequest
        {
            public string NombreCompleto { get; set; }
            public string Usuario { get; set; }
            public string Password { get; set; }
            public string Rol { get; set; } 
        }
    }
}