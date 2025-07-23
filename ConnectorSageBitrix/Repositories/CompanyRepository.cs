using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Extensions;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class CompanyRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;
        private readonly string _empresaSage;

        public CompanyRepository(DatabaseManager db, Logger logger, string empresaSage = "1")
        {
            _db = db;
            _logger = logger;
            _empresaSage = empresaSage;
            _logger.Debug($"CompanyRepository initialized with EmpresaSage: {_empresaSage}");
        }

        public List<Company> GetAll()
        {
            string filter = "WHERE CodigoEmpresa = @EmpresaSage";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@EmpresaSage", _empresaSage)
            };
            return GetWithFilter(filter, parameters);
        }

        public Company GetByCodigoCategoria(string codigoCategoria)
        {
            string query = @"
                SELECT 
                    CodigoCategoriaCliente_,
                    RazonSocial,
                    CodigoDivisa,
                    Domicilio,
                    Domicilio2,
                    Municipio,
                    CodigoPostal,
                    Provincia,
                    Nacion,
                    CodigoNacion,
                    Telefono,
                    EMail1
                FROM 
                    Clientes
                WHERE 
                    CodigoEmpresa = @EmpresaSage 
                    AND CodigoCategoriaCliente_ = @codigoCategoria
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@EmpresaSage", _empresaSage),
                new SqlParameter("@codigoCategoria", codigoCategoria)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToCompany(row);
        }

        public List<Company> GetAllExcept(List<string> codigoList)
        {
            if (codigoList == null || codigoList.Count == 0)
            {
                return GetAll();
            }

            // Build the NOT IN clause parameters
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> paramPlaceholders = new List<string>();

            // Add EmpresaSage parameter
            parameters.Add(new SqlParameter("@EmpresaSage", _empresaSage));

            for (int i = 0; i < codigoList.Count; i++)
            {
                string paramName = $"@p{i}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new SqlParameter(paramName, codigoList[i]));
            }

            string filter = $"WHERE CodigoEmpresa = @EmpresaSage AND CodigoCategoriaCliente_ NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        /// <summary>
        /// Obtiene todos los campos disponibles en la tabla Clientes para introspección
        /// </summary>
        public Dictionary<string, Type> GetAvailableFields()
        {
            try
            {
                _logger.Debug("Discovering available fields in Clientes table");

                string query = "SELECT TOP 1 * FROM Clientes WHERE CodigoEmpresa = @EmpresaSage";
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@EmpresaSage", _empresaSage)
                };

                DataTable result = _db.ExecuteQuery(query, parameters);
                var availableFields = new Dictionary<string, Type>();

                foreach (DataColumn column in result.Columns)
                {
                    availableFields[column.ColumnName] = column.DataType;
                    _logger.Debug($"Available field: {column.ColumnName} ({column.DataType.Name})");
                }

                _logger.Info($"Found {availableFields.Count} available fields in Clientes table");
                return availableFields;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error discovering available fields: {ex.Message}");
                return new Dictionary<string, Type>();
            }
        }

        /// <summary>
        /// Obtiene datos usando mapeo dinámico basado en campos disponibles
        /// </summary>
        public List<Dictionary<string, object>> GetAllDynamic(List<string> fieldsToSelect = null)
        {
            try
            {
                // Si no se especifican campos, obtener todos los disponibles
                if (fieldsToSelect == null || fieldsToSelect.Count == 0)
                {
                    var availableFields = GetAvailableFields();
                    fieldsToSelect = new List<string>(availableFields.Keys);
                }

                // Validar que los campos solicitados existen
                var availableFieldsDict = GetAvailableFields();
                var validFields = fieldsToSelect.Where(f => availableFieldsDict.ContainsKey(f)).ToList();

                if (validFields.Count != fieldsToSelect.Count)
                {
                    var missingFields = fieldsToSelect.Except(validFields).ToList();
                    _logger.Warning($"Some requested fields don't exist: {string.Join(", ", missingFields)}");
                }

                if (!validFields.Any())
                {
                    _logger.Error("No valid fields to select");
                    return new List<Dictionary<string, object>>();
                }

                string selectFields = string.Join(", ", validFields);
                string query = $@"
                    SELECT {selectFields}
                    FROM Clientes
                    WHERE CodigoEmpresa = @EmpresaSage
                ";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@EmpresaSage", _empresaSage)
                };

                DataTable result = _db.ExecuteQuery(query, parameters);
                var companies = new List<Dictionary<string, object>>();

                foreach (DataRow row in result.Rows)
                {
                    var company = new Dictionary<string, object>();
                    foreach (string field in validFields)
                    {
                        if (result.Columns.Contains(field))
                        {
                            company[field] = row[field] == DBNull.Value ? null : row[field];
                        }
                    }
                    companies.Add(company);
                }

                _logger.Debug($"Retrieved {companies.Count} companies with {validFields.Count} dynamic fields");
                return companies;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetAllDynamic: {ex.Message}");
                return new List<Dictionary<string, object>>();
            }
        }

        /// <summary>
        /// Obtiene los nombres de todos los campos disponibles (más rápido que GetAvailableFields)
        /// </summary>
        public List<string> GetAvailableFieldNames()
        {
            try
            {
                var availableFields = GetAvailableFields();
                return availableFields.Keys.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting available field names: {ex.Message}");
                return new List<string>();
            }
        }

        private List<Company> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    CodigoCategoriaCliente_,
                    RazonSocial,
                    CodigoDivisa,
                    Domicilio,
                    Domicilio2,
                    Municipio,
                    CodigoPostal,
                    Provincia,
                    Nacion,
                    CodigoNacion,
                    Telefono,
                    EMail1
                FROM 
                    Clientes
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            List<Company> companies = new List<Company>();

            foreach (DataRow row in result.Rows)
            {
                companies.Add(MapRowToCompany(row));
            }

            _logger.Debug($"Retrieved {companies.Count} companies with filter: {filter}");
            return companies;
        }

        private Company MapRowToCompany(DataRow row)
        {
            Company company = new Company
            {
                CodigoCategoriaCliente = row["CodigoCategoriaCliente_"]?.ToString() ?? "",
                RazonSocial = row["RazonSocial"]?.ToString() ?? "",
                CodigoDivisa = row["CodigoDivisa"]?.ToString() ?? "",
                Domicilio = row["Domicilio"]?.ToString() ?? "",
                Domicilio2 = row["Domicilio2"]?.ToString() ?? "",
                Municipio = row["Municipio"]?.ToString() ?? "",
                CodigoPostal = row["CodigoPostal"]?.ToString() ?? "",
                Provincia = row["Provincia"]?.ToString() ?? "",
                Nacion = row["Nacion"]?.ToString() ?? "",
                CodigoNacion = row["CodigoNacion"]?.ToString() ?? "",
                Telefono = row["Telefono"]?.ToString() ?? "",
                EMail1 = row["EMail1"]?.ToString() ?? ""
            };

            return company;
        }
    }
}