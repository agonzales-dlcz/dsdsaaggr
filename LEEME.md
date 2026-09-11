# dsdsaaggr — herramientas para Revit 2025

Complementos propios para Autodesk Revit 2025, agrupados en la pestaña `dsdsaaggr`.
Se instalan en la carpeta del usuario, sin contraseña de administrador.

| Panel | Botón | Qué hace |
|---|---|---|
| Sectores | Leer coordenadas | Lee los suelos `sector_*` y arma el JSON de sectores y niveles |
| Sectores | Parametrizar sectores | Escribe Sector y Nivel del elemento en los parámetros que elijas |
| Vistas | Rejillas de vínculos | Oculta las rejillas de los modelos vinculados en la vista activa |
| Exportar | Exportar planos | PDF, DWG, DXF, DGN e imagen, en lote |
| Exportar | Exportar tablas | Tablas de planificación a Excel, detalladas por elemento |
| Exportar | Importar tablas | Devuelve al modelo un Excel exportado y editado |
| Guardado | Autoguardado | Guarda la local cada X minutos y sincroniza cada Y |

---

## Instalación

**Cerrá Revit antes.** Windows no deja reemplazar una DLL que Revit tiene tomada.

Hay dos formas. Las dos dejan lo mismo en
`%APPDATA%\Autodesk\Revit\Addins\2025`.

### 1. Con el script — recomendada

No dispara ningún aviso de seguridad. Funciona en cualquier equipo.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\instalar.ps1
```

Necesita la carpeta `addins\` al lado del script. Para desinstalar, agregá `-Desinstalar`.

### 2. Con el ejecutable

Doble clic en `dsdsaaggr.exe`. Más cómodo, pero **puede que Windows lo bloquee**: leé la
sección siguiente.

---

## Por qué Windows desconfía del .exe

`dsdsaaggr.exe` **no tiene firma digital**. Sin firma, Windows lo juzga por lo que hace, y
ahí importa mucho qué APIs del sistema toca el binario.

### El caso que ya nos pasó

Durante un tiempo el instalador funcionó en todas las máquinas. De pronto empezó a saltar
SmartScreen en unas y el Control inteligente de aplicaciones lo bloqueó de plano en otras.

La causa no era el nombre del archivo, ni la marca de descarga, ni el tamaño. Era **una
llamada a `user32.dll`**: el autoguardado escribía un aviso en la barra de estado de Revit
usando `FindWindowEx` + `SetWindowText`. Buscar la ventana de otra aplicación y escribirle
texto es, sin firma que lo respalde, indistinguible de un inyector de interfaz.

Se comprobó comparando los binarios:

```
dsdsaaggr.exe  (instalaba bien)   373 KB   APIs de user32: ninguna
dsdsaaggr.exe  (instalaba bien)   397 KB   APIs de user32: ninguna
dsdsaaggr.exe  (bloqueado)        499 KB   FindWindowEx, SetWindowText, UpdateWindow
```

Se quitó esa función. El autoguardado ahora corre en silencio y deja constancia en su
bitácora. **Regla para lo que venga: mientras el ejecutable no esté firmado, ningún
complemento debe llamar a APIs de Windows de manipulación de ventanas, procesos o memoria.**

### Los dos mecanismos, si vuelve a pasar

**SmartScreen** — "Windows protegió su PC". Deja continuar: *Más información* → *Ejecutar de
todas formas*. Se dispara con binarios sin firma, sin reputación, y marcados como
descargados de internet. Para quitar esa marca:

```powershell
Unblock-File .\dsdsaaggr.exe
```

**Control inteligente de aplicaciones** — "bloqueó esta aplicación". Es una lista blanca de
Windows 11 y **no da la opción de continuar**. No hay ajuste de compilación que lo evite.
Las salidas son: usar `instalar.ps1`, firmar el ejecutable, o apagarlo en ese equipo — y ojo,
apagarlo es de una sola vía, no se puede volver a encender sin reinstalar Windows.

## Firmar el ejecutable

Si conseguís un certificado de firma de código, el build lo usa directo:

```powershell
.\compilar-todo.ps1 -Pfx C:\ruta\certificado.pfx -Clave 'la contraseña'
```

Necesita `signtool.exe` (viene con el Windows SDK). El `.pfx` **nunca** va al repositorio:
está en el `.gitignore`.

Sobre qué certificado:

| Opción | Costo aprox. | SmartScreen |
|---|---|---|
| **Azure Trusted Signing** | ~10 USD/mes | Lo más barato y práctico. Pide identidad verificada |
| Certificado OV | 200–400 USD/año | Reduce el aviso, pero igual hay que construir reputación |
| Certificado EV | 300–600 USD/año | Reputación inmediata. Requiere token físico o HSM |
| Autofirmado | gratis | **No sirve.** Windows no confía en la raíz, y confiarla pide admin |

---

## Compilar

Requiere el SDK de .NET 8 y Revit 2025 instalado (para las referencias a la API).

```powershell
.\compilar-todo.ps1
```

Deja en `instalador\publicado\` las dos formas de entrega: `dsdsaaggr.exe` por un lado, y
`instalar.ps1` con la carpeta `addins\` por el otro.

---

## Estructura

```
sectores/          Leer coordenadas, Parametrizar sectores
vistas/            Rejillas de vínculos
guardado/          Autoguardado
exportar_planos/   Exportar planos
exportar_tablas/   Exportar tablas, Importar tablas
instalador/        El .exe y el instalar.ps1
compilar-todo.ps1  Compila todo y arma la entrega
```

Los iconos de la cinta se dibujan por código (`*/Comun/Iconos.cs`), sin archivos PNG
sueltos. El archivo es idéntico en los cinco proyectos, solo cambia el namespace.
