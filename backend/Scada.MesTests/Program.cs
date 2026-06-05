using Scada.Core.Mes;

Run("delta normal", 5, MesEventRules.CalculateProductionDelta(10, 15));
Run("reset para zero", 0, MesEventRules.CalculateProductionDelta(20, 0));
Run("contador resetado", 3, MesEventRules.CalculateProductionDelta(20, 3));
Run("reset aceito ate limite baixo", 100, MesEventRules.CalculateProductionDelta(5000, 100));
Run("queda acima do limite baixo ignorada", 0, MesEventRules.CalculateProductionDelta(5000, 101));
Run("queda alta nao e reset", 0, MesEventRules.CalculateProductionDelta(8780, 8647));
Run("oscilacao alta nao gera producao", 0, MesEventRules.CalculateProductionDelta(4823, 4822));
Run("aceita reset para zero como nova base", true, MesEventRules.ShouldAcceptCounterValue(20, 0));
Run("aceita reset baixo como nova base", true, MesEventRules.ShouldAcceptCounterValue(5000, 100));
Run("nao aceita queda alta como nova base", false, MesEventRules.ShouldAcceptCounterValue(8780, 8647));
Run("detecta queda alta suspeita", true, MesEventRules.IsSuspiciousCounterDrop(8780, 8647));
Run("reset para zero nao e queda suspeita", false, MesEventRules.IsSuspiciousCounterDrop(20, 0));
Run("status 0", "INATIVA", MesEventRules.DescribeMachineStatus(0));
Run("status 1", "OPERACAO", MesEventRules.DescribeMachineStatus(1));
Run("status 2", "OCIOSA", MesEventRules.DescribeMachineStatus(2));
Run("status 3", "MANUTENCAO", MesEventRules.DescribeMachineStatus(3));
Run("abre parada em ociosa", true, MesEventRules.ShouldOpenDowntime(2));
Run("abre parada em manutencao", true, MesEventRules.ShouldOpenDowntime(3));
Run("nao abre parada em operacao", false, MesEventRules.ShouldOpenDowntime(1));
Run("abre parada em inativa", true, MesEventRules.ShouldOpenDowntime(0));
Run("fecha parada em operacao", true, MesEventRules.ShouldCloseDowntime(1));
Run("nao fecha parada em inativa", false, MesEventRules.ShouldCloseDowntime(0));

Console.WriteLine("Todos os testes MES passaram.");

static void Run<T>(string name, T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Falha em {name}: esperado={expected}, atual={actual}");
    }
}
