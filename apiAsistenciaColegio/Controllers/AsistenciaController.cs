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
        private readonly string _connectionString;

        public AsistenciaController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConexionColegio");
        }

        //===================================================================
        //==================== REGISTRAR ASISTENCIA =========================
        //===================================================================
        [HttpPost("registrar")]
        public IActionResult RegistrarAsistencia([FromBody] EscaneoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoQR))
            {
                return BadRequest(new { estado = "Error", mensaje = "El código QR está vacío o es inválido." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("usp_RegistrarAsistencia", conn);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@CodigoQR", request.CodigoQR);

                var pResultado = new SqlParameter("@Resultado", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pMensaje = new SqlParameter("@Mensaje", SqlDbType.VarChar, 500) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pResultado);
                cmd.Parameters.Add(pMensaje);

                conn.Open();
                cmd.ExecuteNonQuery();

                bool resultado = Convert.ToBoolean(pResultado.Value);
                string mensaje = pMensaje.Value?.ToString() ?? "Sin Respuesta del servidor";

                if (resultado)
                    return Ok(new { estado = "Exito", mensaje });
                else
                    return BadRequest(new { estado = "advertencia", mensaje });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { estado = "Error", mensaje = $"Error interno: {ex.Message}" });
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

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("ups_ObtenerReporteAsistenciaHoy", conn);
                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();
                using var reader = cmd.ExecuteReader();

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

                return Ok(listaReporte);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno al generar reporte: {ex.Message}" });
            }
        }

        //===========================================================================
        //=============GENERAR QR MASIVO PARA TODOS LOS ESTUDIANTES==================
        //===========================================================================
        [HttpGet("generarqrmasivo")]
        public IActionResult GenerarQRMasa()
        {
            try
            {
                // Ruta base principal
                string rutaCarpetaBase = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "qrs");
                if (!Directory.Exists(rutaCarpetaBase))
                {
                    Directory.CreateDirectory(rutaCarpetaBase);
                }

                int generados = 0;

                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("sp_ObtenerEstudiantesParaQR", conn);
                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string codigoQR = reader["CodigoQR"].ToString();
                    string nombres = reader["Nombres"].ToString().Trim();
                    string apellidos = reader["Apellidos"].ToString().Trim();
                    string grado = reader["Grado"].ToString().Trim();
                    string seccion = reader["Seccion"].ToString().Trim();

                    string nombreCompleto = $"{nombres} {apellidos}";
                    // Preparamos la sección para el nombre del archivo
                    string textoSeccionArchivo = string.IsNullOrWhiteSpace(seccion) ? "" : $"{seccion} ";

                    // Unimos todo con espacios normales y limpios
                    string nombreArchivo = $"{grado} {textoSeccionArchivo}{apellidos} {nombres}";

                    // --- 1. LÓGICA DE CREACIÓN DE SUBCARPETAS ---
                    // Si tiene sección le agrega la palabra "Seccion" (ej. "SeccionA"). Si no, lo deja en blanco.
                    string textoSeccion = string.IsNullOrWhiteSpace(seccion) ? "" : $"Seccion{seccion}";

                    // Unimos el grado y la sección, y le quitamos todos los espacios en blanco
                    // Esto convertirá "Primero Basico" + "SeccionA" en "PrimeroBasicoSeccionA"
                    string nombreSubCarpeta = $"{grado}{textoSeccion}".Replace(" ", "");

                    // Combinamos la ruta base con la nueva subcarpeta
                    string rutaDestinoFinal = Path.Combine(rutaCarpetaBase, nombreSubCarpeta);

                    // Si esta subcarpeta específica aún no existe, la creamos al vuelo
                    if (!Directory.Exists(rutaDestinoFinal))
                    {
                        Directory.CreateDirectory(rutaDestinoFinal);
                    }
                    // --------------------------------------------

                    // Optimización de Memoria RAM
                    using var qrGenerator = new QRCodeGenerator();
                    using var qrCodeData = qrGenerator.CreateQrCode(codigoQR, QRCodeGenerator.ECCLevel.Q);
                    using var qrCode = new QRCode(qrCodeData);
                    using var qrBitmap = qrCode.GetGraphic(20);

                    int espacioTexto = 60;
                    using var lienzoFinal = new Bitmap(qrBitmap.Width, qrBitmap.Height + espacioTexto);

                    using (Graphics g = Graphics.FromImage(lienzoFinal))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(qrBitmap, 0, 0);

                        using var fuente = new Font("Arial", 16, FontStyle.Bold);
                        using var brocha = new SolidBrush(Color.Black);
                        var formato = new StringFormat { Alignment = StringAlignment.Center };

                        var espacioParaEscribir = new RectangleF(0, qrBitmap.Height, qrBitmap.Width, espacioTexto);
                        g.DrawString(nombreCompleto, fuente, brocha, espacioParaEscribir, formato);
                    }

                    // --- 2. GUARDAR EN LA SUBCARPETA CORRECTA ---
                    // Guardamos la imagen dentro de su carpeta correspondiente en lugar de la carpeta base
                    string rutaArchivo = Path.Combine(rutaDestinoFinal, $"{nombreArchivo}.png");
                    lienzoFinal.Save(rutaArchivo, ImageFormat.Png);

                    generados++;
                }

                return Ok(new { mensaje = $"Se han generado {generados} QRs clasificados por grado y sección.", ruta = "/qrs/" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        // =======================================================================
        // ======================== CLASES DTO (Modelos) =========================
        // =======================================================================
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