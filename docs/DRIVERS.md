# Documentacao dos Drivers

## Visao Geral

Todos os drivers implementam a interface `IDriver` e seguem o mesmo padrao de uso.

## Driver MQTT

### Caracteristicas
- Suporte a MQTT 3.1.1 e 5.0
- TLS/SSL com certificados
- QoS configuravel (0, 1, 2)
- Last Will Testament (LWT)
- Auto-reconexao
- Subscricao em topicos

### Configuracao

```csharp
var config = new MqttConnectionConfig
{
    Host = "localhost",
    Port = 1883,
    ClientId = "Scada_Client",
    Username = "user",
    Password = "<senha-mqtt>",
    
    // TLS
    UseTls = true,
    ClientCertificatePath = @"C:\certs\client.pfx",
    ClientCertificatePassword = "<senha-certificado>",
    CaCertificatePath = @"C:\certs\ca.crt",
    AllowUntrustedCertificates = false,
    
    // LWT
    UseLastWill = true,
    LastWillTopic = "scada/status",
    LastWillPayload = "offline",
    
    // Auto-reconnect
    AutoReconnect = true,
    ReconnectDelayMs = 5000
};
```

### Uso

```csharp
using var driver = new MqttDriver(config, logger);

// Conectar
await driver.ConnectAsync();

// Escrever
await driver.WriteTagAsync("scada/machine/temp", 25.5, DataType.Double);

// Ler
var tag = await driver.ReadTagAsync("scada/machine/temp", DataType.Double);

// Eventos
driver.TagValueChanged += (sender, e) => {
    Console.WriteLine($"{e.Address} = {e.Value}");
};

// Desconectar
await driver.DisconnectAsync();
```

### Formato de Endereco
- Topico MQTT: `"scada/machine/temperature"`
- Wildcards: `"scada/#"` (todos os topicos sob scada)

### Implementacao correta para AWS IoT com mutual TLS

Esta e a forma validada para conectar no AWS IoT usando MQTTnet no Windows/.NET 8.

#### Estrutura dos certificados

```text
backend/certs/
â”œâ”€â”€ AmazonRootCA1.pem
â”œâ”€â”€ device.pem.crt
â”œâ”€â”€ private.pem.key
â””â”€â”€ aws-device.pfx
```

No AWS IoT, o cliente usa mutual TLS. Portanto, o certificado precisa estar associado Ã  chave privada. Em .NET/Windows, a forma mais estavel e usar um `.pfx`.

Quando os arquivos estiverem em `backend/certs/`, a configuracao MQTT pode guardar somente o nome do arquivo. O driver tambem aceita caminhos absolutos do Windows e tenta resolver arquivos relativos a partir da pasta atual do backend, `backend/certs/` e `C:\certs`.

#### Conversao correta no .NET 8

Se os arquivos estiverem separados como `.crt` e `.key`, crie o PFX com:

```csharp
var pemCertificate = X509Certificate2.CreateFromPemFile(clientCertPath, clientKeyPath);
var pfxBytes = pemCertificate.Export(X509ContentType.Pfx, "");
File.WriteAllBytes(pfxPath, pfxBytes);
```

Depois carregue o PFX assim:

```csharp
var certificate = new X509Certificate2(
    pfxPath,
    "",
    X509KeyStorageFlags.UserKeySet |
    X509KeyStorageFlags.PersistKeySet |
    X509KeyStorageFlags.Exportable
);
```

#### Flags obrigatorias no Windows

Use:

```csharp
X509KeyStorageFlags.UserKeySet |
X509KeyStorageFlags.PersistKeySet |
X509KeyStorageFlags.Exportable
```

nao usar:

```csharp
X509KeyStorageFlags.MachineKeySet
X509KeyStorageFlags.EphemeralKeySet
```

Motivo:
- `MachineKeySet` pode causar `As credenciais fornecidas para o pacote nao foram reconhecidas`.
- `EphemeralKeySet` pode causar `platform does not support ephemeral keys` com `SslStream` no Windows.

#### MQTTnet TLS correto

```csharp
var tlsOptions = new MqttClientTlsOptionsBuilder()
    .UseTls()
    .WithClientCertificates(new List<X509Certificate2> { certificate })
    .WithSslProtocols(SslProtocols.Tls12)
    .WithCertificateValidationHandler(context => true)
    .Build();

var options = new MqttClientOptionsBuilder()
    .WithClientId("iotconsole-f7cc8a61-f2b5-4878-9d0a-46526f9151a8")
    .WithTcpServer("a2j2mrlwb08rz9-ats.iot.sa-east-1.amazonaws.com", 8883)
    .WithTlsOptions(tlsOptions)
    .WithProtocolVersion(MqttProtocolVersion.V500)
    .WithCleanSession(true)
    .Build();

await mqttClient.ConnectAsync(options);
```

