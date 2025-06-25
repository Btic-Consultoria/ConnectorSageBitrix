using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;
using ConnectorSageBitrix.Logging;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixActividad
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

        // Custom fields for Actividades
        [JsonProperty("ufCrm59Guidactividad")]
        public string GuidActividad { get; set; }

        [JsonProperty("ufCrm59Descripcion")]
        public string Descripcion { get; set; }

        [JsonProperty("ufCrm_59_CNAE_93")]
        public string CNAE93 { get; set; }

        [JsonProperty("ufCrm59AltaIae")]
        public string AltaIAE { get; set; }

        [JsonProperty("ufCrm59BajaIae")]
        public string BajaIAE { get; set; }

        [JsonProperty("ufCrm59Epigrafe")]
        public string Epigrafe { get; set; }

        [JsonProperty("ufCrm_59_CNAE_09")]
        public string CNAE09 { get; set; }

        [JsonProperty("ufCrm59Sufijo")]
        public string Sufijo { get; set; }

        [JsonProperty("ufCrm59Principal")]
        public string Principal { get; set; }

        [JsonProperty("ufCrm59TipoEpigrafe")]
        public string TipoEpigrafe { get; set; }

        // Convert to Sage model
        public Actividad ToSageActividad()
        {
            DateTime? altaIAE = null;
            DateTime? bajaIAE = null;

            if (!string.IsNullOrEmpty(AltaIAE))
            {
                DateTime parsedDate;
                if (DateTime.TryParseExact(AltaIAE, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out parsedDate))
                {
                    altaIAE = parsedDate;
                }
            }

            if (!string.IsNullOrEmpty(BajaIAE))
            {
                DateTime parsedDate;
                if (DateTime.TryParseExact(BajaIAE, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out parsedDate))
                {
                    bajaIAE = parsedDate;
                }
            }

            return new Actividad
            {
                GuidActividad = GuidActividad,
                Principal = Principal == "Y",
                SufijoCNAE = Sufijo,
                GrupoCNAE = CNAE09,
                CodigoEpigrafe = Epigrafe,
                BajaIAE = bajaIAE,
                AltaIAE = altaIAE,
                CenaeEpigrafe = CNAE93,
                Epigrafe = Descripcion,
                TipoEpigrafe = TipoEpigrafe
            };
        }

        // Create from Sage model
        public static BitrixActividad FromSageActividad(Actividad actividad)
        {
            
            string principal = actividad.Principal ? "Y" : "N";

            // Format dates for Bitrix
            string altaIAE = actividad.AltaIAE.HasValue
                ? actividad.AltaIAE.Value.ToString("dd/MM/yyyy")
                : null;

            string bajaIAE = actividad.BajaIAE.HasValue
                ? actividad.BajaIAE.Value.ToString("dd/MM/yyyy")
                : null;

            // Use description as title, or epigrafe code if description is empty
            string title = !string.IsNullOrEmpty(actividad.Epigrafe)
                ? actividad.Epigrafe
                : actividad.CodigoEpigrafe;

            return new BitrixActividad
            {
                GuidActividad = actividad.GuidActividad,
                Title = title,
                Descripcion = actividad.Epigrafe,
                CNAE93 = actividad.CenaeEpigrafe,
                AltaIAE = altaIAE,
                BajaIAE = bajaIAE,
                Epigrafe = actividad.CodigoEpigrafe,
                CNAE09 = actividad.GrupoCNAE,
                Sufijo = actividad.SufijoCNAE,
                Principal = principal,
                TipoEpigrafe = actividad.TipoEpigrafe
            };
        }

        // Check if update is needed
        public static bool NeedsActividadUpdate(BitrixActividad bitrixActividad, Actividad sageActividad)
        {
            if (sageActividad == null)
            {
                return false;
            }

            bool principalBitrix = bitrixActividad.Principal == "Y";

            // Check if any fields are different
            if (principalBitrix != sageActividad.Principal)
            {
                return true;
            }

            if (bitrixActividad.Sufijo != sageActividad.SufijoCNAE)
            {
                return true;
            }

            if (bitrixActividad.CNAE09 != sageActividad.GrupoCNAE)
            {
                return true;
            }

            if (bitrixActividad.Epigrafe != sageActividad.CodigoEpigrafe)
            {
                return true;
            }

            if (bitrixActividad.CNAE93 != sageActividad.CenaeEpigrafe)
            {
                return true;
            }

            if (bitrixActividad.TipoEpigrafe != sageActividad.TipoEpigrafe)
            {
                return true;
            }

            if (bitrixActividad.Descripcion != sageActividad.Epigrafe && !string.IsNullOrEmpty(sageActividad.Epigrafe))
            {
                return true;
            }

            // Check AltaIAE date
            if (sageActividad.AltaIAE.HasValue)
            {
                string sageDate = sageActividad.AltaIAE.Value.ToString("dd/MM/yyyy");
                if (bitrixActividad.AltaIAE != sageDate)
                {
                    return true;
                }
            }
            else if (!string.IsNullOrEmpty(bitrixActividad.AltaIAE))
            {
                return true;
            }

            // Check BajaIAE date
            if (sageActividad.BajaIAE.HasValue)
            {
                string sageDate = sageActividad.BajaIAE.Value.ToString("dd/MM/yyyy");
                if (bitrixActividad.BajaIAE != sageDate)
                {
                    return true;
                }
            }
            else if (!string.IsNullOrEmpty(bitrixActividad.BajaIAE))
            {
                return true;
            }

            return false;
        }
    }
}