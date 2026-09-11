// En que parametro se escribe. Es el espejo de Fuente: alla se elige de donde LEER en el
// suelo, aca en que parametro ESCRIBIR sobre cada elemento.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Riga.Sectores.Comun;

namespace Riga.Sectores.Nucleo
{
    internal sealed class Destino
    {
        public string Nombre = "";
        public Guid Guid = System.Guid.Empty;   // del JSON, si lo trae: manda sobre el nombre

        public override string ToString() { return Nombre; }

        /// <summary>
        /// El parametro donde escribir, o null si el elemento no lo tiene o es de solo
        /// lectura. Se busca primero por GUID porque "Sector" y "Nivel del elemento"
        /// pueden repetir nombre entre modelos.
        /// </summary>
        public Parameter En(Element e)
        {
            var p = Parametros.Buscar(e, Guid, Nombre);
            return (p == null || p.IsReadOnly) ? null : p;
        }

        /// <summary>
        /// Los parametros de texto que se pueden escribir, mirando una muestra de
        /// elementos del modelo. No se recorre todo: con unos cientos alcanza para juntar
        /// los nombres y recorrer el modelo entero seria lento sin necesidad.
        /// </summary>
        public static List<string> Disponibles(Document doc, int muestra = 400)
        {
            var vistos = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            int n = 0;

            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (!Categorias.EsCandidato(e, Categorias.Excluidas)) continue;
                if (++n > muestra) break;

                foreach (Parameter p in e.Parameters)
                {
                    try
                    {
                        if (p.Definition == null || p.IsReadOnly) continue;
                        if (p.StorageType != StorageType.String) continue;
                        vistos.Add(p.Definition.Name);
                    }
                    catch { }
                }
            }

            return vistos.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
