// Configuracion completa de una exportacion. Se serializa a XML en %APPDATA%.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Autodesk.Revit.DB;

namespace Riga.ExportarPlanos.Nucleo
{
    public enum ModoArchivo { Separados = 0, UnoSolo = 1, VariosAgrupados = 2 }
    public enum Informe { Ninguno = 0, Log = 1, Csv = 2 }

    public class Perfil
    {
        // --- General ---
        public string Nombre = "Predeterminado";
        public string Carpeta = "";
        public string Patron = "{Numero} - {Nombre}";
        public string Reemplazo = "-";              // reemplaza los caracteres prohibidos
        public bool SubcarpetaPorFormato = false;
        public bool Sobrescribir = true;
        public Informe Reporte = Informe.Log;
        public bool MostrarVistas = false;          // la pestana Seleccion arranca en Planos

        // --- Formatos activos ---
        public bool Pdf = true;
        public bool Dwg = false;
        public bool Dxf = false;
        public bool Dgn = false;
        public bool Img = false;

        // --- PDF: ubicacion en el papel ---
        public PaperPlacementType PdfPosicion = PaperPlacementType.Center;
        public double PdfOffsetX = 0;               // mm, desde la esquina
        public double PdfOffsetY = 0;

        // --- PDF: zoom ---
        public ZoomType PdfZoomTipo = ZoomType.Zoom;
        public int PdfZoom = 100;

        // --- PDF: lineas ocultas y aspecto ---
        public bool PdfVectorial = false;           // false = rasterizado (AlwaysUseRaster)
        public RasterQualityType PdfRaster = RasterQualityType.High;
        public ColorDepthType PdfColor = ColorDepthType.Color;
        public PDFExportQualityType PdfCalidad = PDFExportQualityType.DPI300;

        // --- PDF: opciones ---
        public bool PdfEnlacesAzules = true;
        public bool PdfOcultarPlanosRef = true;
        public bool PdfOcultarEtiquetas = true;
        public bool PdfOcultarCajas = true;
        public bool PdfOcultarCrop = true;
        public bool PdfSemitonosFinos = false;
        public bool PdfLineasCoincidentes = false;
        public bool PdfPararEnError = false;

        // --- PDF: archivo ---
        public ModoArchivo PdfModo = ModoArchivo.Separados;
        public string PdfNombreCombinado = "{NumeroProyecto} - Paquete";
        public string PdfAgruparPor = "Revision";   // Revision | Papel | Parametro
        public string PdfParamGrupo = "";
        public bool PdfPapelAuto = true;            // mantener tamano y giro del cajetin

        // --- DWG / DXF / DGN ---
        public string DwgConfig = "";               // configuracion de exportacion del proyecto
        public ACADVersion DwgVersion = ACADVersion.R2018;
        public ExportColorMode DwgColores = ExportColorMode.IndexColors;
        public bool DwgCoordsCompartidas = true;
        public bool DwgVistasFusionadas = true;
        public string DgnSemilla = "";

        // --- Imagen ---
        public ImageFileType ImgTipo = ImageFileType.PNG;
        public ImageResolution ImgResolucion = ImageResolution.DPI_300;
        public ZoomFitType ImgZoomTipo = ZoomFitType.FitToPage;
        public int ImgPixeles = 2000;
        public int ImgZoom = 100;

        [XmlIgnore]
        public bool AlgunFormato { get { return Pdf || Dwg || Dxf || Dgn || Img; } }

        [XmlIgnore]
        public List<string> FormatosActivos
        {
            get
            {
                var r = new List<string>();
                if (Pdf) r.Add("PDF");
                if (Dwg) r.Add("DWG");
                if (Dxf) r.Add("DXF");
                if (Dgn) r.Add("DGN");
                if (Img) r.Add("IMG");
                return r;
            }
        }

        // ---------- Persistencia ----------

        static string CarpetaBase
        {
            get
            {
                string d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExportarPlanos");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string CarpetaPerfiles
        {
            get
            {
                string d = Path.Combine(CarpetaBase, "perfiles");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string RutaDe(string nombre)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) nombre = nombre.Replace(c, '_');
            return Path.Combine(CarpetaPerfiles, nombre + ".xml");
        }

        public static List<string> Listar()
        {
            var r = new List<string>();
            foreach (var f in Directory.GetFiles(CarpetaPerfiles, "*.xml"))
                r.Add(Path.GetFileNameWithoutExtension(f));
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        void EscribirEn(string ruta)
        {
            using (var w = new StreamWriter(ruta))
                new XmlSerializer(typeof(Perfil)).Serialize(w, this);
        }

        static Perfil LeerDe(string ruta)
        {
            try
            {
                if (!File.Exists(ruta)) return null;
                using (var r = new StreamReader(ruta))
                    return (Perfil)new XmlSerializer(typeof(Perfil)).Deserialize(r);
            }
            catch { return null; }      // perfil de una version vieja o corrupto: se ignora
        }

        public void Guardar() { EscribirEn(RutaDe(Nombre)); }

        public static Perfil Cargar(string nombre) { return LeerDe(RutaDe(nombre)); }

        public static void Borrar(string nombre)
        {
            string ruta = RutaDe(nombre);
            if (File.Exists(ruta)) File.Delete(ruta);
        }

        // El estado con el que se cerro la ventana la ultima vez. Vive fuera de la lista
        // de perfiles para no ensuciarla.
        static string RutaUltimo { get { return Path.Combine(CarpetaBase, "ultimo.xml"); } }

        public void GuardarUltimo()
        {
            try { EscribirEn(RutaUltimo); } catch { }
        }

        public static Perfil CargarUltimo() { return LeerDe(RutaUltimo); }

        public Perfil Copia()
        {
            var m = new MemoryStream();
            var s = new XmlSerializer(typeof(Perfil));
            s.Serialize(m, this);
            m.Position = 0;
            return (Perfil)s.Deserialize(m);
        }
    }
}
