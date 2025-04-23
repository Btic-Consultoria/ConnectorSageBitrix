using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixModelo
    {
        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("createdTime")]
        public DateTime? CreatedTime { get; set; }

        [JsonProperty("updatedTime")]
        public DateTime? UpdatedTime { get; set; }

        [JsonProperty("categoryId")]
        public int? CategoryID { get; set; }

        [JsonProperty("entityTypeId")]
        public int? EntityTypeID { get; set; }

        // Custom fields for Modelos
        [JsonProperty("ufCrm133Codigo")]
        public string CodigoModeloImp { get; set; }

        [JsonProperty("ufCrm133Periodicidad")]
        public string Periodicidad { get; set; }

        [JsonProperty("begindate")]
        public string FechaInicio { get; set; }

        [JsonProperty("closedate")]
        public string FechaCierre { get; set; }

        [JsonProperty("ufCrm133Estado")]
        public string Estado { get; set; }

        // Convert to Sage model
        public Modelo ToSageModelo()
        {
            DateTime? fechaInicio = null;
            DateTime? fechaCierre = null;

            if (!string.IsNullOrEmpty(FechaInicio))
            {
                DateTime parsedDate;
                if (DateTime.TryParse(FechaInicio, out parsedDate))
                {
                    fechaInicio = parsedDate;
                }
            }

            if (!string.IsNullOrEmpty(FechaCierre))
            {
                DateTime parsedDate;
                if (DateTime.TryParse(FechaCierre, out parsedDate))
                {
                    fechaCierre = parsedDate;
                }
            }

            return new Modelo
            {
                CodigoModeloImp = CodigoModeloImp,
                Periodicidad = Periodicidad,
                FechaInicio = fechaInicio,
                FechaCierre = fechaCierre,
                Estado = Estado
            };
        }

        // Create from Sage model
        public static BitrixModelo FromSageModelo(Modelo modelo)
        {
            // Use codigo as title, or add a description if needed
            string title = "Modelo " + modelo.CodigoModeloImp;

            return new BitrixModelo
            {
                Title = title,
                CodigoModeloImp = modelo.CodigoModeloImp,
                Periodicidad = modelo.Periodicidad,
                Estado = "Desconocido" // Default value
            };
        }

        // Check if update is needed
        public static bool NeedsModeloUpdate(BitrixModelo bitrixModelo, Modelo sageModelo)
        {
            if (sageModelo == null)
            {
                return false;
            }

            // Only compare fields that we know exist in the database
            if (bitrixModelo.CodigoModeloImp != sageModelo.CodigoModeloImp)
            {
                return true;
            }

            // For Periodicidad, compare if not null
            if (!string.IsNullOrEmpty(sageModelo.Periodicidad) &&
                bitrixModelo.Periodicidad != sageModelo.Periodicidad)
            {
                return true;
            }

            return false;
        }
    }
}