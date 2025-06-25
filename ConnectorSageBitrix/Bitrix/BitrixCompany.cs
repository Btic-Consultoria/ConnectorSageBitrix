using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixCompany
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

        // Custom fields for Companies
        [JsonProperty("ufCrmCompanyCategoria")]
        public string CodigoCategoriaCliente { get; set; }

        [JsonProperty("ufCrmCompanyRazon")]
        public string RazonSocial { get; set; }

        [JsonProperty("ufCrmCompanyDivisa")]
        public string CodigoDivisa { get; set; }

        [JsonProperty("ufCrmCompanyDomicilio")]
        public string Domicilio { get; set; }

        [JsonProperty("ufCrmCompanyDomicilio2")]
        public string Domicilio2 { get; set; }

        [JsonProperty("ufCrmCompanyMunicipio")]
        public string Municipio { get; set; }

        [JsonProperty("ufCrmCompanyCodigoPostal")]
        public string CodigoPostal { get; set; }

        [JsonProperty("ufCrmCompanyProvincia")]
        public string Provincia { get; set; }

        [JsonProperty("ufCrmCompanyNacion")]
        public string Nacion { get; set; }

        [JsonProperty("ufCrmCompanyCodigoNacion")]
        public string CodigoNacion { get; set; }

        [JsonProperty("ufCrmCompanyTelefono")]
        public string Telefono { get; set; }

        [JsonProperty("ufCrmCompanyEmail")]
        public string EMail1 { get; set; }

        // Convert to Sage model
        public Company ToSageCompany()
        {
            return new Company
            {
                CodigoCategoriaCliente = CodigoCategoriaCliente,
                RazonSocial = RazonSocial,
                CodigoDivisa = CodigoDivisa,
                Domicilio = Domicilio,
                Domicilio2 = Domicilio2,
                Municipio = Municipio,
                CodigoPostal = CodigoPostal,
                Provincia = Provincia,
                Nacion = Nacion,
                CodigoNacion = CodigoNacion,
                Telefono = Telefono,
                EMail1 = EMail1
            };
        }

        // Create from Sage model
        public static BitrixCompany FromSageCompany(Company company)
        {
            // Use RazonSocial as title, or CodigoCategoriaCliente if RazonSocial is empty
            string title = !string.IsNullOrEmpty(company.RazonSocial)
                ? company.RazonSocial
                : company.CodigoCategoriaCliente ?? "Sin nombre";

            return new BitrixCompany
            {
                Title = title,
                CodigoCategoriaCliente = company.CodigoCategoriaCliente,
                RazonSocial = company.RazonSocial,
                CodigoDivisa = company.CodigoDivisa,
                Domicilio = company.Domicilio,
                Domicilio2 = company.Domicilio2,
                Municipio = company.Municipio,
                CodigoPostal = company.CodigoPostal,
                Provincia = company.Provincia,
                Nacion = company.Nacion,
                CodigoNacion = company.CodigoNacion,
                Telefono = company.Telefono,
                EMail1 = company.EMail1
            };
        }

        // Check if update is needed
        public static bool NeedsCompanyUpdate(BitrixCompany bitrixCompany, Company sageCompany)
        {
            if (sageCompany == null)
            {
                return false;
            }

            // Check if any fields are different
            if (bitrixCompany.CodigoCategoriaCliente != sageCompany.CodigoCategoriaCliente)
            {
                return true;
            }

            if (bitrixCompany.RazonSocial != sageCompany.RazonSocial)
            {
                return true;
            }

            if (bitrixCompany.CodigoDivisa != sageCompany.CodigoDivisa)
            {
                return true;
            }

            if (bitrixCompany.Domicilio != sageCompany.Domicilio)
            {
                return true;
            }

            if (bitrixCompany.Domicilio2 != sageCompany.Domicilio2)
            {
                return true;
            }

            if (bitrixCompany.Municipio != sageCompany.Municipio)
            {
                return true;
            }

            if (bitrixCompany.CodigoPostal != sageCompany.CodigoPostal)
            {
                return true;
            }

            if (bitrixCompany.Provincia != sageCompany.Provincia)
            {
                return true;
            }

            if (bitrixCompany.Nacion != sageCompany.Nacion)
            {
                return true;
            }

            if (bitrixCompany.CodigoNacion != sageCompany.CodigoNacion)
            {
                return true;
            }

            if (bitrixCompany.Telefono != sageCompany.Telefono)
            {
                return true;
            }

            if (bitrixCompany.EMail1 != sageCompany.EMail1)
            {
                return true;
            }

            return false;
        }
    }
}