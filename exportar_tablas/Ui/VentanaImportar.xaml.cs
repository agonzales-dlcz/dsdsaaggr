// Ventana de importacion: elegir archivo, mirar que va a cambiar, y recien despues aplicar.
//
// El analisis es de solo lectura y se puede repetir todas las veces que haga falta. La
// escritura va en una sola transaccion, asi que un Ctrl+Z deshace la importacion entera.
//
// Se muestra fila por fila lo que va a cambiar, con el antes y el despues: en una operacion
// que toca cientos de elementos de golpe, aplicar sin mirar es como firmar sin leer.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Riga.ExportarTablas.Nucleo;

namespace Riga.ExportarTablas.Ui
{
    public sealed class HojaElegible
    {
        public string Nombre { get; set; }
        public string Etiqueta { get; set; }
        public bool Elegida { get; set; }
    }

    public sealed class FilaCambio
    {
        public string Hoja { get; set; }
        public int Fila { get; set; }
        public string Id { get; set; }
        public string Columna { get; set; }
        public string Antes { get; set; }
        public string Despues { get; set; }
        public string Donde { get; set; }
    }

    public sealed class FilaSalto
    {
        public string Hoja { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Motivo { get; set; }
    }

    public partial class VentanaImportar : Window
    {
        readonly Document _doc;
        readonly ObservableCollection<HojaElegible> _hojas = new ObservableCollection<HojaElegible>();

        List<HojaLeida> _leidas;
        Importador _imp;

        internal VentanaImportar(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            listaHojas.ItemsSource = _hojas;
            lbResumen.Text = "Elegi un archivo exportado con este mismo boton.";
        }

        // ---------------- archivo ----------------

        void Examinar_Click(object r, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Excel o CSV exportado",
                Filter = "Excel y CSV (*.xlsx;*.csv)|*.xlsx;*.xlsm;*.csv|Excel (*.xlsx)|*.xlsx;*.xlsm|CSV (*.csv)|*.csv",
                CheckFileExists = true
            };
            if (d.ShowDialog(this) != true) return;

            txtRuta.Text = d.FileName;
            Cargar(d.FileName);
        }

        void Cargar(string ruta)
        {
            _hojas.Clear();
            _imp = null;
            grilla.ItemsSource = null;
            grillaSaltos.ItemsSource = null;
            btAplicar.IsEnabled = false;

            try { _leidas = Libro.Leer(ruta); }
            catch (Exception ex)
            {
                _leidas = null;
                btAnalizar.IsEnabled = false;
                lbResumen.Text = "No se pudo leer: " + ex.Message;
                return;
            }

            foreach (var h in _leidas)
            {
                bool sirve = Sirve(h);
                _hojas.Add(new HojaElegible
                {
                    Nombre = h.Nombre,
                    Elegida = sirve,
                    Etiqueta = h.Nombre + "   (" + h.Datos + " filas"
                             + (sirve ? "" : ", sin chapas: no se puede importar") + ")"
                });
            }

            lbHojas.Text = _leidas.Count + " hoja(s) en el archivo. Destilda las que no quieras tocar.";
            btAnalizar.IsEnabled = _hojas.Any(h => h.Elegida);
            lbResumen.Text = btAnalizar.IsEnabled
                ? "Apreta Analizar para ver que cambiaria."
                : "Ninguna hoja tiene columnas con chapa: este archivo no salio del exportador.";
        }

        /// <summary>Una hoja sirve si tiene la columna GUID o Element ID y algun parametro.</summary>
        static bool Sirve(HojaLeida h)
        {
            if (h.Filas.Count < 2) return false;
            bool clave = false, parametro = false;
            foreach (var c in h.Cabecera)
            {
                var ch = Identidad.Leer(c);
                if (ch == null) continue;
                if (ch.Clase == ClaseClave.Fija)
                {
                    if (ch.Clave == Identidad.CLAVE_CONSOLIDADO) return false;   // ver Identidad
                    if (ch.Clave == "guid" || ch.Clave == "id") clave = true;
                }
                else if (ch.Escribible) parametro = true;
            }
            return clave && parametro;
        }

        // ---------------- analisis ----------------

