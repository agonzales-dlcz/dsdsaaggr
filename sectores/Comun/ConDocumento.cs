using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Sectores.Comun
{
    // Deshabilita los botones cuando no hay proyecto abierto.
    public class ConDocumento : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet categorias)
        {
            var ui = app.ActiveUIDocument;
            return ui != null && ui.Document != null && !ui.Document.IsFamilyDocument;
        }
    }
}
