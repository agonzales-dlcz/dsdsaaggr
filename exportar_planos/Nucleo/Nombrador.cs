// Arma el nombre de archivo a partir de un patron con campos entre llaves.
//
//   {Numero} - {Nombre}                 ->  IE-101 - Diagrama unifilar
//   {Numero}_{Revision}                 ->  IE-101_B
//   {P:Sector}                          ->  cualquier parametro del plano o de la vista
//   {C:Escala}                          ->  cualquier parametro del cajetin
//   {Fecha:yyyyMMdd}                    ->  20260906
//
// Los campos propios de plano ({Numero}, {Revision}, ...) quedan vacios sobre una vista:
// verificado, get_Parameter(SHEET_*) devuelve null en vistas. Lo que no se reconoce se
// deja tal cual entre llaves, para que se vea en la vista previa.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace Riga.ExportarPlanos.Nucleo
{
    internal static class Nombrador
    {
        static readonly Regex CAMPO = new Regex(@"\{([^{}]*)\}", RegexOptions.Compiled);

        /// <summary>Campos ofrecidos en el desplegable "Insertar campo".</summary>
        public static readonly string[] CAMPOS =
        {
            "{Numero}", "{Nombre}", "{Revision}", "{FechaRevision}", "{DescRevision}",
            "{FechaEmision}", "{Dibujado}", "{Revisado}", "{Aprobado}",
            "{NumeroProyecto}", "{NombreProyecto}", "{Cliente}",
            "{Papel}", "{TipoVista}", "{Escala}", "{Modelo}", "{Fecha:yyyy-MM-dd}",
            "{P:nombre del parametro}", "{C:parametro del cajetin}", "{Info:parametro del proyecto}"
        };

        public static string Resolver(string patron, Document doc, View vista,
                                      FamilyInstance cajetin, string papel)
        {
            if (string.IsNullOrWhiteSpace(patron)) patron = "{Nombre}";

            return CAMPO.Replace(patron, m =>
            {
                string campo = m.Groups[1].Value.Trim();
                string valor = Valor(campo, doc, vista, cajetin, papel);
                return valor ?? m.Value;
            });
        }

        static string Valor(string campo, Document doc, View vista,
                            FamilyInstance cajetin, string papel)
        {
            int dosPuntos = campo.IndexOf(':');
            if (dosPuntos > 0)
            {
                string clave = campo.Substring(0, dosPuntos).Trim().ToLowerInvariant();
                string arg = campo.Substring(dosPuntos + 1).Trim();
                switch (clave)
                {
                    case "p":     return DeElemento(vista, arg) ?? "";
                    case "c":     return cajetin == null ? "" : (DeElemento(cajetin, arg) ?? "");
                    case "info":  return DeElemento(doc.ProjectInformation, arg) ?? "";
                    case "fecha": return Fecha(arg);
                }
                return null;
            }

            switch (campo.ToLowerInvariant())
            {
                case "numero":          return Bip(vista, BuiltInParameter.SHEET_NUMBER);
                case "nombre":          return vista.Name;
                case "revision":        return Bip(vista, BuiltInParameter.SHEET_CURRENT_REVISION);
                case "fecharevision":   return Bip(vista, BuiltInParameter.SHEET_CURRENT_REVISION_DATE);
                case "descrevision":    return Bip(vista, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION);
                case "fechaemision":    return Bip(vista, BuiltInParameter.SHEET_ISSUE_DATE);
                case "dibujado":        return Bip(vista, BuiltInParameter.SHEET_DRAWN_BY);
                case "revisado":        return Bip(vista, BuiltInParameter.SHEET_CHECKED_BY);
                case "aprobado":        return Bip(vista, BuiltInParameter.SHEET_APPROVED_BY);
                case "numeroproyecto":  return Bip(doc.ProjectInformation, BuiltInParameter.PROJECT_NUMBER);
                case "nombreproyecto":  return Bip(doc.ProjectInformation, BuiltInParameter.PROJECT_NAME);
                case "cliente":         return Bip(doc.ProjectInformation, BuiltInParameter.CLIENT_NAME);
                case "papel":           return papel ?? "";
                case "tipovista":       return vista.ViewType.ToString();
                case "escala":          return Escala(vista);
                case "fecha":           return Fecha("yyyy-MM-dd");
                case "modelo":          return NombreModelo(doc);
            }
            return null;
        }

        static string Escala(View vista)
        {
            try
            {
                if (vista is ViewSheet) return "";
                var p = vista.get_Parameter(BuiltInParameter.VIEW_SCALE);
                if (p == null) return "";
                int d = p.AsInteger();
                return d > 0 ? "1-" + d : "";
            }
            catch { return ""; }
        }

        static string Fecha(string formato)
        {
            try { return DateTime.Now.ToString(formato); }
            catch { return DateTime.Now.ToString("yyyy-MM-dd"); }
        }

        static string NombreModelo(Document doc)
        {
            try
            {
                string t = doc.Title;
                return string.IsNullOrEmpty(t) ? "" : Path.GetFileNameWithoutExtension(t);
            }
            catch { return ""; }
        }

        static string Bip(Element e, BuiltInParameter bip)
        {
            if (e == null) return "";
            var p = e.get_Parameter(bip);
            return p == null ? "" : Texto(p);
        }

        /// <summary>Valor de un parametro cualquiera, para agrupar el PDF combinado.</summary>
        public static string Parametro(Element e, string nombre)
        {
            return DeElemento(e, nombre) ?? "";
        }

        // Instancia primero, despues el tipo: asi funciona igual para parametros de cajetin.
        static string DeElemento(Element e, string nombre)
        {
            if (e == null || string.IsNullOrEmpty(nombre)) return null;

            var p = e.LookupParameter(nombre);
            if (p != null) return Texto(p);

            var doc = e.Document;
            var tipo = doc.GetElement(e.GetTypeId());
            if (tipo != null)
            {
                p = tipo.LookupParameter(nombre);
                if (p != null) return Texto(p);
            }
            return null;
        }

        static string Texto(Parameter p)
        {
            if (p == null || !p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? "";
                case StorageType.Integer:
                    return p.AsValueString() ?? p.AsInteger().ToString();
                case StorageType.Double:
                    return p.AsValueString() ?? p.AsDouble().ToString("0.###");
                case StorageType.ElementId:
                    var e = p.Element.Document.GetElement(p.AsElementId());
                    return e != null ? e.Name : "";
                default:
                    return p.AsValueString() ?? "";
            }
        }

        /// <summary>Deja el texto apto para nombre de archivo y colapsa espacios repetidos.</summary>
        public static string Sanear(string s, string reemplazo, string siQuedaVacio)
        {
            if (reemplazo == null) reemplazo = "";

            var prohibidos = new HashSet<char>(Path.GetInvalidFileNameChars());
            var sb = new StringBuilder((s ?? "").Length);
            foreach (char c in s ?? "")
                sb.Append(prohibidos.Contains(c) ? reemplazo : c.ToString());

            string r = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
            r = r.Trim(' ', '-', '_');
            r = r.TrimEnd('.', ' ');                        // Windows no admite el punto final

            // Un patron de plano aplicado a una vista puede quedar vacio: caemos al nombre.
            if (r.Length == 0)
            {
                r = Regex.Replace(siQuedaVacio ?? "", "[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]", reemplazo);
                r = r.Trim();
            }
            if (r.Length == 0) r = "sin_nombre";
            if (r.Length > 180) r = r.Substring(0, 180).TrimEnd();
            return r;
        }
    }
}