        void Analizar_Click(object r, RoutedEventArgs e)
        {
            if (_leidas == null) return;

            var op = new OpcionesImportar
            {
                Ruta = txtRuta.Text,
                PermitirTipo = chkTipo.IsChecked == true,
                VaciarConCeldaVacia = chkVaciar.IsChecked == true
            };
            foreach (var h in _hojas) if (h.Elegida) op.Hojas.Add(h.Nombre);

            if (op.Hojas.Count == 0)
            {
                lbResumen.Text = "No elegiste ninguna hoja.";
                return;
            }

            _imp = new Importador(_doc);
            try { _imp.Analizar(_leidas, op); }
            catch (Exception ex)
            {
                lbResumen.Text = "Fallo el analisis: " + ex.Message;
                return;
            }

            grilla.ItemsSource = _imp.Cambios.Select(c => new FilaCambio
            {
                Hoja = c.Hoja,
                Fila = c.Fila,
                Id = c.E.Id.Value.ToString(CultureInfo.InvariantCulture),
                Columna = c.Columna,
                Antes = c.Antes,
                Despues = c.Despues,
                Donde = c.EnTipo ? "TIPO" : "instancia"
            }).ToList();

            grillaSaltos.ItemsSource = _imp.Saltos.Select(s => new FilaSalto
            {
                Hoja = s.Hoja,
                Fila = s.Fila == 0 ? "" : s.Fila.ToString(CultureInfo.InvariantCulture),
                Columna = s.Columna,
                Motivo = s.Motivo
            }).ToList();

            tabCambios.Header = "Cambios (" + _imp.Cambios.Count + ")";
            tabSaltos.Header = "Saltados (" + _imp.Saltos.Count + ")";

            int enTipo = _imp.Cambios.Count(c => c.EnTipo);
            lbResumen.Text = _imp.Cambios.Count + " cambio(s) en " + _imp.ElementosTocados
                           + " elemento(s)  ·  " + _imp.Iguales + " ya estaban igual  ·  "
                           + _imp.Saltos.Count + " saltado(s)  ·  " + _imp.FilasLeidas + " filas leidas"
                           + (enTipo > 0 ? "  ·  OJO: " + enTipo + " van al TIPO" : "");

            btAplicar.IsEnabled = _imp.Cambios.Count > 0;
            pestanas.SelectedIndex = _imp.Cambios.Count > 0 ? 0 : 1;
        }

        // ---------------- escritura ----------------

        void Aplicar_Click(object r, RoutedEventArgs e)
        {
            if (_imp == null || _imp.Cambios.Count == 0) return;

            int enTipo = _imp.Cambios.Count(c => c.EnTipo);
            string aviso = "Se van a escribir " + _imp.Cambios.Count + " valores en "
                         + _imp.ElementosTocados + " elementos.";
            if (enTipo > 0)
                aviso += "\n\n" + enTipo + " de esos van a un parametro de TIPO: eso cambia todas "
                       + "las instancias de cada tipo, no solo las filas del Excel.";
            aviso += "\n\nSe deshace con un Ctrl+Z. Continuar?";

            if (MessageBox.Show(this, aviso, "Importar tablas",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var fallos = new List<string>();
            int hechos = 0;
            btAplicar.IsEnabled = false;

            try
            {
                using (var tx = new Transaction(_doc, "Importar tablas"))
                {
                    tx.Start();
                    hechos = _imp.Aplicar(_imp.Cambios, fallos);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                lbResumen.Text = "Fallo al escribir: " + ex.Message;
                btAplicar.IsEnabled = true;
                return;
            }

            string bitacora = Bitacora(hechos, fallos);
            lbResumen.Text = "Escritos " + hechos + " de " + _imp.Cambios.Count
                           + (fallos.Count > 0 ? "  ·  " + fallos.Count + " fallaron" : "")
                           + (bitacora != null ? "  ·  detalle en " + Path.GetFileName(bitacora) : "");

            if (fallos.Count > 0)
                grillaSaltos.ItemsSource = fallos.Select(f => new FilaSalto { Motivo = f }).ToList();

            // Lo escrito ya esta: analizar de nuevo para no aplicar dos veces lo mismo.
            Analizar_Click(null, null);
        }

        string Bitacora(int hechos, IList<string> fallos)
        {
            try
            {
                string carpeta = Path.GetDirectoryName(txtRuta.Text);
                if (string.IsNullOrEmpty(carpeta)) return null;

                string ruta = Path.Combine(carpeta,
                    Path.GetFileNameWithoutExtension(txtRuta.Text) + "_importacion_"
                    + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt");

                var sb = new StringBuilder();
                sb.AppendLine("Importacion a " + _doc.Title);
                sb.AppendLine("Archivo: " + txtRuta.Text);
                sb.AppendLine("Fecha:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Escritos: " + hechos + " de " + _imp.Cambios.Count);
                sb.AppendLine();

                sb.AppendLine("--- cambios ---");
                foreach (var c in _imp.Cambios)
                    sb.AppendLine(c.Hoja + " f" + c.Fila + "  id " + c.E.Id.Value + "  " + c.Columna
                                  + (c.EnTipo ? " [TIPO]" : "") + ":  \"" + c.Antes + "\"  ->  \"" + c.Despues + "\"");

                if (fallos.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- fallos ---");
                    foreach (var f in fallos) sb.AppendLine(f);
                }

                if (_imp.Saltos.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- saltados ---");
                    foreach (var s in _imp.Saltos)
                        sb.AppendLine(s.Hoja + (s.Fila > 0 ? " f" + s.Fila : "")
                                      + (s.Columna.Length > 0 ? "  " + s.Columna : "") + ":  " + s.Motivo);
                }

                File.WriteAllText(ruta, sb.ToString(), Encoding.UTF8);
                return ruta;
            }
            catch { return null; }
        }

        void Cerrar_Click(object r, RoutedEventArgs e) { Close(); }

        internal static void Abrir(Document doc)
        {
            var v = new VentanaImportar(doc);
            try
            {
                var mano = Process.GetCurrentProcess().MainWindowHandle;
                if (mano != IntPtr.Zero) new WindowInteropHelper(v).Owner = mano;
            }
            catch { }
            v.ShowDialog();
        }
    }
}
