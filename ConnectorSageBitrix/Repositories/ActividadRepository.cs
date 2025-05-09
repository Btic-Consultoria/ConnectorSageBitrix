using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class ActividadRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public ActividadRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Actividad> GetAll()
        {
            return GetWithFilter("");
        }

        public Actividad GetByGuid(string guidActividad)
        {
            string query = @"
                SELECT 
                    a.GuidActividad,
                    a.Principal,
                    a.SufijoCNAE,
                    a.GrupoCNAE,
                    a.CodigoEpigrafe,
                    a.BajaIAE,
                    a.AltaIAE,
                    a.CenaeEpigrafe,
                    a.TipoEpigrafe,
                    ei.Epigrafe
                FROM 
                    Actividades a
                    LEFT JOIN EpigrafesIAE ei ON a.CodigoEpigrafe = ei.CodigoEpigrafe
                    AND a.TipoEpigrafe = ei.TipoEpigrafe
                WHERE 
                    a.GuidActividad = @guidActividad
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@guidActividad", guidActividad)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToActividad(row);
        }

        public Actividad GetByEpigrafe(string epigrafe)
        {
            string query = @"
                SELECT 
                    a.GuidActividad,
                    a.Principal,
                    a.SufijoCNAE,
                    a.GrupoCNAE,
                    a.CodigoEpigrafe,
                    a.BajaIAE,
                    a.AltaIAE,
                    a.CenaeEpigrafe,
                    a.TipoEpigrafe,
                    ei.Epigrafe
                FROM 
                    Actividades a
                    LEFT JOIN EpigrafesIAE ei ON a.CodigoEpigrafe = ei.CodigoEpigrafe
                    AND a.TipoEpigrafe = ei.TipoEpigrafe
                WHERE 
                    a.CodigoEpigrafe = @epigrafe
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@epigrafe", epigrafe)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToActividad(row);
        }

        public List<Actividad> GetAllExcept(List<string> guidList)
        {
            if (guidList == null || guidList.Count == 0)
            {
                return GetAll();
            }

            // Build the NOT IN clause parameters
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> paramPlaceholders = new List<string>();

            for (int i = 0; i < guidList.Count; i++)
            {
                string paramName = $"@p{i}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new SqlParameter(paramName, guidList[i]));
            }

            string filter = $"WHERE a.GuidActividad NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        private List<Actividad> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    a.GuidActividad,
                    a.Principal,
                    a.SufijoCNAE,
                    a.GrupoCNAE,
                    a.CodigoEpigrafe,
                    a.BajaIAE,
                    a.AltaIAE,
                    a.CenaeEpigrafe,
                    a.TipoEpigrafe,
                    ei.Epigrafe
                FROM 
                    Actividades a
                    LEFT JOIN EpigrafesIAE ei ON a.CodigoEpigrafe = ei.CodigoEpigrafe
                    AND a.TipoEpigrafe = ei.TipoEpigrafe
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            List<Actividad> actividades = new List<Actividad>();

            foreach (DataRow row in result.Rows)
            {
                actividades.Add(MapRowToActividad(row));
            }

            return actividades;
        }

        private Actividad MapRowToActividad(DataRow row)
        {
            Actividad actividad = new Actividad
            {
                GuidActividad = row["GuidActividad"].ToString(),
                Principal = Convert.ToBoolean(row["Principal"]),
                SufijoCNAE = row["SufijoCNAE"].ToString(),
                GrupoCNAE = row["GrupoCNAE"].ToString(),
                CodigoEpigrafe = row["CodigoEpigrafe"].ToString(),
                CenaeEpigrafe = row["CenaeEpigrafe"].ToString(),
                TipoEpigrafe = row["TipoEpigrafe"].ToString()
            };

            // Handle potential NULL values
            if (row["Epigrafe"] != DBNull.Value)
            {
                actividad.Epigrafe = row["Epigrafe"].ToString();
            }

            if (row["BajaIAE"] != DBNull.Value)
            {
                actividad.BajaIAE = Convert.ToDateTime(row["BajaIAE"]);
            }

            if (row["AltaIAE"] != DBNull.Value)
            {
                actividad.AltaIAE = Convert.ToDateTime(row["AltaIAE"]);
            }

            return actividad;
        }
    }
}