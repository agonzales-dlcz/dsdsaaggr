// El que cuenta los minutos. Ahora son dos contadores.
//
// POR QUE Idling Y NO UN TIMER
//
// La API de Revit solo se puede tocar desde su propio hilo y cuando Revit no esta en medio
// de un comando. Un System.Timers.Timer dispara en un hilo cualquiera y ahi la API tira
// excepcion. Idling avisa justo cuando Revit esta ocioso, que ademas es exactamente cuando
// uno quiere guardar: nunca en medio de que el usuario dibuja algo.
//
// El handler corre muchas veces por minuto, asi que lo primero que hace es comparar dos
// DateTime y salir. Todo lo caro esta detras de esa comparacion.
//
// DOS RELOJES QUE SE PISAN A PROPOSITO
//
// Guardado local y sincronizacion tienen cada uno su intervalo. Pero la sincronizacion
// guarda la local como primer paso, asi que cuando corre la sincronizacion tambien se
// reinicia el contador del guardado local: seria absurdo guardar de nuevo dos minutos
// despues de haber sincronizado.
//
// Si los dos vencen en la misma vuelta gana la sincronizacion, por lo mismo: ya incluye al
// otro. Nunca corren las dos cosas seguidas.
//
// Nada de lo de aca puede dejar escapar una excepcion: una excepcion suelta en Idling se
// come Revit entero.
//
// Y nada de lo de aca se ve. Ni ventana ni renglon en la barra de estado.
//
// Hubo una version que escribia un aviso en la barra de estado de Revit. Para eso hay que
// buscar el control por su clase de Win32 y mandarle el texto: FindWindowEx + SetWindowText.
// El problema es que eso es, literalmente, la firma de un inyector de interfaz, y en un
// instalador sin firma digital hizo que SmartScreen empezara a avisar y que el Control
// inteligente de aplicaciones lo bloqueara de plano en varias maquinas. Verificado
// comparando los binarios: las versiones que instalaban bien no importan NINGUNA api de
// user32; la que empezo a fallar importaba esas tres.
//
// Un renglon informativo no vale que la herramienta no se pueda instalar. Lo que pasa queda
// en la bitacora, que para eso esta.

