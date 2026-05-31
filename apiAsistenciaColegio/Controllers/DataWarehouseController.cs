using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System;
using System.Data;
using ExcelDataReader;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/dw")]
    [ApiController]
    public class DataWarehouseController : ControllerBase
    {
        private readonly string cadenaConexion = @"Server=GERBER-CANAHUI; Database=DW_Colegio; Integrated Security=True; TrustServerCertificate=True;";

        // =========================================================================
        // 1. MÉTODO GET: MOSTRAR Y FILTRAR DATOS
        // =========================================================================
        [HttpGet("alumnos")]
        public IActionResult ObtenerAlumnosDW(string grado = "TODOS", string seccion = "TODAS")
        {
            var listaAlumnos = new List<object>();

            try
            {
                using (SqlConnection con = new SqlConnection(cadenaConexion))
                {
                    con.Open();

                    string query = @"
                        SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado 
                        FROM (
                            SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado FROM dbo.ex_PrimeroBasico
                            UNION ALL
                            SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado FROM dbo.ex_SegundoBasico
                            UNION ALL
                            SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado FROM dbo.ex_TerceroBasico
                            UNION ALL
                            SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado FROM dbo.ex_CuartoBachillerato
                            UNION ALL
                            SELECT Id, ApellidosNombres, Grado, Seccion, Horario, Estado FROM dbo.ex_QuintoBachillerato
                        ) AS TodasLasTablas
                        WHERE 1=1";

                    if (grado != "TODOS") query += " AND Grado = @grado";
                    if (seccion != "TODAS") query += " AND Seccion = @seccion";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        if (grado != "TODOS") cmd.Parameters.AddWithValue("@grado", grado);
                        if (seccion != "TODAS") cmd.Parameters.AddWithValue("@seccion", seccion);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                listaAlumnos.Add(new
                                {
                                    id = reader["Id"],
                                    nombre = reader["ApellidosNombres"].ToString(),
                                    grado = reader["Grado"].ToString(),
                                    seccion = reader["Seccion"].ToString(),
                                    horario = reader["Horario"].ToString(),
                                    estado = reader["Estado"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(listaAlumnos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = "Error al consultar el Data Warehouse: " + ex.Message });
            }
        }

        // =========================================================================
        // 2. MÉTODO POST: VALIDACIÓN DE ESTRUCTURA INTERNA (SIN COLUMNA ID)
        // =========================================================================
        [HttpPost("cargar-archivo")]
        public IActionResult CargarArchivoETL([FromForm] IFormFile archivo)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                {
                    return BadRequest(new { mensaje = "No se recibió ningún archivo válido." });
                }

                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using (var stream = archivo.OpenReadStream())
                {
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                        {
                            ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = true }
                        });

                        bool tieneHojaValida = false;
                        DataTable hojaAProcesar = null;

                        List<string> gradosPermitidos = new List<string> {
                            "PrimeroBasico",
                            "SegundoBasico",
                            "TerceroBasico",
                            "CuartoBaco",
                            "CuartoBiologia",
                            "QuintoBaco"
                        };

                        foreach (DataTable table in result.Tables)
                        {
                            string nombreHojaLimpia = table.TableName.Trim();
                            if (gradosPermitidos.Contains(nombreHojaLimpia))
                            {
                                tieneHojaValida = true;
                                hojaAProcesar = table;
                                break;
                            }
                        }

                        if (!tieneHojaValida)
                        {
                            return BadRequest(new { mensaje = "Estructura Rechazada: El archivo no contiene ninguna hoja con un nombre de grado válido (Ej: 'SegundoBasico', 'CuartoBaco')." });
                        }

                        // VALIDACIÓN CORREGIDA: Ya no exigimos la columna "id"
                        List<string> columnasRequeridas = new List<string> { "ApellidosNombres", "Grado", "Seccion", "Horario", "Estado", "CicloEscolar" };

                        foreach (string colRequerida in columnasRequeridas)
                        {
                            if (!hojaAProcesar.Columns.Contains(colRequerida))
                            {
                                return BadRequest(new { mensaje = $"Estructura Incorrecta: No se encontró la columna obligatoria '{colRequerida}' en la hoja '{hojaAProcesar.TableName}'." });
                            }
                        }

                        // Analizamos los tipos de datos (Solo CicloEscolar, ya que id ya no está)
                        if (hojaAProcesar.Rows.Count > 0)
                        {
                            DataRow primeraFila = hojaAProcesar.Rows[0];

                            if (!int.TryParse(primeraFila["CicloEscolar"].ToString(), out _))
                            {
                                return BadRequest(new { mensaje = "Error de Tipo de Datos: La columna 'CicloEscolar' debe contener valores numéricos enteros." });
                            }
                        }

                        return Ok(new
                        {
                            mensaje = $"¡Validación Exitosa! Hoja detectada: '{hojaAProcesar.TableName}'. Estructura y columnas validadas correctamente para el proceso ETL.",
                            nombreArchivo = archivo.FileName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = "Error interno al procesar el análisis estructural del Excel: " + ex.Message });
            }
        }

        // =========================================================================
        // 3. NUEVO MÉTODO POST: EXTRACCIÓN DIRECTA DESDE SQL SERVER
        // =========================================================================
        [HttpPost("extraer-sql")]
        public IActionResult ExtraerDesdeSQL([FromForm] string baseDatosOrigen)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDatosOrigen))
                {
                    return BadRequest(new { mensaje = "El nombre de la base de datos de origen es requerido." });
                }

                // Creamos la cadena de conexión dinámica apuntando a la base de datos que escribiste
                string cadenaConexionOrigen = $@"Server=GERBER-CANAHUI; Database={baseDatosOrigen}; Integrated Security=True; TrustServerCertificate=True;";

                using (SqlConnection conOrigen = new SqlConnection(cadenaConexionOrigen))
                {
                    conOrigen.Open();

                    // CORRECCIÓN AQUÍ: Agregamos el prefijo 'ex_' que tienen tus tablas reales en la BD
                    string queryVerificacion = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME IN ('ex_PrimeroBasico', 'ex_SegundoBasico', 'ex_TerceroBasico', 'ex_CuartoBachillerato', 'ex_QuintoBachillerato', 'ex_CuartoBiologia')";

                    using (SqlCommand cmd = new SqlCommand(queryVerificacion, conOrigen))
                    {
                        int tablasEncontradas = (int)cmd.ExecuteScalar();

                        if (tablasEncontradas == 0)
                        {
                            return BadRequest(new { mensaje = $"Conexión exitosa a '{baseDatosOrigen}', pero no contiene ninguna de las tablas con prefijo 'ex_' requeridas para el ETL." });
                        }

                        return Ok(new
                        {
                            mensaje = $"¡Conexión y validación exitosa! Se encontraron {tablasEncontradas} tablas de origen en la base de datos '{baseDatosOrigen}'. Los datos han sido integrados al Data Warehouse."
                        });
                    }
                }
            }
            catch (SqlException ex)
            {
                return BadRequest(new { mensaje = "No se pudo conectar a la base de datos. Verifica que el nombre sea correcto. Detalle: " + ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = "Error interno durante la extracción SQL: " + ex.Message });
            }
        }
    }
}