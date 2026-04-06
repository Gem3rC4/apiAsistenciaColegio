using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly string _connectionString;

        // Esta es la "Firma" única del colegio para que nadie pueda falsificar los gafetes. 
        // ¡Mantenla secreta!
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
                // Vamos a la tabla a buscar al usuario. ¡Solo dejamos pasar si el estado es 1 (Activo)!
                using var cmd = new SqlCommand("SELECT nombreCompleto, rol FROM tblUsuarios WHERE usuario = @user AND passwordHash = @pass AND estado = 1", conn);
                cmd.Parameters.AddWithValue("@user", request.Usuario);
                cmd.Parameters.AddWithValue("@pass", request.Password);

                conn.Open();
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    string nombre = reader["nombreCompleto"].ToString();
                    string rol = reader["rol"].ToString();

                    // 1. Si el usuario existe, le fabricamos su Token (Gafete Virtual)
                    var tokenHandler = new JwtSecurityTokenHandler();
                    var llave = Encoding.ASCII.GetBytes(_secretKey);
                    var tokenDescriptor = new SecurityTokenDescriptor
                    {
                        Subject = new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.Name, nombre),
                            new Claim(ClaimTypes.Role, rol)
                        }),
                        // El gafete expira en 8 horas (un día de clases normal)
                        Expires = DateTime.UtcNow.AddHours(8),
                        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(llave), SecurityAlgorithms.HmacSha256Signature)
                    };

                    var token = tokenHandler.CreateToken(tokenDescriptor);
                    var tokenString = tokenHandler.WriteToken(token);

                    // 2. Le entregamos el gafete al navegador web
                    return Ok(new { token = tokenString, nombre, rol });
                }
                else
                {
                    // Error 401: Detenido en la puerta
                    return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        // Modelo de datos para recibir el JSON del frontend
        public class UsuarioRequest
        {
            public string Usuario { get; set; }
            public string Password { get; set; }
        }
    }
}