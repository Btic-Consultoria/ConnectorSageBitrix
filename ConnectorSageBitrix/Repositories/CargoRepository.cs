using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class CargoRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public CargoRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Cargo> GetAll()
        {
            return GetWithFilter("");
        }

        public Cargo GetByDNI(string dni)
        {
            string query = @"
                SELECT 
                    cfh.CodigoEmpresa,
                    p.GuidPersona,
                    p.Dni,
                    cfh.CargoFechaHasta,
                    cfh.CargoAdministrador,
                    cfh.SocioUnico
                FROM 
                    Personas p
                    LEFT JOIN CargosFiscalHistorico cfh ON p.GuidPersona = cfh.GuidPersona
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
            return MapRowToCargo(row);
        }

        public List<Cargo> GetAllExcept(List<string> dniList)
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

            string filter = $"WHERE p.DNI NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        private List<Cargo> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    cfh.CodigoEmpresa,
                    p.GuidPersona,
                    p.Dni,
                    cfh.CargoFechaHasta,
                    cfh.CargoAdministrador,
                    cfh.SocioUnico
                FROM 
                    CargosFiscalHistorico cfh
                    INNER JOIN Personas p ON cfh.GuidPersona = p.GuidPersona
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            List<Cargo> cargos = new List<Cargo>();

            foreach (DataRow row in result.Rows)
            {
                cargos.Add(MapRowToCargo(row));
            }

            return cargos;
        }

        private Cargo MapRowToCargo(DataRow row)
        {
            Cargo cargo = new Cargo
            {
                GuidPersona = row["GuidPersona"].ToString(),
                DNI = row["Dni"].ToString(),
                SocioUnico = Convert.ToBoolean(row["SocioUnico"])
            };

            // Handle potential NULL values
            if (row["CodigoEmpresa"] != DBNull.Value)
            {
                cargo.CodigoEmpresa = Convert.ToInt32(row["CodigoEmpresa"]);
            }

            if (row["CargoFechaHasta"] != DBNull.Value)
            {
                cargo.CargoFechaHasta = Convert.ToDateTime(row["CargoFechaHasta"]);
            }

            if (row["CargoAdministrador"] != DBNull.Value)
            {
                cargo.CargoAdministrador = row["CargoAdministrador"].ToString();
            }

            return cargo;
        }
    }
}