#### Regras para nao quebrar a conexao

- Sempre usar porta `8883` para AWS IoT com mutual TLS.
- Sempre usar `SslProtocols.Tls12`.
- Sempre validar que `certificate.HasPrivateKey == true`.
- Preferir MQTT `V500` quando o broker/conta suportar.
- nao usar `ServicePointManager.SecurityProtocol`.
- nao carregar apenas o `.crt` sem a `.key`.
- nao depender de PEM separado em runtime industrial; gerar/carregar PFX.

#### Evidencia validada

Teste executado com sucesso em `backend/MqttSimpleTest`:

```text
PFX criado: CN=AWS IoT Certificate
HasPrivateKey: True
Conectado com sucesso!
Subscrevendo ao topico: scada/test
Mensagem publicada
Mensagem recebida no topico scada/test
Teste concluido com sucesso!
```

## Driver OPC-UA

### Caracteristicas
- OPC-UA 1.04
- Seguranca com certificados X.509
- autenticacao usuario/senha ou anÃ´nima
- Subscricao com sampling configuravel
- Auto-reconexao
- Suporte a nos complexos

### Configuracao

```csharp
var config = new OpcUaConnectionConfig
{
    EndpointUrl = "opc.tcp://localhost:4840",
    ApplicationName = "SCADA Client",
    Username = "admin",
    Password = "<senha-opcua>",
    UseSecurity = true,
    SessionTimeout = 60000,
    KeepAliveInterval = 10000,
    
    // Subscricao
    AutoSubscribe = true,
    PublishingInterval = 1000,
    SamplingInterval = 500
};
```

### Uso

```csharp
using var driver = new OpcUaDriver(config, logger);

await driver.ConnectAsync();

// Ler no
var tag = await driver.ReadTagAsync("ns=2;s=Machine.Temperature", DataType.Double);

// Escrever
await driver.WriteTagAsync("ns=2;s=Machine.Setpoint", 75.0, DataType.Double);

// Subscrever
await driver.SubscribeToTagAsync("ns=2;s=Machine.Temperature", DataType.Double);

// Eventos
driver.TagValueChanged += (sender, e) => {
    Console.WriteLine($"{e.Address} = {e.Value}");
};
```

### Formato de Endereco
- no por ID: `"i=84"` (Root Folder)
- no por string: `"ns=2;s=Machine.Temperature"`
- no por GUID: `"g=..."`
- no por path: `"ns=2;s=MyFolder.MyVariable"`

### Arrays OPC UA e selecao de elemento

Quando o servidor OPC UA publicar um array e o MES precisar de apenas um item, cadastre a TAG com o sufixo `::indice`.

Exemplo real validado:

```text
NodeId no servidor: ns=2;s=PJ-08.Tags.PRODUCAO.STATUS
Valor publicado:    {2,0,0,0,0,0,0,0,0,0,0,0}  UInt32[]
TAG cadastrada:     ns=2;s=PJ-08.Tags.PRODUCAO.STATUS::0
Valor usado no MES: 2
Significado:        OCIOSA
Tipo da TAG:        Int32
```

O backend usa o trecho antes de `::` como `NodeId` real e extrai o item indicado antes de enviar o valor ao runtime MES. Para o primeiro elemento, use `::0`.
### Implementacao correta para OPC-UA

Esta e a forma validada para conexao OPC-UA usando `OPCFoundation.NetStandard.Opc.Ua.Client` no .NET 8.

#### Endpoint validado

```text
opc.tcp://DESKTOP-EDDY:4840/G01
```

#### Configuracao obrigatoria

O `ApplicationConfiguration` precisa incluir `ClientConfiguration`. Sem isso, a sessao falha com:

```text
The client configuration does not specify the ClientConfiguration.
```

Configuracao correta:

```csharp
var config = new ApplicationConfiguration()
{
    ApplicationName = "SCADA OPC-UA Client",
    ApplicationType = ApplicationType.Client,
    SecurityConfiguration = new SecurityConfiguration
    {
        ApplicationCertificate = new CertificateIdentifier
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(Environment.CurrentDirectory, "pki", "own"),
            SubjectName = "SCADA OPC-UA Client"
        },
        TrustedPeerCertificates = new CertificateTrustList(),
        TrustedIssuerCertificates = new CertificateTrustList()
    },
    ClientConfiguration = new ClientConfiguration
    {
        DefaultSessionTimeout = 60000
    }
};
```

