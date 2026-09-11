// Boton "Leer coordenadas": abre la ventana donde se eligen los suelos y de donde sale
// cada valor. La lectura y el JSON viven en Nucleo.Extractor.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Sectores.Comandos
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdExtraerSectores : IExternalCommand
    {
        public Result Execute(ExternalCommandData datos, ref string mensaje, ElementSet elementos)
        {
            var uidoc = datos.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                mensaje = "No hay ningun proyecto abierto.";
                return Result.Failed;
            }

            try
            {
                var v = new Ui.VentanaCoordenadas(uidoc.Document);
                new WindowInteropHelper(v).Owner = datos.Application.MainWindowHandle;
                v.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                mensaje = Comun.Fallo.Volcar(ex, "LeerCoordenadas");
                return Result.Failed;
            }
        }
    }
}
