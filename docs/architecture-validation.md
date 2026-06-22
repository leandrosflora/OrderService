# Validação arquitetural contra `leandrosflora/logistica-envios-demo-arch`

Data da validação: 2026-06-21.

## Fontes consultadas

- `AGENTS.md` local deste repositório.
- `https://github.com/leandrosflora/logistica-envios-demo-arch`:
  - `AGENTS.md`.
  - `docs/contracts/services-map.md`.
  - `docs/contracts/logistica-envios-apis.openapi.yaml`.
  - `docs/contracts/kafka-events.md`.
  - `docs/adr/0003-hexagonal-clean-architecture.md`.
  - `docs/adr/0005-idempotency-strategy.md`.
  - `docs/services/order-service.md`.

> Observação operacional: `git clone` do repositório de arquitetura falhou no ambiente por bloqueio de túnel HTTP 403, então a validação usou os arquivos públicos via GitHub/raw.

## Resultado executivo

O serviço está parcialmente aderente à documentação arquitetural. Ele respeita o domínio principal do `OrderService`, usa .NET 8, separa pastas por `Api`, `Application`, `Contracts`, `Domain` e `Infrastructure`, persiste `Order`, usa Inbox/Outbox e publica/consome tópicos Kafka relevantes. Porém há divergências contratuais e técnicas que precisam ser tratadas antes de considerar o serviço totalmente aderente.

## Aderências encontradas

1. **Responsabilidade do serviço**: o código mantém o escopo de pedido confirmado e saga de criação, sem acessar banco de outro microserviço.
2. **Stack base**: o projeto usa `net8.0`, ASP.NET Core Minimal APIs, EF Core/Npgsql e Kafka.
3. **Separação por camadas/pastas**: há diretórios explícitos para API, aplicação, domínio, contratos e infraestrutura.
4. **Outbox Pattern**: produtores gravam mensagens no Outbox antes da publicação Kafka.
5. **Inbox Pattern**: handlers usam deduplicação por `MessageId`/`eventId` via `InboxMessages`.
6. **Kafka envelope canônico**: há `IntegrationEventEnvelope<T>` com `eventId`, `eventType`, `schemaVersion`, `occurredAt`, `correlationId`, `producer` e `payload`.
7. **Tópicos internos de saga**: o serviço usa `inventory.commands`, `fulfillment.commands`, `payment.commands` e `shipment.commands`, que existem como tópicos internos da saga do `OrderService` na documentação arquitetural.
8. **Consumo de `shipment.status.updated`**: o consumer usa envelope canônico e atualiza visão local do status de entrega no pedido.

## Divergências e riscos

### 1. Rotas HTTP não seguem o prefixo versionado `/v1`

- Documentação: o contrato canônico expõe `POST /v1/orders`, `GET /v1/orders/{orderId}` e `POST /v1/orders/{orderId}/cancel`.
- Implementação: os endpoints estão mapeados em `/orders` e `/orders/{orderId}/cancel`.
- Impacto: clientes/BFF que usam o contrato OpenAPI não conseguem chamar este serviço diretamente sem ajuste de rota.

### 2. `POST /v1/orders` documentado não está implementado

- Documentação: existe endpoint interno `POST /v1/orders` idempotente via `x-idempotency-key` para criar pedido a partir de checkout confirmado.
- Implementação: a criação de pedido existe como fluxo de evento `CheckoutConfirmedIntegrationEvent`, mas não como endpoint HTTP.
- Impacto: se o contrato OpenAPI for obrigatório para este serviço, a superfície HTTP está incompleta. Se a criação for exclusivamente assíncrona, a documentação consolidada ou a spec do serviço precisa refletir isso.

### 3. Header `X-Correlation-Id` não é validado/propagado nas APIs HTTP

- Documentação: exige propagação de correlation id em APIs e eventos.
- Implementação: o endpoint de cancelamento valida apenas `Idempotency-Key`; não há middleware ou validação de `X-Correlation-Id`.
- Impacto: rastreabilidade ponta a ponta fica incompleta em comandos HTTP.

### 4. Nome/capitalização do header de idempotência diverge da documentação

- Documentação: usa `x-idempotency-key`.
- Implementação: lê `Idempotency-Key`.
- Observação: headers HTTP são case-insensitive, mas o nome operacional padronizado deveria ser mantido em mensagens/erros/docs do serviço para reduzir ambiguidade.

### 5. Eventos canônicos `order.confirmed` e `order.cancelled` não usam envelope canônico e tópico canônico

- Documentação: `order.confirmed` e `order.cancelled` são eventos canônicos publicados pelo `order-service` e devem usar envelope padrão.
- Implementação: `OrderConfirmedIntegrationEvent` e `OrderCancelledIntegrationEvent` são gravados no tópico interno `order.events`, sem `IntegrationEventEnvelope<T>`.
- Impacto: consumidores externos documentados (`notification-service`, `audit-service`, `shipment-service`, `inventory-service`) não receberão os eventos canônicos conforme contrato.

### 6. Payload de `order.cancelled` diverge do contrato canônico