#### Criacao correta da sessao

```csharp
var endpoint = CoreClientUtils.SelectEndpoint(endpointUrl, false, 10000);

var session = await Session.Create(
    config,
    new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create()),
    false,
    "SCADA OPC-UA Client",
    60000,
    new UserIdentity(new AnonymousIdentityToken()),
    null
);
```

#### Leitura correta de nos

Para ler metadados de um no:

```csharp
var node = session.ReadNode(ObjectIds.ObjectsFolder);
Console.WriteLine($"{node.DisplayName.Text} - {node.NodeClass}");
```

Para ler valor de variavel:

```csharp
var value = session.ReadValue(new NodeId(2258));
Console.WriteLine(value.WrappedValue.Value);
```

#### Atencao ao RootFolder

nao usar `ReadValue` em nos que nao sao variaveis, como `RootFolder` (`i=84`). Isso causa:

```text
BadAttributeIdInvalid
```

Use `ReadNode` para objetos/pastas e `ReadValue` somente para variaveis.

#### nos basicos validados

```csharp
var nodesToTest = new[]
{
    ObjectIds.ObjectsFolder,
    ObjectTypes.BaseObjectType,
    VariableTypeIds.BaseVariableType
};
```

#### Browse real da arvore OPC UA

Implementacao validada para navegacao na arvore OPC UA usando `Session.Browse()` com a assinatura correta do SDK.

**Endpoint da API:**
```text
GET /api/config/opcua/browse?node_id={node_id}
```

**Implementacao correta:**

```csharp
// Configurar descricao do browse
var browseDescription = new Opc.Ua.BrowseDescription
{
    NodeId = nodeIdObj,
    BrowseDirection = Opc.Ua.BrowseDirection.Forward,
    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
    IncludeSubtypes = true,
    NodeClassMask = (uint)Opc.Ua.NodeClass.Object | (uint)Opc.Ua.NodeClass.Variable | (uint)Opc.Ua.NodeClass.Method | (uint)Opc.Ua.NodeClass.ObjectType | (uint)Opc.Ua.NodeClass.VariableType | (uint)Opc.Ua.NodeClass.ReferenceType | (uint)Opc.Ua.NodeClass.DataType,
    ResultMask = (uint)Opc.Ua.BrowseResultMask.All
};

// Executar browse usando Session.Browse() com assinatura correta
Opc.Ua.BrowseResultCollection results;
Opc.Ua.DiagnosticInfoCollection diagnosticInfos;

session.Browse(
    null,
    null,
    0,
    new Opc.Ua.BrowseDescriptionCollection { browseDescription },
    out results,
    out diagnosticInfos
);

// Iterar sobre os nos encontrados
foreach (var reference in results[0].References)
{
    var referenceNodeId = (Opc.Ua.NodeId)reference.NodeId;
    var node = session.ReadNode(referenceNodeId);
    
    // Verificar se tem filhos
    var hasChildrenDescription = new Opc.Ua.BrowseDescription
    {
        NodeId = referenceNodeId,
        BrowseDirection = Opc.Ua.BrowseDirection.Forward,
        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
        IncludeSubtypes = true,
        NodeClassMask = (uint)Opc.Ua.NodeClass.Object | (uint)Opc.Ua.NodeClass.Variable,
        ResultMask = (uint)Opc.Ua.BrowseResultMask.None
    };
    
    Opc.Ua.BrowseResultCollection hasChildrenResults;
    Opc.Ua.DiagnosticInfoCollection hasChildrenDiagnosticInfos;
    
    session.Browse(
        null,
        null,
        0,
        new Opc.Ua.BrowseDescriptionCollection { hasChildrenDescription },
        out hasChildrenResults,
        out hasChildrenDiagnosticInfos
    );
    
    // Retornar dados do no
    var nodeData = new
    {
        node_id = referenceNodeId.ToString(),
        browse_name = reference.DisplayName.Text,
        display_name = reference.DisplayName.Text,
        node_class = node.NodeClass.ToString(),
        has_children = hasChildrenResults[0].References.Count > 0
    };
}
```

**SDK utilizado:**
```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Client" Version="1.5.374.118" />
```

