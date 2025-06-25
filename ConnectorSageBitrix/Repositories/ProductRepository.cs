using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Repositories
{
    public class ProductRepository
    {
        private readonly DatabaseManager _db;
        private readonly Logger _logger;

        public ProductRepository(DatabaseManager db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<Product> GetAll()
        {
            return GetWithFilter("");
        }

        public Product GetByCodigoArticulo(string codigoArticulo)
        {
            string query = @"
                SELECT 
                    DescripcionArticulo,
                    CodigoArticulo,
                    ObsoletoLc,
                    DescripcionLinea,
                    PrecioVenta,
                    CodigoDivisa,
                    IvaIncluido
                FROM 
                    Articulos
                WHERE 
                    CodigoArticulo = @codigoArticulo
            ";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@codigoArticulo", codigoArticulo)
            };

            DataTable result = _db.ExecuteQuery(query, parameters);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            DataRow row = result.Rows[0];
            return MapRowToProduct(row);
        }

        public List<Product> GetAllExcept(List<string> codigoList)
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

            string filter = $"WHERE CodigoArticulo NOT IN ({string.Join(", ", paramPlaceholders)})";
            return GetWithFilter(filter, parameters.ToArray());
        }

        private List<Product> GetWithFilter(string filter, params SqlParameter[] parameters)
        {
            string query = $@"
                SELECT 
                    DescripcionArticulo,
                    CodigoArticulo,
                    ObsoletoLc,
                    DescripcionLinea,
                    PrecioVenta,
                    CodigoDivisa,
                    IvaIncluido
                FROM 
                    Articulos
                {filter}
            ";

            DataTable result = _db.ExecuteQuery(query, parameters);
            List<Product> products = new List<Product>();

            foreach (DataRow row in result.Rows)
            {
                products.Add(MapRowToProduct(row));
            }

            return products;
        }

        private Product MapRowToProduct(DataRow row)
        {
            Product product = new Product
            {
                DescripcionArticulo = row["DescripcionArticulo"].ToString(),
                CodigoArticulo = row["CodigoArticulo"].ToString(),
                ObsoletoLc = Convert.ToBoolean(row["ObsoletoLc"]),
                DescripcionLinea = row["DescripcionLinea"].ToString(),
                CodigoDivisa = row["CodigoDivisa"].ToString(),
                IvaIncluido = Convert.ToBoolean(row["IvaIncluido"])
            };

            // Handle potential NULL values for PrecioVenta
            if (row["PrecioVenta"] != DBNull.Value)
            {
                product.PrecioVenta = Convert.ToDecimal(row["PrecioVenta"]);
            }

            return product;
        }
    }
}