// Escribe un renglon en la barra de estado de Revit, la de abajo a la izquierda.
//
// No hay API para esto. La barra es un control Win32 comun colgado de la ventana principal,
// asi que se la busca por su clase y se le manda el texto. Es la unica forma de avisar algo
// sin abrir una ventana encima del trabajo.
//
// Todo el archivo es "si no se puede, no pasa nada": si Revit cambia el control o el texto
// no entra, la funcion no hace nada y el autoguardado sigue igual. Un aviso nunca puede
// romper el guardado.
//
// Revit pisa este texto en cuanto hace cualquier cosa suya. Es a proposito: es un aviso de
// paso, no un cartel. Lo que queda para consultar despues es la bitacora.

using System;
using System.Runtime.InteropServices;

namespace Riga.Guardado.Nucleo
{
    internal static class BarraEstado
    {
        const string CLASE = "msctls_statusbar32";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr padre, IntPtr despuesDe, string clase, string titulo);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowText(IntPtr ventana, string texto);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UpdateWindow(IntPtr ventana);

        static IntPtr _barra = IntPtr.Zero;
        static bool _buscada;

        public static void Decir(string texto)
        {
            try
            {
                var h = Barra();
                if (h == IntPtr.Zero) return;

                SetWindowText(h, texto ?? "");

                // Sin esto el texto no se ve hasta que Revit vuelva a pintar, y justo despues
                // de esta llamada Revit se queda bloqueado sincronizando. UpdateWindow fuerza
                // el repintado ya, que es lo que hace util el aviso de "voy a sincronizar".
                UpdateWindow(h);
            }
            catch { }
        }

        static IntPtr Barra()
        {
            if (_buscada) return _barra;
            _buscada = true;
            try
            {
                var principal = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (principal != IntPtr.Zero)
                    _barra = FindWindowEx(principal, IntPtr.Zero, CLASE, null);
            }
            catch { _barra = IntPtr.Zero; }
            return _barra;
        }
    }
}
