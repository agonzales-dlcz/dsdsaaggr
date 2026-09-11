// Ventana del instalador. La copia en si vive en Paquete, que tambien usa el modo
// silencioso (/instalar y /desinstalar).

using System.Windows;

namespace Riga.Instalador
{
    public partial class Ventana : Window
    {
        public Ventana()
        {
            InitializeComponent();
            lbDestino.Text = "Carpeta: " + Paquete.Destino;
            Escribir("Listo para instalar. " + Paquete.Recursos().Count + " archivos en el paquete.");
        }

        void Instalar_Click(object s, RoutedEventArgs e)
        {
            if (Avisar()) return;
            Paquete.Instalar(Escribir);
            Escribir("Abri Revit " + Paquete.VERSION_REVIT + " y busca la pestana dsdsaaggr.");
        }

        void Desinstalar_Click(object s, RoutedEventArgs e)
        {
            if (Avisar()) return;
            if (MessageBox.Show(this,
                    "Se quitan las herramientas de esta carpeta:\n\n" + Paquete.Destino +
                    "\n\nTus modelos y tus archivos exportados no se tocan.",
                    "Desinstalar", MessageBoxButton.OKCancel,
                    MessageBoxImage.Question) != MessageBoxResult.OK) return;

            Paquete.Desinstalar(Escribir);
        }

        void Cerrar_Click(object s, RoutedEventArgs e) { Close(); }

        /// <summary>Devuelve true si hay que frenar porque Revit esta abierto.</summary>
        bool Avisar()
        {
            if (!Paquete.RevitAbierto()) return false;

            Escribir("Revit esta abierto: cerralo y volve a intentar.");
            MessageBox.Show(this,
                "Revit esta abierto y tiene tomados los archivos.\n\nCerralo y volve a intentar.",
                "Revit abierto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        void Escribir(string linea)
        {
            lbLog.Text += (lbLog.Text.Length == 0 ? "" : System.Environment.NewLine) + linea;
            scroll.ScrollToEnd();
        }
    }
}