**Regras obrigatorias:**
- Usar `Session.Browse()` com `BrowseDescriptionCollection` (nao usar BrowseAsync com assinaturas incorretas)
- Usar `BrowseResultCollection` e `DiagnosticInfoCollection` com out parameters
- Converter `ExpandedNodeId` para `NodeId` usando cast: `(Opc.Ua.NodeId)reference.NodeId`
- NÃƒO usar classes inexistentes como `BrowseArguments`
- NÃƒO usar `byte.Empty` (nao existe) - usar `Array.Empty<byte>()` ou `new byte[0]` se necessario
- Usar `ReadNode` para ler metadados de nos
- Usar `ReadValue` apenas para variaveis

**Lazy loading:**
O browse e executado por no sob demanda (lazy loading). O frontend solicita os filhos de um no especifico usando o `node_id` como parÃ¢metro.

Tambem foi validado:

```csharp
var currentTimeNodeId = new NodeId(2258); // ServerStatus.CurrentTime
var currentTime = session.ReadValue(currentTimeNodeId);
```

#### Evidencia validada

Teste executado com sucesso em `backend/OpcUaSimpleTest`:

```text
Endpoint: opc.tcp://DESKTOP-EDDY:4840/G01
Conectado com sucesso!
no i=85: Objects - Object
no i=58: BaseObjectType - ObjectType
no i=62: BaseVariableType - VariableType
Server CurrentTime: 05/11/2026 22:05:57
Teste concluido com sucesso!
```

#### Regras para nao quebrar a conexao

- Sempre definir `ClientConfiguration`.
- Usar `CoreClientUtils.SelectEndpoint`.
- Usar `Session.Create` diretamente.
- Usar `ReadNode` para objetos/pastas.
- Usar `ReadValue` somente para variaveis.
- nao tentar ler valor do `RootFolder` (`i=84`).
- Manter `SessionTimeout` explicito.
- Manter pasta de certificados em `pki/own`.

#### Runtime correto, certificados e diagnostico

Para runtime industrial, use `Subscription` + `MonitoredItem` em vez de polling por `Read()`. Em um caso real, o browse da aplicacao estava saudavel e o UaExpert mostrava `Good`, mas a leitura direta retornava `BadNoCommunication`; a troca para assinatura resolveu a aquisicao.

O driver aceita os dois cenarios:

```text
Sem seguranca:
SecurityPolicy = None
SecurityMode   = None
certificate    = vazio
private_key    = vazio
```

```text
Com seguranca:
SecurityPolicy = Basic256Sha256 (ou politica suportada pelo servidor)
SecurityMode   = Sign ou SignAndEncrypt
certificate    = .pfx/.p12 ou .pem/.crt
private_key    = obrigatoria quando o certificado for PEM/CRT
```

Regras:
- `.pfx` e `.p12` podem carregar a chave privada embutida.
- `.pem` e `.crt` exigem a chave privada correspondente.
- Nao preencher certificado quando `SecurityPolicy` e `SecurityMode` estiverem em `None`.
- Se o UaExpert mostrar `Good`, mas a tela do SCADA mostrar `DISCONNECTED`, confirmar primeiro o tipo real do valor: escalar ou array.
- Se o no retornar array, usar `::indice` e cadastrar o tipo final esperado pelo MES, nao o container original do servidor.

Fluxo de diagnostico recomendado:
1. Abrir `Browse Address Space` e confirmar que o no aparece como `Variable`.
2. Comparar o `NodeId` com o UaExpert, caractere por caractere.
3. Verificar no UaExpert o tipo real publicado pelo servidor.
4. Para array, cadastrar `::0`, `::1`, etc., conforme o elemento desejado.
5. Reiniciar o backend depois de mudancas de codigo ou configuracao.
6. Confirmar que a qualidade passou para `GOOD` e que o valor final chegou como escalar.

Caso validado:

```text
UaExpert: STATUS = {2,0,0,0,0,0,0,0,0,0,0,0}, UInt32[], Good
Cadastro correto: ns=2;s=PJ-08.Tags.PRODUCAO.STATUS::0
Resultado esperado: STATUS = 2, GOOD, estado OCIOSA
```
## Driver Modbus

### Caracteristicas
- Modbus TCP e RTU
- Funcoes: 1, 2, 3, 4, 5, 6, 16
- Polling automatico configuravel
- Suporte a multiplos tipos de dados
- Configuracao de porta serial flexivel

### Configuracao TCP

```csharp
var config = new ModbusConnectionConfig
{
    IsTcp = true,
    Host = "192.168.1.100",
    Port = 502,
    TimeoutMs = 5000,
    AutoPoll = true,
    PollIntervalMs = 1000
};
```

