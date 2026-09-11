// Editor del patron de nombre de archivo, con vista previa sobre los items reales.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Riga.ExportarPlanos.Nucleo;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Riga.ExportarPlanos.Ui
{
    public partial class DlgNombre : Window
    {
        class Fila
        {
            public string Numero { get; set; }
            public string Nombre { get; set; }
            public string Archivo { get; set; }
        }

        readonly Document _doc;
        readonly List<Item> _muestra;
        readonly Func<ElementId, View> _vistaDe;
        readonly Func<ElementId, FamilyInstance> _cajetinDe;
        bool _cargando = true;

        public string Patron { get; private set; }
        public string Reemplazo { get; private set; }

        public DlgNombre(Document doc, IEnumerable<Item> items,
                         Func<ElementId, View> vistaDe, Func<ElementId, FamilyInstance> cajetinDe,
                         string patron, string reemplazo)
        {
            InitializeComponent();
            _doc = doc;
            _vistaDe = vistaDe;
            _cajetinDe = cajetinDe;
            _muestra = items.Take(30).ToList();

            cbCampos.ItemsSource = new[] { "Insertar campo..." }.Concat(Nombrador.CAMPOS).ToList();
            cbCampos.SelectedIndex = 0;

            txPatron.Text = patron;
            txReemplazo.Text = reemplazo;
            _cargando = false;
            Refrescar();
        }

        void Refrescar()
        {
            if (_cargando) return;

            var filas = new List<Fila>();
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int repetidos = 0;

            foreach (var it in _muestra)
            {
                var vista = _vistaDe(it.Id);
                if (vista == null) continue;

                string bruto = Nombrador.Resolver(txPatron.Text, _doc, vista, _cajetinDe(it.Id), it.Papel);
                string limpio = Nombrador.Sanear(bruto, txReemplazo.Text, vista.Name);
                if (!usados.Add(limpio)) repetidos++;

                filas.Add(new Fila { Numero = it.Numero, Nombre = it.Nombre, Archivo = limpio });
            }

            grilla.ItemsSource = filas;
            lbAviso.Text = repetidos > 0
                ? repetidos + " nombres repetidos en esta muestra: se numeraran automaticamente."
                : "";
        }

        void Patron_Cambio(object s, TextChangedEventArgs e) { Refrescar(); }

        void Campo_Elegido(object s, SelectionChangedEventArgs e)
        {
            if (_cargando || cbCampos.SelectedIndex <= 0) return;
            string campo = cbCampos.SelectedItem as string;
            cbCampos.SelectedIndex = 0;
            if (campo == null) return;

            int cursor = txPatron.SelectionStart;
            txPatron.Text = txPatron.Text.Insert(cursor, campo);
            txPatron.SelectionStart = cursor + campo.Length;
            txPatron.Focus();
        }

        void Aceptar_Click(object s, RoutedEventArgs e)
        {
            Patron = txPatron.Text;
            Reemplazo = txReemplazo.Text;
            DialogResult = true;
        }
    }
}
