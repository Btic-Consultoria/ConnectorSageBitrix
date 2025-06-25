using System;

namespace ConnectorSageBitrix.Models
{
    public class Product
    {
        public string DescripcionArticulo { get; set; }
        public string CodigoArticulo { get; set; }
        public bool ObsoletoLc { get; set; }
        public string DescripcionLinea { get; set; }
        public decimal PrecioVenta { get; set; }
        public string CodigoDivisa { get; set; }
        public bool IvaIncluido { get; set; }
    }
}