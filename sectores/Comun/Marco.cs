using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.Sectores.Comun
{
    // Marco de coordenadas compartido: origen en el Punto de Reconocimiento
    // (OST_SharedBasePoint), unidades en mm.
    internal sealed class Marco
    {
        public const double FT = 304.8;

        public Transform T;      // inversa de la transformada del emplazamiento activo
        public XYZ Origen;       // punto de reconocimiento ya llevado a T
        public double Dz;        // desplazamiento de cota

        public static Marco De(Document doc)
        {
            var pr = new FilteredElementCollector(doc).OfClass(typeof(BasePoint))
                .Cast<BasePoint>().FirstOrDefault(b => b.IsShared);
            if (pr == null) throw new InvalidOperationException("sin punto de reconocimiento");

            Transform t = doc.ActiveProjectLocation.GetTotalTransform().Inverse;
            XYZ o = t.OfPoint(pr.Position);
            return new Marco { T = t, Origen = o, Dz = t.OfPoint(XYZ.Zero).Z - o.Z };
        }

        // punto interno -> [x, y] en mm respecto al punto de reconocimiento
        public double[] XY(XYZ p)
        {
            XYZ s = T.OfPoint(p);
            return new double[] { (s.X - Origen.X) * FT, (s.Y - Origen.Y) * FT };
        }

        // cota interna -> mm
        public double Z(double zInterna)
        {
            return (zInterna + Dz) * FT;
        }
    }
}
