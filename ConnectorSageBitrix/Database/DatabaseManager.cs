using System;
using System.Data;
using System.Data.SqlClient;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Logging;

namespace ConnectorSageBitrix.Database
{
    public class DatabaseManager : IDisposable
    {
        private readonly string _connectionString;
        private readonly Logger _logger;
        private SqlConnection _connection;

        public DatabaseManager(AppConfig config, Logger logger)
        {
            _connectionString = config.DB.ConnectionString;
            _logger = logger;

            // Initialize connection
            _connection = new SqlConnection(_connectionString);

            // Test connection
            try
            {
                _connection.Open();
                _logger.Info("Successfully connected to database");
                _connection.Close();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error connecting to database: {ex.Message}");
                throw;
            }
        }

        public SqlConnection GetConnection()
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }
            return _connection;
        }

        public void Close()
        {
            if (_connection != null && _connection.State != ConnectionState.Closed)
            {
                _connection.Close();
            }
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State != ConnectionState.Closed)
                {
                    _connection.Close();
                }
                _connection.Dispose();
                _connection = null;
            }
        }

        public DataTable ExecuteQuery(string query, params SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }

                        using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error executing query: {ex.Message}");
                _logger.Error($"Query: {query}");
                throw;
            }

            return dataTable;
        }

        public int ExecuteNonQuery(string query, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }

                        return command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error executing non-query: {ex.Message}");
                _logger.Error($"Query: {query}");
                throw;
            }
        }

        public object ExecuteScalar(string query, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            command.Parameters.AddRange(parameters);
                        }

                        return command.ExecuteScalar();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error executing scalar: {ex.Message}");
                _logger.Error($"Query: {query}");
                throw;
            }
        }
    }
}