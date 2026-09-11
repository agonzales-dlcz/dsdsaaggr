using Autodesk.Revit.DB;

namespace Riga.Sectores.Comun
{
    // Descarta las advertencias del lote para que no interrumpan la transaccion.
    internal sealed class Silencio : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (var f in fa.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }

        public static void Aplicar(Transaction tx)
        {
            var op = tx.GetFailureHandlingOptions();
            op.SetFailuresPreprocessor(new Silencio());
            tx.SetFailureHandlingOptions(op);
        }
    }
}