using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Riga.Guardado.Nucleo
{
    internal static class Reloj
    {
        public static Ajustes Cfg = new Ajustes();

        public static DateTime ProximaLocal = DateTime.MaxValue;
        public static DateTime ProximaSync = DateTime.MaxValue;

        public static DateTime UltimaHora = DateTime.MinValue;
        public static string UltimoResultado = "todavia no corrio en esta sesion";

        static bool _ocupado;
        static bool _enganchado;

        public static void Arrancar(UIControlledApplication app)
        {
            Cfg = Ajustes.Cargar();
            Reprogramar();
            if (!_enganchado)
            {
                app.Idling += AlEstarOcioso;
                _enganchado = true;
            }
            Bitacora.Anotar("Revit arranco. Autoguardado " + (Cfg.Activo ? "activo" : "apagado")
                            + ", local cada " + Cfg.MinutosLocal + " min"
                            + ", sync cada " + Cfg.MinutosSync + " min.");
        }

        public static void Soltar(UIControlledApplication app)
        {
            if (!_enganchado) return;
            try { app.Idling -= AlEstarOcioso; } catch { }
            _enganchado = false;
        }

        public static void Reprogramar()
        {
            ReprogramarLocal();
            ReprogramarSync();
        }

        static void ReprogramarLocal()
        {
            ProximaLocal = (Cfg.Activo && Cfg.GuardarLocal)
                ? DateTime.Now.AddMinutes(Cfg.MinutosLocal)
                : DateTime.MaxValue;
        }

        static void ReprogramarSync()
        {
            ProximaSync = (Cfg.Activo && Cfg.Sincronizar)
                ? DateTime.Now.AddMinutes(Cfg.MinutosSync)
                : DateTime.MaxValue;
        }

        public static void Posponer(int minutos, Accion accion)
        {
            var cuando = DateTime.Now.AddMinutes(minutos);
            if (accion == Accion.Sync) ProximaSync = cuando;
            else ProximaLocal = cuando;
        }

        /// <summary>Toma ajustes nuevos en caliente, sin reiniciar Revit.</summary>
        public static void Aplicar(Ajustes nuevos)
        {
            Cfg = nuevos;
            Cfg.Guardar();
            Reprogramar();
            Bitacora.Anotar("Ajustes cambiados: " + (Cfg.Activo ? "activo" : "apagado")
                            + ", local=" + (Cfg.GuardarLocal ? "cada " + Cfg.MinutosLocal + " min" : "no")
                            + ", sync=" + (Cfg.Sincronizar ? "cada " + Cfg.MinutosSync + " min" : "no") + ".");
        }

        public static string FaltaParaLocal() { return Falta(ProximaLocal, Cfg.Activo && Cfg.GuardarLocal); }
        public static string FaltaParaSync() { return Falta(ProximaSync, Cfg.Activo && Cfg.Sincronizar); }

        static string Falta(DateTime cuando, bool encendido)
        {
            if (!encendido) return "apagado";
            if (cuando == DateTime.MaxValue) return "sin programar";
            var falta = cuando - DateTime.Now;
            if (falta.TotalSeconds <= 0) return "en la proxima pausa";
            if (falta.TotalMinutes < 1) return "en " + (int)falta.TotalSeconds + " s";
            return "en " + (int)Math.Ceiling(falta.TotalMinutes) + " min ("
                 + cuando.ToString("HH:mm") + ")";
        }

        // ---------------- el latido ----------------

        static void AlEstarOcioso(object remitente, IdlingEventArgs e)
        {
            try
            {
                if (_ocupado) return;
                if (!Cfg.Activo || !Cfg.HayTarea) return;

                var ahora = DateTime.Now;
                bool tocaSync = Cfg.Sincronizar && ahora >= ProximaSync;
                bool tocaLocal = Cfg.GuardarLocal && ahora >= ProximaLocal;
                if (!tocaSync && !tocaLocal) return;

                var uiapp = remitente as UIApplication;
                if (uiapp == null) { Reprogramar(); return; }

                // La sincronizacion gana: ya guarda la local como primer paso.
                var accion = tocaSync ? Accion.Sync : Accion.Local;

                _ocupado = true;
                try { Correr(uiapp, accion); }
                finally { _ocupado = false; }
            }
            catch (Exception ex)
            {
                _ocupado = false;
                Anotar("error inesperado: " + ex.Message);
                Reprogramar();
            }
        }

        /// <summary>Corre una accion. En silencio: lo que pasa queda en la bitacora.</summary>
        public static void Correr(UIApplication uiapp, Accion accion)
        {
            var uidoc = uiapp == null ? null : uiapp.ActiveUIDocument;
            var doc = uidoc == null ? null : uidoc.Document;

            if (doc == null)
            {
                Anotar("no habia proyecto abierto");
                Vencido(accion);
                return;
            }

            try
            {
                var r = Tarea.Ejecutar(uiapp, Cfg, accion);
                Anotar(Etiqueta(accion) + r.Texto + "  [" + doc.Title + "]");

                Vencido(accion);
                // La sincronizacion ya dejo la local guardada: su contador vuelve a cero.
                if (r.GuardoLocal) ReprogramarLocal();
            }
            catch (Autodesk.Revit.Exceptions.CentralModelContentionException)
            {
                Posponer(2, accion);
                Anotar("la central estaba ocupada, reintento en 2 min");
            }
            catch (Exception ex)
            {
                Anotar(Etiqueta(accion) + "fallo: " + ex.GetType().Name + ": " + ex.Message);
                Vencido(accion);
            }
        }

        /// <summary>Vuelve a poner en hora el reloj de la accion que acaba de correr.</summary>
        static void Vencido(Accion accion)
        {
            if (accion == Accion.Sync) ReprogramarSync();
            else ReprogramarLocal();
        }

        static string Etiqueta(Accion a) { return a == Accion.Sync ? "[sync] " : "[local] "; }

        static void Anotar(string texto)
        {
            UltimoResultado = texto;
            UltimaHora = DateTime.Now;
            Bitacora.Anotar(texto);
        }
    }
}
