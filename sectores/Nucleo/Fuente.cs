// De donde sale el valor de Sector y el de Nivel del elemento al leer un suelo.
//
// Ojo con la distincion: esto es el ORIGEN (que se lee del suelo). El DESTINO, o sea en
// que parametro se escribe despues sobre los elementos, sigue siendo "Sector" y "Nivel
// del elemento": eso es lo que hace la herramienta y no se elige.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Riga.Sectores.Comun;

namespace Riga.Sectores.Nucleo
{
    internal sealed class Fuente
    {
        public string Etiqueta = "";
        public string Parametro;          // null en las fuentes calculadas
        public bool NombreDeTipo;
        public bool NombreDeNivel;

        public override string ToString() { return Etiqueta; }

        public string Leer(Document doc, Element e)
        {
            try
            {
                if (NombreDeTipo)
                {
                    var t = doc.GetElement(e.GetTypeId()) as ElementType;
                    return t == null ? "" : (t.Name ?? "");
                }
                if (NombreDeNivel) return Parametros.Nivel(doc, e);
                if (string.IsNullOrEmpty(Parametro)) return "";

                // Instancia primero y despues el tipo: un parametro compartido puede estar
                // declarado de instancia y tener el valor cargado en el tipo.
                string v = Texto(e.LookupParameter(Parametro));
                if (v.Length > 0) return v;

                var tipo = doc.GetElement(e.GetTypeId());
                return tipo == null ? "" : Texto(tipo.LookupParameter(Parametro));
            }
            catch { return ""; }
        }

        static string Texto(Parameter p)
        {
            if (p == null || !p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.ElementId:
                    try
                    {
                        var e = p.Element.Document.GetElement(p.AsElementId());
                        if (e != null) return e.Name ?? "";
                    }
                    catch { }
                    return p.AsValueString() ?? "";
                default: return p.AsValueString() ?? "";
            }
        }

        // ---------------- armado de la lista ----------------

        /// <summary>
        /// Las fuentes posibles: las dos calculadas y despues todos los parametros de
        /// texto que traen los suelos elegidos, de instancia o de tipo.
        /// </summary>
        public static List<Fuente> Disponibles(Document doc, IEnumerable<Element> suelos)
        {
            var r = new List<Fuente>
            {
                new Fuente { Etiqueta = "(nombre del tipo)", NombreDeTipo = true },
                new Fuente { Etiqueta = "(nivel del suelo)", NombreDeNivel = true }
            };

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in suelos.Take(50))          // con unos pocos alcanza para juntar los nombres
            {
                Juntar(e, vistos);
                try
                {
                    var t = doc.GetElement(e.GetTypeId());
                    if (t != null) Juntar(t, vistos);
                }
                catch { }
            }

            foreach (var n in vistos.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                r.Add(new Fuente { Etiqueta = n, Parametro = n });

            return r;
        }

        static void Juntar(Element e, HashSet<string> destino)
        {
            foreach (Parameter p in e.Parameters)
            {
                try
                {
                    if (p.Definition == null) continue;
                    // Solo lo que se pueda usar como nombre: texto o referencia a elemento.
                    if (p.StorageType != StorageType.String && p.StorageType != StorageType.ElementId) continue;
                    destino.Add(p.Definition.Name);
                }
                catch { }
            }
        }

        /// <summary>La que mejor encaje con ese nombre, o la calculada de respaldo.</summary>
        public static Fuente Preferida(List<Fuente> lista, string nombre, Fuente respaldo)
        {
            foreach (var f in lista)
                if (f.Parametro != null && string.Equals(f.Parametro, nombre, StringComparison.OrdinalIgnoreCase))
                    return f;
            return respaldo;
        }
    }
}
