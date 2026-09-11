// La ventana de configuracion.
//
// Lo que se cambia aca vale de inmediato: Reloj.Aplicar reemplaza los ajustes vivos y vuelve
// a contar desde cero los dos relojes. No hace falta reiniciar Revit, que era el pedido.
//
// La guarda _cargando existe porque WPF conecta los handlers dentro de InitializeComponent y
// recien despues aplica las propiedades: sin ella, poner IsChecked al abrir dispara los
// eventos antes de que los controles existan. Ya nos mordio una vez en el exportador.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Riga.Guardado.Nucleo;

namespace Riga.Guardado.Ui
{
    public partial class VentanaAjustes : Window
    {
        readonly Autodesk.Revit.UI.UIApplication _uiapp;
        bool _cargando = true;

        internal VentanaAjustes(Autodesk.Revit.UI.UIApplication uiapp)
        {
            InitializeComponent();
            _uiapp = uiapp;

            var a = Reloj.Cfg;
            chkActivo.IsChecked = a.Activo;
            chkLocal.IsChecked = a.GuardarLocal;
            txtMinLocal.Text = a.MinutosLocal.ToString(CultureInfo.InvariantCulture);
            chkSync.IsChecked = a.Sincronizar;
            txtMinSync.Text = a.MinutosSync.ToString(CultureInfo.InvariantCulture);
            chkLiberar.IsChecked = a.LiberarPrestados;
            txtComentario.Text = a.Comentario;
            chkSoloCambios.IsChecked = a.SoloSiHayCambios;

            _cargando = false;
            AjustarHabilitados();
            RefrescarEstado();
        }

        // ---------------- eventos ----------------

        void Activo_Cambio(object r, RoutedEventArgs e) { AjustarHabilitados(); }
        void Que_Cambio(object r, RoutedEventArgs e) { AjustarHabilitados(); }

        void AjustarHabilitados()
        {
            if (_cargando) return;
            if (panelLocal == null || panelSyncTodo == null || panelSync == null
                || panelComun == null || txtMinLocal == null || txtMinSync == null) return;

            bool activo = chkActivo.IsChecked == true;
            panelLocal.IsEnabled = activo;
            panelSyncTodo.IsEnabled = activo;
            panelComun.IsEnabled = activo;

            txtMinLocal.IsEnabled = activo && chkLocal.IsChecked == true;
            txtMinSync.IsEnabled = activo && chkSync.IsChecked == true;
            panelSync.IsEnabled = activo && chkSync.IsChecked == true;
        }

        void RefrescarEstado()
        {
            lbProxLocal.Text = "Proximo guardado local: " + Reloj.FaltaParaLocal();
            lbProxSync.Text = "Proxima sincronizacion: " + Reloj.FaltaParaSync();
            lbUltima.Text = Reloj.UltimaHora == DateTime.MinValue
                ? "Ultima: " + Reloj.UltimoResultado
                : "Ultima (" + Reloj.UltimaHora.ToString("HH:mm:ss") + "): " + Reloj.UltimoResultado;
            lbLog.Text = Bitacora.UltimasLineas(10);
        }

        void Cancelar_Click(object r, RoutedEventArgs e) { Close(); }

        void Guardar_Click(object r, RoutedEventArgs e)
        {
            Ajustes a;
            if (!Leer(out a)) return;
            Reloj.Aplicar(a);
            Close();
        }

        void LocalYa_Click(object r, RoutedEventArgs e) { Ya(Accion.Local); }
        void SyncYa_Click(object r, RoutedEventArgs e) { Ya(Accion.Sync); }

        /// <summary>
        /// A mano no se avisa en la barra: el usuario ya esta mirando esta ventana. Los
        /// ajustes se aplican primero para que lo que corra sea lo que se ve en pantalla.
        /// </summary>
        void Ya(Accion accion)
        {
            Ajustes a;
            if (!Leer(out a)) return;
            Reloj.Aplicar(a);

            btLocalYa.IsEnabled = false;
            btSyncYa.IsEnabled = false;
            try { Reloj.Correr(_uiapp, accion); }
            finally
            {
                btLocalYa.IsEnabled = true;
                btSyncYa.IsEnabled = true;
            }

            RefrescarEstado();
        }

        /// <summary>Pasa la ventana a un objeto de ajustes, avisando si algo no es un numero.</summary>
        bool Leer(out Ajustes a)
        {
            a = null;

            int local, sync;
            if (!Minutos(txtMinLocal, "del guardado local", out local)) return false;
            if (!Minutos(txtMinSync, "de la sincronizacion", out sync)) return false;

            a = new Ajustes
            {
                Activo = chkActivo.IsChecked == true,
                GuardarLocal = chkLocal.IsChecked == true,
                MinutosLocal = local,
                Sincronizar = chkSync.IsChecked == true,
                MinutosSync = sync,
                LiberarPrestados = chkLiberar.IsChecked == true,
                Comentario = txtComentario.Text,
                SoloSiHayCambios = chkSoloCambios.IsChecked == true
            };
            a.Normalizar();
            return true;
        }

        bool Minutos(System.Windows.Controls.TextBox caja, string cual, out int valor)
        {
            if (int.TryParse(caja.Text.Trim(), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out valor)
                && valor >= Ajustes.MIN_MINUTOS && valor <= Ajustes.MAX_MINUTOS)
                return true;

            MessageBox.Show(this,
                "Los minutos " + cual + " tienen que ser un numero entero entre "
                + Ajustes.MIN_MINUTOS + " y " + Ajustes.MAX_MINUTOS + ".",
                "Autoguardado", MessageBoxButton.OK, MessageBoxImage.Warning);
            caja.Focus();
            caja.SelectAll();
            return false;
        }

        internal static void Abrir(Autodesk.Revit.UI.UIApplication uiapp)
        {
            var v = new VentanaAjustes(uiapp);
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
