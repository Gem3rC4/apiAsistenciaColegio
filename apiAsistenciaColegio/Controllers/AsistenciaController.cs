using Microsoft.AspNetCore.Mvc;
using QRCoder;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;

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

        // REGISTRAR ASISTENCIA 
        [HttpPost("registrar")]
        public IActionResult RegistrarAsistencia([FromBody] EscaneoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoQR))
            {
                return BadRequest(new { estado = "Error", mensaje = "El código QR está vacío o es inválido." });
            }

            string alertaTrigger = string.Empty;

            try
            {
                using var conn = new SqlConnection(_connectionString);

                conn.InfoMessage += (sender, e) => { alertaTrigger += e.Message + "\n"; };

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
                alertaTrigger = alertaTrigger.Trim();

                if (resultado)
                    return Ok(new { estado = "Exito", mensaje, alerta = !string.IsNullOrEmpty(alertaTrigger) ? alertaTrigger : null });
                else
                    return BadRequest(new { estado = "advertencia", mensaje, alerta = !string.IsNullOrEmpty(alertaTrigger) ? alertaTrigger : null });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { estado = "Error", mensaje = $"Error interno: {ex.Message}" });
            }
        }

        //VER TABLA CON TODAS LAS ASISTENCIAS Y FALTAS
        [HttpGet("hoy")]
        public IActionResult ObtenerReporteHoy()
        {
            try
            {
                var listaReporte = new List<ReporteAsistenciaDto>();

                using var conn = new SqlConnection(_connectionString);

                // Esta consulta hace el trabajo pesado: une la entrada con su salida (si existe) 
                // y calcula el tiempo usando SQL.
                string query = @"
            SELECT 
                g.nombreGrado AS grado,
                a.Seccion AS seccion,
                (e.nombres + ' ' + e.apellidos) AS nombreEstudiante,
                CONVERT(VARCHAR(5), ent.fechaHora, 108) AS horaLlegada,
                ISNULL(CONVERT(VARCHAR(5), sal.fechaHora, 108), '--:--') AS horaSalida,
                CASE 
                    WHEN sal.idAsistencia IS NOT NULL THEN 'RETIRADO' 
                    ELSE 'PRESENTE' 
                END AS estado,
                ISNULL(calc.TiempoFormateado, '00:00') AS tiempoTotal
            FROM tblAsistencia ent
            INNER JOIN tblEstudiantes e ON ent.idEstudiante = e.idEstudiante
            INNER JOIN tblAsignaciones a ON e.idEstudiante = a.idEstudiante
            INNER JOIN tblGrados g ON a.idGrado = g.idGrado
            LEFT JOIN tblAsistencia sal ON ent.idEstudiante = sal.idEstudiante 
                                        AND CAST(ent.fechaHora AS DATE) = CAST(sal.fechaHora AS DATE)
                                        AND sal.tipoMovimiento = 'Salida'
            OUTER APPLY dbo.fn_CalcularTiempoEnClase(ent.idEstudiante) calc
            WHERE ent.tipoMovimiento = 'Entrada' 
              AND CAST(ent.fechaHora AS DATE) = CAST(GETDATE() AS DATE)";

                using var cmd = new SqlCommand(query, conn);
                conn.Open();

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    listaReporte.Add(new ReporteAsistenciaDto
                    {
                        Grado = reader["grado"].ToString(),
                        Seccion = reader["seccion"].ToString(),
                        NombreEstudiante = reader["nombreEstudiante"].ToString(),
                        HoraLlegada = reader["horaLlegada"].ToString(),
                        HoraSalida = reader["horaSalida"].ToString(),
                        Estado = reader["estado"].ToString(),
                        TiempoTotal = reader["tiempoTotal"].ToString()
                    });
                }

                return Ok(listaReporte);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno al generar reporte: {ex.Message}" });
            }
        }

        //Funciones

        [HttpGet("estado/{codigoQR}")]
        public IActionResult ObtenerEstadoActual(string codigoQR)
        {
            if (string.IsNullOrWhiteSpace(codigoQR))
                return BadRequest(new { mensaje = "El código es requerido." });

            try
            {
                using var conn = new SqlConnection(_connectionString);

                string query = @"
                    SELECT (e.nombres + ' ' + e.apellidos) AS nombreCompleto, 
                           dbo.fn_ObtenerEstadoActual(e.idEstudiante) AS EstadoActual,
                           m.fotoPerfilPath
                    FROM tblEstudiantes e
                    LEFT JOIN tblEstudiantes_Multimedia m ON e.idEstudiante = m.idEstudiante
                    WHERE e.CodigoQR = @qr";

                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@qr", codigoQR);

                conn.Open();
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return Ok(new
                    {
                        nombre = reader["nombreCompleto"].ToString(),
                        estado = reader["EstadoActual"].ToString(),
                        foto = reader["fotoPerfilPath"] != DBNull.Value ? reader["fotoPerfilPath"].ToString() : ""
                    });
                }

                return NotFound(new { mensaje = "No se encontró ningún estudiante con ese código." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        //OBTENER LISTA DE GRADOS 
        [HttpGet("grados")]
        public IActionResult ObtenerGrados()
        {
            try
            {
                var listaGrados = new List<object>();
                using var conn = new SqlConnection(_connectionString);

                // Consultamos todos los grados ordenados por su ID
                string query = "SELECT idGrado, nombreGrado FROM tblGrados ORDER BY idGrado";
                using var cmd = new SqlCommand(query, conn);

                conn.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    listaGrados.Add(new
                    {
                        idGrado = Convert.ToInt32(reader["idGrado"]),
                        nombreGrado = reader["nombreGrado"].ToString()
                    });
                }

                return Ok(listaGrados);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        // REGISTRAR NUEVO ESTUDIANTE
        [HttpPost("nuevo-estudiante")]
        public IActionResult RegistrarNuevoEstudiante([FromBody] NuevoEstudianteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Nombres) || string.IsNullOrWhiteSpace(request.Apellidos) || request.IdGrado <= 0)
            {
                return BadRequest(new { mensaje = "Nombres, apellidos y grado son obligatorios." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                Random rnd = new Random();
                string nuevoCodigoQR = $"SCJ-{DateTime.Now.Year}-{rnd.Next(1000, 9999)}";

                using SqlTransaction transaccion = conn.BeginTransaction();

                try
                {
                    // Insertar en tblEstudiantes
                    string queryEstudiante = @"INSERT INTO tblEstudiantes (Nombres, Apellidos, CodigoQR) 
                                               VALUES (@nom, @ape, @qr);
                                               SELECT SCOPE_IDENTITY();";

                    int idNuevoEstudiante = 0;
                    using (var cmdEst = new SqlCommand(queryEstudiante, conn, transaccion))
                    {
                        cmdEst.Parameters.AddWithValue("@nom", request.Nombres.Trim());
                        cmdEst.Parameters.AddWithValue("@ape", request.Apellidos.Trim());
                        cmdEst.Parameters.AddWithValue("@qr", nuevoCodigoQR);
                        idNuevoEstudiante = Convert.ToInt32(cmdEst.ExecuteScalar());
                    }

                    // Insertar en tblAsignaciones (Para vincularlo a un grado y sección)
                    string queryAsignacion = @"INSERT INTO tblAsignaciones (idEstudiante, idGrado, Seccion, cicloEscolar) 
                                               VALUES (@id, @grado, @sec, @ciclo)";

                    using (var cmdAsig = new SqlCommand(queryAsignacion, conn, transaccion))
                    {
                        cmdAsig.Parameters.AddWithValue("@id", idNuevoEstudiante);
                        cmdAsig.Parameters.AddWithValue("@grado", request.IdGrado);
                        cmdAsig.Parameters.AddWithValue("@sec", string.IsNullOrWhiteSpace(request.Seccion) ? "A" : request.Seccion.Trim().ToUpper());
                        cmdAsig.Parameters.AddWithValue("@ciclo", DateTime.Now.Year);
                        cmdAsig.ExecuteNonQuery();
                    }

                    // Procesar y guardar la fotografía (Si se adjuntó)
                    if (!string.IsNullOrWhiteSpace(request.FotoBase64))
                    {
                        string carpetaBase = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documentos");
                        if (!Directory.Exists(carpetaBase)) Directory.CreateDirectory(carpetaBase);

                        byte[] bytesFoto = Convert.FromBase64String(request.FotoBase64);
                        string nombreArchivo = $"FOTO_{idNuevoEstudiante}_{DateTime.Now.Ticks}.png";
                        System.IO.File.WriteAllBytes(Path.Combine(carpetaBase, nombreArchivo), bytesFoto);
                        string rutaRelativa = $"/documentos/{nombreArchivo}";

                        string queryFoto = "INSERT INTO tblEstudiantes_Multimedia (idEstudiante, fotoPerfilPath) VALUES (@id, @ruta)";
                        using (var cmdFoto = new SqlCommand(queryFoto, conn, transaccion))
                        {
                            cmdFoto.Parameters.AddWithValue("@id", idNuevoEstudiante);
                            cmdFoto.Parameters.AddWithValue("@ruta", rutaRelativa);
                            cmdFoto.ExecuteNonQuery();
                        }
                    }

                    transaccion.Commit();

                    return Ok(new
                    {
                        mensaje = "Estudiante inscrito exitosamente.",
                        carnetGenerado = nuevoCodigoQR
                    });
                }
                catch (Exception exTransaction)
                {
                    transaccion.Rollback();
                    throw new Exception("Error durante la transacción: " + exTransaction.Message);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        public class NuevoEstudianteRequest
        {
            public string Nombres { get; set; }
            public string Apellidos { get; set; }
            public int IdGrado { get; set; }
            public string Seccion { get; set; }
            public string FotoBase64 { get; set; }
        }

        //DESCARGAR QR INDIVIDUAL
        [HttpGet("descargar-qr/{codigoQR}")]
        public IActionResult DescargarQRIndividual(string codigoQR)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                string query = "SELECT Nombres, Apellidos FROM tblEstudiantes WHERE CodigoQR = @qr";
                string nombreCompleto = "Estudiante";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@qr", codigoQR);
                    conn.Open();
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        nombreCompleto = $"{reader["Nombres"]} {reader["Apellidos"]}".Trim();
                    }
                }

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

                using var ms = new MemoryStream();
                lienzoFinal.Save(ms, ImageFormat.Png);

                // Retornamos el archivo directamente al navegador
                return File(ms.ToArray(), "image/png", $"QR_{nombreCompleto.Replace(" ", "_")}.png");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar QR: {ex.Message}");
            }
        }

        //DESCARGAR TODOS LOS QR EN FORMATO ZIP
        [HttpGet("descargar-qrs-zip")]
        public IActionResult DescargarQRsZip()
        {
            try
            {
                using var msZip = new MemoryStream();
                using (var archive = new ZipArchive(msZip, ZipArchiveMode.Create, true))
                {
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
                        string textoSeccionArchivo = string.IsNullOrWhiteSpace(seccion) ? "" : $"{seccion} ";
                        string nombreArchivo = $"{grado} {textoSeccionArchivo}{apellidos} {nombres}";

                        string textoSeccionCarpeta = string.IsNullOrWhiteSpace(seccion) ? "" : $"_Seccion_{seccion}";
                        string nombreSubCarpeta = $"{grado}{textoSeccionCarpeta}".Replace(" ", "_");

                        // Generar QR
                        using var qrGenerator = new QRCodeGenerator();
                        using var qrCodeData = qrGenerator.CreateQrCode(codigoQR, QRCodeGenerator.ECCLevel.Q);
                        using var qrCode = new QRCode(qrCodeData);
                        using var qrBitmap = qrCode.GetGraphic(20);

                        using var lienzoFinal = new Bitmap(qrBitmap.Width, qrBitmap.Height + 60);
                        using (Graphics g = Graphics.FromImage(lienzoFinal))
                        {
                            g.Clear(Color.White);
                            g.DrawImage(qrBitmap, 0, 0);
                            using var fuente = new Font("Arial", 16, FontStyle.Bold);
                            using var brocha = new SolidBrush(Color.Black);
                            var formato = new StringFormat { Alignment = StringAlignment.Center };
                            g.DrawString(nombreCompleto, fuente, brocha, new RectangleF(0, qrBitmap.Height, qrBitmap.Width, 60), formato);
                        }

                        // Guardar la imagen directamente dentro del archivo ZIP
                        var zipEntry = archive.CreateEntry($"{nombreSubCarpeta}/{nombreArchivo}.png");
                        using var entryStream = zipEntry.Open();
                        lienzoFinal.Save(entryStream, ImageFormat.Png);
                    }
                }

                msZip.Position = 0;
                string nombreZip = $"QRs_Colegio_Masivo_{DateTime.Now:yyyyMMdd}.zip";

                // Retornamos el ZIP al navegador
                return File(msZip.ToArray(), "application/zip", nombreZip);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar ZIP: {ex.Message}");
            }
        }

        //ACTUALIZAR FOTO DE PERFIL DE ALUMNO
        [HttpPost("subir-foto")]
        public IActionResult SubirFotoPerfil([FromBody] FotoUploadRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoQR) || string.IsNullOrWhiteSpace(request.FotoBase64))
            {
                return BadRequest(new { mensaje = "El carnet y la imagen son obligatorios." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                // 1. Obtener el ID del alumno
                string queryEst = "SELECT idEstudiante FROM tblEstudiantes WHERE CodigoQR = @qr";
                int idEstudiante = 0;
                using (var cmdEst = new SqlCommand(queryEst, conn))
                {
                    cmdEst.Parameters.AddWithValue("@qr", request.CodigoQR);
                    var res = cmdEst.ExecuteScalar();
                    if (res != null) idEstudiante = Convert.ToInt32(res);
                }

                if (idEstudiante == 0) return NotFound(new { mensaje = "Estudiante no encontrado." });

                // 2. Procesar y guardar el archivo en wwwroot
                string carpetaBase = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documentos");
                if (!Directory.Exists(carpetaBase)) Directory.CreateDirectory(carpetaBase);

                byte[] bytesFoto = Convert.FromBase64String(request.FotoBase64);
                string nombreArchivo = $"FOTO_{idEstudiante}_{DateTime.Now.Ticks}.png";
                System.IO.File.WriteAllBytes(Path.Combine(carpetaBase, nombreArchivo), bytesFoto);
                string rutaRelativa = $"/documentos/{nombreArchivo}";

                // 3. Aplicar UPSERT: Si ya existe registro lo actualiza, si no, lo inserta
                string queryUpsert = @"
                    IF EXISTS (SELECT 1 FROM tblEstudiantes_Multimedia WHERE idEstudiante = @id)
                        UPDATE tblEstudiantes_Multimedia SET fotoPerfilPath = @ruta WHERE idEstudiante = @id;
                    ELSE
                        INSERT INTO tblEstudiantes_Multimedia (idEstudiante, fotoPerfilPath) VALUES (@id, @ruta);";

                using (var cmdUpsert = new SqlCommand(queryUpsert, conn))
                {
                    cmdUpsert.Parameters.AddWithValue("@id", idEstudiante);
                    cmdUpsert.Parameters.AddWithValue("@ruta", rutaRelativa);
                    cmdUpsert.ExecuteNonQuery();
                }

                return Ok(new { mensaje = "Fotografía actualizada exitosamente.", ruta = rutaRelativa });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        public class FotoUploadRequest
        {
            public string CodigoQR { get; set; }
            public string FotoBase64 { get; set; }
        }

        //OBTENER ALUMNOS EN RIESGO (MULTI-TABLA)
        [HttpGet("riesgo")]
        public IActionResult ObtenerAlumnosEnRiesgo([FromQuery] int mes, [FromQuery] int anio, [FromQuery] int maxFaltas)
        {
            try
            {
                var listaRiesgo = new List<object>();
                using var conn = new SqlConnection(_connectionString);

                // Ejecutamos la Función Multi-Tabla como si fuera una tabla normal
                string query = "SELECT idEstudiante, nombreCompleto, faltasAcumuladas, nivelRiesgo FROM dbo.fn_AlumnosEnRiesgo(@mes, @anio, @maxFaltas)";

                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@mes", mes);
                cmd.Parameters.AddWithValue("@anio", anio);
                cmd.Parameters.AddWithValue("@maxFaltas", maxFaltas);

                conn.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    listaRiesgo.Add(new
                    {
                        idEstudiante = reader["idEstudiante"],
                        nombreCompleto = reader["nombreCompleto"].ToString(),
                        faltasAcumuladas = reader["faltasAcumuladas"],
                        nivelRiesgo = reader["nivelRiesgo"].ToString()
                    });
                }

                return Ok(listaRiesgo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        // REGISTRAR JUSTIFICACIÓN DE FALTA 
        [HttpPost("justificar")]
        public IActionResult RegistrarJustificacion([FromBody] JustificacionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoQR))
            {
                return BadRequest(new { mensaje = "El código del alumno es obligatorio." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);

                // 1. Buscamos el ID del estudiante
                string queryBusqueda = "SELECT idEstudiante, (nombres + ' ' + apellidos) AS nombreCompleto FROM tblEstudiantes WHERE CodigoQR = @qr";
                int idEstudiante = 0;
                string nombreEstudiante = "";

                using (var cmdBusqueda = new SqlCommand(queryBusqueda, conn))
                {
                    cmdBusqueda.Parameters.AddWithValue("@qr", request.CodigoQR);
                    conn.Open();
                    using var reader = cmdBusqueda.ExecuteReader();
                    if (reader.Read())
                    {
                        idEstudiante = Convert.ToInt32(reader["idEstudiante"]);
                        nombreEstudiante = reader["nombreCompleto"].ToString();
                    }
                }

                if (idEstudiante == 0) return NotFound(new { mensaje = "No se encontró ningún estudiante con ese código." });

                // 2. Lógica para crear los archivos físicos a partir del Base64
                string rutaPdf = null;
                string rutaFirma = null;

                // Creamos la carpeta 'documentos' dentro de wwwroot si no existe
                string carpetaBase = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documentos");
                if (!Directory.Exists(carpetaBase)) Directory.CreateDirectory(carpetaBase);

                // Si viene un PDF
                if (!string.IsNullOrWhiteSpace(request.PdfBase64))
                {
                    byte[] bytesPdf = Convert.FromBase64String(request.PdfBase64);
                    string nombrePdf = $"PDF_{idEstudiante}_{DateTime.Now.Ticks}.pdf";
                    System.IO.File.WriteAllBytes(Path.Combine(carpetaBase, nombrePdf), bytesPdf);
                    rutaPdf = $"/documentos/{nombrePdf}"; 
                }

                // Si viene una Firma
                if (!string.IsNullOrWhiteSpace(request.FirmaBase64))
                {
                    byte[] bytesFirma = Convert.FromBase64String(request.FirmaBase64);
                    string nombreFirma = $"FIRMA_{idEstudiante}_{DateTime.Now.Ticks}.png";
                    System.IO.File.WriteAllBytes(Path.Combine(carpetaBase, nombreFirma), bytesFirma);
                    rutaFirma = $"/documentos/{nombreFirma}";
                }

                // 3. Insertamos en la Base de Datos
                string queryInsert = @"INSERT INTO tblJustificaciones (idEstudiante, fechaFalta, motivo, documentoRespaldoPath, firmaPath) 
                                       VALUES (@id, @fecha, @motivo, @doc, @firma)";

                using (var cmdInsert = new SqlCommand(queryInsert, conn))
                {
                    cmdInsert.Parameters.AddWithValue("@id", idEstudiante);
                    cmdInsert.Parameters.AddWithValue("@fecha", request.FechaFalta);
                    cmdInsert.Parameters.AddWithValue("@motivo", request.Motivo);
                    cmdInsert.Parameters.AddWithValue("@doc", (object)rutaPdf ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("@firma", (object)rutaFirma ?? DBNull.Value);

                    cmdInsert.ExecuteNonQuery();
                }

                return Ok(new { mensaje = $"Justificación registrada exitosamente para {nombreEstudiante}." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        public class JustificacionRequest
        {
            public string CodigoQR { get; set; }
            public DateTime FechaFalta { get; set; }
            public string Motivo { get; set; }
            public string PdfBase64 { get; set; }
            public string FirmaBase64 { get; set; }
        }

        //LISTAR JUSTIFICACIONES (PARA EL MAESTRO)
        [HttpGet("lista-justificaciones")]
        public IActionResult ListarJustificaciones()
        {
            try
            {
                var lista = new List<object>();
                using var conn = new SqlConnection(_connectionString);

                // Hacemos un JOIN para saber el nombre del alumno que justificó
                string query = @"
                    SELECT j.idJustificacion, (e.nombres + ' ' + e.apellidos) AS Estudiante, 
                           j.fechaFalta, j.motivo, j.documentoRespaldoPath, j.firmaPath 
                    FROM tblJustificaciones j
                    INNER JOIN tblEstudiantes e ON j.idEstudiante = e.idEstudiante
                    ORDER BY j.fechaRegistro DESC";

                using var cmd = new SqlCommand(query, conn);
                conn.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    lista.Add(new
                    {
                        id = reader["idJustificacion"],
                        estudiante = reader["Estudiante"].ToString(),
                        fechaFalta = Convert.ToDateTime(reader["fechaFalta"]).ToString("dd/MM/yyyy"),
                        motivo = reader["motivo"].ToString(),
                        // Si es nulo en la BD, mandamos una cadena vacía
                        pdf = reader["documentoRespaldoPath"] != DBNull.Value ? reader["documentoRespaldoPath"].ToString() : "",
                        firma = reader["firmaPath"] != DBNull.Value ? reader["firmaPath"].ToString() : ""
                    });
                }

                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno: {ex.Message}" });
            }
        }

        //REGISTRAR Y ASIGNAR UN ENCARGADO
        [HttpPost("asignar-encargado")]
        public IActionResult AsignarEncargado([FromBody] EncargadoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoQR) || string.IsNullOrWhiteSpace(request.Dpi) || 
                string.IsNullOrWhiteSpace(request.Nombres) || string.IsNullOrWhiteSpace(request.Apellidos) || 
                string.IsNullOrWhiteSpace(request.Telefono) || string.IsNullOrWhiteSpace(request.Parentesco))
            {
                return BadRequest(new { mensaje = "Todos los campos obligatorios deben estar completos." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                // 1. Buscamos al estudiante utilizando su código de carnet / QR
                string queryEstudiante = "SELECT idEstudiante, (nombres + ' ' + apellidos) AS nombreCompleto FROM tblEstudiantes WHERE CodigoQR = @qr";
                int idEstudiante = 0;
                string nombreEstudiante = "";

                using (var cmdEst = new SqlCommand(queryEstudiante, conn))
                {
                    cmdEst.Parameters.AddWithValue("@qr", request.CodigoQR.Trim());
                    using var readerEst = cmdEst.ExecuteReader();
                    if (readerEst.Read())
                    {
                        idEstudiante = Convert.ToInt32(readerEst["idEstudiante"]);
                        nombreEstudiante = readerEst["nombreCompleto"].ToString();
                    }
                }

                if (idEstudiante == 0)
                {
                    return NotFound(new { mensaje = "No se encontró ningún estudiante con el carnet especificado." });
                }

                string queryEncargado = "SELECT idEncargado FROM tblEncargados WHERE dpi = @dpi";
                int idEncargado = 0;

                using (var cmdEnc = new SqlCommand(queryEncargado, conn))
                {
                    cmdEnc.Parameters.AddWithValue("@dpi", request.Dpi.Trim());
                    var res = cmdEnc.ExecuteScalar();
                    if (res != null)
                    {
                        idEncargado = Convert.ToInt32(res);
                    }
                }

                if (idEncargado == 0)
                {
                    string queryInsertEnc = @"INSERT INTO tblEncargados (dpi, nombres, apellidos, telefono, correo) 
                                              VALUES (@dpi, @nombres, @apellidos, @telefono, @correo);
                                              SELECT SCOPE_IDENTITY();"; 
                    
                    using (var cmdInsEnc = new SqlCommand(queryInsertEnc, conn))
                    {
                        cmdInsEnc.Parameters.AddWithValue("@dpi", request.Dpi.Trim());
                        cmdInsEnc.Parameters.AddWithValue("@nombres", request.Nombres.Trim());
                        cmdInsEnc.Parameters.AddWithValue("@apellidos", request.Apellidos.Trim());
                        cmdInsEnc.Parameters.AddWithValue("@telefono", request.Telefono.Trim());
                        cmdInsEnc.Parameters.AddWithValue("@correo", string.IsNullOrWhiteSpace(request.Correo) ? (object)DBNull.Value : request.Correo.Trim());
                        
                        idEncargado = Convert.ToInt32(cmdInsEnc.ExecuteScalar());
                    }
                }

                string queryRelacion = "SELECT COUNT(1) FROM tblEstudiante_Encargado WHERE idEstudiante = @idEst AND idEncargado = @idEnc";
                using (var cmdRel = new SqlCommand(queryRelacion, conn))
                {
                    cmdRel.Parameters.AddWithValue("@idEst", idEstudiante);
                    cmdRel.Parameters.AddWithValue("@idEnc", idEncargado);
                    int relacionExiste = (int)cmdRel.ExecuteScalar();

                    if (relacionExiste > 0)
                    {
                        return BadRequest(new { mensaje = $"Este encargado ya está vinculado al estudiante {nombreEstudiante}." });
                    }
                }

                string queryInsertRel = @"INSERT INTO tblEstudiante_Encargado (idEstudiante, idEncargado, parentesco, esContactoEmergencia) 
                                          VALUES (@idEst, @idEnc, @parentesco, 1)";
                
                using (var cmdInsRel = new SqlCommand(queryInsertRel, conn))
                {
                    cmdInsRel.Parameters.AddWithValue("@idEst", idEstudiante);
                    cmdInsRel.Parameters.AddWithValue("@idEnc", idEncargado);
                    cmdInsRel.Parameters.AddWithValue("@parentesco", request.Parentesco.Trim());
                    cmdInsRel.ExecuteNonQuery();
                }

                return Ok(new { mensaje = $"Encargado asignado exitosamente al estudiante {nombreEstudiante}." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error interno del servidor: {ex.Message}" });
            }
        }

        public class EncargadoRequest
        {
            public string CodigoQR { get; set; }
            public string Dpi { get; set; }
            public string Nombres { get; set; }
            public string Apellidos { get; set; }
            public string Telefono { get; set; }
            public string Correo { get; set; }
            public string Parentesco { get; set; }
        }

        //GENERAR QR MASIVO PARA TODOS LOS ESTUDIANTES
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

                    string textoSeccionArchivo = string.IsNullOrWhiteSpace(seccion) ? "" : $"{seccion} ";


                    string nombreArchivo = $"{grado} {textoSeccionArchivo}{apellidos} {nombres}";

                    // LÓGICA DE CREACIÓN DE SUBCARPETAS 
                    string textoSeccion = string.IsNullOrWhiteSpace(seccion) ? "" : $"Seccion{seccion}";

                    string nombreSubCarpeta = $"{grado}{textoSeccion}".Replace(" ", "");

                    string rutaDestinoFinal = Path.Combine(rutaCarpetaBase, nombreSubCarpeta);

                    if (!Directory.Exists(rutaDestinoFinal))
                    {
                        Directory.CreateDirectory(rutaDestinoFinal);
                    }

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

                    // GUARDAR EN LA SUBCARPETA CORRECTA
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

        // CLASES DTO (Modelos) 
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
            public string TiempoTotal { get; set; }
        }
    }
}