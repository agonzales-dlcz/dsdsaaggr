// Ventana en tres pasos, con la misma disposicion que ProSheets clasico:
// Seleccion -> Formato -> Crear.
//
// Es modal a proposito: asi todo el trabajo ocurre dentro del contexto de la API de Revit
// y no hace falta ExternalEvent.

using System;
using System.Collections;
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
using Riga.ExportarPlanos.Nucleo;
// Revit.UI tambien define ComboBox y TextBox (los de la cinta): desambiguamos.
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using RevitView = Autodesk.Revit.DB.View;

namespace Riga.ExportarPlanos.Ui
{
    /// <summary>Pinta en rojo lo que fallo y en verde lo que salio.</summary>
    public class ColorEstado : IValueConverter
    {
        static readonly Brush ROJO = new SolidColorBrush(Color.FromRgb(0xC0, 0x20, 0x20));
        static readonly Brush VERDE = new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B));

        public object Convert(object valor, Type destino, object parametro, CultureInfo cultura)
        {
            string s = valor as string;
            if (string.IsNullOrEmpty(s)) return Brushes.Black;
            if (s.StartsWith("ERROR")) return ROJO;
            if (s.StartsWith("OK")) return VERDE;
            return Brushes.Black;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }

    public partial class Ventana : Window
    {
        // Elemento de un desplegable: guarda el valor real y muestra una etiqueta en castellano.
        class Op
        {
            public string Etiqueta;
            public object Valor;
            public override string ToString() { return Etiqueta; }
        }

        readonly UIDocument _uidoc;
        readonly Document _doc;

        readonly ObservableCollection<Item> _planos = new ObservableCollection<Item>();
        readonly ObservableCollection<Item> _vistas = new ObservableCollection<Item>();
        readonly ObservableCollection<Salida> _salidas = new ObservableCollection<Salida>();

        readonly Dictionary<ElementId, RevitView> _elementos = new Dictionary<ElementId, RevitView>();
        readonly Dictionary<ElementId, FamilyInstance> _cajetines = new Dictionary<ElementId, FamilyInstance>();
        readonly HashSet<ElementId> _abiertas = new HashSet<ElementId>();
        readonly Dictionary<string, HashSet<ElementId>> _conjuntos =
            new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Item, Dictionary<string, Salida>> _mapaSalidas =
            new Dictionary<Item, Dictionary<string, Salida>>();

        List<Item> _mostrados = new List<Item>();
        bool _vistasCargadas;
        Perfil _perfil = new Perfil();
        bool _cargando = true;
        bool _exportando;
        bool _cancelar;

        public Ventana(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;

            foreach (var uv in _uidoc.GetOpenUIViews()) _abiertas.Add(uv.ViewId);

            LlenarDesplegables();
            CargarPlanos();
            CargarConjuntos();

            grillaSalida.ItemsSource = _salidas;

            _perfil = Perfil.CargarUltimo() ?? PerfilInicial();
            DelPerfilALaUi(_perfil);
            RefrescarPerfiles(_perfil.Nombre);

            _cargando = false;
            if (_perfil.MostrarVistas) rbVistas.IsChecked = true;
            RefrescarNombres();
            Repintar();
            RefrescarResumen();
            ActualizarBotones();
        }

        Perfil PerfilInicial()
        {
            var p = new Perfil();
            try
            {
                string ruta = _doc.PathName;
                p.Carpeta = string.IsNullOrEmpty(ruta)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : Path.GetDirectoryName(ruta);
            }
            catch
            {
                p.Carpeta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            return p;
        }

        // ==================== carga ====================

        void CargarPlanos()
        {
            var planos = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(v => !v.IsPlaceholder)
                .ToList();
            planos.Sort((a, b) => Natural.Comparar(a.SheetNumber, b.SheetNumber));

            // Un solo colector para todos los cajetines. Ver Papel.Cajetines: pedirlos plano
            // por plano obliga a Revit a generar los graficos de cada vista.
            var cajetines = Papel.Cajetines(_doc);

            foreach (var v in planos)
            {
                FamilyInstance cajetin;
                cajetines.TryGetValue(v.Id, out cajetin);
                ExportPaperFormat f; PageOrientationType orientacion;
                string papel = Papel.Describir(cajetin, out f, out orientacion);

                _elementos[v.Id] = v;
                _cajetines[v.Id] = cajetin;

                Agregar(new Item
                {
                    Id = v.Id,
                    EsPlano = true,
                    Abierta = _abiertas.Contains(v.Id),
                    Numero = v.SheetNumber,
                    Nombre = v.Name,
                    Revision = Leer(v, BuiltInParameter.SHEET_CURRENT_REVISION),
                    Tipo = "Plano",
                    Conjunto = NombreColeccion(v),
                    PapelDetectado = f,
                    OrientacionDetectada = orientacion,
                    Papel = papel,
                    Orientacion = Papel.Rotulo(orientacion),
                    PapelDefinido = true,
                    OrientacionDefinida = true
                }, _planos);
            }
        }

        // Las vistas se cargan recien cuando hacen falta: en un modelo grande son cientos.
        void CargarVistas()
        {
            if (_vistasCargadas) return;
            _vistasCargadas = true;

            var vistas = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitView))
                .Cast<RevitView>()
                .Where(v => !v.IsTemplate && !(v is ViewSheet) && v.CanBePrinted)
                .ToList();
            vistas.Sort((a, b) => Natural.Comparar(a.Name, b.Name));

            foreach (var v in vistas)
            {
                _elementos[v.Id] = v;

                Agregar(new Item
                {
                    Id = v.Id,
                    EsPlano = false,
                    Abierta = _abiertas.Contains(v.Id),
                    Numero = "",
                    Nombre = v.Name,
                    Revision = "",
                    Tipo = v.ViewType.ToString(),
                    Conjunto = "",
                    Papel = "(definir)",
                    Orientacion = "(definir)",
                    PapelDefinido = false,
                    OrientacionDefinida = false
                }, _vistas);
            }
        }

        void Agregar(Item it, ObservableCollection<Item> destino)
        {
            it.PropertyChanged += (o, ev) =>
            {
                if (ev.PropertyName == "Marcada") RefrescarResumen();
            };
            destino.Add(it);
        }

        void CargarConjuntos()
        {
            foreach (var s in new FilteredElementCollector(_doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>())
            {
                var ids = new HashSet<ElementId>();
                foreach (RevitView v in s.Views) ids.Add(v.Id);
                if (ids.Count > 0) _conjuntos["Conjunto: " + s.Name] = ids;
            }

            foreach (var g in _planos.Where(x => !string.IsNullOrEmpty(x.Conjunto)).GroupBy(x => x.Conjunto))
                _conjuntos["Coleccion: " + g.Key] = new HashSet<ElementId>(g.Select(x => x.Id));

            var nombres = _conjuntos.Keys.ToList();
            nombres.Sort(StringComparer.OrdinalIgnoreCase);
            cbConjunto.ItemsSource = nombres;
            if (nombres.Count > 0) cbConjunto.SelectedIndex = 0;
        }

        string NombreColeccion(ViewSheet v)
        {
            try
            {
                var id = v.SheetCollectionId;
                if (id == null || id == ElementId.InvalidElementId) return "";
                var e = _doc.GetElement(id);
                return e != null ? e.Name : "";
            }
            catch { return ""; }
        }

        static string Leer(Element e, BuiltInParameter bip)
        {
            var p = e.get_Parameter(bip);
            return p == null ? "" : (p.AsString() ?? p.AsValueString() ?? "");
        }

        void LlenarDesplegables()
        {
            Llenar(cbCalidad, typeof(PDFExportQualityType));
            Llenar(cbRaster, typeof(RasterQualityType));
            Llenar(cbColor, typeof(ColorDepthType));
            Llenar(cbDwgVersion, typeof(ACADVersion));
            Llenar(cbDwgColores, typeof(ExportColorMode));
            Llenar(cbImgTipo, typeof(ImageFileType));
            Llenar(cbImgResolucion, typeof(ImageResolution));
            Llenar(cbImgZoomTipo, typeof(ZoomFitType));

            cbMargen.ItemsSource = new List<Op>
            {
                new Op { Etiqueta = "Sin margen", Valor = PaperPlacementType.LowerLeft },
                new Op { Etiqueta = "Margenes",   Valor = PaperPlacementType.Margins }
            };
            cbMargen.SelectedIndex = 0;

            cbAgrupar.ItemsSource = new[] { "Revision", "Papel", "Parametro" };
            cbAgrupar.SelectedIndex = 0;

            cbInforme.ItemsSource = new List<Op>
            {
                new Op { Etiqueta = "No guardar informe", Valor = Informe.Ninguno },
                new Op { Etiqueta = "Guardar informe .log", Valor = Informe.Log },
                new Op { Etiqueta = "Guardar informe .csv", Valor = Informe.Csv }
            };
            cbInforme.SelectedIndex = 1;

            cbFijarPapel.ItemsSource = Papel.Nombres();
            cbFijarOrientacion.ItemsSource = new List<Op>
            {
                new Op { Etiqueta = "Automatico", Valor = PageOrientationType.Auto },
                new Op { Etiqueta = "Apaisado",   Valor = PageOrientationType.Landscape },
                new Op { Etiqueta = "Vertical",   Valor = PageOrientationType.Portrait }
            };

            var configs = new List<string> { "(ninguna)" };
            try { configs.AddRange(BaseExportOptions.GetPredefinedSetupNames(_doc)); } catch { }
            cbDwgConfig.ItemsSource = configs;
            cbDwgConfig.SelectedIndex = 0;
        }

        static void Llenar(ComboBox cb, Type tipoEnum)
        {
            var lista = new List<Op>();
            foreach (var v in Enum.GetValues(tipoEnum))
                lista.Add(new Op { Etiqueta = Etiquetar(v), Valor = v });
            cb.ItemsSource = lista;
            if (lista.Count > 0) cb.SelectedIndex = 0;
        }

        static string Etiquetar(object v)
        {
            switch (v.ToString())
            {
                case "Color": return "Color";
                case "GrayScale": return "Escala de grises";
                case "BlackLine": return "Blanco y negro";
                case "Low": return "Baja";
                case "Medium": return "Media";
                case "High": return "Alta";
                case "Presentation": return "Presentacion";
                case "FitToPage": return "Ajustar a la pagina";
                case "Zoom": return "Zoom";
                case "Default": return "Predeterminado";
                case "IndexColors": return "Colores indexados";
                case "TrueColor": return "Color verdadero";
                case "TrueColorPerView": return "Color verdadero por vista";
                default: return v.ToString().Replace('_', ' ');
            }
        }

        static void Poner(ComboBox cb, object valor)
        {
            var fuente = cb.ItemsSource as IEnumerable;
            if (fuente != null)
                foreach (var o in fuente.OfType<Op>())
                    if (Equals(o.Valor, valor)) { cb.SelectedItem = o; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        static T Sacar<T>(ComboBox cb, T porDefecto)
        {
            var o = cb.SelectedItem as Op;
            return o == null ? porDefecto : (T)o.Valor;
        }

        // ==================== perfil <-> ui ====================

        void DelPerfilALaUi(Perfil p)
        {
            bool antes = _cargando;
            _cargando = true;

            txCarpeta.Text = p.Carpeta;
            rbPorFormato.IsChecked = p.SubcarpetaPorFormato;
            rbMismaCarpeta.IsChecked = !p.SubcarpetaPorFormato;
            chSobrescribir.IsChecked = p.Sobrescribir;
            Poner(cbInforme, p.Reporte);

            chPdf.IsChecked = p.Pdf; chDwg.IsChecked = p.Dwg; chDxf.IsChecked = p.Dxf;
            chDgn.IsChecked = p.Dgn; chImg.IsChecked = p.Img;

            rbCentrado.IsChecked = p.PdfPosicion == PaperPlacementType.Center;
            rbEsquina.IsChecked = p.PdfPosicion != PaperPlacementType.Center;
            if (p.PdfPosicion != PaperPlacementType.Center) Poner(cbMargen, p.PdfPosicion);
            txOffsetX.Text = p.PdfOffsetX.ToString("0.##");
            txOffsetY.Text = p.PdfOffsetY.ToString("0.##");

            rbAjustar.IsChecked = p.PdfZoomTipo == ZoomType.FitToPage;
            rbZoom.IsChecked = p.PdfZoomTipo != ZoomType.FitToPage;
            txZoom.Text = p.PdfZoom.ToString();

            rbVectorial.IsChecked = p.PdfVectorial;
            rbRaster.IsChecked = !p.PdfVectorial;
            Poner(cbRaster, p.PdfRaster);
            Poner(cbColor, p.PdfColor);
            Poner(cbCalidad, p.PdfCalidad);

            chEnlacesAzules.IsChecked = p.PdfEnlacesAzules;
            chOcultarPlanosRef.IsChecked = p.PdfOcultarPlanosRef;
            chOcultarEtiquetas.IsChecked = p.PdfOcultarEtiquetas;
            chOcultarCajas.IsChecked = p.PdfOcultarCajas;
            chOcultarCrop.IsChecked = p.PdfOcultarCrop;
            chSemitonos.IsChecked = p.PdfSemitonosFinos;
            chCoincidentes.IsChecked = p.PdfLineasCoincidentes;
            chPararEnError.IsChecked = p.PdfPararEnError;

            rbSeparados.IsChecked = p.PdfModo == ModoArchivo.Separados;
            rbUnoSolo.IsChecked = p.PdfModo == ModoArchivo.UnoSolo;
            rbVarios.IsChecked = p.PdfModo == ModoArchivo.VariosAgrupados;
            txNombreCombinado.Text = p.PdfNombreCombinado;
            cbAgrupar.SelectedItem = p.PdfAgruparPor;
            if (cbAgrupar.SelectedItem == null) cbAgrupar.SelectedIndex = 0;
            txParamGrupo.Text = p.PdfParamGrupo;
            chPapelAuto.IsChecked = p.PdfPapelAuto;

            cbDwgConfig.SelectedItem = string.IsNullOrEmpty(p.DwgConfig) ? "(ninguna)" : p.DwgConfig;
            if (cbDwgConfig.SelectedItem == null) cbDwgConfig.SelectedIndex = 0;
            Poner(cbDwgVersion, p.DwgVersion);
            Poner(cbDwgColores, p.DwgColores);
            chDwgCoords.IsChecked = p.DwgCoordsCompartidas;
            chDwgFusionar.IsChecked = p.DwgVistasFusionadas;
            txDgnSemilla.Text = p.DgnSemilla;

            Poner(cbImgTipo, p.ImgTipo);
            Poner(cbImgResolucion, p.ImgResolucion);
            Poner(cbImgZoomTipo, p.ImgZoomTipo);
            txImgPixeles.Text = p.ImgPixeles.ToString();
            txImgZoom.Text = p.ImgZoom.ToString();

            _cargando = antes;
            AjustarHabilitados();
        }

        void DeLaUiAlPerfil(Perfil p)
        {
            p.Carpeta = txCarpeta.Text.Trim();
            p.SubcarpetaPorFormato = rbPorFormato.IsChecked == true;
            p.Sobrescribir = chSobrescribir.IsChecked == true;
            p.Reporte = Sacar(cbInforme, Informe.Log);
            p.MostrarVistas = rbVistas.IsChecked == true;

            p.Pdf = chPdf.IsChecked == true; p.Dwg = chDwg.IsChecked == true;
            p.Dxf = chDxf.IsChecked == true; p.Dgn = chDgn.IsChecked == true;
            p.Img = chImg.IsChecked == true;

            p.PdfPosicion = rbCentrado.IsChecked == true
                ? PaperPlacementType.Center
                : Sacar(cbMargen, PaperPlacementType.LowerLeft);
            p.PdfOffsetX = Decimal(txOffsetX.Text, 0);
            p.PdfOffsetY = Decimal(txOffsetY.Text, 0);

            p.PdfZoomTipo = rbAjustar.IsChecked == true ? ZoomType.FitToPage : ZoomType.Zoom;
            p.PdfZoom = Entero(txZoom.Text, 100, 1, 1000);

            p.PdfVectorial = rbVectorial.IsChecked == true;
            p.PdfRaster = Sacar(cbRaster, RasterQualityType.High);
            p.PdfColor = Sacar(cbColor, ColorDepthType.Color);
            p.PdfCalidad = Sacar(cbCalidad, PDFExportQualityType.DPI300);

            p.PdfEnlacesAzules = chEnlacesAzules.IsChecked == true;
            p.PdfOcultarPlanosRef = chOcultarPlanosRef.IsChecked == true;
            p.PdfOcultarEtiquetas = chOcultarEtiquetas.IsChecked == true;
            p.PdfOcultarCajas = chOcultarCajas.IsChecked == true;
            p.PdfOcultarCrop = chOcultarCrop.IsChecked == true;
            p.PdfSemitonosFinos = chSemitonos.IsChecked == true;
            p.PdfLineasCoincidentes = chCoincidentes.IsChecked == true;
            p.PdfPararEnError = chPararEnError.IsChecked == true;

            p.PdfModo = rbUnoSolo.IsChecked == true ? ModoArchivo.UnoSolo
                      : rbVarios.IsChecked == true ? ModoArchivo.VariosAgrupados
                      : ModoArchivo.Separados;
            p.PdfNombreCombinado = txNombreCombinado.Text;
            p.PdfAgruparPor = (cbAgrupar.SelectedItem as string) ?? "Revision";
            p.PdfParamGrupo = txParamGrupo.Text.Trim();
            p.PdfPapelAuto = chPapelAuto.IsChecked == true;

            string cfg = cbDwgConfig.SelectedItem as string;
            p.DwgConfig = (cfg == null || cfg == "(ninguna)") ? "" : cfg;
            p.DwgVersion = Sacar(cbDwgVersion, ACADVersion.R2018);
            p.DwgColores = Sacar(cbDwgColores, ExportColorMode.IndexColors);
            p.DwgCoordsCompartidas = chDwgCoords.IsChecked == true;
            p.DwgVistasFusionadas = chDwgFusionar.IsChecked == true;
            p.DgnSemilla = txDgnSemilla.Text.Trim();

            p.ImgTipo = Sacar(cbImgTipo, ImageFileType.PNG);
            p.ImgResolucion = Sacar(cbImgResolucion, ImageResolution.DPI_300);
            p.ImgZoomTipo = Sacar(cbImgZoomTipo, ZoomFitType.FitToPage);
            p.ImgPixeles = Entero(txImgPixeles.Text, 2000, 32, 20000);
            p.ImgZoom = Entero(txImgZoom.Text, 100, 1, 1000);
        }

        static int Entero(string s, int porDefecto, int min, int max)
        {
            int v;
            if (!int.TryParse((s ?? "").Trim(), out v)) return porDefecto;
            return v < min ? min : (v > max ? max : v);
        }

        static double Decimal(string s, double porDefecto)
        {
            double v;
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return porDefecto;
        }

        void AjustarHabilitados()
        {
            // WPF engancha los handlers ANTES de aplicar los valores del XAML, asi que un
            // IsChecked="True" dispara su Checked mientras la ventana se sigue construyendo
            // y la mitad de los controles todavia es null. Verificado con el stack real.
            if (rbEsquina == null || rbZoom == null || rbVarios == null || cbConjunto == null) return;

            bool esquina = rbEsquina.IsChecked == true;
            cbMargen.IsEnabled = esquina;
            txOffsetX.IsEnabled = esquina;
            txOffsetY.IsEnabled = esquina;

            txZoom.IsEnabled = rbZoom.IsChecked == true;

            bool combina = rbUnoSolo.IsChecked == true || rbVarios.IsChecked == true;
            txNombreCombinado.IsEnabled = combina;
            cbAgrupar.IsEnabled = rbVarios.IsChecked == true;
            txParamGrupo.IsEnabled = rbVarios.IsChecked == true
                                     && (cbAgrupar.SelectedItem as string) == "Parametro";

            cbConjunto.IsEnabled = chFiltrarConjunto.IsChecked == true;
        }

        // ==================== filtro y nombres ====================

        ObservableCollection<Item> Actual { get { return rbVistas.IsChecked == true ? _vistas : _planos; } }

        bool Pasa(Item it)
        {
            if (it == null) return false;

            if (chSoloAbiertos.IsChecked == true && !it.Abierta) return false;

            if (chFiltrarConjunto.IsChecked == true)
            {
                var clave = cbConjunto.SelectedItem as string;
                HashSet<ElementId> ids;
                if (clave == null || !_conjuntos.TryGetValue(clave, out ids)) return false;
                if (!ids.Contains(it.Id)) return false;
            }

            string q = (txBuscar.Text ?? "").Trim();
            if (q.Length > 0)
            {
                string heno = it.Numero + " " + it.Nombre;
                if (heno.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Rearma la lista visible y la asigna al grid.
        ///
        /// A proposito NO se usa ICollectionView: enchufar y desenchufar dos vistas del
        /// mismo DataGrid lo deja en un estado invalido y al volver revienta con
        /// NullReferenceException dentro de DataGrid.OnItemsSourceChanged. Reproducido y
        /// verificado; filtrar a mano ademas es mas predecible.
        /// </summary>
        void Repintar()
        {
            if (_cargando || grilla == null) return;
            _mostrados = Actual.Where(Pasa).ToList();
            grilla.ItemsSource = _mostrados;
        }

        void RefrescarNombres()
        {
            if (_cargando) return;

            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in _planos.Concat(_vistas))
            {
                RevitView v;
                if (!_elementos.TryGetValue(it.Id, out v)) continue;

                string limpio;
                if (it.ArchivoManual)
                {
                    limpio = it.Archivo;
                }
                else
                {
                    FamilyInstance cajetin;
                    _cajetines.TryGetValue(it.Id, out cajetin);
                    string bruto = Nombrador.Resolver(_perfil.Patron, _doc, v, cajetin, it.Papel);
                    limpio = Nombrador.Sanear(bruto, _perfil.Reemplazo, v.Name);
                }

                // Dos items no pueden terminar en el mismo archivo: al repetido se le numera.
                if (!usados.Add(limpio))
                    for (int i = 2; ; i++)
                    {
                        string alterno = limpio + "_" + i;
                        if (usados.Add(alterno)) { limpio = alterno; break; }
                    }

                it.Archivo = limpio;
            }
        }

        void RefrescarResumen()
        {
            int p = _planos.Count(x => x.Marcada);
            int v = _vistas.Count(x => x.Marcada);
            lbResumen.Text = p + " planos y " + v + " vistas seleccionados.  Total: " + (p + v);
            ActualizarBotones();
        }

        void ActualizarBotones()
        {
            if (btAtras == null || btSiguiente == null || btCerrar == null || pestanas == null) return;
            int i = pestanas.SelectedIndex;
            btAtras.IsEnabled = i > 0 && !_exportando;
            btSiguiente.IsEnabled = i < pestanas.Items.Count - 1 && !_exportando;
            btCerrar.IsEnabled = !_exportando;
        }

        // ==================== eventos: seleccion ====================

        void Fuente_Cambio(object s, RoutedEventArgs e)
        {
            if (_cargando || grilla == null) return;

            if (rbVistas.IsChecked == true)
            {
                if (!_vistasCargadas)
                {
                    System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                    try { CargarVistas(); RefrescarNombres(); }
                    finally { System.Windows.Input.Mouse.OverrideCursor = null; }
                }
                lbPista.Text = _vistas.Count + " vistas imprimibles. Exportar una vista pesada puede tardar bastante mas que un plano.";
            }
            else
            {
                lbPista.Text = "Clic en el titulo de la columna Nombre de archivo para armar el patron. Cada fila se puede editar a mano.";
            }
            Filtro_Cambio(s, e);
        }

        void Conjunto_Cambio(object s, RoutedEventArgs e)
        {
            if (cbConjunto == null) return;
            AjustarHabilitados();
            Filtro_Cambio(s, e);
        }

        void Buscar_Cambio(object s, TextChangedEventArgs e) { Filtro_Cambio(s, e); }

        void Filtro_Cambio(object s, RoutedEventArgs e) { Repintar(); }

        void Todos_Click(object s, RoutedEventArgs e)
        {
            var ch = s as CheckBox;
            bool valor = ch != null && ch.IsChecked == true;
            foreach (var it in _mostrados) it.Marcada = valor;
            RefrescarResumen();
        }

        void Celda_Editada(object s, DataGridCellEditEndingEventArgs e)
        {
            // La cabecera ya no es texto sino un boton, asi que comparamos la columna.
            if (!ReferenceEquals(e.Column, colArchivo)) return;
            var it = e.Row.Item as Item;
            if (it == null) return;

            it.ArchivoManual = true;
            // El binding todavia no escribio: saneamos despues de que se confirme.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                it.Archivo = Nombrador.Sanear(it.Archivo, _perfil.Reemplazo, it.Nombre);
            }), DispatcherPriority.Background);
        }

        // ==================== eventos: formato ====================

        void Formato_Cambio(object s, RoutedEventArgs e)
        {
            if (_cargando) return;
            ArmarSalidas();
        }

        void Posicion_Cambio(object s, RoutedEventArgs e) { if (!_cargando) AjustarHabilitados(); }
        void Zoom_Cambio(object s, RoutedEventArgs e) { if (!_cargando) AjustarHabilitados(); }
        void Modo_Cambio(object s, RoutedEventArgs e) { if (!_cargando) AjustarHabilitados(); }
        void Agrupar_Cambio(object s, SelectionChangedEventArgs e) { if (!_cargando) AjustarHabilitados(); }

        void NombreArchivo_Click(object s, RoutedEventArgs e)
        {
            var muestra = Actual.Where(x => x.Marcada).ToList();
            if (muestra.Count == 0) muestra = Actual.Take(30).ToList();

            var dlg = new DlgNombre(_doc, muestra,
                                    id => { RevitView v; return _elementos.TryGetValue(id, out v) ? v : null; },
                                    id => { FamilyInstance c; return _cajetines.TryGetValue(id, out c) ? c : null; },
                                    _perfil.Patron, _perfil.Reemplazo)
            { Owner = this };

            if (dlg.ShowDialog() != true) return;

            _perfil.Patron = dlg.Patron;
            _perfil.Reemplazo = dlg.Reemplazo;
            foreach (var it in _planos.Concat(_vistas)) it.ArchivoManual = false;
            RefrescarNombres();
        }

        void Semilla_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Archivo semilla DGN",
                Filter = "Archivos DGN (*.dgn)|*.dgn|Todos (*.*)|*.*"
            };
            if (d.ShowDialog(this) == true) txDgnSemilla.Text = d.FileName;
        }

        // ==================== eventos: crear ====================

        void Carpeta_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta de salida" };
            if (Directory.Exists(txCarpeta.Text)) d.InitialDirectory = txCarpeta.Text;
            if (d.ShowDialog(this) == true) txCarpeta.Text = d.FolderName;
        }

        /// <summary>Una fila por cada item marcado y cada formato pedido.</summary>
        void ArmarSalidas()
        {
            _salidas.Clear();
            _mapaSalidas.Clear();

            DeLaUiAlPerfil(_perfil);
            var formatos = _perfil.FormatosActivos;

            foreach (var it in _planos.Concat(_vistas).Where(x => x.Marcada))
            {
                var porFormato = new Dictionary<string, Salida>();
                foreach (var f in formatos)
                {
                    var sal = new Salida { Origen = it, Formato = f };
                    _salidas.Add(sal);
                    porFormato[f] = sal;
                }
                _mapaSalidas[it] = porFormato;
            }

            RefrescarSalidas();
        }

        void RefrescarSalidas()
        {
            if (lbSalidas == null) return;
            int faltan = SinDefinir(_salidas.Select(x => x.Origen).Distinct()).Count;
            lbSalidas.Text = _salidas.Count + " archivos a generar"
                + (faltan > 0 ? "   -   " + faltan + " vistas sin tamano u orientacion" : "");
        }

        /// <summary>Vistas a las que todavia les falta definir el tamano o la orientacion.</summary>
        static List<Item> SinDefinir(IEnumerable<Item> items)
        {
            return items.Where(x => !x.EsPlano && (!x.PapelDefinido || !x.OrientacionDefinida)).ToList();
        }

        /// <summary>
        /// Las filas elegidas que son vistas. Los planos se dejan afuera a proposito: su
        /// tamano y su orientacion salen del cajetin y no hay razon para pisarlos.
        /// Devuelve null (y avisa) si no hay ninguna vista en la seleccion.
        /// </summary>
        List<Item> Vistas(IEnumerable<Salida> filas)
        {
            var todas = filas.Select(x => x.Origen).Distinct().ToList();
            if (todas.Count == 0) { Aviso("Elegi primero las filas de la lista."); return null; }

            var vistas = todas.Where(x => !x.EsPlano).ToList();
            if (vistas.Count == 0)
            {
                Aviso("Solo elegiste planos. El tamano y la orientacion de un plano salen de su "
                      + "cajetin; los que hay que definir a mano son los de las vistas.");
                return null;
            }
            return vistas;
        }

        void FijarPapel_Cambio(object s, SelectionChangedEventArgs e)
        {
            if (_cargando || cbFijarPapel.SelectedItem == null) return;
            string nombre = cbFijarPapel.SelectedItem as string;
            var vistas = Vistas(grillaSalida.SelectedItems.OfType<Salida>());
            if (vistas == null) return;

            foreach (var it in vistas)
            {
                if (nombre == "Automatico")
                {
                    it.PapelDetectado = ExportPaperFormat.Default;
                    it.Papel = "(definir)";
                    it.PapelDefinido = false;
                }
                else
                {
                    it.PapelDetectado = Papel.PorNombre(nombre);
                    it.Papel = nombre;
                    it.PapelForzado = true;
                    it.PapelDefinido = true;
                }
            }
            foreach (var f in _salidas) f.Refrescar();
            RefrescarSalidas();
        }

        void FijarOrientacion_Cambio(object s, SelectionChangedEventArgs e)
        {
            if (_cargando || cbFijarOrientacion.SelectedItem == null) return;
            var orientacion = Sacar(cbFijarOrientacion, PageOrientationType.Auto);
            var vistas = Vistas(grillaSalida.SelectedItems.OfType<Salida>());
            if (vistas == null) return;

            foreach (var it in vistas)
            {
                it.OrientacionDetectada = orientacion;
                it.Orientacion = Papel.Rotulo(orientacion);
                it.OrientacionDefinida = true;
            }
            foreach (var f in _salidas) f.Refrescar();
            RefrescarSalidas();
        }

        // ==================== perfiles ====================

        void RefrescarPerfiles(string seleccionar)
        {
            bool antes = _cargando;
            _cargando = true;
            cbPerfil.ItemsSource = Perfil.Listar();
            cbPerfil.Text = seleccionar ?? "";
            _cargando = antes;
        }

        void Perfil_Cambio(object s, SelectionChangedEventArgs e)
        {
            if (_cargando) return;
            string nombre = cbPerfil.SelectedItem as string;
            if (string.IsNullOrEmpty(nombre)) return;

            var p = Perfil.Cargar(nombre);
            if (p == null) { Aviso("No se pudo leer el perfil '" + nombre + "'."); return; }

            _perfil = p;
            DelPerfilALaUi(p);
            foreach (var it in _planos.Concat(_vistas)) it.ArchivoManual = false;
            RefrescarNombres();
        }

        void PerfilGuardar_Click(object s, RoutedEventArgs e)
        {
            string nombre = (cbPerfil.Text ?? "").Trim();
            if (nombre.Length == 0) { Aviso("Escribi un nombre para el perfil."); return; }

            DeLaUiAlPerfil(_perfil);
            _perfil.Nombre = nombre;
            try { _perfil.Guardar(); RefrescarPerfiles(nombre); }
            catch (Exception ex) { Aviso("No se pudo guardar: " + ex.Message); }
        }

        void PerfilBorrar_Click(object s, RoutedEventArgs e)
        {
            string nombre = (cbPerfil.Text ?? "").Trim();
            if (nombre.Length == 0) return;
            if (MessageBox.Show(this, "Borrar el perfil '" + nombre + "'?", "Exportar planos",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { Perfil.Borrar(nombre); RefrescarPerfiles(""); }
            catch (Exception ex) { Aviso("No se pudo borrar: " + ex.Message); }
        }

        // ==================== navegacion ====================

        void Pestana_Cambio(object s, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, pestanas)) return;   // no las pestanas de formato
            if (_cargando) return;
            if (pestanas.SelectedIndex == 2) ArmarSalidas();
            ActualizarBotones();
        }

        void Atras_Click(object s, RoutedEventArgs e)
        {
            if (pestanas.SelectedIndex > 0) pestanas.SelectedIndex--;
        }

        void Siguiente_Click(object s, RoutedEventArgs e)
        {
            if (pestanas.SelectedIndex < pestanas.Items.Count - 1) pestanas.SelectedIndex++;
        }

        void Cerrar_Click(object s, RoutedEventArgs e) { Close(); }

        // ==================== exportacion ====================

        void Crear_Click(object s, RoutedEventArgs e)
        {
            if (_exportando) { _cancelar = true; btCrear.Content = "Cancelando..."; return; }

            if (pestanas.SelectedIndex != 2) { pestanas.SelectedIndex = 2; return; }

            DeLaUiAlPerfil(_perfil);

            if (!_perfil.AlgunFormato) { Aviso("Elegi al menos un formato en la pestana Formato."); return; }
            if (_perfil.Carpeta.Length == 0) { Aviso("Elegi la carpeta de salida."); return; }

            var lista = _planos.Concat(_vistas).Where(x => x.Marcada).ToList();
            if (lista.Count == 0) { Aviso("No hay ningun plano ni vista marcado."); return; }

            // El papel solo manda en PDF: bloquear un DWG por eso no tendria sentido.
            if (_perfil.Pdf)
            {
                var faltan = SinDefinir(lista);
                if (faltan.Count > 0)
                {
                    pestanas.SelectedIndex = 2;
                    Aviso(faltan.Count + " vistas no tienen definido el tamano de papel o la orientacion.\n\n"
                        + "Elegilas en la lista de abajo y usa \"Fijar tamano\" y \"Fijar orientacion\".\n"
                        + "Los planos no hace falta: los toman de su cajetin.");
                    return;
                }
            }

            try { Directory.CreateDirectory(_perfil.Carpeta); }
            catch (Exception ex) { Aviso("No se puede usar esa carpeta:\n" + ex.Message); return; }

            foreach (var it in lista) it.Estado = "";
            foreach (var sal in _salidas) sal.Progreso = "";

            _exportando = true;
            _cancelar = false;
            btCrear.Content = "Cancelar";
            ActualizarBotones();
            barra.Maximum = lista.Count;
            barra.Value = 0;

            var reloj = Stopwatch.StartNew();
            var exp = new Exportador(_doc, _perfil);
            try
            {
                exp.Correr(lista,
                    (i, texto) =>
                    {
                        barra.Value = i;
                        lbProgreso.Text = "Exportando " + (i + 1) + " de " + lista.Count;
                        lbDetalle.Text = texto;
                        Respirar();
                    },
                    (it, formato, resultado) =>
                    {
                        Dictionary<string, Salida> porFormato;
                        Salida sal;
                        if (_mapaSalidas.TryGetValue(it, out porFormato) &&
                            porFormato.TryGetValue(formato, out sal))
                            sal.Progreso = resultado;
                        Respirar();
                    },
                    () => _cancelar);
            }
            catch (Exception ex)
            {
                exp.Bitacora.Add("ERROR GENERAL\t-\t" + ex.Message);
            }
            finally
            {
                reloj.Stop();
                barra.Value = barra.Maximum;
                _exportando = false;
                btCrear.Content = "Crear";
                ActualizarBotones();
            }

            string informe = exp.GuardarInforme(_perfil.Carpeta);
            GuardarUltimo();

            lbProgreso.Text = exp.Fallidos == 0
                ? "Listo: " + exp.Exportados + " items"
                : exp.Exportados + " OK, " + exp.Fallidos + " con error";
            lbDetalle.Text = "Tiempo: " + reloj.Elapsed.ToString(@"mm\:ss");

            Informar(exp, reloj.Elapsed, informe);
        }

        void Informar(Exportador exp, TimeSpan tiempo, string informe)
        {
            var td = new TaskDialog("Exportar planos")
            {
                MainInstruction = exp.Fallidos == 0
                    ? "Listo: " + exp.Exportados + " items exportados."
                    : exp.Exportados + " exportados, " + exp.Fallidos + " con error.",
                MainContent = "Carpeta: " + _perfil.Carpeta +
                              "\nTiempo: " + tiempo.ToString(@"mm\:ss") +
                              (informe != null ? "\nInforme: " + Path.GetFileName(informe) : ""),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir la carpeta de salida");
            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _perfil.Carpeta + "\"")
                    { UseShellExecute = true });
                }
                catch { }
            }
        }

        /// <summary>Deja respirar a la UI: sin esto la barra no se mueve y no se puede cancelar.</summary>
        static void Respirar()
        {
            var marco = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => marco.Continue = false));
            Dispatcher.PushFrame(marco);
        }

        void GuardarUltimo()
        {
            try { DeLaUiAlPerfil(_perfil); _perfil.GuardarUltimo(); } catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_exportando) { e.Cancel = true; _cancelar = true; return; }
            GuardarUltimo();
            base.OnClosing(e);
        }

        void Aviso(string texto)
        {
            MessageBox.Show(this, texto, "Exportar planos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Ordena IE-2 antes que IE-10, que es lo que uno espera de un numero de plano.</summary>
    internal static class Natural
    {
        public static int Comparar(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int i0 = i, j0 = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;

                    string na = a.Substring(i0, i - i0).TrimStart('0');
                    string nb = b.Substring(j0, j - j0).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i) - (b.Length - j);
        }
    }
}
