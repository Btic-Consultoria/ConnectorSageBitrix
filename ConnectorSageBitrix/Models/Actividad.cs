using System;

namespace ConnectorSageBitrix.Models
{
    public class Actividad
    {
        public string GuidActividad { get; set; }
        public bool Principal { get; set; }
        public string SufijoCNAE { get; set; }
        public string GrupoCNAE { get; set; }
        public string CodigoEpigrafe { get; set; }
        public DateTime? BajaIAE { get; set; }
        public DateTime? AltaIAE { get; set; }
        public string CenaeEpigrafe { get; set; }
        public string Epigrafe { get; set; }
        public string TipoEpigrafe { get; set; }
    }
}