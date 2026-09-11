// El trabajo en si: guardar la local, o sincronizar con la central.
//
// Son dos acciones separadas porque tienen su propio reloj: guardar la local es barato y se
// puede hacer seguido; sincronizar cuesta minutos y congela Revit.
//
// SINCRONIZAR = GUARDAR LOCAL, ESPERAR, Y RECIEN AHI SINCRONIZAR
//
// La secuencia se hace explicita: primero Guardar(), que es sincrono y no vuelve hasta que
// termino, y despues SynchronizeWithCentral. No es adorno: si la sincronizacion falla
// (central ocupada, red caida), tu trabajo ya quedo guardado en la local igual. Con una sola
// llamada de sincronizacion no hay forma de garantizar eso.
//
// Ademas SaveLocalBefore/After van en true SIEMPRE. Con la casilla apagada esto fallaba en
// cada vuelta contra el modelo de IREN:
//
//   InvalidOperationException: Saving local before first reload latest and after saving
//   changes to central in Synchronize with Central is mandatory for server-based local
//   models.
//
// O sea que para un modelo en Autodesk Docs o en Revit Server, Revit EXIGE las dos. Eso
// convierte al Guardar() previo en un guardado de mas, pero como no cambio nada entre uno y
// otro le sale casi gratis. Los dos tiempos van por separado en la bitacora, para poder
// mirar cuanto cuesta de verdad.
//
// EL MODELO PUEDE ESTAR EN LA NUBE
//
// El de IREN esta en Autodesk Docs, y ahi el guardado local NO es Document.Save() sino
// Document.SaveCloudModel(). Como no pude comprobar en vivo cual acepta cada caso (el MCP
// corre todo dentro de una transaccion abierta y ambos metodos rechazan eso antes de mirar
// nada mas), se intenta el que corresponde por IsModelInCloud y si falla se prueba el otro.
// Lo que termino funcionando queda escrito en la bitacora.
//
// Nunca espera a que se libere la central: si otro esta sincronizando, esto se rinde al toque
// y reintenta en la proxima vuelta. Un autoguardado que congela Revit veinte minutos
// esperando un turno es peor que no tenerlo.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Guardado.Nucleo
{
    internal enum Accion { Local, Sync }

    /// <summary>Que paso, en una linea, mas lo que el reloj necesita saber.</summary>
    internal sealed class Resultado
    {
        public string Texto = "";

        /// <summary>
        /// La local quedo guardada. El reloj lo usa para reiniciar tambien el contador del
        /// guardado local cuando lo que corrio fue una sincronizacion: no tiene sentido
        /// guardar de nuevo dos minutos despues de haber sincronizado.
        /// </summary>
        public bool GuardoLocal;
    }

    /// <summary>Le dice a Revit que no haga cola por la central.</summary>
    internal class SinEspera : ICentralLockedCallback
    {
        public bool ShouldWaitForLockAvailability() { return false; }
    }

    internal static class Tarea
    {
        public static Resultado Ejecutar(UIApplication uiapp, Ajustes a, Accion accion)
        {
            var uidoc = uiapp == null ? null : uiapp.ActiveUIDocument;
            var doc = uidoc == null ? null : uidoc.Document;

            if (doc == null) return Solo("no habia proyecto abierto");
            if (doc.IsFamilyDocument) return Solo("el documento activo es una familia");
            if (doc.IsReadOnly) return Solo("el documento es de solo lectura");
            if (string.IsNullOrEmpty(doc.PathName)) return Solo("el proyecto todavia no se guardo nunca");

            return accion == Accion.Sync ? Sincronizar(doc, a) : Local(doc, a);
        }

        // ---------------- guardado local ----------------

        static Resultado Local(Document doc, Ajustes a)
        {
            if (a.SoloSiHayCambios && !doc.IsModified)
                return Solo("sin cambios, no guarde");

            var reloj = System.Diagnostics.Stopwatch.StartNew();
            string como = Guardar(doc);
            return new Resultado
            {
                Texto = "guardado local con " + como + " en " + Segundos(reloj.ElapsedMilliseconds),
                GuardoLocal = true
            };
        }

        // ---------------- sincronizacion ----------------

        static Resultado Sincronizar(Document doc, Ajustes a)
        {
            bool compartido = doc.IsWorkshared && !doc.IsDetached;
            if (!compartido)
            {
                // No hay central. Si el usuario ademas pidio guardado local, ya se ocupa su
                // propio reloj; aca solo se avisa por que no se sincronizo.
                return Solo("el modelo no es compartido, no hay con que sincronizar");
            }

            if (a.SoloSiHayCambios && !doc.IsModified)
            {
                bool alDia;
                try { alDia = doc.HasAllChangesFromCentral(); }
                catch { alDia = false; }
                if (alDia) return Solo("sin cambios y al dia con la central, no sincronice");
            }

            // 1. la local primero, y se espera a que termine
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            string como = Guardar(doc);
            long msLocal = reloj.ElapsedMilliseconds;

            // 2. y recien ahora la central
            var transaccion = new TransactWithCentralOptions();
            transaccion.SetLockCallback(new SinEspera());

            var opciones = new SynchronizeWithCentralOptions();
            opciones.Comment = a.Comentario;
            opciones.Compact = false;
            opciones.SaveLocalBefore = true;      // obligatorio en modelos de servidor
            opciones.SaveLocalAfter = true;
            opciones.SetRelinquishOptions(new RelinquishOptions(a.LiberarPrestados));

            reloj.Restart();
            doc.SynchronizeWithCentral(transaccion, opciones);
            long msSync = reloj.ElapsedMilliseconds;

            return new Resultado
            {
                Texto = "sincronizado: local con " + como + " en " + Segundos(msLocal)
                      + ", central en " + Segundos(msSync)
                      + (a.LiberarPrestados ? " (liberando prestados)" : ""),
                GuardoLocal = true
            };
        }

        // ---------------- guardar, sea donde sea ----------------

        static string Guardar(Document doc)
        {
            var fallos = new List<string>();

            // El orden lo decide donde vive el modelo; el otro queda de respaldo.
            var intentos = doc.IsModelInCloud
                ? new[] { "SaveCloudModel", "Save" }
                : new[] { "Save", "SaveCloudModel" };

            foreach (var cual in intentos)
            {
                try
                {
                    if (cual == "SaveCloudModel") doc.SaveCloudModel();
                    else doc.Save();
                    return cual + "()";
                }
                catch (Exception ex)
                {
                    fallos.Add(cual + "(): " + ex.Message);
                }
            }

            throw new InvalidOperationException(string.Join("  |  ", fallos));
        }

        // ---------------- varios ----------------

        static Resultado Solo(string texto) { return new Resultado { Texto = texto }; }

        static string Segundos(long ms)
        {
            if (ms < 1000) return ms + " ms";
            return (ms / 1000.0).ToString("0.0",
                System.Globalization.CultureInfo.InvariantCulture) + " s";
        }
    }
}
