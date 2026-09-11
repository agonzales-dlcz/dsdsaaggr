using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Guardado.Comun
{
    // La configuracion se puede mirar y cambiar sin ningun proyecto abierto, asi que este
    // boton nunca se apaga. Revit apaga los comandos externos sin documento si no le dicen
    // lo contrario.
    public class Siempre : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet categorias) { return true; }
    }
}