- Documentação: `order.cancelled` exige `orderId`, `checkoutId`, `buyerId`, `sellerId`, `cancellationReason` e `cancelledAt`.
- Implementação: `OrderCancelledIntegrationEvent` contém `MessageId`, `OrderId`, `Status` e `CancellationReason`.
- Impacto: consumers canônicos não terão dados obrigatórios sem lookup adicional.

### 7. Payload de `order.confirmed` diverge do contrato canônico

- Documentação: `order.confirmed` exige `orderId`, `checkoutId`, `buyerId`, `sellerId` e `confirmedAt`.
- Implementação: possui os campos obrigatórios, mas também `totalAmount`, `currency` e `shipmentId`, e não está envelopado/publicado no tópico canônico `order.confirmed`.
- Impacto: o problema principal é tópico/envelope; campos extras podem ser aceitáveis apenas se a governança permitir evolução compatível.

### 8. Contrato consumido de `checkout.confirmed` parece enriquecido além do contrato canônico

- Documentação de Kafka: `checkout.confirmed` contém `checkoutId`, `buyerId`, `sellerId`, `shippingPromiseId`, `items`, `totalAmount`, `currency` e `confirmedAt`.
- Implementação: `CheckoutConfirmedIntegrationEvent` espera também `ShippingPrice`, `PricingQuoteId`, `PaymentMethodToken`, `FulfillmentCenterId` por item e campos logísticos opcionais para `order.created`.
- Impacto: se o producer seguir estritamente o contrato canônico, o consumer pode não ter todos os dados necessários para criar o pedido/saga. Isso precisa ser alinhado no contrato arquitetural ou adaptado no serviço.

### 9. `order.created` é publicado no momento da criação local, não após conclusão da saga

- Documentação do `services-map` diz que o `Order Service` publica `order.created` ao concluir a saga com sucesso.
- Documentação de Kafka diz que `order.created` é consumido por `shipment-service` para criar entrega e inclui dados enriquecidos para esse fluxo.
- Implementação publica `order.created` logo após criar o agregado e antes das reservas/pagamento/envio.
- Impacto: há ambiguidade entre documentos. A implementação está alinhada ao contrato Kafka de disparar criação de shipment, mas conflita com a frase do mapa de serviços sobre “concluir a saga”. Essa divergência deve ser resolvida na arquitetura.

### 10. Arquitetura hexagonal está em um único projeto físico

- Documentação ADR-0003 exige quatro projetos `.Domain`, `.Application`, `.Infrastructure` e `.API`.
- Implementação usa um único `OrderService.csproj` com pastas correspondentes.
- Impacto: a separação lógica existe, mas não há enforcement de dependências por assembly. Além disso, `Application` referencia `Infrastructure` diretamente em alguns pontos, o que contraria a regra de dependência da ADR.

### 11. API injeta infraestrutura diretamente

- Documentação ADR-0003 diz que controllers devem injetar interfaces de Application, nunca Infrastructure diretamente.
- Implementação do GET injeta `OrderDbContext` diretamente em `Api/OrderEndpoints.cs`.
- Impacto: acoplamento da API à persistência, menor testabilidade e violação de boundary.

### 12. Não há evidência de autenticação/autorização Bearer conforme OpenAPI

- Documentação OpenAPI define autenticação JWT Bearer para endpoints não públicos.
- Implementação não configura autenticação/autorização no `Program.cs`.
- Impacto: endpoints de pedido ficam sem proteção conforme contrato consolidado.

### 13. Health checks não seguem `/health/live` e `/health/ready`

- Documentação OpenAPI define `/health/live` e `/health/ready`.
- Implementação expõe apenas `/health`.
- Impacto: probes padronizados de plataforma/Kubernetes podem falhar.

### 14. Falta `dotnet format --verify-no-changes` como validação esperada pela arquitetura

- Documentação do repositório arquitetural inclui `dotnet format --verify-no-changes` para microserviços .NET.
- A instrução local exige `dotnet restore`, `dotnet build` e `dotnet test`; recomenda-se adicionar `dotnet format --verify-no-changes` ao checklist de CI/local.

## Recomendação de próximos passos

1. Decidir se o serviço deve implementar a superfície HTTP completa (`/v1/orders`) ou se a arquitetura deve documentar criação exclusivamente por Kafka.
2. Versionar as rotas existentes para `/v1/orders` e expor health checks em `/health/live` e `/health/ready`.
3. Adicionar middleware para `X-Correlation-Id` e padronizar `x-idempotency-key` nas respostas/erros/documentação.
4. Publicar `order.confirmed` e `order.cancelled` nos tópicos canônicos, usando `IntegrationEventEnvelope<T>` e payloads exatamente iguais ao contrato.
5. Reconciliar o contrato de `checkout.confirmed` com os dados realmente necessários pelo `OrderService`.
6. Remover acoplamento da API e Application com Infrastructure, preferindo portas/use cases e, idealmente, projetos físicos separados.
7. Configurar autenticação/autorização ou documentar explicitamente se este serviço roda apenas atrás de gateway/BFF em ambiente local.
8. Incluir `dotnet format --verify-no-changes` no fluxo de validação.
