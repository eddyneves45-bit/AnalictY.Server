using System.Globalization;
using System.Text;
using Scada.Api.Services;

public static class MetricsEndpoints
{
    public static WebApplication MapMetricsEndpoints(this WebApplication app)
    {
        app.MapGet("/metrics", async (
            ITagValueQueue queue,
            IIndustrialHeartbeatService heartbeatService,
            IIndustrialMetricsService metricsService,
            IMySqlPersistenceQueue mySqlPersistenceQueue,
            CancellationToken cancellationToken) =>
        {
            var heartbeat = heartbeatService.GetMetricsSnapshot();
            var processing = metricsService.GetSnapshot();
            var mySql = await mySqlPersistenceQueue.GetHealthAsync(cancellationToken);
            var builder = new StringBuilder();

            AppendMetric(builder, "scada_tag_queue_depth", "Quantidade aproximada de mensagens aguardando processamento.", queue.ApproximateCount);
            AppendMetric(builder, "scada_tag_queue_enqueued_total", "Total de mensagens aceitas pela fila.", queue.EnqueuedCount);
            AppendMetric(builder, "scada_tag_queue_dequeued_total", "Total de mensagens retiradas da fila.", queue.DequeuedCount);
            AppendMetric(builder, "scada_tag_queue_dropped_total", "Total de mensagens descartadas pela fila.", queue.DroppedCount);
            AppendMetric(builder, "scada_tag_messages_processed_total", "Total de mensagens de TAG processadas com sucesso.", processing.ProcessedMessages);
            AppendMetric(builder, "scada_tag_messages_failed_total", "Total de falhas no processamento de mensagens de TAG.", processing.FailedMessages);
            AppendMetric(builder, "scada_tag_processing_delay_seconds", "Atraso da ultima mensagem processada.", processing.LastProcessingDelaySeconds);
            AppendMetric(builder, "scada_tag_processing_delay_max_seconds", "Maior atraso observado no processamento.", processing.MaxProcessingDelaySeconds);

            AppendMetric(builder, "scada_tags_total", "Total de TAGs registradas no heartbeat industrial.", heartbeat.TagsTotal);
            AppendMetric(builder, "scada_tags_online", "Total de TAGs online.", heartbeat.TagsOnline);
            AppendMetric(builder, "scada_tags_stale", "Total de TAGs sem atualizacao recente.", heartbeat.TagsStale);
            AppendMetric(builder, "scada_tags_bad", "Total de TAGs com qualidade ruim.", heartbeat.TagsBad);
            AppendMetric(builder, "scada_tags_never_received", "Total de TAGs ainda sem leitura recebida.", heartbeat.TagsNeverReceived);
            AppendMetric(builder, "scada_connections_total", "Total de conexoes observadas pelo heartbeat industrial.", heartbeat.ConnectionsTotal);
            AppendMetric(builder, "scada_connections_online", "Total de conexoes com mensagem recente.", heartbeat.ConnectionsOnline);
            AppendMetric(builder, "scada_connections_stale", "Total de conexoes sem mensagem recente.", heartbeat.ConnectionsStale);
            AppendMetric(builder, "scada_connections_offline", "Total de conexoes registradas que ainda nao receberam mensagem.", heartbeat.ConnectionsOffline);
            AppendMetric(builder, "scada_mysql_reachable", "Indica se o banco MySQL primario responde.", mySql.DatabaseReachable ? 1 : 0);
            AppendMetric(builder, "scada_mysql_outbox_pending", "Total de envelopes aguardando persistencia no MySQL.", mySql.PendingCount);
            AppendMetric(builder, "scada_mysql_outbox_failed", "Total de envelopes pendentes que ja tiveram falha.", mySql.FailedCount);

            return Results.Text(builder.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        })
        .WithName("GetPrometheusMetrics");

        return app;
    }

    private static void AppendMetric(StringBuilder builder, string name, string help, double value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" gauge");
        builder.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendMetric(StringBuilder builder, string name, string help, long value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(name.EndsWith("_total", StringComparison.Ordinal) ? " counter" : " gauge");
        builder.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }
}
