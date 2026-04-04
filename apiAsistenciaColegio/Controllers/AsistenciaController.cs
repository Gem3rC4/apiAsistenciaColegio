using Microsoft.AspNetCore.Mvc;
using QRCoder;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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



        //===========================================================================
        //=============GENERAR QR MASIVO PARA TODOS LOS ESTUDIANTES==================
        //===========================================================================
        [HttpGet("/api/asistencia/generarqrmasivo")]
        public IActionResult GenerarQRMasa()
        {
            try
            {
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

                    // Store procedure que nos devuelve el codigo QR, nombre, apellido, grado y seccion de cada estudiante
                    using (SqlCommand cmd = new SqlCommand("sp_ObtenerEstudiantesParaQR", conn))
                    {
                        // definimos que es un procedimiento almacenado
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                //Extraer el qr, nombre y apellido
                                string codigoQR = reader["CodigoQR"].ToString();
                                string nombres = reader["Nombres"].ToString().Trim();
                                string apellidos = reader["Apellidos"].ToString().Trim();

                                // Extraer grado y seccion
                                string grado = reader["Grado"].ToString().Trim();
                                string seccion = reader["Seccion"].ToString().Trim();

                                string nombreCompleto = $"{nombres} {apellidos}";

                                // Nombre del archivo
                                string nombreArchivo = $"{grado}_{seccion}_{apellidos}_{nombres}".Replace(" ", "_");

                                //Generamos solo el QR
                                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                                QRCodeData qrCodeData = qrGenerator.CreateQrCode(codigoQR, QRCodeGenerator.ECCLevel.Q);
                                QRCode qrCode = new QRCode(qrCodeData);
                                Bitmap qrBitmap = qrCode.GetGraphic(20);

                                //Para que el texto quede legible, le damos un espacio extra debajo del QR para escribir el nombre completo
                                int espacioTexto = 60;
                                Bitmap lienzoFinal = new Bitmap(qrBitmap.Width, qrBitmap.Height + espacioTexto);

                                //Para dibujar el QR y el texto, usamos Graphics
                                using (Graphics g = Graphics.FromImage(lienzoFinal))
                                {
                                    // Se pinta el fondo de blanco para que el texto sea legible
                                    g.Clear(Color.White);

                                    // Para pegar QR en la parte de arriba por coordenadas
                                    g.DrawImage(qrBitmap, 0, 0);

                                    // Configuracion del tipo de letra
                                    Font fuente = new Font("Arial", 16, FontStyle.Bold);
                                    SolidBrush brocha = new SolidBrush(Color.Black);

                                    // Centrar el texto
                                    StringFormat formato = new StringFormat();
                                    formato.Alignment = StringAlignment.Center;

                                    // Para colocar el texto debajo del qr
                                    RectangleF espacioParaEscribir = new RectangleF(0, qrBitmap.Height, qrBitmap.Width, espacioTexto);
                                    g.DrawString(nombreCompleto, fuente, brocha, espacioParaEscribir, formato);
                                }

                                // Se guarda la imagen con el qr y el nombre
                                string rutaArchivo = Path.Combine(rutaCarpeta, $"{nombreArchivo}.png");
                                lienzoFinal.Save(rutaArchivo, ImageFormat.Png);

                                generados++;
                            }
                        }
                    }
                }

                return Ok(new { mensaje = $"Estan Creados {generados} QRs generados con nombre incluido.", ruta = "/qrs/" });
            }
            catch (Exception ex)
            {
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

