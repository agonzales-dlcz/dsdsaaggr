# Exportar planos

Exportacion en lote de **planos y vistas** para Revit 2025, con la disposicion de
ProSheets clasico: **Seleccion -> Formato -> Crear**.

Pestana **RIGA** > panel **Exportar** > boton **Exportar planos**.

## Las tres pestanas

**Seleccion.** Arriba, el par `Planos / Vistas`, el filtro por conjunto o coleccion y el
buscador. La grilla trae numero, nombre, revision, tamano y la columna **Nombre de
archivo**, que se puede editar a mano fila por fila. El tilde de la cabecera marca o
desmarca todo lo que se este viendo. Abajo a la derecha, "Mostrar solo los abiertos en
Revit".

**Formato.** Una solapa por formato (PDF, DWG, DXF, DGN, IMG) con el tilde en la propia
solapa: el tilde lo habilita, la solapa muestra sus opciones. El PDF replica las tres
columnas de ProSheets:

| Columna izquierda | Columna central | Columna derecha |
|---|---|---|
| Ubicacion en el papel | Vistas con lineas ocultas | Opciones (los 8 tildes) |
| Zoom | Aspecto (calidad raster, colores) | Archivo (separados / uno solo / varios agrupados) |
| Calidad de salida (DPI) | | |

El nombrado de archivo **no** vive aca: esta en la pestana Seleccion, en la cabecera de
su columna.

**Crear.** Reglas de exportacion (carpeta, misma carpeta o subcarpeta por formato,
informe), el progreso, y la lista de salidas: una fila por cada item y cada formato, con
su tamano, su giro y el resultado. Los desplegables **Fijar tamano** y **Fijar giro**
se aplican a las filas que tengas seleccionadas.

## Vistas, no solo planos

El radio `Vistas` lista todas las vistas imprimibles que no son plantilla. Las vistas se
cargan la primera vez que las pedis, no al abrir: en este modelo son 685 y tardarian.

Dos advertencias medidas sobre este modelo:

- Una vista pesada tarda **mucho** mas que un plano: 27 s contra 4,7 s en la prueba.
- Los campos propios de plano (`{Numero}`, `{Revision}`, ...) quedan **vacios** sobre una
  vista. Si el patron queda vacio del todo, cae al nombre de la vista.

## Campos del nombre de archivo

Se editan haciendo **clic en el titulo de la columna "Nombre de archivo"** de la pestana
Seleccion, igual que la columna "Custom File Name" de ProSheets. El dialogo muestra la
vista previa sobre los items reales. Cada fila ademas se puede editar a mano.

| Campo | Sale de |
|---|---|
| `{Numero}` `{Nombre}` | numero y nombre del plano o de la vista |
| `{Revision}` `{FechaRevision}` `{DescRevision}` | revision actual del plano |
| `{FechaEmision}` `{Dibujado}` `{Revisado}` `{Aprobado}` | cajetin del plano |
| `{NumeroProyecto}` `{NombreProyecto}` `{Cliente}` | informacion del proyecto |
| `{Papel}` `{TipoVista}` `{Escala}` `{Modelo}` | calculados |
| `{Fecha:yyyy-MM-dd}` | fecha de hoy, con el formato que le pongas |
| `{P:Sector}` | **cualquier** parametro del plano o de la vista |
| `{C:Escala}` | **cualquier** parametro del cajetin |
| `{Info:Fase}` | cualquier parametro de la informacion del proyecto |
| `{Grupo}` | solo en el nombre del PDF combinado por grupos |

Si dos items terminan con el mismo nombre, al segundo se le agrega `_2`.

## Instalar

Con Revit 2025 **cerrado**:

```powershell
.\instalar.ps1
```

Compila, borra los restos del addin anterior (`RigaPublicador`) para no terminar con dos
botones, y copia cuatro archivos a `%APPDATA%\Autodesk\Revit\Addins\2025`.
Para desinstalar, borra esos cuatro archivos.

## Lo que se aprendio probando contra el modelo

Todo esto se verifico ejecutando codigo dentro de Revit sobre el modelo IEL, no leyendo
documentacion.

- **Una exportacion no deja un solo archivo.** Un plano a DWG con las vistas fusionadas
  deja el dibujo, un `.pcp` y **7 imagenes** (logos y firmas del cajetin). Si las vistas
  *no* van fusionadas, deja ademas **un DWG por cada vista colocada**, como referencia
  externa: en la prueba, **mas de 200 archivos para un solo plano**.

  Por eso la cosecha distingue el archivo **principal** (el que Revit llama `salida.*`) de
  los **acompanantes**: el principal se renombra segun el patron, los acompanantes se
  copian al lado **con su nombre original**, porque el dibujo los enlaza por nombre y
  renombrarlos romperia el enlace. Por lo mismo, "fusionar las vistas" viene tildado.

- **Un colector acotado a una vista genera los graficos de esa vista.** Buscar el cajetin
  con `new FilteredElementCollector(doc, plano.Id)` obliga a Revit a calcular que se ve en
  el plano, y eso dispara el cartel "Generando graficos para Plano...". Hecho plano por
  plano en un modelo de 160 planos, la ventana tardaba muchisimo en abrir (y dejaba a Revit
  sin responder).

  `Papel.Cajetines()` lo resuelve con **un solo** colector de documento sobre la categoria
  de cajetines, agrupando por `OwnerViewId`, que es el plano al que pertenece cada uno.
  Regla general para este addin: nada de colectores por vista en la carga de la ventana.

- **Un DataGrid no admite que le enchufen y desenchufen `ICollectionView`.** Tener una
  vista de coleccion para los planos y otra para las vistas, e ir cambiando cual esta
  puesta en el grid, hace que al volver a la primera reviente con NullReferenceException
  dentro de `DataGrid.OnItemsSourceChanged` -> `ListCollectionView.get_CanRemove()`.
  Reproducido en WPF puro, fuera de Revit.

  Por eso el filtrado no usa `ICollectionView`: `Repintar()` arma una `List<Item>` con lo
  que pasa el filtro y la asigna al grid. Mas simple y, medido en la reproduccion, es lo
  unico de las tres alternativas que ademas filtra bien.

- **La imagen se nombra distinto.** Revit deja `salida - Plano - <numero> - <nombre>.png`,
  con sufijo. La cosecha primero busca el nombre exacto y, si no lo encuentra, cae al que
  empieza igual.

- **El cajetin del proyecto mide 841 x 594 mm**, o sea A1 apaisado, y la tabla de tamanos
  lo resuelve bien.

- **`GetExportInBackground()` ya venia en `false`** en este equipo, asi que el export en
  segundo plano no es el peligro que parecia. Se fija igual, explicitamente, porque no
  cuesta nada y no depende de la configuracion de la maquina.

- Las **configuraciones de exportacion CAD** guardadas en el proyecto se leen bien:
  `EXPORT. TIC`, `EXPORT. IEL`, `IEL-IMT-A1`.

## Limitaciones conocidas

- No exporta IFC ni NWC: son exportaciones de modelo, no de plano, y dependen de que esten
  instalados los exportadores correspondientes.
- El orden del PDF combinado es el de la lista; no hay reordenamiento manual.
- Con una configuracion CAD del proyecto elegida, manda ella sobre la version y los
  colores de la solapa DWG.
- La ventana es modal: mientras exporta no se puede tocar Revit. Es a proposito, para
  correr dentro del contexto de la API sin `ExternalEvent`.
