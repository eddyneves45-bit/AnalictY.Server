CREATE TABLE IF NOT EXISTS turnos (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    codigo VARCHAR(32) NOT NULL,
    nome VARCHAR(128) NOT NULL,
    hora_inicio TIME NOT NULL,
    hora_fim TIME NOT NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uk_turnos_codigo (codigo)
);

CREATE TABLE IF NOT EXISTS historico_tags (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_tag INT NULL,
    nome_tag VARCHAR(255) NOT NULL,
    id_maquina VARCHAR(64) NULL,
    valor_texto TEXT NULL,
    qualidade VARCHAR(64) NOT NULL DEFAULT 'UNKNOWN',
    registrado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_historico_tags_tag_tempo (nome_tag, registrado_em),
    INDEX idx_historico_tags_maquina_tempo (id_maquina, registrado_em)
);

CREATE TABLE IF NOT EXISTS eventos_status_maquina (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    status_maquina TINYINT NOT NULL,
    descricao_status VARCHAR(64) NOT NULL,
    inicio_em DATETIME(6) NOT NULL,
    fim_em DATETIME(6) NULL,
    duracao_segundos DOUBLE NULL,
    id_tag_origem INT NULL,
    qualidade VARCHAR(64) NOT NULL DEFAULT 'UNKNOWN',
    INDEX idx_eventos_status_maquina_inicio (id_maquina, inicio_em),
    INDEX idx_eventos_status_status_inicio (status_maquina, inicio_em)
);

CREATE TABLE IF NOT EXISTS eventos_producao (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    id_tag_origem INT NULL,
    valor_anterior DOUBLE NULL,
    valor_atual DOUBLE NULL,
    quantidade DOUBLE NOT NULL DEFAULT 0,
    ocorrido_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_eventos_producao_maquina_tempo (id_maquina, ocorrido_em)
);

CREATE TABLE IF NOT EXISTS eventos_perda (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    id_tag_origem INT NULL,
    valor_anterior DOUBLE NULL,
    valor_atual DOUBLE NULL,
    quantidade DOUBLE NOT NULL DEFAULT 0,
    ocorrido_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_eventos_perda_maquina_tempo (id_maquina, ocorrido_em)
);

CREATE TABLE IF NOT EXISTS motivos_parada (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    codigo VARCHAR(64) NOT NULL,
    descricao VARCHAR(255) NOT NULL,
    categoria VARCHAR(128) NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uk_motivos_parada_codigo (codigo)
);

CREATE TABLE IF NOT EXISTS eventos_parada (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    inicio_em DATETIME(6) NOT NULL,
    fim_em DATETIME(6) NULL,
    duracao_segundos DOUBLE NULL,
    status_origem TINYINT NULL,
    id_motivo_parada BIGINT NULL,
    motivo_informado VARCHAR(255) NULL,
    observacao TEXT NULL,
    reconhecida_por VARCHAR(255) NULL,
    reconhecida_em DATETIME(6) NULL,
    INDEX idx_eventos_parada_maquina_inicio (id_maquina, inicio_em),
    CONSTRAINT fk_eventos_parada_motivo
        FOREIGN KEY (id_motivo_parada) REFERENCES motivos_parada(id)
);

CREATE TABLE IF NOT EXISTS alertas (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tipo_alerta VARCHAR(64) NOT NULL,
    severidade VARCHAR(64) NOT NULL,
    titulo VARCHAR(255) NOT NULL,
    mensagem TEXT NOT NULL,
    id_maquina VARCHAR(64) NULL,
    reconhecido BOOLEAN NOT NULL DEFAULT FALSE,
    reconhecido_por VARCHAR(255) NULL,
    reconhecido_em DATETIME(6) NULL,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_alertas_maquina_criado (id_maquina, criado_em)
);

