// Ventana de "Parametrizar sectores": se elige el JSON y en que dos parametros escribir.
//
// Los GUID que trae el JSON se usan como valor por defecto, pero el usuario manda: si
// elige otro nombre, se escribe en ese.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.Sectores.Nucleo;

namespace Riga.Sectores.Ui
{
    public class FilaZona
    {
        public string Sector { get; set; }
        public string Nivel { get; set; }
        public double ZMin { get; set; }
        public double ZMax { get; set; }
        public int Vertices { get; set; }
    }

    public partial class VentanaParametrizar : Window
    {
        readonly Document _doc;
        List<Zona> _zonas;
        Guid _gSector = Guid.Empty, _gNivel = Guid.Empty;
        bool _corriendo;

        public VentanaParametrizar(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            var disponibles = Destino.Disponibles(_doc);
            cbSector.ItemsSource = disponibles;
            cbNivel.ItemsSource = disponibles;
            cbSector.Text = Elegir(disponibles, Extractor.P_SECTOR);
            cbNivel.Text = Elegir(disponibles, Extractor.P_NIVEL);

            lbResumen.Text = "Elegi el archivo de sectores.";
        }

        static string Elegir(List<string> disponibles, string preferido)
        {
            foreach (var d in disponibles)
                if (string.Equals(d, preferido, StringComparison.CurrentCultureIgnoreCase)) return d;
            return preferido;      // no esta en la muestra, pero puede existir igual
        }

        // ==================== eventos ====================

        void Examinar_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar el JSON de sectores",
                Filter = "JSON (*.json)|*.json|Todos (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                _zonas = Parametrizador.Leer(dlg.FileName, out _gSector, out _gNivel);
                txJson.Text = dlg.FileName;

                grilla.ItemsSource = _zonas.Select(z => new FilaZona
                {
                    Sector = z.Sector,
                    Nivel = z.Nivel,
                    ZMin = z.ZMin,
                    ZMax = z.ZMax,
                    Vertices = z.Pol.Length
                }).ToList();

                lbResumen.Text = _zonas.Count + " sectores leidos del archivo.";
                btCorrer.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _zonas = null;
                grilla.ItemsSource = null;
                btCorrer.IsEnabled = false;
                lbResumen.Text = "No se pudo leer el archivo.";
                Aviso("No se pudo leer el JSON:\n\n" + ex.Message);
            }
        }

        void Correr_Click(object s, RoutedEventArgs e)
        {
            if (_corriendo || _zonas == null) return;

            string nSector = (cbSector.Text ?? "").Trim();
            string nNivel = (cbNivel.Text ?? "").Trim();
            if (nSector.Length == 0 || nNivel.Length == 0)
            {
                Aviso("Elegi los dos parametros de destino."); return;
            }
            if (string.Equals(nSector, nNivel, StringComparison.CurrentCultureIgnoreCase))
            {
                Aviso("Los dos destinos no pueden ser el mismo parametro."); return;
            }

            // El GUID del JSON solo se hereda si el nombre sigue siendo el de destino
            // original; si el usuario eligio otro parametro, se busca por nombre.
            var dSector = new Destino
            {
                Nombre = nSector,
                Guid = string.Equals(nSector, Extractor.P_SECTOR, StringComparison.CurrentCultureIgnoreCase)
                       ? _gSector : Guid.Empty
            };
            var dNivel = new Destino
            {
                Nombre = nNivel,
                Guid = string.Equals(nNivel, Extractor.P_NIVEL, StringComparison.CurrentCultureIgnoreCase)
                       ? _gNivel : Guid.Empty
            };

            _corriendo = true;
            btCorrer.IsEnabled = false;
            btCerrar.IsEnabled = false;

            var p = new Parametrizador(_doc);
            string error = null;
            try
            {
                p.Correr(_zonas, dSector, dNivel,
                    (i, total) =>
                    {
                        lbResumen.Text = "Recorriendo " + i + " de " + total + " elementos...";
                        Respirar();
                    },
                    () => false);
            }
            catch (Exception ex) { error = Raiz(ex); }
            finally
            {
                _corriendo = false;
                btCorrer.IsEnabled = true;
                btCerrar.IsEnabled = true;
            }

            if (error != null)
            {
                lbResumen.Text = "Fallo.";
                Aviso("No se pudo parametrizar:\n\n" + error);
                return;
            }

            lbResumen.Text = p.Asignados + " elementos parametrizados de " + p.Evaluados + " evaluados.";

            var td = new TaskDialog("Parametrizar sectores")
            {
                MainInstruction = p.Asignados + " elementos parametrizados.",
                MainContent = "Evaluados: " + p.Evaluados
                            + "\nSector -> " + nSector
                            + "\nNivel del elemento -> " + nNivel
                            + (p.SinParametro > 0
                                ? "\n\n" + p.SinParametro + " elementos caian dentro de un sector pero no tienen esos parametros, o son de solo lectura."
                                : "")
                            + (p.Errores > 0 ? "\n" + p.Errores + " no se pudieron escribir." : ""),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
        }

        void Cerrar_Click(object s, RoutedEventArgs e) { if (!_corriendo) Close(); }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_corriendo) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        /// <summary>Deja respirar a la UI mientras corre el lote.</summary>
        static void Respirar()
        {
            var marco = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => marco.Continue = false));
            Dispatcher.PushFrame(marco);
        }

        static string Raiz(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message;
        }

        void Aviso(string t)
        {
            MessageBox.Show(this, t, "Parametrizar sectores", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
