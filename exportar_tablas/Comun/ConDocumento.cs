// El boton solo se habilita si hay un proyecto abierto (no familia).

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.ExportarTablas.Comun
{
    public class ConDocumento : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet categorias)
        {
            var doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;
            return doc != null && !doc.IsFamilyDocument;
        }
    }
}
