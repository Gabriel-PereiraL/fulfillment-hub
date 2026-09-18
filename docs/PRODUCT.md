# PRODUCT — FulfillmentHub

## 1. O que é

**FulfillmentHub** é uma plataforma backend de gestão e orquestração de **pedidos, pagamentos e entregas**.
Ela recebe pedidos de clientes, valida e reserva estoque, inicia e confirma o pagamento junto a um provedor,
cota e solicita a entrega junto a um provedor de logística, acompanha os eventos (webhooks) até o estado final
e oferece uma área operacional para acompanhar tudo isso — incluindo falhas, retries e mensagens em DLQ.

É um projeto de **portfólio** construído para demonstrar engenharia backend séria em **C#/.NET**:
consistência, idempotência, resiliência, mensageria, segurança, observabilidade e operação.

> **Aviso obrigatório (repetido em todo material público):**
> This project does not connect to Uber infrastructure or to any real payment provider.
> The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.
> No real deliveries, charges or credentials are involved.

## 2. Por que este produto

Um fluxo de pedido → pagamento → entrega concentra, em um domínio fácil de entender, quase todos os problemas
de engenharia que um backend de produção enfrenta:

| Problema real | Onde aparece no FulfillmentHub |
|---|---|
| Consistência entre banco e efeitos externos | salvar pedido + publicar evento (outbox) |
| Idempotência | `POST /orders` repetido, webhook duplicado, retry de criação de entrega |
| Concorrência | reserva de estoque simultânea, confirmação de pagamento duplicada |
| Integração HTTP com falhas | provider de entrega lento/instável (timeout, 429, 5xx) |
| Eventos fora de ordem | webhook "delivered" chegando antes de "pickup" |
| Processamento assíncrono | worker, filas SQS, retries com backoff, DLQ |
| Segurança | autenticação, autorização por papel, assinatura de webhooks, secrets |
| Observabilidade | trace de um pedido atravessando API → fila → worker → provider |
| Operação | admin vê falhas, reprocessa, investiga incidente |

## 3. Nome

Nome provisório e adotado: **FulfillmentHub**. Alternativas consideradas (Dispatchly, OrderFlow, Fulfilla) foram
descartadas por serem menos descritivas ou soarem como marca comercial. O nome pode ser revisto na Fase 19
(documentation hardening) sem impacto técnico — namespaces raiz `FulfillmentHub.*` só mudam se houver decisão explícita.

## 4. Atores

| Ator | Descrição | Papel (role) |
|---|---|---|
| Cliente | cria pedidos, acompanha status, cancela enquanto permitido | `Customer` |
| Operador | acompanha pedidos/pagamentos/entregas, reprocessa falhas, cancela entregas | `Operator` |
| Administrador | tudo do operador + gerencia usuários, produtos, configurações | `Admin` |
| Provider de pagamento (simulado) | recebe solicitações de pagamento, envia webhooks | sistema externo |
| Provider de entrega (simulado, "Uber-like") | cota, cria, cancela entregas; envia webhooks de status | sistema externo |
| Worker | processa outbox, filas, reconciliação, retries | interno |

## 5. Fluxo macro (happy path)

```
Cliente                      FulfillmentHub (API)              Worker                    Providers (simulados)
  |  POST /orders (Idempotency-Key)  |                            |                              |
  |--------------------------------->| valida, reserva estoque,   |                              |
  |                                  | salva Order(Created) +     |                              |
  |  201 Created (order)             | Outbox(OrderPlaced)        |                              |
  |<---------------------------------|                            |                              |
  |                                  |                            | lê outbox → CreatePayment    |
  |                                  |                            |----------------------------->| Payment provider
  |                                  |                            |  payment pending             |
  |                                  |  Order → AwaitingPayment   |<-----------------------------|
  |                                  |                            |                              |
  |                                  |  POST /webhooks/payments   |                              | (webhook: paid)
  |                                  |<--------------------------------------------------------- |
  |                                  | dedup (event id), salva    |                              |
  |                                  | WebhookEvent, enfileira    |                              |
  |                                  |                            | processa → Payment Paid,     |
  |                                  |                            | Order → Paid, Outbox(OrderPaid)|
  |                                  |                            | → RequestDelivery:           |
  |                                  |                            |   POST delivery_quotes ----->| Delivery provider
  |                                  |                            |   POST deliveries (idem key)>|
  |                                  |  Order → DeliveryRequested |                              |
  |                                  |                            |                              |
  |                                  |  POST /webhooks/deliveries |                              | (status: pickup,
  |                                  |<--------------------------------------------------------- |  dropoff, delivered)
  |                                  |                            | aplica evento (ordem por     |
  |                                  |                            | status/timestamp), Order →   |
  |                                  |                            | InDelivery → Delivered       |
  |  GET /orders/{id}                |                            |                              |
  |--------------------------------->| status atual + histórico   |                              |
```

## 6. Funcionalidades por módulo

