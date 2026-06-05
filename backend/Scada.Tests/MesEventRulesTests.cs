using Scada.Core.Mes;

namespace Scada.Tests;

public static class MesEventRulesTests
{
    public static void Run()
    {
        AssertEqual(5, MesEventRules.CalculateProductionDelta(10, 15), "delta normal");
        AssertEqual(0, MesEventRules.CalculateProductionDelta(20, 0), "reset para zero");
        AssertEqual(3, MesEventRules.CalculateProductionDelta(20, 3), "contador resetado");
        AssertEqual(100, MesEventRules.CalculateProductionDelta(5000, 100), "reset aceito ate limite baixo");
        AssertEqual(0, MesEventRules.CalculateProductionDelta(5000, 101), "queda acima do limite baixo ignorada");
        AssertEqual(0, MesEventRules.CalculateProductionDelta(8780, 8647), "queda alta nao e reset");
        AssertEqual(0, MesEventRules.CalculateProductionDelta(4823, 4822), "oscilacao alta nao gera producao");
        AssertEqual(true, MesEventRules.ShouldAcceptCounterValue(20, 0), "aceita reset para zero como nova base");
        AssertEqual(true, MesEventRules.ShouldAcceptCounterValue(5000, 100), "aceita reset baixo como nova base");
        AssertEqual(false, MesEventRules.ShouldAcceptCounterValue(8780, 8647), "nao aceita queda alta como nova base");
        AssertEqual(true, MesEventRules.IsSuspiciousCounterDrop(8780, 8647), "detecta queda alta suspeita");
        AssertEqual(false, MesEventRules.IsSuspiciousCounterDrop(20, 0), "reset para zero nao e queda suspeita");
        AssertEqual("INATIVA", MesEventRules.DescribeMachineStatus(0), "status 0");
        AssertEqual("OPERACAO", MesEventRules.DescribeMachineStatus(1), "status 1");
        AssertEqual("OCIOSA", MesEventRules.DescribeMachineStatus(2), "status 2");
        AssertEqual("MANUTENCAO", MesEventRules.DescribeMachineStatus(3), "status 3");
        AssertEqual(true, MesEventRules.ShouldOpenDowntime(2), "abre parada em ociosa");
        AssertEqual(true, MesEventRules.ShouldOpenDowntime(3), "abre parada em manutencao");
        AssertEqual(false, MesEventRules.ShouldOpenDowntime(1), "nao abre parada em operacao");
        AssertEqual(true, MesEventRules.ShouldOpenDowntime(0), "abre parada em inativa");
        AssertEqual(true, MesEventRules.ShouldCloseDowntime(1), "fecha parada em operacao");
        AssertEqual(false, MesEventRules.ShouldCloseDowntime(0), "nao fecha parada em inativa");
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Falha em {name}: esperado={expected}, atual={actual}");
        }
    }
}