### Configuracao RTU

```csharp
var config = new ModbusConnectionConfig
{
    IsTcp = false,
    PortName = "COM1",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One,
    TimeoutMs = 5000
};
```

### Uso

```csharp
using var driver = new ModbusDriver(config, logger);

await driver.ConnectAsync();

// Formato: slaveId:address:function
// Funcoes: 1=Coils, 2=Inputs, 3=Holding, 4=Input
var tag = await driver.ReadTagAsync("1:100:03", DataType.Int16);

// Escrever
await driver.WriteTagAsync("1:100:03", 123, DataType.Int16);

// Polling
driver.RegisterAddressForPolling("1:100:03", DataType.Int16);
```

### Formato de Endereco
- `"slaveId:address:function"` - Exemplo: `"1:100:03"`
- Funcoes: `01` (Coils), `02` (Inputs), `03` (Holding), `04` (Input)
- Se funcao omitida, usa `03` (Holding Registers)

### Tipos de Dados
- Bool: 1 coil/input
- Int16/UInt16: 1 register
- Int32/UInt32/Float: 2 registers
- Int64/UInt64/Double: 4 registers

## Driver Ethernet/IP CIP

### Caracteristicas
- Protocolo CIP (Common Industrial Protocol)
- Suporte a PLCs Allen-Bradley/Rockwell
- Comunicacao via TCP
- Polling automatico
- Suporte a multiplos tipos de dados

### Configuracao

```csharp
var config = new EthernetIpConnectionConfig
{
    Host = "192.168.1.1",
    Port = 44818,
    TimeoutMs = 5000,
    AutoPoll = true,
    PollIntervalMs = 1000
};
```

### Uso

```csharp
using var driver = new EthernetIpDriver(config, logger);

await driver.ConnectAsync();

// Formato: class:instance:attribute (hexadecimal)
var tag = await driver.ReadTagAsync("0x67:1:1", DataType.String);

// Escrever
await driver.WriteTagAsync("0x67:1:1", 100, DataType.Int16);

// Polling
driver.RegisterTagForPolling("0x67:1:1", DataType.Int16);
```

### Formato de Endereco
- `"class:instance:attribute"` - Exemplo: `"0x67:1:1"`
- Class codes comuns:
  - `0x67` - Identity Object
  - `0x6C` - Message Router
  - `0x6F` - Connection Manager

## Eventos Comuns

Todos os drivers suportam os mesmos eventos:

```csharp
// Valor de tag mudou
driver.TagValueChanged += (sender, e) => {
    Console.WriteLine($"Tag: {e.Address}");
    Console.WriteLine($"Valor: {e.Value}");
    Console.WriteLine($"Timestamp: {e.Timestamp}");
    Console.WriteLine($"Qualidade: {e.Quality}");
};

// Status de conexao mudou
driver.ConnectionStatusChanged += (sender, e) => {
    Console.WriteLine($"Conectado: {e.IsConnected}");
    Console.WriteLine($"Mensagem: {e.Message}");
};
```

## Tratamento de Erros

```csharp
try {
    await driver.ConnectAsync();
    var tag = await driver.ReadTagAsync("address", DataType.Double);
}
catch (InvalidOperationException ex) {
    // Driver nao conectado
}
catch (TimeoutException ex) {
    // Timeout na operacao
}
catch (Exception ex) {
    // Outros erros
}
```

## Best Practices

1. **Sempre usar using** para garantir dispose correto
2. **Configurar timeouts** apropriados para cada ambiente
3. **Usar eventos** para atualizacoes em tempo real
4. **Tratar excecoes** adequadamente
5. **Testar conexoes** antes de usar em producao
6. **Usar polling** apenas quando necessario (prefira subscricao)
7. **Fechar conexoes** quando nao mais necessarias

## Testes de conexao

Use os projetos simples dedicados para validar cada integracao:

```bash
cd backend/MqttSimpleTest
dotnet run

cd backend/OpcUaSimpleTest
dotnet run

cd backend/ModbusSimpleTest
dotnet run
```

`backend/Scada.Tests` foi aposentado e mantido apenas como referencia historica.

## Arquitetura de Banco de Dados

O SCADA utiliza uma arquitetura de banco de dados hibrida com 3 conexoes independentes:

### 1. SQLite (Principal) - Configuracoes e Metadados

**Proposito:** Armazenar configuracoes internas e metadados do sistema.