### Pedidos (Orders)
- Criar pedido com itens (produto, quantidade), endereço de entrega (snapshot) e chave de idempotência.
- Validar: cliente ativo, produtos ativos, quantidades > 0, estoque disponível (reserva com concorrência otimista).
- Preço: preço unitário congelado no item (snapshot), subtotal, taxa de entrega (após cotação), total.
- Estado com histórico de transições (quem/quando/por quê).
- Cancelamento pelo cliente (enquanto não pago ou antes da coleta, conforme regra) e pelo operador; libera estoque; solicita estorno/cancelamento de entrega quando aplicável.
- Transação: pedido + reserva de estoque + cotação de entrega + evento `OrderPlaced` na outbox no mesmo commit (Fase 8). **Consistência eventual**: `POST /orders` responde `Created`; o pagamento é criado pelo Worker em seguida (normalmente < 1 s) e o pedido passa a `AwaitingPayment` → `Paid` → `DeliveryRequested` sem intervenção — o cliente acompanha por `GET /orders/{id}`.

### Pagamentos (Payments)
- Provider simulado (sem dinheiro real). Ciclo: `Pending → Authorized → Paid` | `Failed` | `Cancelled` | `Refunded`.
- Criação assíncrona via outbox (evento `OrderPlaced`); tentativas registradas (`PaymentAttempt`).
- Webhook do provider com `event_id` (dedup), assinatura HMAC, possível duplicidade/atraso.
- Reconciliação: job periódico consulta o provider para pagamentos "pendentes há muito tempo".
- Falha temporária do provider → retry com backoff; falha permanente → pedido cancelado, estoque liberado.

### Entregas (Deliveries)
- Cotação (`DeliveryQuote`) com validade (`expires`); recotar se expirada antes de criar.
- Criação com `idempotency_key`; acompanhamento por webhooks e por consulta (`GET`) de reconciliação.
- Status espelhados do provider e mapeados para o domínio; histórico de eventos com `provider_event_id`, `occurred_at`, `received_at`.
- Cancelamento (respeitando `noncancelable_delivery` do provider).
- Eventos fora de ordem e duplicados tratados por regra (estado + timestamp), nunca por ordem de chegada.

### Catálogo (Catalog)
- Produtos com SKU, preço, estoque, ativo/inativo. CRUD simples (Admin). Cenário de concorrência: estoque.

### Clientes (Customers)
- Perfil de cliente (nome, e-mail, telefone), endereços salvos. Vinculado a um `User` com papel `Customer`.

### Identidade (Identity)
- Usuários, papéis (`Customer`, `Operator`, `Admin`), login com JWT, políticas de autorização. Sem OAuth externo (fora de escopo).

### Operações / Admin (Operations)
- Visão de pedidos, pagamentos, entregas, eventos de webhook, outbox (pendentes/falhos), mensagens em DLQ, tentativas/retries,
  status de integrações (circuit breaker aberto? provider lento?), logs relevantes por correlation id.
- Ações: reprocessar outbox/webhook falho, cancelar entrega, forçar reconciliação.
- Tecnologia: **Blazor** (Web App, render interativo no servidor), Fase 17. Funcionalidade acima de estética.

## 7. Fora de escopo (explicitamente)

- Integração real com Uber, PSPs ou qualquer fornecedor comercial.
- Microserviços, Kubernetes, Kafka, service mesh.
- Frontend rico para o cliente final (apenas API + Admin operacional).
- Multi-tenancy, multi-moeda real (moeda é um campo, mas só BRL é usado), fiscal/NF-e, marketplace.
- Produção comercial real: os ambientes são local e "dev/cloud" de aprendizado.

## 8. Requisitos não funcionais (metas mensuráveis para o portfólio)

| Requisito | Meta | Como provar |
|---|---|---|
| Idempotência | 0 duplicatas em `POST /orders` repetido, webhooks duplicados e retries de criação de entrega | testes de integração + constraints |
| Consistência | nenhum evento perdido entre commit e publicação | outbox + testes de falha injetada |
| Resiliência | provider com 30% de falha/latência ainda leva 100% dos pedidos ao estado final (ou cancelamento controlado) | cenário com simulator configurado |
| Concorrência | 50 requisições simultâneas ao mesmo produto nunca deixam estoque negativo | teste de race condition |
| Observabilidade | um `correlation id` permite seguir um pedido de ponta a ponta (API → fila → worker → provider) | trace + logs + runbook |
| Segurança | OWASP Top 10 revisado; nenhum secret em código; SAST e dependency scan em CI | SECURITY.md + CI |
| Operação | operador consegue ver e reprocessar falhas sem acessar o banco | Admin UI |
| Deploy | `terraform apply` + pipeline sobem o ambiente dev na AWS; `terraform destroy` derruba | DEPLOYMENT.md |

## 9. Glossário (linguagem ubíqua)

| Termo | Significado |
|---|---|
| Order (Pedido) | agregado principal; agrupa itens, endereço, pagamento e entrega |
| Order Item | linha do pedido com snapshot de produto e preço |
| Payment (Pagamento) | agregado do módulo de pagamentos; 1 pedido → 1 pagamento ativo; tentativas em `PaymentAttempt` |
| Delivery Quote (Cotação) | oferta de entrega do provider com taxa, ETA e validade |
| Delivery (Entrega) | agregado que espelha a entrega no provider e seu histórico de eventos |
| Webhook Event | evento recebido de um provider, persistido bruto, deduplicado por `provider_event_id` |
| Outbox Message | evento de domínio persistido no mesmo commit da mudança de estado, publicado depois |
| Idempotency Record | registro de uma requisição idempotente (chave + hash + resposta) |
| Provider | sistema externo (simulado) de pagamento ou entrega |
| Simulator | processo .NET que imita o provider, com falhas configuráveis |
| DLQ | dead-letter queue: mensagens que esgotaram tentativas |
