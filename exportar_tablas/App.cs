// Suma el boton de tablas al panel "Exportar" de la pestana RIGA.

using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace Riga.ExportarTablas
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";
        const string PANEL = "Exportar";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }        // ya existe si hay otro addin

            RibbonPanel panel = null;
            foreach (var p in app.GetRibbonPanels(TAB))         // comparte panel con el de planos
                if (p.Name == PANEL) { panel = p; break; }
            if (panel == null) panel = app.CreateRibbonPanel(TAB, PANEL);

            string dll = Assembly.GetExecutingAssembly().Location;
            var datos = new PushButtonData(
                "ExportarTablas", "Exportar\ntablas", dll, "Riga.ExportarTablas.Comandos.CmdExportar")
            {
                ToolTip = "Exporta tablas de planificacion a Excel, detalladas por elemento y sin el redondeo de la tabla.",
                LongDescription =
                    "Rearma los elementos que muestra cada tabla y lee sus parametros con toda la precision. " +
                    "Cada tabla va a una pestana, mas una pestana consolidada con todo apilado."
            };
            var boton = panel.AddItem(datos) as PushButton;
            if (boton != null)
            {
                boton.AvailabilityClassName = typeof(Comun.ConDocumento).FullName;
                boton.LargeImage = Comun.Iconos.Inca(32);
                boton.Image = Comun.Iconos.Inca(16);
            }

            var vuelta = new PushButtonData(
                "ImportarTablas", "Importar\ntablas", dll, "Riga.ExportarTablas.Comandos.CmdImportar")
            {
                ToolTip = "Devuelve al modelo un Excel exportado con el boton de al lado y editado a mano.",
                LongDescription =
                    "Cada fila se ubica por el GUID que lleva, y cada columna por la chapa que el " +
                    "exportador dejo en su cabecera: el GUID del parametro compartido, el nombre del " +
                    "de sistema o el id del de proyecto. Primero muestra que va a cambiar, con el antes " +
                    "y el despues, y recien despues escribe. Todo en una transaccion: un Ctrl+Z deshace " +
                    "la importacion entera."
            };
            var boton2 = panel.AddItem(vuelta) as PushButton;
            if (boton2 != null)
            {
                boton2.AvailabilityClassName = typeof(Comun.ConDocumento).FullName;
                boton2.LargeImage = Comun.Iconos.Apache(32);
                boton2.Image = Comun.Iconos.Apache(16);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
    }
}
