using System;

namespace ConnectorSageBitrix.Models
{
    public class Cargo
    {
        public int? CodigoEmpresa { get; set; }
        public string GuidPersona { get; set; }
        public string DNI { get; set; }
        public DateTime? CargoFechaHasta { get; set; }
        public string CargoAdministrador { get; set; }
        public bool SocioUnico { get; set; }
        public string RazonSocialEmpleado { get; set; }
    }
}