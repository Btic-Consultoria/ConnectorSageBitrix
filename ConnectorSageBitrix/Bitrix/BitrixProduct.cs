using System;
using Newtonsoft.Json;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixProduct
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

        // Custom fields for Products
        [JsonProperty("ufCrmProductCodigo")]
        public string CodigoArticulo { get; set; }

        [JsonProperty("ufCrmProductDescripcion")]
        public string DescripcionArticulo { get; set; }

        [JsonProperty("ufCrmProductObsoleto")]
        public string ObsoletoLc { get; set; }

        [JsonProperty("ufCrmProductLinea")]
        public string DescripcionLinea { get; set; }

        [JsonProperty("ufCrmProductPrecio")]
        public string PrecioVenta { get; set; }

        [JsonProperty("ufCrmProductDivisa")]
        public string CodigoDivisa { get; set; }

        [JsonProperty("ufCrmProductIvaIncluido")]
        public string IvaIncluido { get; set; }

        // Convert to Sage model
        public Product ToSageProduct()
        {
            decimal precioVenta = 0;
            decimal.TryParse(PrecioVenta, out precioVenta);

            return new Product
            {
                CodigoArticulo = CodigoArticulo,
                DescripcionArticulo = DescripcionArticulo,
                ObsoletoLc = ObsoletoLc == "Y",
                DescripcionLinea = DescripcionLinea,
                PrecioVenta = precioVenta,
                CodigoDivisa = CodigoDivisa,
                IvaIncluido = IvaIncluido == "Y"
            };
        }

        // Create from Sage model
        public static BitrixProduct FromSageProduct(Product product)
        {
            string obsoleto = product.ObsoletoLc ? "Y" : "N";
            string ivaIncluido = product.IvaIncluido ? "Y" : "N";

            // Use DescripcionArticulo as title, or CodigoArticulo if description is empty
            string title = !string.IsNullOrEmpty(product.DescripcionArticulo)
                ? product.DescripcionArticulo
                : product.CodigoArticulo ?? "Sin descripción";

            return new BitrixProduct
            {
                Title = title,
                CodigoArticulo = product.CodigoArticulo,
                DescripcionArticulo = product.DescripcionArticulo,
                ObsoletoLc = obsoleto,
                DescripcionLinea = product.DescripcionLinea,
                PrecioVenta = product.PrecioVenta.ToString("F2"),
                CodigoDivisa = product.CodigoDivisa,
                IvaIncluido = ivaIncluido
            };
        }

        // Check if update is needed
        public static bool NeedsProductUpdate(BitrixProduct bitrixProduct, Product sageProduct)
        {
            if (sageProduct == null)
            {
                return false;
            }

            bool obsoletoBitrix = bitrixProduct.ObsoletoLc == "Y";
            bool ivaIncluidoBitrix = bitrixProduct.IvaIncluido == "Y";
            decimal precioBitrix = 0;
            decimal.TryParse(bitrixProduct.PrecioVenta, out precioBitrix);

            // Check if any fields are different
            if (bitrixProduct.CodigoArticulo != sageProduct.CodigoArticulo)
            {
                return true;
            }

            if (bitrixProduct.DescripcionArticulo != sageProduct.DescripcionArticulo)
            {
                return true;
            }

            if (obsoletoBitrix != sageProduct.ObsoletoLc)
            {
                return true;
            }

            if (bitrixProduct.DescripcionLinea != sageProduct.DescripcionLinea)
            {
                return true;
            }

            // Small tolerance for decimal differences
            decimal difference = precioBitrix - sageProduct.PrecioVenta;
            if (difference < -0.01m || difference > 0.01m)
            {
                return true;
            }

            if (bitrixProduct.CodigoDivisa != sageProduct.CodigoDivisa)
            {
                return true;
            }

            if (ivaIncluidoBitrix != sageProduct.IvaIncluido)
            {
                return true;
            }

            return false;
        }
    }
}