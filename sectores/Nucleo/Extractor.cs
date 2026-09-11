// Arma el JSON de sectores a partir de los suelos elegidos.
//
// Coordenadas en mm referidas al Punto de Reconocimiento (OST_SharedBasePoint).
//
// El esquema NO cambia respecto de la version anterior: "Parametrizar sectores" lee este
// mismo archivo. Lo unico que se volvio configurable es de donde salen los dos valores.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Riga.Sectores.Comun;

namespace Riga.Sectores.Nucleo
{
    internal sealed class Extractor
    {
        // Nombres de DESTINO: en estos se escribe despues, y no se eligen.
        public const string P_SECTOR = "Sector";
        public const string P_NIVEL = "Nivel del elemento";

        readonly Document _doc;

        public int Escritos, SinContorno;

        public Extractor(Document doc) { _doc = doc; }

        public string Json(IList<Element> suelos, Fuente deSector, Fuente deNivel)
        {
            Escritos = 0; SinContorno = 0;

            var marco = Marco.De(_doc);
            var filas = new List<string>();

            // Los GUID que van al JSON son los del parametro DESTINO, no los del origen:
            // "Parametrizar sectores" los usa para saber donde escribir.
            Guid gSec = Guid.Empty, gNiv = Guid.Empty;

            foreach (var e in suelos)
            {
                var cara = Geometria.CaraInferior(e);
                if (cara == null) { SinContorno++; continue; }

                var pol = Geometria.Contorno(cara).Select(p => marco.XY(p)).ToList();
                if (pol.Count < 3) { SinContorno++; continue; }
                if (Geometria.Area(pol) < 0) pol.Reverse();

                var bb = e.get_BoundingBox(null);
                if (bb == null) { SinContorno++; continue; }

                if (gSec == Guid.Empty) gSec = GuidDe(e, P_SECTOR);
                if (gNiv == Guid.Empty) gNiv = GuidDe(e, P_NIVEL);

                filas.Add("    { \"sector\": \"" + Geometria.Esc(deSector.Leer(_doc, e)) + "\""
                    + ", \"nivel\": \"" + Geometria.Esc(deNivel.Leer(_doc, e)) + "\""
                    + ", \"z_min\": " + Geometria.N(marco.Z(bb.Min.Z))
                    + ", \"z_max\": " + Geometria.N(marco.Z(bb.Max.Z))
                    + ", \"poligono\": " + Geometria.Pol(pol) + " }");
            }

            if (filas.Count == 0)
                throw new InvalidOperationException(
                    "ninguno de los " + suelos.Count + " suelos elegidos dio contorno.");

            var js = new StringBuilder();
            js.Append("{\n");
            js.Append("  \"unidades\": \"mm\",\n");
            js.Append("  \"origen\": \"Punto de Reconocimiento (OST_SharedBasePoint)\",\n");
            js.Append("  \"parametros\": { \"sector\": \"" + gSec + "\", \"nivel\": \"" + gNiv + "\" },\n");
            js.Append("  \"sectores\": [\n");
            js.Append(string.Join(",\n", filas.ToArray()));
            js.Append("\n  ]\n}\n");

            Escritos = filas.Count;
            return js.ToString();
        }

        /// <summary>GUID del parametro compartido de destino, si el suelo lo tiene.</summary>
        Guid GuidDe(Element e, string nombre)
        {
            try
            {
                var p = e.LookupParameter(nombre);
                return (p != null && p.IsShared) ? p.GUID : Guid.Empty;
            }
            catch { return Guid.Empty; }
        }

        /// <summary>Todos los suelos del modelo, ordenados por tipo.</summary>
        public static List<Element> Suelos(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements()
                .OrderBy(e => NombreTipo(doc, e), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static string NombreTipo(Document doc, Element e)
        {
            try
            {
                var t = doc.GetElement(e.GetTypeId()) as ElementType;
                return t == null ? "" : (t.Name ?? "");
            }
            catch { return ""; }
        }
    }
}
