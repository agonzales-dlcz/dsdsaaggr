// Motor de exportacion. Trabaja igual sobre planos y sobre vistas sueltas.
//
// Estrategia: Revit decide el nombre del archivo que produce (le agrega el nombre de la
// vista, resuelve duplicados, etc.) y cada formato lo hace distinto. Para poder mandar
// nosotros, cada item se exporta a una carpeta temporal vacia y despues se cosecha lo que
// haya quedado ahi y se mueve al destino con el nombre que armo el patron.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.ExportarPlanos.Nucleo
{
    internal sealed class Exportador
    {
        readonly Document _doc;
        readonly Perfil _p;

        public readonly List<string> Bitacora = new List<string>();
        public int Exportados, Fallidos;

        public Exportador(Document doc, Perfil perfil)
        {
            _doc = doc;
            _p = perfil;
            Bitacora.Add("item\tformato\tresultado");
        }

        /// <param name="avance">(indice base 0, texto) antes de cada item.</param>
        /// <param name="paso">(item, formato, resultado) despues de cada formato.</param>
        /// <param name="cancelado">se consulta entre items.</param>
        public void Correr(IList<Item> lista, Action<int, string> avance,
                           Action<Item, string, string> paso, Func<bool> cancelado)
        {
            Directory.CreateDirectory(_p.Carpeta);

            bool combinar = _p.Pdf && _p.PdfModo != ModoArchivo.Separados;
            var resultadoCombinado = new Dictionary<Item, string>();

            if (combinar)
            {
                avance(0, "PDF combinado...");
                CombinarPdf(lista, resultadoCombinado, paso);

                if (!_p.Dwg && !_p.Dxf && !_p.Dgn && !_p.Img)
                {
                    foreach (var it in lista)
                    {
                        bool ok = resultadoCombinado[it] == "OK";
                        it.Estado = ok ? "OK  PDF" : "ERROR  PDF";
                        if (ok) Exportados++; else Fallidos++;
                    }
                    return;
                }
            }

            for (int i = 0; i < lista.Count; i++)
            {
                if (cancelado()) { Bitacora.Add("(cancelado por el usuario)"); break; }

                var it = lista[i];
                avance(i, string.IsNullOrEmpty(it.Numero) ? it.Nombre : it.Numero);

                var vista = _doc.GetElement(it.Id) as View;
                if (vista == null)
                {
                    it.Estado = "ERROR  no existe";
                    Fallidos++;
                    Bitacora.Add(Etiqueta(it) + "\t-\tERROR: el elemento ya no existe en el modelo.");
                    continue;
                }

                var hechos = new List<string>();
                var fallas = new List<string>();

                if (combinar)
                {
                    string r = resultadoCombinado[it];
                    if (r == "OK") hechos.Add("PDF"); else fallas.Add("PDF");
                }
                else if (_p.Pdf) Intentar("PDF", it, hechos, fallas, paso, t => Pdf(vista, it, t));

                if (_p.Dwg) Intentar("DWG", it, hechos, fallas, paso, t => Dwg(vista, t));
                if (_p.Dxf) Intentar("DXF", it, hechos, fallas, paso, t => Dxf(vista, t));
                if (_p.Dgn) Intentar("DGN", it, hechos, fallas, paso, t => Dgn(vista, t));
                if (_p.Img) Intentar("IMG", it, hechos, fallas, paso, t => Imagen(vista, t));

                if (fallas.Count == 0)
                {
                    it.Estado = "OK  " + string.Join(" ", hechos);
                    Exportados++;
                }
                else
                {
                    it.Estado = "ERROR  " + string.Join(" ", fallas);
                    Fallidos++;
                }
            }
        }

        static string Etiqueta(Item it)
        {
            return string.IsNullOrEmpty(it.Numero) ? it.Nombre : it.Numero;
        }

        // Envoltorio comun: carpeta temporal, exportar, cosechar, limpiar.
        void Intentar(string formato, Item it, List<string> hechos, List<string> fallas,
                      Action<Item, string, string> paso, Action<string> exportar)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ExportarPlanos", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                exportar(tmp);

                var salidas = Cosechar(tmp, CarpetaDestino(formato), it.Archivo);

                hechos.Add(formato);
                foreach (var s in salidas) Bitacora.Add(Etiqueta(it) + "\t" + formato + "\t" + s);
                if (paso != null) paso(it, formato, "OK");
            }
            catch (Exception ex)
            {
                fallas.Add(formato);
                Bitacora.Add(Etiqueta(it) + "\t" + formato + "\tERROR: " + Raiz(ex));
                if (paso != null) paso(it, formato, "ERROR: " + Raiz(ex));
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        static string Raiz(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message.Replace("\r", " ").Replace("\n", " ");
        }

        string CarpetaDestino(string formato)
        {
            string d = _p.SubcarpetaPorFormato ? Path.Combine(_p.Carpeta, formato) : _p.Carpeta;
            Directory.CreateDirectory(d);
            return d;
        }

        const string PREFIJO = "salida";

        /// <summary>
        /// Mueve lo que Revit dejo en la carpeta temporal al destino.
        ///
        /// Ojo: una exportacion NO deja un solo archivo. Un plano a DWG deja el dibujo, un
        /// .pcp y todas las imagenes del cajetin (logos, firmas); y si las vistas no van
        /// fusionadas, ademas un DWG por cada vista colocada, como referencia externa.
        /// Solo se renombra el archivo principal: los acompanantes estan enlazados por
        /// nombre desde el dibujo, asi que se copian tal cual.
        /// </summary>
        List<string> Cosechar(string tmp, string destino, string nombreBase)
        {
            var todos = Directory.GetFiles(tmp, "*", SearchOption.AllDirectories);
            if (todos.Length == 0)
                throw new InvalidOperationException("Revit no genero ningun archivo.");

            Array.Sort(todos, StringComparer.OrdinalIgnoreCase);

            // Revit escribe el principal como "salida.<ext>". Cuando el formato le agrega
            // sufijos (la imagen deja "salida - Plano - ..."), caemos al que empieza igual.
            var principales = todos
                .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), PREFIJO,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (principales.Count == 0)
                principales = todos
                    .Where(f => Path.GetFileName(f).StartsWith(PREFIJO, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (principales.Count == 0)
                throw new InvalidOperationException("Revit genero archivos pero ninguno reconocible.");

            var salidas = new List<string>();
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in principales)
            {
                string ext = Path.GetExtension(f);
                string nom = nombreBase + ext;
                if (!usados.Add(nom))
                {
                    nom = nombreBase + "_" + usados.Count + ext;
                    usados.Add(nom);
                }

                string ruta = Path.Combine(destino, nom);
                if (File.Exists(ruta))
                {
                    if (_p.Sobrescribir) File.Delete(ruta);
                    else ruta = SinPisar(destino, Path.GetFileNameWithoutExtension(nom), ext);
                }
                File.Move(f, ruta);
                salidas.Add(ruta);
            }

            // Acompanantes: van al lado del principal y con su nombre original, si no estan ya.
            int acompanantes = 0;
            var jugados = new HashSet<string>(principales, StringComparer.OrdinalIgnoreCase);
            foreach (var f in todos)
            {
                if (jugados.Contains(f)) continue;
                string dst = Path.Combine(destino, Path.GetFileName(f));
                if (!File.Exists(dst)) { File.Move(f, dst); acompanantes++; }
            }
            if (acompanantes > 0)
                salidas.Add("(+" + acompanantes + " archivos enlazados)");

            return salidas;
        }

        static string SinPisar(string carpeta, string nombreBase, string ext)
        {
            for (int i = 2; i < 1000; i++)
            {
                string r = Path.Combine(carpeta, nombreBase + " (" + i + ")" + ext);
                if (!File.Exists(r)) return r;
            }
            return Path.Combine(carpeta, nombreBase + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        // ---------------- PDF ----------------

        PDFExportOptions OpcionesPdf()
        {
            var o = new PDFExportOptions
            {
                ColorDepth = _p.PdfColor,
                RasterQuality = _p.PdfRaster,
                ExportQuality = _p.PdfCalidad,
                PaperFormat = ExportPaperFormat.Default,
                PaperOrientation = PageOrientationType.Auto,
                PaperPlacement = _p.PdfPosicion,
                ZoomType = _p.PdfZoomTipo,
                ZoomPercentage = _p.PdfZoom,
                HideCropBoundaries = _p.PdfOcultarCrop,
                HideScopeBoxes = _p.PdfOcultarCajas,
                HideReferencePlane = _p.PdfOcultarPlanosRef,
                HideUnreferencedViewTags = _p.PdfOcultarEtiquetas,
                ViewLinksInBlue = _p.PdfEnlacesAzules,
                MaskCoincidentLines = _p.PdfLineasCoincidentes,
                AlwaysUseRaster = !_p.PdfVectorial,
                ReplaceHalftoneWithThinLines = _p.PdfSemitonosFinos,
                StopOnError = _p.PdfPararEnError
            };

            if (_p.PdfPosicion != PaperPlacementType.Center)
            {
                o.OriginOffsetX = UnitUtils.ConvertToInternalUnits(_p.PdfOffsetX, UnitTypeId.Millimeters);
                o.OriginOffsetY = UnitUtils.ConvertToInternalUnits(_p.PdfOffsetY, UnitTypeId.Millimeters);
            }

            // Revit 2025 puede exportar PDF en segundo plano y volver antes de escribir el
            // archivo. Lo fijamos explicitamente para que la cosecha no llegue vacia.
            try { o.SetExportInBackground(false); } catch { }
            return o;
        }

        void Pdf(View vista, Item it, string tmp)
        {
            var o = OpcionesPdf();

            if (_p.PdfPapelAuto)
            {
                o.PaperFormat = it.PapelDetectado;
                o.PaperOrientation = it.OrientacionDetectada;
            }

            // Combine con un solo item: garantiza un unico PDF y un nombre conocido.
            o.Combine = true;
            o.FileName = "salida";

            if (!_doc.Export(tmp, new List<ElementId> { vista.Id }, o))
                throw new InvalidOperationException("Revit rechazo la exportacion a PDF.");
        }

        void CombinarPdf(IList<Item> lista, Dictionary<Item, string> resultado,
                         Action<Item, string, string> paso)
        {
            foreach (var g in Agrupar(lista))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ExportarPlanos", Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(tmp);

                    var o = OpcionesPdf();
                    o.Combine = true;
                    o.FileName = "salida";

                    if (!_doc.Export(tmp, g.Value.Select(x => x.Id).ToList(), o))
                        throw new InvalidOperationException("Revit rechazo la exportacion a PDF.");

                    var primero = _doc.GetElement(g.Value[0].Id) as View;
                    string nombre = Nombrador.Resolver(_p.PdfNombreCombinado, _doc, primero, null, "");
                    nombre = nombre.Replace("{Grupo}", g.Key);
                    nombre = Nombrador.Sanear(nombre, _p.Reemplazo, "Paquete");

                    var salidas = Cosechar(tmp, CarpetaDestino("PDF"), nombre);
                    foreach (var s in salidas) Bitacora.Add("(combinado " + g.Key + ")\tPDF\t" + s);
                    foreach (var it in g.Value)
                    {
                        resultado[it] = "OK";
                        if (paso != null) paso(it, "PDF", "OK (combinado)");
                    }
                }
                catch (Exception ex)
                {
                    Bitacora.Add("(combinado " + g.Key + ")\tPDF\tERROR: " + Raiz(ex));
                    foreach (var it in g.Value)
                    {
                        resultado[it] = "ERROR";
                        if (paso != null) paso(it, "PDF", "ERROR: " + Raiz(ex));
                    }
                }
                finally
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                }
            }
        }

        /// <summary>Un solo grupo, o uno por clave si se pidio "varios archivos".</summary>
        List<KeyValuePair<string, List<Item>>> Agrupar(IList<Item> lista)
        {
            var r = new List<KeyValuePair<string, List<Item>>>();
            if (_p.PdfModo != ModoArchivo.VariosAgrupados)
            {
                r.Add(new KeyValuePair<string, List<Item>>("", lista.ToList()));
                return r;
            }

            var orden = new List<string>();
            var mapa = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in lista)
            {
                string k = Clave(it);
                if (!mapa.ContainsKey(k)) { mapa[k] = new List<Item>(); orden.Add(k); }
                mapa[k].Add(it);
            }
            foreach (var k in orden) r.Add(new KeyValuePair<string, List<Item>>(k, mapa[k]));
            return r;
        }

        string Clave(Item it)
        {
            string k;
            switch (_p.PdfAgruparPor)
            {
                case "Papel":
                    k = it.Papel;
                    break;
                case "Parametro":
                    var e = _doc.GetElement(it.Id);
                    k = Nombrador.Parametro(e, _p.PdfParamGrupo);
                    break;
                default:
                    k = it.Revision;
                    break;
            }
            return string.IsNullOrWhiteSpace(k) ? "sin valor" : k.Trim();
        }

        // ---------------- DWG / DXF / DGN ----------------

        DWGExportOptions OpcionesDwg()
        {
            DWGExportOptions o = null;
            if (!string.IsNullOrWhiteSpace(_p.DwgConfig))
            {
                try { o = DWGExportOptions.GetPredefinedOptions(_doc, _p.DwgConfig); } catch { }
            }
            if (o != null) return o;                 // si hay configuracion guardada, manda ella

            return new DWGExportOptions
            {
                FileVersion = _p.DwgVersion,
                Colors = _p.DwgColores,
                SharedCoords = _p.DwgCoordsCompartidas,
                MergedViews = _p.DwgVistasFusionadas
            };
        }

        void Dwg(View vista, string tmp)
        {
            if (!_doc.Export(tmp, "salida", new List<ElementId> { vista.Id }, OpcionesDwg()))
                throw new InvalidOperationException("Revit rechazo la exportacion a DWG.");
        }

        void Dxf(View vista, string tmp)
        {
            DXFExportOptions o = null;
            if (!string.IsNullOrWhiteSpace(_p.DwgConfig))
            {
                try { o = DXFExportOptions.GetPredefinedOptions(_doc, _p.DwgConfig); } catch { }
            }
            if (o == null) o = new DXFExportOptions();

            if (!_doc.Export(tmp, "salida", new List<ElementId> { vista.Id }, o))
                throw new InvalidOperationException("Revit rechazo la exportacion a DXF.");
        }

        void Dgn(View vista, string tmp)
        {
            var o = new DGNExportOptions();
            if (!string.IsNullOrWhiteSpace(_p.DgnSemilla) && File.Exists(_p.DgnSemilla))
                o.SeedName = _p.DgnSemilla;

            if (!_doc.Export(tmp, "salida", new List<ElementId> { vista.Id }, o))
                throw new InvalidOperationException("Revit rechazo la exportacion a DGN.");
        }

        // ---------------- Imagen ----------------

        void Imagen(View vista, string tmp)
        {
            var o = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(tmp, "salida"),
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = _p.ImgTipo,
                ShadowViewsFileType = _p.ImgTipo,
                ImageResolution = _p.ImgResolucion,
                ZoomType = _p.ImgZoomTipo,
                PixelSize = _p.ImgPixeles,
                Zoom = _p.ImgZoom
            };
            o.SetViewsAndSheets(new List<ElementId> { vista.Id });
            _doc.ExportImage(o);
        }

        // ---------------- Informe ----------------

        public string GuardarInforme(string carpeta)
        {
            if (_p.Reporte == Informe.Ninguno) return null;
            try
            {
                string ext = _p.Reporte == Informe.Csv ? ".csv" : ".log";
                string ruta = Path.Combine(carpeta,
                    "exportacion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext);

                var lineas = _p.Reporte == Informe.Csv
                    ? Bitacora.Select(l => string.Join(";", l.Split('\t').Select(c => "\"" + c.Replace("\"", "\"\"") + "\"")))
                    : Bitacora.AsEnumerable();

                File.WriteAllLines(ruta, lineas, System.Text.Encoding.UTF8);
                return ruta;
            }
            catch { return null; }
        }
    }
}