CREATE TABLE IF NOT EXISTS agendamentos_relatorio (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(255) NOT NULL,
    tipo_relatorio VARCHAR(64) NOT NULL,
    parametros JSON NULL,
    formato VARCHAR(32) NOT NULL DEFAULT 'xlsx',
    periodicidade VARCHAR(64) NOT NULL,
    horario TIME NULL,
    destino VARCHAR(255) NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    proxima_execucao_em DATETIME(6) NULL,
    ultima_execucao_em DATETIME(6) NULL,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
);

CREATE TABLE IF NOT EXISTS execucoes_exportacao (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_agendamento BIGINT NULL,
    tipo_relatorio VARCHAR(64) NOT NULL,
    parametros JSON NULL,
    formato VARCHAR(32) NOT NULL,
    caminho_arquivo VARCHAR(512) NULL,
    status_execucao VARCHAR(64) NOT NULL,
    mensagem TEXT NULL,
    iniciado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    finalizado_em DATETIME(6) NULL,
    CONSTRAINT fk_execucoes_exportacao_agendamento
        FOREIGN KEY (id_agendamento) REFERENCES agendamentos_relatorio(id)
);

CREATE TABLE IF NOT EXISTS metas_maquina (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    meta_producao_dia DOUBLE NULL,
    meta_producao_hora DOUBLE NULL,
    tempo_ciclo_ideal_segundos DOUBLE NULL,
    vigente_de DATETIME(6) NOT NULL,
    vigente_ate DATETIME(6) NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_metas_maquina_vigencia (id_maquina, vigente_de, vigente_ate),
    INDEX idx_metas_maquina_ativa (id_maquina, ativo)
);

CREATE TABLE IF NOT EXISTS resumos_producao_hora (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    data_referencia DATE NOT NULL,
    hora_referencia TINYINT NOT NULL,
    quantidade_produzida DOUBLE NOT NULL DEFAULT 0,
    quantidade_perdida DOUBLE NOT NULL DEFAULT 0,
    quantidade_boa DOUBLE NOT NULL DEFAULT 0,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uk_resumos_producao_hora_maquina_data_hora (id_maquina, data_referencia, hora_referencia),
    INDEX idx_resumos_producao_hora_data (data_referencia, hora_referencia)
);

CREATE TABLE IF NOT EXISTS resumos_producao_turno (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    data_referencia DATE NOT NULL,
    id_turno BIGINT NOT NULL,
    quantidade_produzida DOUBLE NOT NULL DEFAULT 0,
    quantidade_perdida DOUBLE NOT NULL DEFAULT 0,
    quantidade_boa DOUBLE NOT NULL DEFAULT 0,
    tempo_producao_segundos DOUBLE NOT NULL DEFAULT 0,
    tempo_ociosa_segundos DOUBLE NOT NULL DEFAULT 0,
    tempo_manutencao_segundos DOUBLE NOT NULL DEFAULT 0,
    tempo_inativa_segundos DOUBLE NOT NULL DEFAULT 0,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    atualizado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uk_resumos_producao_turno_maquina_data_turno (id_maquina, data_referencia, id_turno),
    INDEX idx_resumos_producao_turno_data (data_referencia, id_turno),
    CONSTRAINT fk_resumos_producao_turno_turno
        FOREIGN KEY (id_turno) REFERENCES turnos(id)
);

CREATE TABLE IF NOT EXISTS resumos_eficiencia_maquina (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    id_maquina VARCHAR(64) NOT NULL,
    inicio_periodo DATETIME(6) NOT NULL,
    fim_periodo DATETIME(6) NOT NULL,
    disponibilidade_percentual DOUBLE NOT NULL DEFAULT 0,
    performance_percentual DOUBLE NOT NULL DEFAULT 0,
    qualidade_percentual DOUBLE NOT NULL DEFAULT 0,
    oee_percentual DOUBLE NOT NULL DEFAULT 0,
    criado_em DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_resumos_eficiencia_maquina_periodo (id_maquina, inicio_periodo, fim_periodo)
);
