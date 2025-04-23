using System;

namespace ConnectorSageBitrix.Models
{
    public class Modelo
    {
        public string CodigoModeloImp { get; set; }
        public string Periodicidad { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaCierre { get; set; }
        public string Estado { get; set; }
    }
}