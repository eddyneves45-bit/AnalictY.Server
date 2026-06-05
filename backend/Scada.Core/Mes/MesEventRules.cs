namespace Scada.Core.Mes;

public static class MesEventRules
{
    private const double ResetAcceptanceThreshold = 100;

    public static double CalculateProductionDelta(double previousValue, double currentValue)
    {
        if (currentValue >= previousValue)
        {
            return currentValue - previousValue;
        }

        // Reset real de contador normalmente volta para zero ou para valor baixo.
        // Queda para valor alto costuma ser leitura atrasada/fora de ordem e nao pode virar producao.
        if (currentValue <= ResetAcceptanceThreshold)
        {
            return currentValue;
        }

        return 0;
    }

    public static bool ShouldAcceptCounterValue(double previousValue, double currentValue)
    {
        return currentValue >= previousValue || currentValue <= ResetAcceptanceThreshold;
    }

    public static bool IsSuspiciousCounterDrop(double previousValue, double currentValue)
    {
        return currentValue < previousValue && currentValue > ResetAcceptanceThreshold;
    }

    public static string DescribeMachineStatus(int statusValue) => statusValue switch
    {
        0 => "INATIVA",
        1 => "OPERACAO",
        2 => "OCIOSA",
        3 => "MANUTENCAO",
        _ => "DESCONHECIDA"
    };

    public static bool ShouldOpenDowntime(int statusValue) => statusValue is 0 or 2 or 3;
    public static bool ShouldCloseDowntime(int statusValue) => statusValue is 1;
}
