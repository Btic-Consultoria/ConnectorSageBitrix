using System;
using System.Collections.Generic;
using System.Linq;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Extensions;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Mapping
{
    /// <summary>
    /// Sistema de mapeo dinámico que se adapta a los campos disponibles en cada base de datos
    /// </summary>
    public class DynamicFieldMapper
    {
        private readonly Logger _logger;
        private readonly Dictionary<string, Type> _availableFields;
        private readonly List<FieldMapping> _configuredMappings;

        public DynamicFieldMapper(Logger logger, Dictionary<string, Type> availableFields, List<FieldMapping> configuredMappings)
        {
            _logger = logger;
            _availableFields = availableFields ?? new Dictionary<string, Type>();
            _configuredMappings = configuredMappings ?? new List<FieldMapping>();
        }

        /// <summary>
        /// Valida que campos del mapping están realmente disponibles en la BD
        /// </summary>
        public MappingValidationResult ValidateFieldMappings()
        {
            var result = new MappingValidationResult();

            foreach (var mapping in _configuredMappings.Where(m => m.IsActive))
            {
                if (_availableFields.ContainsKey(mapping.SageFieldName))
                {
                    result.ValidMappings.Add(new ValidMapping
                    {
                        SageFieldName = mapping.SageFieldName,
                        BitrixFieldName = mapping.BitrixFieldName,
                        DataType = _availableFields[mapping.SageFieldName],
                        IsMandatory = mapping.IsMandatory
                    });

                    _logger.Debug($"✅ Valid mapping: {mapping.SageFieldName} -> {mapping.BitrixFieldName}");
                }
                else
                {
                    result.MissingFields.Add(mapping.SageFieldName);

                    if (mapping.IsMandatory)
                        result.MissingMandatoryFields.Add(mapping.SageFieldName);

                    _logger.Warning($"❌ Missing field in DB: {mapping.SageFieldName} (required for {mapping.BitrixFieldName})");
                }
            }

            result.IsValid = result.MissingMandatoryFields.Count == 0;

            _logger.Info($"Mapping validation: {result.ValidMappings.Count} valid, {result.MissingFields.Count} missing, {result.MissingMandatoryFields.Count} mandatory missing");

            return result;
        }

        /// <summary>
        /// Aplica mapeos dinámicos solo a campos que existen
        /// </summary>
        public Dictionary<string, object> ApplyDynamicMappings(Dictionary<string, object> sourceData)
        {
            var result = new Dictionary<string, object>();
            var validationResult = ValidateFieldMappings();

            foreach (var validMapping in validationResult.ValidMappings)
            {
                if (sourceData.ContainsKey(validMapping.SageFieldName))
                {
                    var value = sourceData[validMapping.SageFieldName];

                    // Limpiar y convertir el valor según su tipo
                    var cleanValue = CleanValue(value, validMapping.DataType);

                    if (cleanValue != null || !validMapping.IsMandatory)
                    {
                        result[validMapping.BitrixFieldName] = cleanValue;
                        _logger.Debug($"Mapped: {validMapping.SageFieldName} ({cleanValue}) -> {validMapping.BitrixFieldName}");
                    }
                    else if (validMapping.IsMandatory)
                    {
                        _logger.Warning($"Mandatory field {validMapping.SageFieldName} has null value");
                    }
                }
                else if (validMapping.IsMandatory)
                {
                    _logger.Error($"Mandatory field {validMapping.SageFieldName} not found in source data");
                }
            }

            _logger.Debug($"Dynamic mapping applied: {result.Count} fields mapped from {sourceData.Count} source fields");
            return result;
        }

        /// <summary>
        /// Obtiene solo los campos que están configurados Y disponibles
        /// </summary>
        public List<string> GetOptimalFieldSelection()
        {
            var validationResult = ValidateFieldMappings();
            var optimalFields = validationResult.ValidMappings.Select(m => m.SageFieldName).ToList();

            // Siempre incluir el campo clave si está disponible
            if (_availableFields.ContainsKey("CodigoCategoriaCliente_") && !optimalFields.Contains("CodigoCategoriaCliente_"))
            {
                optimalFields.Insert(0, "CodigoCategoriaCliente_");
            }

            _logger.Debug($"Optimal field selection: {string.Join(", ", optimalFields)}");
            return optimalFields;
        }

        /// <summary>
        /// Sugiere campos adicionales que están disponibles pero no mapeados
        /// </summary>
        public List<string> GetUnmappedAvailableFields()
        {
            var mappedFields = _configuredMappings.Select(m => m.SageFieldName).ToHashSet();
            var unmappedFields = _availableFields.Keys.Where(field => !mappedFields.Contains(field)).ToList();

            _logger.Debug($"Found {unmappedFields.Count} unmapped available fields");
            return unmappedFields;
        }

        /// <summary>
        /// Obtiene estadísticas del mapeo
        /// </summary>
        public MappingStatistics GetStatistics()
        {
            var validationResult = ValidateFieldMappings();
            var unmappedFields = GetUnmappedAvailableFields();

            return new MappingStatistics
            {
                TotalAvailableFields = _availableFields.Count,
                TotalConfiguredMappings = _configuredMappings.Count,
                ValidMappings = validationResult.ValidMappings.Count,
                MissingFields = validationResult.MissingFields.Count,
                MissingMandatoryFields = validationResult.MissingMandatoryFields.Count,
                UnmappedAvailableFields = unmappedFields.Count,
                IsValid = validationResult.IsValid
            };
        }

        private object CleanValue(object value, Type expectedType)
        {
            if (value == null || value == DBNull.Value)
                return null;

            try
            {
                string stringValue = value.ToString()?.Trim();

                if (string.IsNullOrEmpty(stringValue))
                    return null;

                // Limpiar según el tipo esperado
                if (expectedType == typeof(string))
                {
                    return stringValue;
                }
                else if (expectedType == typeof(int) || expectedType == typeof(int?))
                {
                    return int.TryParse(stringValue, out int intValue) ? (object)intValue : null;
                }
                else if (expectedType == typeof(decimal) || expectedType == typeof(decimal?))
                {
                    return decimal.TryParse(stringValue, out decimal decValue) ? (object)decValue : null;
                }
                else if (expectedType == typeof(double) || expectedType == typeof(double?))
                {
                    return double.TryParse(stringValue, out double doubleValue) ? (object)doubleValue : null;
                }
                else if (expectedType == typeof(DateTime) || expectedType == typeof(DateTime?))
                {
                    return DateTime.TryParse(stringValue, out DateTime dateValue) ? (object)dateValue : null;
                }
                else if (expectedType == typeof(bool) || expectedType == typeof(bool?))
                {
                    return bool.TryParse(stringValue, out bool boolValue) ? (object)boolValue : null;
                }

                return stringValue; // Default fallback
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error cleaning value '{value}' for type {expectedType.Name}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Resultado de la validación de mapeos
    /// </summary>
    public class MappingValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidMapping> ValidMappings { get; set; } = new List<ValidMapping>();
        public List<string> MissingFields { get; set; } = new List<string>();
        public List<string> MissingMandatoryFields { get; set; } = new List<string>();
    }

    /// <summary>
    /// Mapeo válido que se puede usar
    /// </summary>
    public class ValidMapping
    {
        public string SageFieldName { get; set; }
        public string BitrixFieldName { get; set; }
        public Type DataType { get; set; }
        public bool IsMandatory { get; set; }
    }

    /// <summary>
    /// Estadísticas del sistema de mapeo
    /// </summary>
    public class MappingStatistics
    {
        public int TotalAvailableFields { get; set; }
        public int TotalConfiguredMappings { get; set; }
        public int ValidMappings { get; set; }
        public int MissingFields { get; set; }
        public int MissingMandatoryFields { get; set; }
        public int UnmappedAvailableFields { get; set; }
        public bool IsValid { get; set; }

        public override string ToString()
        {
            return $"Mapping Stats: {ValidMappings}/{TotalConfiguredMappings} valid, {MissingMandatoryFields} mandatory missing, {UnmappedAvailableFields} unused fields";
        }
    }
}