using System;
using System.Collections.Generic;
using System.Linq;
using ConnectorSageBitrix.Logging;

namespace ConnectorSageBitrix.Models
{
    /// <summary>
    /// Representa un mapeo entre un campo de Bitrix24 y un campo de Sage
    /// </summary>
    public class FieldMapping
    {
        public string BitrixFieldName { get; set; }
        public string BitrixFieldType { get; set; }
        public string SageFieldName { get; set; }
        public string SageFieldDescription { get; set; }
        public bool IsActive { get; set; }
        public bool IsMandatory { get; set; }

        public FieldMapping()
        {
            IsActive = true;
            IsMandatory = false;
        }
    }

    /// <summary>
    /// Gestor de mapeos de campos
    /// </summary>
    public class FieldMappingManager
    {
        private readonly List<FieldMapping> _mappings;
        private readonly Logger _logger;

        public FieldMappingManager(List<FieldMapping> mappings, Logger logger)
        {
            _mappings = mappings ?? new List<FieldMapping>();
            _logger = logger;
        }

        /// <summary>
        /// Obtiene el valor mapeado de Sage para un campo de Bitrix24
        /// </summary>
        public object GetMappedValue(string bitrixFieldName, Dictionary<string, object> sageData)
        {
            try
            {
                var mapping = _mappings.Find(m =>
                    m.BitrixFieldName == bitrixFieldName && m.IsActive);

                if (mapping == null)
                {
                    _logger.Debug($"No mapping found for Bitrix field: {bitrixFieldName}");
                    return null;
                }

                if (sageData.ContainsKey(mapping.SageFieldName))
                {
                    var value = sageData[mapping.SageFieldName];
                    _logger.Debug($"Mapped {bitrixFieldName} -> {mapping.SageFieldName}: {value}");
                    return value;
                }

                if (mapping.IsMandatory)
                {
                    _logger.Error($"Mandatory field {mapping.SageFieldName} not found in Sage data");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error mapping field {bitrixFieldName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Aplica todos los mapeos activos a los datos de Sage
        /// </summary>
        public Dictionary<string, object> ApplyMappings(Dictionary<string, object> sageData)
        {
            var bitrixData = new Dictionary<string, object>();

            foreach (var mapping in _mappings.Where(m => m.IsActive))
            {
                var value = GetMappedValue(mapping.BitrixFieldName, sageData);
                if (value != null)
                {
                    bitrixData[mapping.BitrixFieldName] = value;
                }
                else if (mapping.IsMandatory)
                {
                    _logger.Error($"Mandatory mapping failed: {mapping.BitrixFieldName} <- {mapping.SageFieldName}");
                }
            }

            return bitrixData;
        }

        /// <summary>
        /// Obtiene todos los mapeos activos
        /// </summary>
        public List<FieldMapping> GetActiveMappings()
        {
            return _mappings.Where(m => m.IsActive).ToList();
        }

        /// <summary>
        /// Valida que todos los campos obligatorios estén mapeados
        /// </summary>
        public List<string> ValidateMandatoryMappings()
        {
            var missingFields = new List<string>();

            foreach (var mapping in _mappings.Where(m => m.IsMandatory && m.IsActive))
            {
                _logger.Info($"Validated mandatory mapping: {mapping.BitrixFieldName}");
            }

            return missingFields;
        }

        /// <summary>
        /// Obtiene el número total de mapeos
        /// </summary>
        public int GetTotalMappingsCount()
        {
            return _mappings.Count;
        }

        /// <summary>
        /// Obtiene el número de mapeos activos
        /// </summary>
        public int GetActiveMappingsCount()
        {
            return _mappings.Count(m => m.IsActive);
        }

        /// <summary>
        /// Obtiene el número de mapeos obligatorios
        /// </summary>
        public int GetMandatoryMappingsCount()
        {
            return _mappings.Count(m => m.IsMandatory && m.IsActive);
        }
    }
}