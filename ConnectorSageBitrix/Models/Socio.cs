using System;

namespace ConnectorSageBitrix.Models
{
    public class Socio
    {
        public int CodigoEmpresa { get; set; }
        public double PorParticipacion { get; set; }
        public bool Administrador { get; set; }
        public string CargoAdministrador { get; set; }
        public string DNI { get; set; }
        public string NombreEmpleado { get; set; }
    }
}