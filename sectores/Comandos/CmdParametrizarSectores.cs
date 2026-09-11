// Boton "Parametrizar sectores": abre la ventana donde se elige el JSON y en que dos
// parametros escribir. El recorrido y la escritura viven en Nucleo.Parametrizador.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Sectores.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdParametrizarSectores : IExternalCommand
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
                var v = new Ui.VentanaParametrizar(uidoc.Document);
                new WindowInteropHelper(v).Owner = datos.Application.MainWindowHandle;
                v.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                mensaje = Comun.Fallo.Volcar(ex, "ParametrizarSectores");
                return Result.Failed;
            }
        }
    }
}
