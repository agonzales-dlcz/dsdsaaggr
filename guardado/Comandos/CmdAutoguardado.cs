// El boton: abre la configuracion.
//
// El que cuenta los minutos no es este comando sino App, que se engancha a Idling al
// arrancar Revit. Este solo deja mirar y cambiar los ajustes, que es lo que hace falta
// poder hacer con Revit ya andando.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.Guardado.Ui;

namespace Riga.Guardado.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoguardado : IExternalCommand
    {
        public Result Execute(ExternalCommandData datos, ref string mensaje, ElementSet elementos)
        {
            try
            {
                VentanaAjustes.Abrir(datos.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                mensaje = ex.Message;
                TaskDialog.Show("Autoguardado", "No se pudo abrir la configuracion.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
