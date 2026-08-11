# Teseu

Teseu é uma plataforma self-hosted para gerenciamento e monitoramento de homelab. O backend atual é uma ASP.NET Core 10 Web API que consulta métricas do Prometheus, node-exporter e cAdvisor.

## Assistente local de IA

O assistente usa uma LLM local no Ollama para interpretar perguntas e redigir respostas. A LLM não acessa Docker, shell, filesystem ou Prometheus diretamente. Ela pode solicitar somente tools read-only registradas pela API; a API executa essas consultas através de seus próprios serviços e devolve os resultados ao modelo.

```text
Flutter
   |
Teseu API
   |
Teseu AI
  /      \
Tools   Ollama
  |
Prometheus / cAdvisor
```

Tools disponíveis nesta versão:

- `GetServerStatus`: visão geral e diagnóstico;
- `GetCpuStatus`, `GetMemoryStatus`, `GetStorageStatus`, `GetNetworkStatus`, `GetTemperatureStatus` e `GetUptimeStatus`;
- `GetContainers` e `GetContainerStatus`: métricas expostas pelo cAdvisor.

Todas são exclusivamente de leitura. Não há tools para executar shell, acessar o Docker socket, alterar configurações, modificar dados ou iniciar/parar containers. Futuras actions mutáveis deverão ter autorização e confirmação separadas.

### Configuração e inicialização

O Compose pressupõe que a rede externa `monitoring_default`, usada pelo Prometheus existente, já esteja disponível.

```bash
cd infrastructure/docker/teseu-api
cp .env.example .env
docker compose up -d ollama
docker exec ollama ollama pull qwen3:4b-instruct-2507-q4_K_M
docker compose up -d --build teseu-api
```

`qwen3:4b-instruct-2507-q4_K_M` é o padrão por ser a variante não-thinking do Qwen3 4B, com melhor seguimento de instruções, correlação de métricas e uso de tools, mantendo consumo compatível com o servidor de 16 GB de RAM. O desempenho real depende do hardware. Para trocar o modelo, altere `OLLAMA_MODEL` no `.env` e baixe exatamente a mesma tag com `ollama pull`; a API não carrega nem mantém múltiplos modelos por conta própria.

Variáveis disponíveis:

| Variável | Padrão | Uso |
|---|---|---|
| `OLLAMA_BASE_URL` | `http://ollama:11434` | Endereço interno usado pela API |
| `OLLAMA_MODEL` | `qwen3:4b-instruct-2507-q4_K_M` | Variante não-thinking usada no chat |
| `OLLAMA_TIMEOUT_SECONDS` | `180` | Timeout por chamada ao modelo |
| `OLLAMA_KEEP_ALIVE` | `15m` | Tempo que o modelo permanece carregado |
| `OLLAMA_ENABLE_THINKING` | `false` | Ativa o raciocínio estendido do modelo; desativado para reduzir latência e não expor traces |

O volume nomeado `teseu-ollama-data` persiste os modelos fora do Git. O Ollama não publica porta no host e participa apenas da rede `teseu-network`; somente a API também participa da rede de monitoramento.

### Endpoint

`POST /api/ai/chat`

```json
{
  "message": "Qual o uso da CPU agora?"
}
```

Exemplo de resposta:

```json
{
  "answer": "A CPU está utilizando 17,4% neste momento.",
  "toolsUsed": ["GetCpuStatus"]
}
```

Outras perguntas possíveis incluem “Quanto de RAM está sendo usado?”, “O servidor está sobrecarregado?”, “Qual container consome mais memória?”, “O Palworld está visível nas métricas?” e “Há temperatura disponível?”. A resposta acompanha o idioma da pergunta. Valores ausentes são informados como indisponíveis; o modelo é instruído a nunca estimá-los.

### Limitações atuais

- O chat não mantém histórico entre requisições.
- O estado de containers é inferido somente pelas séries que o cAdvisor expõe ao Prometheus; ausência nas métricas não prova que um serviço esteja parado.
- Os dados de rede atuais são contadores acumulados, não taxa instantânea de tráfego.
- Temperatura depende de `node_hwmon_temp_celsius` estar exposta.
- Não há status específico de jogos, jogadores, alertas ou backups nesta versão.
- O primeiro pedido após o modelo sair da memória pode ser lento, especialmente em CPU antiga.

Quando Ollama está indisponível ou o modelo não está instalado, a API responde `503`; timeout retorna `504`; uma resposta inválida do modelo retorna `502`. Falhas ou ausência de séries do Prometheus aparecem nas tools como dados indisponíveis, sem métricas inventadas e sem stack traces no cliente.

## Teste manual pelo terminal

Execute os passos abaixo a partir da raiz do repositório. Os exemplos usam apenas Docker e `curl`; `jq` é opcional para formatar o JSON.

1. Valide a configuração do Compose:

   ```bash
   docker compose -f infrastructure/docker/teseu-api/docker-compose.yml config --quiet
   ```

2. Suba Ollama e a API:

   ```bash
   docker compose -f infrastructure/docker/teseu-api/docker-compose.yml up -d --build
   ```

3. Confirme que ambos os containers estão ativos:

   ```bash
   docker compose -f infrastructure/docker/teseu-api/docker-compose.yml ps
   ```

4. Confirme o modelo configurado e, se necessário, baixe-o:

   ```bash
   docker exec ollama ollama list
   docker exec ollama ollama pull qwen3:4b-instruct-2507-q4_K_M
   ```

   Se `OLLAMA_MODEL` tiver sido alterado no `.env`, use a mesma tag no comando `ollama pull`.

5. Verifique se as métricas reais chegam à API:

   ```bash
   curl --fail --show-error http://localhost:5050/api/server/status
   ```

6. Teste uma pergunta simples de CPU:

   ```bash
   curl --fail --show-error \
     --max-time 200 \
     -H 'Content-Type: application/json' \
     -d '{"message":"Qual o uso da CPU agora?"}' \
     http://localhost:5050/api/ai/chat
   ```

   A resposta deve ter HTTP 200, um valor coerente com `/api/server/status` e `"toolsUsed":["GetCpuStatus"]`.

7. Teste diagnóstico, uptime em inglês e containers:

   ```bash
   curl --fail --show-error --max-time 200 \
     -H 'Content-Type: application/json' \
     -d '{"message":"O servidor está sobrecarregado?"}' \
     http://localhost:5050/api/ai/chat

   curl --fail --show-error --max-time 200 \
     -H 'Content-Type: application/json' \
     -d '{"message":"How long has the server been running?"}' \
     http://localhost:5050/api/ai/chat

   curl --fail --show-error --max-time 200 \
     -H 'Content-Type: application/json' \
     -d '{"message":"Qual container está consumindo mais memória?"}' \
     http://localhost:5050/api/ai/chat
   ```

   Espere respectivamente `GetServerStatus`, `GetUptimeStatus` e `GetContainers` em `toolsUsed`. A pergunta em inglês deve receber resposta em inglês.

8. Verifique a validação da entrada:

   ```bash
   curl --include --show-error \
     -H 'Content-Type: application/json' \
     -d '{"message":""}' \
     http://localhost:5050/api/ai/chat
   ```

   O resultado esperado é HTTP 400 sem stack trace.

9. Se uma requisição falhar, consulte os logs sem acessar o host pela LLM:

   ```bash
   docker logs --tail 100 teseu-api
   docker logs --tail 100 ollama
   ```

Em hardware limitado, faça esses testes sequencialmente. Várias inferências simultâneas entram em fila no único modelo carregado e podem atingir o timeout configurado.
