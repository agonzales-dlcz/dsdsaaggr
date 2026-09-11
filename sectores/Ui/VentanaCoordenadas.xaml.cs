// Ventana de "Leer coordenadas": elegis los suelos y de donde sale cada valor, y se ve
// en la grilla lo que se va a exportar antes de exportarlo.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.Sectores.Nucleo;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Riga.Sectores.Ui
{
    public class FilaSuelo : INotifyPropertyChanged
    {
        public Element Elemento;
        public string Id { get; set; }
        public string Tipo { get; set; }
        public string Nivel { get; set; }

        bool _m;
        public bool Marcado
        {
            get { return _m; }
            set { if (_m != value) { _m = value; Avisar("Marcado"); } }
        }

        string _vs = "";
        public string ValorSector
        {
            get { return _vs; }
            set { if (_vs != value) { _vs = value; Avisar("ValorSector"); } }
        }

        string _vn = "";
        public string ValorNivel
        {
            get { return _vn; }
            set { if (_vn != value) { _vn = value; Avisar("ValorNivel"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    public partial class VentanaCoordenadas : Window
    {
        // Los suelos que historicamente definian los sectores: se marcan solos.
        const string PREFIJO = "sector_";

        readonly Document _doc;
        readonly List<FilaSuelo> _filas = new List<FilaSuelo>();
        bool _cargando = true;

        public VentanaCoordenadas(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            var suelos = Extractor.Suelos(_doc);
            var fuentes = Fuente.Disponibles(_doc, suelos);

            cbSector.ItemsSource = fuentes;
            cbNivel.ItemsSource = fuentes;
            cbSector.SelectedItem = Fuente.Preferida(fuentes, Extractor.P_SECTOR, fuentes[0]);   // si no, nombre del tipo
            cbNivel.SelectedItem = Fuente.Preferida(fuentes, Extractor.P_NIVEL, fuentes[1]);     // si no, nivel del suelo

            foreach (var e in suelos)
            {
                string tipo = Extractor.NombreTipo(_doc, e);
                var f = new FilaSuelo
                {
                    Elemento = e,
                    Id = e.Id.ToString(),
                    Tipo = tipo,
                    Nivel = Comun.Parametros.Nivel(_doc, e),
                    Marcado = tipo.StartsWith(PREFIJO, StringComparison.OrdinalIgnoreCase)
                };
                f.PropertyChanged += (o, ev) => { if (ev.PropertyName == "Marcado") Resumen(); };
                _filas.Add(f);
            }

            _cargando = false;
            Recalcular();
            Repintar();
        }

        // ==================== datos ====================

        Fuente FSector { get { return cbSector.SelectedItem as Fuente; } }
        Fuente FNivel { get { return cbNivel.SelectedItem as Fuente; } }

        void Recalcular()
        {
            if (_cargando) return;
            var fs = FSector; var fn = FNivel;
            if (fs == null || fn == null) return;

            foreach (var f in _filas)
            {
                f.ValorSector = fs.Leer(_doc, f.Elemento);
                f.ValorNivel = fn.Leer(_doc, f.Elemento);
            }
            Resumen();
        }

        void Repintar()
        {
            if (_cargando) return;
            string q = (txBuscar.Text ?? "").Trim();

            grilla.ItemsSource = q.Length == 0
                ? _filas
                : _filas.Where(f =>
                       Contiene(f.Id, q) || Contiene(f.Tipo, q) || Contiene(f.Nivel, q)
                    || Contiene(f.ValorSector, q) || Contiene(f.ValorNivel, q)).ToList();
            Resumen();
        }

        static bool Contiene(string texto, string q)
        {
            return !string.IsNullOrEmpty(texto)
                && texto.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        void Resumen()
        {
            if (lbResumen == null) return;
            int n = _filas.Count(f => f.Marcado);
            int sinValor = _filas.Count(f => f.Marcado && string.IsNullOrWhiteSpace(f.ValorSector));

            lbResumen.Text = n + " suelos elegidos de " + _filas.Count
                + (sinValor > 0 ? "   -   " + sinValor + " sin valor de Sector" : "");
            if (btGuardar != null) btGuardar.IsEnabled = n > 0;
        }

        // ==================== eventos ====================

        void Fuente_Cambio(object s, SelectionChangedEventArgs e) { Recalcular(); Repintar(); }
        void Buscar_Cambio(object s, TextChangedEventArgs e) { Repintar(); }

        void Todos_Click(object s, RoutedEventArgs e)
        {
            bool v = chTodos.IsChecked == true;
            var visibles = grilla.ItemsSource as IEnumerable<FilaSuelo>;
            if (visibles == null) return;
            foreach (var f in visibles.ToList()) f.Marcado = v;
            Resumen();
        }

        void Cerrar_Click(object s, RoutedEventArgs e) { Close(); }

        void Guardar_Click(object s, RoutedEventArgs e)
        {
            var elegidos = _filas.Where(f => f.Marcado).Select(f => f.Elemento).ToList();
            if (elegidos.Count == 0) { Aviso("No marcaste ningun suelo."); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar el JSON de sectores",
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json",
                FileName = "sectores.json"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var ex = new Extractor(_doc);
                string json = ex.Json(elegidos, FSector, FNivel);
                File.WriteAllText(dlg.FileName, json, new UTF8Encoding(false));

                var td = new TaskDialog("Leer coordenadas")
                {
                    MainInstruction = ex.Escritos + " sectores exportados.",
                    MainContent = "Archivo: " + dlg.FileName
                                + (ex.SinContorno > 0
                                    ? "\n\n" + ex.SinContorno + " suelos quedaron afuera porque no dieron contorno."
                                    : ""),
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                td.Show();
                Close();
            }
            catch (Exception ex)
            {
                Aviso("No se pudo exportar:\n\n" + ex.Message);
            }
        }

        void Aviso(string t)
        {
            MessageBox.Show(this, t, "Leer coordenadas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
