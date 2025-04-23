using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixSocio
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

        // Custom fields for Socios
        [JsonProperty("ufCrm55Dni")]
        public string DNI { get; set; }

        [JsonProperty("ufCrm55Cargo")]
        public string Cargo { get; set; }

        [JsonProperty("ufCrm55Admin")]
        public string Administrador { get; set; }

        [JsonProperty("ufCrm55Participacion")]
        public string Participacion { get; set; }

        // Convert to Sage model
        public Socio ToSageSocio()
        {
            double participacion = 0;
            double.TryParse(Participacion, out participacion);

            return new Socio
            {
                DNI = DNI,
                PorParticipacion = participacion,
                Administrador = Administrador == "Y",
                CargoAdministrador = Cargo
            };
        }

        // Create from Sage model
        public static BitrixSocio FromSageSocio(Socio socio)
        {
            string administrador = socio.Administrador ? "Y" : "N";

            // Ensure valid default values
            string cargo = socio.CargoAdministrador;
            if (string.IsNullOrEmpty(cargo))
            {
                cargo = "No especificado";
            }

            return new BitrixSocio
            {
                Title = socio.DNI,
                DNI = socio.DNI,
                Cargo = cargo,
                Administrador = administrador,
                Participacion = socio.PorParticipacion.ToString("F2")
            };
        }

        // Check if update is needed
        public static bool NeedsUpdate(BitrixSocio bitrixSocio, Socio sageSocio)
        {
            if (sageSocio == null)
            {
                return false;
            }

            bool adminBitrix = bitrixSocio.Administrador == "Y";
            double participacionBitrix = 0;
            double.TryParse(bitrixSocio.Participacion, out participacionBitrix);

            // Check if any fields are different
            if (adminBitrix != sageSocio.Administrador)
            {
                return true;
            }

            if (bitrixSocio.Cargo != sageSocio.CargoAdministrador)
            {
                return true;
            }

            if (bitrixSocio.DNI != sageSocio.DNI)
            {
                return true;
            }

            // Small tolerance for decimal differences
            double difference = participacionBitrix - sageSocio.PorParticipacion;
            if (difference < -0.01 || difference > 0.01)
            {
                return true;
            }

            return false;
        }
    }
}