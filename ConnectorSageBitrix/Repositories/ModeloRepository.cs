using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class ModeloRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public ModeloRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Modelo> GetAll()
        {
            return GetWithFilter("");
        }

        public Modelo GetByCodigoModelo(string codigoModelo)
        {
            if (string.IsNullOrEmpty(codigoModelo))
            {
                _logger.Info($"GetByCodigoModelo llamado con código nulo o vacío");
                return null;
            }

            string query = @"
        SELECT 
            imp.CodigoModeloImp,
            mo.Periodicidad
        FROM 
            IOF_ModelosPresentar imp
            LEFT JOIN ModelosOficiales mo ON imp.CodigoModeloImp = mo.CodigoModeloImp
        WHERE 
            imp.CodigoModeloImp = @codigoModelo
    ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@codigoModelo", codigoModelo)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToModelo(row);
        }

        public List<Modelo> GetAllExcept(List<string> codigoList)
        {
            if (codigoList == null || codigoList.Count == 0)
            {
                return GetAll();
            }

            // Build the NOT IN clause parameters
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> paramPlaceholders = new List<string>();

            for (int i = 0; i < codigoList.Count; i++)
            {
                string paramName = $"@p{i}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new SqlParameter(paramName, codigoList[i]));
            }

            string filter = $"WHERE imp.CodigoModeloImp NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        private List<Modelo> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    imp.CodigoModeloImp,
                    mo.Periodicidad
                FROM 
                    IOF_ModelosPresentar imp
                    LEFT JOIN ModelosOficiales mo ON imp.CodigoModeloImp = mo.CodigoModeloImp
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            Dictionary<string, Modelo> uniqueModelos = new Dictionary<string, Modelo>();

            foreach (DataRow row in result.Rows)
            {
                Modelo modelo = MapRowToModelo(row);
                string codigoModeloImp = modelo.CodigoModeloImp?.Trim() ?? "";

                // Solo añadir modelos únicos basados en el código
                if (!string.IsNullOrEmpty(codigoModeloImp) && !uniqueModelos.ContainsKey(codigoModeloImp))
                {
                    uniqueModelos.Add(codigoModeloImp, modelo);
                    _logger.Debug($"Añadido modelo único: {codigoModeloImp}");
                }
                else if (!string.IsNullOrEmpty(codigoModeloImp))
                {
                    _logger.Debug($"Modelo duplicado ignorado: {codigoModeloImp}");
                }
            }

            List<Modelo> modelos = uniqueModelos.Values.ToList();
            _logger.Info($"Recuperados {modelos.Count} modelos únicos de {result.Rows.Count} filas totales");

            return modelos;
        }

        private Modelo MapRowToModelo(DataRow row)
        {
            Modelo modelo = new Modelo
            {
                CodigoModeloImp = row["CodigoModeloImp"]?.ToString()?.Trim(),
                Estado = "Desconocido" // Default value
            };

            // Handle potential NULL values
            if (row["Periodicidad"] != DBNull.Value)
            {
                modelo.Periodicidad = row["Periodicidad"].ToString()?.Trim();
            }

            return modelo;
        }
    }
}