using System;
using Autodesk.Revit.DB;

namespace Riga.Sectores.Comun
{
    internal static class Parametros
    {
        public const string NIVEL = "Nivel";

        // Prioriza el GUID del parametro compartido; cae al nombre si no hay GUID.
        // Necesario porque "Sector" y "Nivel del elemento" tienen GUID duplicados entre modelos.
        public static Parameter Buscar(Element e, Guid g, string nombre)
        {
            Parameter p = null;
            if (g != Guid.Empty) { try { p = e.get_Parameter(g); } catch { } }
            return p ?? e.LookupParameter(nombre);
        }

        // Nivel como texto: parametro "Nivel" -> nivel asociado -> nivel de tabla.
        public static string Nivel(Document doc, Element e)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p.Definition == null || !string.Equals(p.Definition.Name, NIVEL, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.StorageType == StorageType.String)
                {
                    string s = p.AsString();
                    if (!string.IsNullOrEmpty(s)) return s.Trim().ToUpperInvariant();
                }
                else if (p.StorageType == StorageType.ElementId)
                {
                    var l = doc.GetElement(p.AsElementId()) as Level;
                    if (l != null) return l.Name.Trim().ToUpperInvariant();
                }
            }
            try { var lv = doc.GetElement(e.LevelId) as Level; if (lv != null) return lv.Name.Trim().ToUpperInvariant(); }
            catch { }
            var q = e.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
            if (q != null && q.StorageType == StorageType.ElementId)
            {
                var l2 = doc.GetElement(q.AsElementId()) as Level;
                if (l2 != null) return l2.Name.Trim().ToUpperInvariant();
            }
            return "";
        }
    }
}
