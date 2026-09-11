// Resolucion de unidades para cada columna de una tabla.
//
// La regla, y es la parte importante de todo este exportador:
//
//   * la UNIDAD sale del formato de campo de la propia tabla (Apariencia > Formato de
//     campo). Si esa columna se muestra en milimetros, se exporta en milimetros; si se
//     muestra en metros, en metros. Asi el numero significa lo mismo que en la tabla.
//
//   * el REDONDEO de ese mismo formato se ignora a proposito. Es justamente lo que
//     produce el error silencioso: un area con redondeo a entero muestra 95 m2 donde el
//     modelo dice 94,76 m2.
//
// Si el campo no tiene formato propio (usa el del proyecto), se cae al formato del
// proyecto, que es lo mismo que muestra la tabla.

using System;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class Unidad
    {
        /// <summary>Que magnitud es: longitud, area, volumen, corriente... o null si no aplica.</summary>
        public ForgeTypeId Spec;

        /// <summary>Unidad a la que hay que convertir desde las internas. null = no convertir.</summary>
        public ForgeTypeId Destino;

        /// <summary>Rotulo para la cabecera: "milimetros", "metros cuadrados"...</summary>
        public string Etiqueta = "";

        public bool Convierte { get { return Destino != null && !Destino.Empty(); } }

        /// <summary>Pasa un valor interno de Revit (pies, pies2, pies3...) a la unidad de la tabla.</summary>
        public double Convertir(double interno)
        {
            if (!Convierte) return interno;
            try { return UnitUtils.ConvertFromInternalUnits(interno, Destino); }
            catch { return interno; }
        }
    }

    internal static class Unidades
    {
        static readonly Unidad NINGUNA = new Unidad();

        /// <summary>Unidad de una columna de la tabla.</summary>
        public static Unidad DeCampo(Document doc, ScheduleField campo)
        {
            ForgeTypeId spec = null;
            try { spec = campo.GetSpecTypeId(); } catch { }
            if (spec == null || spec.Empty()) return NINGUNA;

            bool medible;
            try { medible = UnitUtils.IsMeasurableSpec(spec); } catch { medible = false; }
            if (!medible) return NINGUNA;

            ForgeTypeId destino = DelCampo(campo) ?? DelProyecto(doc, spec);
            if (destino == null || destino.Empty()) return NINGUNA;

            return new Unidad { Spec = spec, Destino = destino, Etiqueta = Etiqueta(destino) };
        }

        /// <summary>Unidad de un parametro suelto, cuando el campo no declara especificacion.</summary>
        public static Unidad DeParametro(Document doc, Parameter p)
        {
            if (p == null || p.StorageType != StorageType.Double) return NINGUNA;

            ForgeTypeId spec = null;
            try { spec = p.Definition.GetDataType(); } catch { }
            if (spec == null || spec.Empty()) return NINGUNA;

            bool medible;
            try { medible = UnitUtils.IsMeasurableSpec(spec); } catch { medible = false; }
            if (!medible) return NINGUNA;

            var destino = DelProyecto(doc, spec);
            if (destino == null || destino.Empty()) return NINGUNA;

            return new Unidad { Spec = spec, Destino = destino, Etiqueta = Etiqueta(destino) };
        }

        // El formato propio de la columna, si la tabla lo tiene definido.
        static ForgeTypeId DelCampo(ScheduleField campo)
        {
            try
            {
                var fo = campo.GetFormatOptions();
                if (fo == null || fo.UseDefault) return null;   // usa el del proyecto
                var u = fo.GetUnitTypeId();
                return (u == null || u.Empty()) ? null : u;
            }
            catch { return null; }
        }

        static ForgeTypeId DelProyecto(Document doc, ForgeTypeId spec)
        {
            try
            {
                var fo = doc.GetUnits().GetFormatOptions(spec);
                var u = fo.GetUnitTypeId();
                return (u == null || u.Empty()) ? null : u;
            }
            catch { return null; }
        }

        static string Etiqueta(ForgeTypeId unidad)
        {
            try { return LabelUtils.GetLabelForUnit(unidad) ?? ""; }
            catch { return ""; }
        }

        /// <summary>Cabecera de la columna con la unidad entre parentesis.</summary>
        public static string Cabecera(string nombre, Unidad u)
        {
            if (u == null || !u.Convierte || u.Etiqueta.Length == 0) return nombre;
            return nombre + " (" + u.Etiqueta + ")";
        }
    }
}
