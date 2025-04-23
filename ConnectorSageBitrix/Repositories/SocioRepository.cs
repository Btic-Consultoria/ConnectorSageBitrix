using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class SocioRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public SocioRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Socio> GetAll()
        {
            return GetWithFilter("");
        }

        public Socio GetByDNI(string dni)
        {
            string query = @"
                SELECT 
                    cfh.CodigoEmpresa,
                    sh.PorParticipacion,
                    cfh.Administrador,
                    cfh.CargoAdministrador,
                    p.Dni,
                    p.NombreEmpleado 
                FROM 
                    Personas p
                    INNER JOIN SociosHistorico sh ON p.GuidPersona = sh.GuidPersona
                    INNER JOIN CargosFiscalHistorico cfh ON p.GuidPersona = cfh.GuidPersona
                WHERE 
                    p.Dni = @dni
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@dni", dni)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToSocio(row);
        }

        public List<Socio> GetAllExcept(List<string> dniList)
        {
            if (dniList == null || dniList.Count == 0)
            {
                return GetAll();
            }

            // Build the NOT IN clause parameters
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> paramPlaceholders = new List<string>();

            for (int i = 0; i < dniList.Count; i++)
            {
                string paramName = $"@p{i}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new SqlParameter(paramName, dniList[i]));
            }

            string filter = $"WHERE p.Dni NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        private List<Socio> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    cfh.CodigoEmpresa,
                    sh.PorParticipacion,
                    cfh.Administrador,
                    cfh.CargoAdministrador,
                    p.Dni,
                    p.NombreEmpleado 
                FROM 
                    Personas p
                    INNER JOIN SociosHistorico sh ON p.GuidPersona = sh.GuidPersona
                    INNER JOIN CargosFiscalHistorico cfh ON p.GuidPersona = cfh.GuidPersona
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            List<Socio> socios = new List<Socio>();

            foreach (DataRow row in result.Rows)
            {
                socios.Add(MapRowToSocio(row));
            }

            return socios;
        }

        private Socio MapRowToSocio(DataRow row)
        {
            Socio socio = new Socio
            {
                CodigoEmpresa = Convert.ToInt32(row["CodigoEmpresa"]),
                PorParticipacion = Convert.ToDouble(row["PorParticipacion"]),
                Administrador = Convert.ToBoolean(row["Administrador"]),
                DNI = row["Dni"].ToString(),
                NombreEmpleado = row["NombreEmpleado"].ToString()
            };

            // Handle potential NULL values
            if (row["CargoAdministrador"] != DBNull.Value)
            {
                socio.CargoAdministrador = row["CargoAdministrador"].ToString();
            }

            return socio;
        }
    }
}