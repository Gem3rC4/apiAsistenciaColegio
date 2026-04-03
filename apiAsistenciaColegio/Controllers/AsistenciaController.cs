using QRCoder;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Data.SqlClient;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AsistenciaController : ControllerBase
    {
        private readonly string connectionString = "Server=localhost;Database=AsistenciaColegio;Trusted_Connection=True;";

        //El constructor lee la conexion que pusimos en appsettings.json
        public AsistenciaController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("ConexionColegio");
        }

        //===================================================================
        //==================== REGISTRAR ASISTENCIA =========================
        //===================================================================
        [HttpPost("registrar")]
        public IActionResult RegistrarAsistencia([FromBody] EscaneoRequest request)
        {
            //Validamos que el celular no nos haya mandado un codigo vacio
            if (string.IsNullOrEmpty(request.CodigoQR))
            {
                return BadRequest(new { estado = "Error", mensaje = "El codigo QR esta vacio." });
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("usp_RegistrarAsistencia", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        //Parametro de entrada (El QR que leyo el celular)
                        cmd.Parameters.AddWithValue("@CodigoQR", request.CodigoQR);

                        //Parametros de salida (Las respuestas en SQL Server)
                        var pResultado = new SqlParameter("@Resultado", SqlDbType.Int) { Direction = ParameterDirection.Output };
                        var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 500) { Direction = ParameterDirection.Output };

                        cmd.Parameters.Add(pResultado);
                        cmd.Parameters.Add(pMensaje);

                        //Disparamos el procedimiento almacenado
                        cmd.ExecuteNonQuery();

                        //Leemos que nos respondio SQL Server
                        bool resultado = Convert.ToBoolean(pResultado.Value);
                        string mensaje = pMensaje.Value?.ToString() ?? "Sin Respuesta del servidor";

                        //Le respondemos al celular con la respuesta de SQL Server
                        if (resultado)
                            return Ok(new { estado = "Exito", mensaje });
                        else
                            return BadRequest(new { estado = "advertencia", mensaje = mensaje });
                    }
                }
            }
            catch (Exception ex)
            {
                //Si la base de datos esta apagada o hay un error grave
                return StatusCode(500, new { estado = "Error", mensaje = "Error interno: " + ex.Message });
            }
        }

        //===========================================================================
        //=============VER TABLA CON TODAS LAS ASISTENCIAS Y FALTAS==================
        //===========================================================================

        [HttpGet("hoy")]
        public IActionResult ObtenerReporteHoy()
        {
            try
            {
                var listaReporte = new List<ReporteAsistenciaDto>();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("ups_ObtenerReporteAsistenciaHoy", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                listaReporte.Add(new ReporteAsistenciaDto
                                {
                                    Grado = reader["Grado"].ToString(),
                                    Seccion = reader["Seccion"].ToString(),
                                    NombreEstudiante = reader["NombreEstudiante"].ToString(),
                                    HoraLlegada = reader["HoraLlegada"].ToString(),
                                    HoraSalida = reader["HoraSalida"].ToString(),
                                    Estado = reader["Estado"].ToString()
                                });
                            }
                            
                        }
                    }
                }
                return Ok(listaReporte);
            }
            catch (Exception ex)
            {

                return StatusCode(500, new { mensaje = "Error interno al generar reporte: " + ex.Message});
            }
        }


        [HttpGet("/api/asistencia/generarqrmasivo")]
        public IActionResult GenerarQRMasa()
        {
            try
            {
                // 1. CORRECCIÓN: "wwwroot" bien escrito
                string rutaCarpeta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "qrs");
                if (!Directory.Exists(rutaCarpeta))
                {
                    Directory.CreateDirectory(rutaCarpeta);
                }

                int generados = 0;
                string cadenaConexion = connectionString;

                using (SqlConnection conn = new SqlConnection(cadenaConexion))
                {
                    conn.Open();

                    // 2. CORRECCIÓN: Filtramos los NULOS y traemos nombres (Asegúrate que estas columnas existan en tu tabla 'Estudiantes')
                    string consulta = "SELECT Nombres, Apellidos, CodigoQR FROM tblEstudiantes WHERE CodigoQR IS NOT NULL AND CodigoQR <> ''";

                    using (SqlCommand cmd = new SqlCommand(consulta, conn))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // 3. CORRECCIÓN: Usamos .ToString() que es mucho más seguro que GetString()
                                string codigoQR = reader["CodigoQR"].ToString();

                                // Obtenemos los nombres y limpiamos espacios extra
                                string nombres = reader["Nombres"].ToString().Trim();
                                string apellidos = reader["Apellidos"].ToString().Trim();

                                // Creamos el nombre del archivo (Ej: Lopez_Ana)
                                string nombreEstudiante = $"{apellidos}_{nombres}".Replace(" ", "_");

                                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                                QRCodeData qrCodeData = qrGenerator.CreateQrCode(codigoQR, QRCodeGenerator.ECCLevel.Q);
                                PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
                                byte[] qrCodeImage = qrCode.GetGraphic(20);

                                // Guardamos con el nombre del estudiante
                                string rutaArchivo = Path.Combine(rutaCarpeta, $"{nombreEstudiante}.png");
                                System.IO.File.WriteAllBytes(rutaArchivo, qrCodeImage);

                                generados++;
                            }
                        }
                    }
                }

                return Ok(new { mensaje = $"QRs generados exitosamente: {generados} nombrados por estudiante.", ruta = "/qrs/" });
            }
            catch (Exception ex)
            {
                // Ahora si algo falla, te dirá EXACTAMENTE qué fue en la pantalla
                return BadRequest(new { mensaje = "Error interno: " + ex.Message });
            }
        }


        public class EscaneoRequest
        {
            public string CodigoQR { get; set; }
        }

        public class ReporteAsistenciaDto
        {
            public string Grado { get; set; }
            public string Seccion { get; set; }
            public string NombreEstudiante { get; set; }
            public string HoraLlegada { get; set; }
            public string HoraSalida { get; set; }
            public string Estado { get; set; }
        }
    }
}