**Responsabilidades:**
- Configuracoes de conexao (endpoints, credenciais)
- Lista de maquinas/equipamentos
- Lista de tags e pontos de dados
- Configuracoes de drivers
- ParÃ¢metros do sistema

**Vantagens:**
- SCADA nao depende de MySQL para funcionar
- Leveza e portabilidade
- Zero Configuracao necessaria
- Backup simples (arquivo unico)
- Performance para leituras frequentes de configuracoes

**Localizacao:**
```
data/scada.db
```

**conexao validada:**
```csharp
var connectionString = "Data Source=data/scada.db;Mode=ReadWriteCreate;";
```

### 2. MySQL Local - Armazenamento Local de Historico

**Proposito:** Armazenar historico de eventos e dados de longo prazo localmente.

**Responsabilidades:**
- Historico de dados de tags
- Historico de alarmes
- Logs de eventos do sistema
- Dados de producao e OEE
- Rastreabilidade

**Vantagens:**
- Acesso rapido sem dependencia de internet
- Alta capacidade de armazenamento
- Consultas complexas suportadas
- Backup e replicacao locais

**Exemplo de configuracao:**
```csharp
var connectionString = new MySqlConnectionStringBuilder
{
    Server = "localhost",
    Database = "banco_mes_mundial",
    UserID = "mes_user",
    Password = "<senha-local>",
    Port = 3306,
    SslMode = MySqlSslMode.None,
    AllowPublicKeyRetrieval = true
}.ToString();
```

**Teste validado:** `backend/MySqlSimpleTest`

### 3. MySQL remoto/opcional - Sincronizacao externa

**Proposito:** Sincronizar dados historicos com um ambiente externo para backup e acesso remoto.

**Responsabilidades:**
- Sincronizacao bidirecional com MySQL local
- Backup em ambiente externo
- Acesso remoto aos dados
- Integracao com sistemas corporativos
- Analytics e reporting centralizado

**Vantagens:**
- Backup automatico em ambiente externo
- Alta disponibilidade (ambiente remoto)
- Escalabilidade
- Acesso global aos dados
- Integracao com outros servicos AWS

**Exemplo de configuracao:**
```csharp
var connectionString = new MySqlConnectionStringBuilder
{
    Server = "<endpoint-rds>",
    Database = "<database>",
    UserID = "<usuario>",
    Password = "<senha>",
    Port = 3306,
    SslMode = MySqlSslMode.Required
}.ToString();
```

**Teste validado:** `backend/MySqlSimpleTest`

### Fluxo de Sincronizacao

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                         SCADA Backend                           â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚                                                                  â”‚
â”‚  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”      â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”      â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  â”‚
â”‚  â”‚   SQLite     â”‚      â”‚ MySQL Local  â”‚      â”‚  MySQL AWS   â”‚  â”‚
â”‚  â”‚              â”‚â—„â”€â”€â”€â”€â–ºâ”‚              â”‚â—„â”€â”€â”€â”€â–ºâ”‚     RDS      â”‚  â”‚
â”‚  â”‚ Configuracoesâ”‚      â”‚  Historico  â”‚      â”‚  Sincroniza  â”‚  â”‚
â”‚  â”‚ Maquinas     â”‚      â”‚  Local      â”‚      â”‚     cao      â”‚  â”‚
â”‚  â”‚ Tags         â”‚      â”‚             â”‚      â”‚             â”‚  â”‚
â”‚  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜      â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜      â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜  â”‚
â”‚                                                                  â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

**Logica de sincronizacao:**
1. SCADA escreve dados em MySQL local (prioridade - baixa latencia)
2. Servico de sincronizacao replica dados para MySQL externo
3. Se ambiente remoto estiver indisponivel, dados ficam em cache local
4. Quando conexao com ambiente remoto for restaurada, sincronizacao automatica
5. SQLite e independente - SCADA funciona mesmo sem MySQL

### Resumo da Arquitetura

| Banco de Dados | Proposito | Dependencia | Validado |
|----------------|-----------|-------------|----------|
| SQLite | Configuracoes e metadados | Nenhuma (SCADA funciona sem MySQL) | âœ… |
| MySQL Local | Historico local | Opcional (SCADA funciona sem) | âœ… |
| MySQL externo | Sincronizacao ambiente externo | Opcional (SCADA funciona sem) | âœ… |

**Principio de design:** O SCADA nunca para de funcionar se MySQL (local ou remoto) estiver indisponivel. SQLite garante que o sistema continue operando com todas as configuracoes.





