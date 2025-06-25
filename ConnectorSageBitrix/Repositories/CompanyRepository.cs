using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class CompanyRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public CompanyRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Company> GetAll()
        {
            return GetWithFilter("");
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
                    CodigoCategoriaCliente_ = @codigoCategoria
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
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

            for (int i = 0; i < codigoList.Count; i++)
            {
                string paramName = $"@p{i}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new SqlParameter(paramName, codigoList[i]));
            }

            string filter = $"WHERE CodigoCategoriaCliente_ NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
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

            return companies;
        }

        private Company MapRowToCompany(DataRow row)
        {
            Company company = new Company
            {
                CodigoCategoriaCliente = row["CodigoCategoriaCliente_"].ToString(),
                RazonSocial = row["RazonSocial"].ToString(),
                CodigoDivisa = row["CodigoDivisa"].ToString(),
                Domicilio = row["Domicilio"].ToString(),
                Domicilio2 = row["Domicilio2"].ToString(),
                Municipio = row["Municipio"].ToString(),
                CodigoPostal = row["CodigoPostal"].ToString(),
                Provincia = row["Provincia"].ToString(),
                Nacion = row["Nacion"].ToString(),
                CodigoNacion = row["CodigoNacion"].ToString(),
                Telefono = row["Telefono"].ToString(),
                EMail1 = row["EMail1"].ToString()
            };

            return company;
        }
    }
}