// Deduce el tamano de papel a partir del cajetin colocado en el plano.
// Verificado contra el modelo IEL: el cajetin A1 mide 841 x 594 mm.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.ExportarPlanos.Nucleo
{
    internal static class Papel
    {
        struct Medida
        {
            public string Nombre;
            public double Corto, Largo;             // en mm
            public ExportPaperFormat Formato;
        }

        // Solo los formatos que la API de exportacion sabe nombrar.
        static readonly Medida[] TABLA =
        {
            new Medida { Nombre = "A4",   Corto = 210,   Largo = 297,   Formato = ExportPaperFormat.ISO_A4 },
            new Medida { Nombre = "A3",   Corto = 297,   Largo = 420,   Formato = ExportPaperFormat.ISO_A3 },
            new Medida { Nombre = "A2",   Corto = 420,   Largo = 594,   Formato = ExportPaperFormat.ISO_A2 },
            new Medida { Nombre = "A1",   Corto = 594,   Largo = 841,   Formato = ExportPaperFormat.ISO_A1 },
            new Medida { Nombre = "A0",   Corto = 841,   Largo = 1189,  Formato = ExportPaperFormat.ISO_A0 },
            new Medida { Nombre = "B4",   Corto = 250,   Largo = 353,   Formato = ExportPaperFormat.ISO_B4 },
            new Medida { Nombre = "B3",   Corto = 353,   Largo = 500,   Formato = ExportPaperFormat.ISO_B3 },
            new Medida { Nombre = "B2",   Corto = 500,   Largo = 707,   Formato = ExportPaperFormat.ISO_B2 },
            new Medida { Nombre = "B1",   Corto = 707,   Largo = 1000,  Formato = ExportPaperFormat.ISO_B1 },
            new Medida { Nombre = "ANSI A", Corto = 215.9, Largo = 279.4, Formato = ExportPaperFormat.ANSI_A },
            new Medida { Nombre = "ANSI B", Corto = 279.4, Largo = 431.8, Formato = ExportPaperFormat.ANSI_B },
            new Medida { Nombre = "ANSI C", Corto = 431.8, Largo = 558.8, Formato = ExportPaperFormat.ANSI_C },
            new Medida { Nombre = "ANSI D", Corto = 558.8, Largo = 863.6, Formato = ExportPaperFormat.ANSI_D },
            new Medida { Nombre = "ANSI E", Corto = 863.6, Largo = 1117.6, Formato = ExportPaperFormat.ANSI_E },
            new Medida { Nombre = "ARCH A", Corto = 228.6, Largo = 304.8, Formato = ExportPaperFormat.ARCH_A },
            new Medida { Nombre = "ARCH B", Corto = 304.8, Largo = 457.2, Formato = ExportPaperFormat.ARCH_B },
            new Medida { Nombre = "ARCH C", Corto = 457.2, Largo = 609.6, Formato = ExportPaperFormat.ARCH_C },
            new Medida { Nombre = "ARCH D", Corto = 609.6, Largo = 914.4, Formato = ExportPaperFormat.ARCH_D },
            new Medida { Nombre = "ARCH E", Corto = 914.4, Largo = 1219.2, Formato = ExportPaperFormat.ARCH_E },
        };

        const double TOLERANCIA = 6.0;              // mm; los cajetines rara vez son exactos

        /// <summary>Nombres para el desplegable "Fijar tamano de papel".</summary>
        public static string[] Nombres()
        {
            return new[] { "Automatico" }.Concat(TABLA.Select(m => m.Nombre)).ToArray();
        }

        public static ExportPaperFormat PorNombre(string nombre)
        {
            foreach (var m in TABLA) if (m.Nombre == nombre) return m.Formato;
            return ExportPaperFormat.Default;
        }

        /// <summary>
        /// Todos los cajetines del documento, indexados por el plano que los contiene.
        ///
        /// Se resuelve con UN colector de documento, no con uno acotado a cada vista: un
        /// colector por vista obliga a Revit a calcular que se ve ahi, y para eso genera
        /// los graficos de la vista. Hacerlo plano por plano dispara el cartel "Generando
        /// graficos para Plano..." y en un modelo grande tarda de minutos a horas antes de
        /// que la ventana llegue a abrirse.
        ///
        /// Los cajetines son elementos de vista, asi que su OwnerViewId ya dice a que plano
        /// pertenecen: no hace falta preguntarle a la vista.
        /// </summary>
        public static Dictionary<ElementId, FamilyInstance> Cajetines(Document doc)
        {
            var mapa = new Dictionary<ElementId, FamilyInstance>();
            foreach (var fi in new FilteredElementCollector(doc)
                                   .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                   .WhereElementIsNotElementType()
                                   .OfType<FamilyInstance>())
            {
                var dueno = fi.OwnerViewId;
                if (dueno == null || dueno == ElementId.InvalidElementId) continue;
                if (!mapa.ContainsKey(dueno)) mapa[dueno] = fi;   // un plano, un cajetin
            }
            return mapa;
        }

        /// <summary>
        /// Rotulo del tamano ("A1", "594 x 420 mm"), y por salida el formato y el giro
        /// que hay que pasarle a la exportacion PDF.
        /// </summary>
        public static string Describir(FamilyInstance cajetin,
                                       out ExportPaperFormat formato, out PageOrientationType giro)
        {
            formato = ExportPaperFormat.Default;
            giro = PageOrientationType.Auto;
            if (cajetin == null) return "-";

            double ancho, alto;
            if (!Medir(cajetin, out ancho, out alto)) return "-";

            giro = ancho >= alto ? PageOrientationType.Landscape : PageOrientationType.Portrait;
            double corto = Math.Min(ancho, alto), largo = Math.Max(ancho, alto);

            foreach (var m in TABLA)
                if (Math.Abs(m.Corto - corto) <= TOLERANCIA && Math.Abs(m.Largo - largo) <= TOLERANCIA)
                {
                    formato = m.Formato;
                    return m.Nombre;
                }

            return string.Format("{0:0} x {1:0} mm", ancho, alto);
        }

        public static string Rotulo(PageOrientationType giro)
        {
            if (giro == PageOrientationType.Landscape) return "Apaisado";
            if (giro == PageOrientationType.Portrait) return "Vertical";
            return "Automatico";
        }

        static bool Medir(FamilyInstance cajetin, out double anchoMm, out double altoMm)
        {
            anchoMm = altoMm = 0;
            var pa = cajetin.get_Parameter(BuiltInParameter.SHEET_WIDTH);
            var ph = cajetin.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
            if (pa == null || ph == null) return false;
            anchoMm = UnitUtils.ConvertFromInternalUnits(pa.AsDouble(), UnitTypeId.Millimeters);
            altoMm = UnitUtils.ConvertFromInternalUnits(ph.AsDouble(), UnitTypeId.Millimeters);
            return anchoMm > 1 && altoMm > 1;
        }
    }
}
