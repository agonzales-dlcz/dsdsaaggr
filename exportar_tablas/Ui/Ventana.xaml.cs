// Ventana del exportador de tablas, con la disposicion de SheetLink: tablas a la
// izquierda, parametros a la derecha con los colores de Instancia / Tipo / Solo lectura.
//
// Regla de rendimiento: al abrir NO se toca ningun elemento. La lista sale de las
// definiciones de las tablas, que es instantaneo. Enumerar los elementos de una tabla
// cuesta segundos la primera vez, asi que eso ocurre recien al exportar.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.ExportarTablas.Nucleo;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;

namespace Riga.ExportarTablas.Ui
{
    /// <summary>Los mismos colores que usa SheetLink para clasificar el parametro.</summary>
    public class ColorClase : IValueConverter
    {
        static readonly Brush INSTANCIA = new SolidColorBrush(Color.FromRgb(0xEA, 0xF5, 0xE1));
        static readonly Brush TIPO = new SolidColorBrush(Color.FromRgb(0xFB, 0xF3, 0xD5));
        static readonly Brush LECTURA = new SolidColorBrush(Color.FromRgb(0xFA, 0xDB, 0xDB));

        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            string s = v as string;
            if (s == "Tipo") return TIPO;
            if (s == "Solo lectura") return LECTURA;
            return INSTANCIA;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    public class FilaTabla : INotifyPropertyChanged
    {
        public ViewSchedule Tabla;
        public string Nombre { get; set; }
        public string Detalle { get; set; }

        bool _m;
        public bool Marcada
        {
            get { return _m; }
            set { if (_m != value) { _m = value; Avisar("Marcada"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    public class FilaParam : INotifyPropertyChanged
    {
        public string Nombre { get; set; }
        public string Clase { get; set; }        // Instancia | Tipo | Solo lectura

        bool _m = true;
        public bool Marcado
        {
            get { return _m; }
            set { if (_m != value) { _m = value; Avisar("Marcado"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    public partial class Ventana : Window
    {
        readonly UIDocument _uidoc;
        readonly Document _doc;
        readonly Lector _lector;

        readonly List<FilaTabla> _tablas = new List<FilaTabla>();
        readonly ObservableCollection<FilaParam> _params = new ObservableCollection<FilaParam>();

        bool _cargando = true;
        bool _exportando;

        public Ventana(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _lector = new Lector(_doc);

            CargarTablas();
            listaParams.ItemsSource = _params;

            txArchivo.Text = RutaSugerida();

            _cargando = false;
            RepintarTablas();
            RefrescarResumen();
        }

        string RutaSugerida()
        {
            string carpeta;
            try
            {
                carpeta = string.IsNullOrEmpty(_doc.PathName)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : Path.GetDirectoryName(_doc.PathName);
            }
            catch { carpeta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }

            string nombre = _doc.Title;
            if (nombre.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                nombre = nombre.Substring(0, nombre.Length - 4);
            foreach (char c in Path.GetInvalidFileNameChars()) nombre = nombre.Replace(c, '-');

            return Path.Combine(carpeta, nombre + " - tablas " + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx");
        }

        // ==================== carga ====================

        void CargarTablas()
        {
            var tablas = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(NosSirve)
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var t in tablas)
            {
                var f = new FilaTabla { Tabla = t, Nombre = t.Name, Detalle = Detalle(t) };
                f.PropertyChanged += (o, e) =>
                {
                    if (e.PropertyName != "Marcada") return;
                    RefrescarParametros();
                    RefrescarResumen();
                };
                _tablas.Add(f);
            }
        }

        /// <summary>
        /// Las de revision de cajetin son internas: hay una por cajetin y en este modelo son
        /// mas de doscientas. No tiene sentido ofrecerlas.
        /// </summary>
        static bool NosSirve(ViewSchedule t)
        {
            try
            {
                if (t.IsTemplate) return false;
                if (t.IsTitleblockRevisionSchedule) return false;
                if (t.IsInternalKeynoteSchedule) return false;
                return true;
            }
            catch { return false; }
        }

        string Detalle(ViewSchedule t)
        {
            try
            {
                var d = t.Definition;
                string cat = "multicategoria";
                if (d.CategoryId != null && d.CategoryId != ElementId.InvalidElementId)
                {
                    var c = Category.GetCategory(_doc, d.CategoryId);
                    if (c != null) cat = c.Name;
                }
                string extra = d.GetFilterCount() > 0 ? "  ·  " + d.GetFilterCount() + " filtros" : "";
                return cat + "  ·  " + d.GetFieldCount() + " campos" + extra;
            }
            catch { return ""; }
        }

        // ==================== listas ====================

        List<FilaTabla> Visibles()
        {
            string q = (txBuscarTabla.Text ?? "").Trim();
            if (q.Length == 0) return _tablas;
            return _tablas.Where(x => x.Nombre.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        void RepintarTablas()
        {
            if (_cargando) return;
            listaTablas.ItemsSource = Visibles();
        }

        /// <summary>
        /// Los parametros de las tablas marcadas, unificados por nombre. Solo se leen
        /// definiciones: no se toca ningun elemento.
        /// </summary>
        void RefrescarParametros()
        {
            if (_cargando) return;

            var previos = _params.ToDictionary(x => x.Nombre, x => x.Marcado, StringComparer.OrdinalIgnoreCase);
            var vistos = new Dictionary<string, FilaParam>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in _tablas.Where(x => x.Marcada))
            {
                foreach (var c in _lector.Columnas(f.Tabla))
                {
                    if (c.Oculta && chOcultas.IsChecked != true) continue;
                    if (vistos.ContainsKey(c.Nombre)) continue;

                    bool marcado;
                    vistos[c.Nombre] = new FilaParam
                    {
                        Nombre = c.Nombre,
                        Clase = ClaseDe(c),
                        Marcado = previos.TryGetValue(c.Nombre, out marcado) ? marcado : true
                    };
                }
            }

            _params.Clear();
            foreach (var p in vistos.Values.OrderBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase))
                _params.Add(p);

            FiltrarParametros();
        }

        static string ClaseDe(Columna c)
        {
            if (c.Clase == Clase.Parametro) return c.EsDeTipo ? "Tipo" : "Instancia";
            return "Solo lectura";      // recuento, formula y lo que no se resuelve
        }

        void FiltrarParametros()
        {
            string q = (txBuscarParam.Text ?? "").Trim();
            listaParams.ItemsSource = q.Length == 0
                ? (System.Collections.IEnumerable)_params
                : _params.Where(x => x.Nombre.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        void RefrescarResumen()
        {
            if (lbResumen == null) return;
            int n = _tablas.Count(x => x.Marcada);
            lbResumen.Text = "Tablas seleccionadas: " + n + " de " + _tablas.Count
                           + "   ·   parametros encontrados: " + _params.Count;
        }

        // ==================== eventos ====================

        void BuscarTabla_Cambio(object s, TextChangedEventArgs e) { RepintarTablas(); }
        void BuscarParam_Cambio(object s, TextChangedEventArgs e) { if (!_cargando) FiltrarParametros(); }

        void Todas_Click(object s, RoutedEventArgs e)
        {
            bool v = chTodas.IsChecked == true;
            _cargando = true;
            foreach (var f in Visibles()) f.Marcada = v;
            _cargando = false;
            RefrescarParametros();
            RefrescarResumen();
        }

        void Archivo_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar el Excel",
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = Path.GetFileName(txArchivo.Text),
                InitialDirectory = SeguroDirectorio(txArchivo.Text)
            };
            if (d.ShowDialog(this) == true) txArchivo.Text = d.FileName;
        }

        static string SeguroDirectorio(string ruta)
        {
            try { var d = Path.GetDirectoryName(ruta); return Directory.Exists(d) ? d : ""; }
            catch { return ""; }
        }

        void Cerrar_Click(object s, RoutedEventArgs e) { Close(); }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_exportando) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        // ==================== exportacion ====================

        void Exportar_Click(object s, RoutedEventArgs e)
        {
            if (_exportando) return;

            var elegidas = _tablas.Where(x => x.Marcada).Select(x => x.Tabla).ToList();
            if (elegidas.Count == 0) { Aviso("No marcaste ninguna tabla."); return; }

            string ruta = (txArchivo.Text ?? "").Trim();
            if (ruta.Length == 0) { Aviso("Elegi donde guardar el Excel."); return; }
            try { Directory.CreateDirectory(Path.GetDirectoryName(ruta)); }
            catch (Exception ex) { Aviso("No se puede usar esa carpeta:\n" + ex.Message); return; }

            var descartados = new HashSet<string>(
                _params.Where(x => !x.Marcado).Select(x => x.Nombre), StringComparer.OrdinalIgnoreCase);

            var op = new Opciones
            {
                Ruta = ruta,
                HojaConsolidada = chConsolidada.IsChecked == true,
                ColumnasOcultas = chOcultas.IsChecked == true,
                CompletarCalculadas = chCalculadas.IsChecked == true,
                ColumnasDescartadas = descartados
            };

            _exportando = true;
            btExportar.IsEnabled = false;
            btCerrar.IsEnabled = false;
            // Window.Visibility tapa al enum del mismo nombre: hay que calificarlo.
            barra.Visibility = System.Windows.Visibility.Visible;
            barra.Maximum = elegidas.Count;
            barra.Value = 0;

            var reloj = Stopwatch.StartNew();
            var exp = new Exportador(_doc);
            string error = null;
            try
            {
                exp.Correr(elegidas, op,
                    (i, nombre) =>
                    {
                        barra.Value = i;
                        lbResumen.Text = "Leyendo " + (i + 1) + " de " + elegidas.Count + ":  " + nombre;
                        Respirar();
                    },
                    () => false);
            }
            catch (Exception ex) { error = Raiz(ex); }
            finally
            {
                reloj.Stop();
                barra.Value = barra.Maximum;
                _exportando = false;
                btExportar.IsEnabled = true;
                btCerrar.IsEnabled = true;
            }

            RefrescarResumen();
            Informe(exp, op, reloj.Elapsed, error);
        }

        void Informe(Exportador exp, Opciones op, TimeSpan tiempo, string error)
        {
            if (error != null)
            {
                Aviso("No se pudo terminar la exportacion:\n\n" + error);
                return;
            }

            var td = new TaskDialog("Exportar tablas")
            {
                MainInstruction = exp.TablasHechas + " tablas, " + exp.FilasTotal + " filas.",
                MainContent = "Archivo: " + op.Ruta
                            + "\nTiempo: " + tiempo.ToString(@"mm\:ss")
                            + (exp.Notas.Count > 0
                                ? "\n\n" + exp.Notas.Count + " columnas quedaron vacias. Estan detalladas en la hoja Notas."
                                : ""),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir el Excel");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Abrir la carpeta");

            var r = td.Show();
            try
            {
                if (r == TaskDialogResult.CommandLink1)
                    Process.Start(new ProcessStartInfo(op.Ruta) { UseShellExecute = true });
                else if (r == TaskDialogResult.CommandLink2)
                    Process.Start(new ProcessStartInfo("explorer.exe",
                        "/select,\"" + op.Ruta + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>Deja respirar a la UI: sin esto la barra no se mueve.</summary>
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

        void Aviso(string texto)
        {
            MessageBox.Show(this, texto, "Exportar tablas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
