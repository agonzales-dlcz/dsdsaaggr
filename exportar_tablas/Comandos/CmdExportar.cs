// Abre la ventana. Modal: todo corre dentro del contexto de la API.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.ExportarTablas.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdExportar : IExternalCommand
    {
        public Result Execute(ExternalCommandData datos, ref string mensaje, ElementSet elementos)
        {
            var uidoc = datos.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                mensaje = "No hay ningun proyecto abierto.";
                return Result.Failed;
            }
            if (uidoc.Document.IsFamilyDocument)
            {
                mensaje = "Esta herramienta trabaja sobre proyectos, no sobre familias.";
                return Result.Failed;
            }

            try
            {
                var ventana = new Ui.Ventana(uidoc);
                new WindowInteropHelper(ventana).Owner = datos.Application.MainWindowHandle;
                ventana.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                mensaje = Volcar(ex);
                return Result.Failed;
            }
        }

        // Un mensaje suelto no alcanza para saber donde fallo: dejamos el stack en un archivo.
        static string Volcar(Exception ex)
        {
            var raiz = ex;
            while (raiz.InnerException != null) raiz = raiz.InnerException;

            string texto = raiz.GetType().FullName + ": " + raiz.Message +
                           Environment.NewLine + Environment.NewLine + raiz.StackTrace;
            try
            {
                string carpeta = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExportarTablas");
                System.IO.Directory.CreateDirectory(carpeta);
                string ruta = System.IO.Path.Combine(carpeta,
                    "error_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                System.IO.File.WriteAllText(ruta, texto);
                return raiz.Message + Environment.NewLine + Environment.NewLine + "Detalle en: " + ruta;
            }
            catch { return texto; }
        }
    }
}
