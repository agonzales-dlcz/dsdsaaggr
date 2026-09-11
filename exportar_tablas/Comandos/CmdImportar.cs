// El boton de vuelta: del Excel editado al modelo.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.ExportarTablas.Ui;

namespace Riga.ExportarTablas.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdImportar : IExternalCommand
    {
        public Result Execute(ExternalCommandData datos, ref string mensaje, ElementSet elementos)
        {
            var uidoc = datos.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                mensaje = "No hay ningun proyecto abierto.";
                return Result.Failed;
            }

            var doc = uidoc.Document;
            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Importar tablas",
                    "El proyecto esta abierto en solo lectura: no se puede escribir nada.");
                return Result.Cancelled;
            }

            try
            {
                VentanaImportar.Abrir(doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                mensaje = ex.Message;
                TaskDialog.Show("Importar tablas", "No se pudo abrir la ventana.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
