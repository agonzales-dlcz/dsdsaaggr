using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Riga.Sectores.Comun
{
    internal static class Categorias
    {
        // Categorias de modelo que nunca se parametrizan.
        public static readonly HashSet<BuiltInCategory> Excluidas = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_VolumeOfInterest, BuiltInCategory.OST_ProjectBasePoint,
            BuiltInCategory.OST_SharedBasePoint, BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_SectionBox, BuiltInCategory.OST_RvtLinks,
            BuiltInCategory.OST_Constraints, BuiltInCategory.OST_CLines,
            BuiltInCategory.OST_Matchline, BuiltInCategory.OST_Views,
            BuiltInCategory.OST_Sheets, BuiltInCategory.OST_Viewports,
            BuiltInCategory.OST_Lines, BuiltInCategory.OST_PointClouds,
            BuiltInCategory.OST_CoordinateSystem,
        };

        // Zona/Ambiente ademas excluye las propias habitaciones.
        public static readonly HashSet<BuiltInCategory> ExcluidasConHabitaciones =
            new HashSet<BuiltInCategory>(Excluidas) { BuiltInCategory.OST_Rooms };

        // Elemento candidato a recibir parametros: modelo, no anidado, no especifico de vista.
        public static bool EsCandidato(Element e, HashSet<BuiltInCategory> excluidas)
        {
            var cat = e.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model) return false;
            if (excluidas.Contains(cat.BuiltInCategory) || cat.Parent != null) return false;
            if (e.ViewSpecific) return false;
            if (e is RevitLinkInstance || e is ImportInstance || e is Group) return false;
            return true;
        }
    }
}
