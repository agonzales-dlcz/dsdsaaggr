// Panel "Guardado" de la pestana compartida.
//
// Aca esta lo que hace que el autoguardado exista sin que nadie apriete nada: OnStartup
// engancha el reloj al evento Idling de Revit. El boton de la cinta solo abre la ventana.

using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Riga.Guardado.Comun;
using Riga.Guardado.Nucleo;

namespace Riga.Guardado
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";
        const string PANEL = "Guardado";
        static readonly string SALTO = Environment.NewLine + Environment.NewLine;

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }   // ya existe si arranco otro addin antes

            RibbonPanel panel = null;
            foreach (var p in app.GetRibbonPanels(TAB))
                if (p.Name == PANEL) { panel = p; break; }
            if (panel == null) panel = app.CreateRibbonPanel(TAB, PANEL);

            string dll = Assembly.GetExecutingAssembly().Location;

            var datos = new PushButtonData(
                "Autoguardado", "Auto\nguardado", dll,
                "Riga.Guardado.Comandos.CmdAutoguardado")
            {
                ToolTip = "Guarda la local cada X minutos y sincroniza con la central cada Y, por separado.",
                LongDescription =
                    "Se engancha al arrancar Revit y actua cuando Revit esta ocioso, nunca en medio "
                    + "de un comando." + SALTO
                    + "Cada cosa tiene su propio reloj: guardar la local es barato y se puede poner "
                    + "seguido; sincronizar cuesta minutos, asi que va mas espaciado. La "
                    + "sincronizacion guarda la local primero, espera a que termine, y recien ahi "
                    + "sincroniza; cuando eso pasa, el reloj del guardado local vuelve a cero." + SALTO
                    + "Los intervalos se cambian con Revit abierto y valen desde la vuelta siguiente. "
                    + "Viene apagado: hay que activarlo desde esta ventana."
            };

            var boton = panel.AddItem(datos) as PushButton;
            if (boton != null)
            {
                boton.AvailabilityClassName = typeof(Siempre).FullName;
                boton.LargeImage = Iconos.Mongol(32);
                boton.Image = Iconos.Mongol(16);
            }

            try { Reloj.Arrancar(app); }
            catch (Exception ex)
            {
                // Si el reloj no arranca, el addin igual carga: el boton sigue sirviendo
                // para mirar la bitacora y entender que paso.
                Bitacora.Anotar("no se pudo enganchar el reloj: " + ex.Message);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { Reloj.Soltar(app); } catch { }
            return Result.Succeeded;
        }
    }
}
