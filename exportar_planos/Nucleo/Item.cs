// Una fila de la grilla: un plano o una vista, mas lo que se calcula sobre el.

using System.ComponentModel;
using Autodesk.Revit.DB;

namespace Riga.ExportarPlanos.Nucleo
{
    public class Item : INotifyPropertyChanged
    {
        public ElementId Id;
        public bool EsPlano;
        public bool Abierta;                    // esta abierta ahora mismo en Revit
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Revision { get; set; }
        public string Tipo { get; set; }        // "Plano", "Planta", "Seccion", ...
        public string Conjunto { get; set; }    // coleccion de planos (Revit 2025) o vacio

        // Tamano y orientacion detectados del cajetin. El usuario puede forzarlos desde "Crear".
        public ExportPaperFormat PapelDetectado = ExportPaperFormat.Default;
        public PageOrientationType OrientacionDetectada = PageOrientationType.Auto;
        public bool PapelForzado;

        // Los planos los traen del cajetin. Las vistas no tienen cajetin, asi que hay que
        // definirlos a mano en "Crear" antes de poder exportarlas a PDF.
        public bool PapelDefinido;
        public bool OrientacionDefinida;

        string _papel = "";
        public string Papel
        {
            get { return _papel; }
            set { if (_papel != value) { _papel = value; Avisar("Papel"); } }
        }

        string _orientacion = "";
        public string Orientacion
        {
            get { return _orientacion; }
            set { if (_orientacion != value) { _orientacion = value; Avisar("Orientacion"); } }
        }

        bool _marcada;
        public bool Marcada
        {
            get { return _marcada; }
            set { if (_marcada != value) { _marcada = value; Avisar("Marcada"); } }
        }

        /// <summary>El usuario escribio el nombre a mano: el patron deja de pisarlo.</summary>
        public bool ArchivoManual;

        string _archivo = "";
        public string Archivo
        {
            get { return _archivo; }
            set
            {
                if (_archivo == value) return;
                _archivo = value;
                Avisar("Archivo");
            }
        }

        string _estado = "";
        public string Estado
        {
            get { return _estado; }
            set { if (_estado != value) { _estado = value; Avisar("Estado"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>Una fila de la pestana "Crear": un item por cada formato pedido.</summary>
    public class Salida : INotifyPropertyChanged
    {
        public Item Origen;
        public string Formato { get; set; }

        public string Numero { get { return Origen.Numero; } }
        public string Nombre { get { return Origen.Nombre; } }
        public string Clase { get { return Origen.EsPlano ? "Plano" : "Vista"; } }

        public string Papel { get { return Formato == "PDF" || Formato == "IMG" ? Origen.Papel : Formato; } }
        public string Orientacion { get { return Formato == "PDF" ? Origen.Orientacion : "-"; } }

        string _progreso = "";
        public string Progreso
        {
            get { return _progreso; }
            set { if (_progreso != value) { _progreso = value; Avisar("Progreso"); } }
        }

        public void Refrescar()
        {
            Avisar("Papel");
            Avisar("Orientacion");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }
}
