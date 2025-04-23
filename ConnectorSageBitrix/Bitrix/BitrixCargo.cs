using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixCargo
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

        // Custom fields for Cargos
        [JsonProperty("ufCrm57Dni")]
        public string DNI { get; set; }

        [JsonProperty("ufCrm57Cargo")]
        public string Cargo { get; set; }

        [JsonProperty("ufCrm57Unico")]
        public string SocioUnico { get; set; }

        [JsonProperty("ufCrm57Caducidad")]
        public string Caducidad { get; set; }

        // Convert to Sage model
        public Cargo ToSageCargo()
        {
            DateTime? cargoFechaHasta = null;
            if (!string.IsNullOrEmpty(Caducidad))
            {
                DateTime parsedDate;
                if (DateTime.TryParse(Caducidad, out parsedDate))
                {
                    cargoFechaHasta = parsedDate;
                }
            }

            return new Cargo
            {
                DNI = DNI,
                CargoAdministrador = Cargo,
                SocioUnico = SocioUnico == "Y",
                CargoFechaHasta = cargoFechaHasta
            };
        }

        // Create from Sage model
        public static BitrixCargo FromSageCargo(Cargo cargo)
        {
            string socioUnico = cargo.SocioUnico ? "Y" : "N";

            // Default value for empty cargo
            string cargoAdministrador = cargo.CargoAdministrador;
            if (string.IsNullOrEmpty(cargoAdministrador))
            {
                cargoAdministrador = "No especificat";
            }

            // Format caducidad date
            string caducidad = cargo.CargoFechaHasta.HasValue
                ? cargo.CargoFechaHasta.Value.ToString("yyyy-MM-ddTHH:mm:sszzz")
                : null;

            return new BitrixCargo
            {
                Title = cargo.DNI,
                DNI = cargo.DNI,
                Cargo = cargoAdministrador,
                SocioUnico = socioUnico,
                Caducidad = caducidad
            };
        }

        // Check if update is needed
        public static bool NeedsCargoUpdate(BitrixCargo bitrixCargo, Cargo sageCargo)
        {
            if (sageCargo == null)
            {
                return false;
            }

            bool socioUnicoBitrix = bitrixCargo.SocioUnico == "Y";

            // Check if any fields are different
            if (socioUnicoBitrix != sageCargo.SocioUnico)
            {
                return true;
            }

            // Get the cargo value, or empty string if not valid
            string cargoAdministrador = sageCargo.CargoAdministrador ?? "";

            // Compare with bitrix value
            if (bitrixCargo.Cargo != cargoAdministrador &&
                !(bitrixCargo.Cargo == "No especificat" && string.IsNullOrEmpty(cargoAdministrador)))
            {
                return true;
            }

            if (bitrixCargo.DNI != sageCargo.DNI)
            {
                return true;
            }

            // Check caducidad date
            if (sageCargo.CargoFechaHasta.HasValue)
            {
                if (string.IsNullOrEmpty(bitrixCargo.Caducidad))
                {
                    return true;
                }
            }
            else if (!string.IsNullOrEmpty(bitrixCargo.Caducidad))
            {
                return true;
            }

            return false;
        }
    }
}