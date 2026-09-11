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

`dsdsaaggr.exe` **no tiene firma digital**. Eso dispara dos mecanismos distintos, y
conviene no confundirlos porque tienen solución distinta:

### SmartScreen — "Windows protegió su PC"

Aparece cuando un ejecutable sin firma y sin reputación llega marcado como descargado de
internet. Deja continuar: *Más información* → *Ejecutar de todas formas*.

Se puede evitar sin firmar, de tres maneras:

- **Usando el script** en vez del exe.
- **Evitando la marca de descarga**: si el archivo llega por carpeta de red, USB o
  `git clone` en vez de por navegador o correo, SmartScreen no se activa. También se puede
  quitar la marca a mano:
  ```powershell
  Unblock-File .\dsdsaaggr.exe
  ```
- **Firmando el exe**, que es la solución de fondo.

### Control inteligente de aplicaciones — "bloqueó esta aplicación"

Esto es otra cosa. Es una lista blanca de Windows 11: si el binario no está firmado por una
autoridad que Microsoft reconozca, **lo bloquea y no da la opción de continuar**.

**No hay ningún ajuste de compilación, manifiesto ni metadato que lo evite.** Es
exactamente su función. Las únicas salidas reales son:

1. **Usar el script** (`instalar.ps1`). El Control inteligente no filtra scripts.
2. **Firmar el ejecutable** con un certificado de firma de código.
3. Apagar el Control inteligente en ese equipo — y ojo, **es de una sola vía**: una vez
   apagado no se puede volver a encender sin reinstalar Windows. No lo recomiendo.

> En la versión anterior de estas herramientas esto no pasaba porque la instalación era un
> `.ps1`. El aviso apareció al empaquetar todo en un único `.exe`.

---

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
