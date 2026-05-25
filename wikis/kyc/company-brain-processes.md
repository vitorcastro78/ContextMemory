# Processos executáveis (Company Brain)

Estes blocos são sincronizados automaticamente via `POST /companies/{id}/sync` quando a wiki está ligada como fonte Markdown.

## Process: PEP Screening

Category: Compliance
Triagem de clientes Politically Exposed Persons (PEP) conforme política interna.

#### Triggers
- pep
- politically exposed
- pessoa politicamente exposta

1. Confirmar classificação PEP na base interna.
2. Recolher documentação de origem de fundos.
3. Submeter caso ao Compliance Officer para aprovação.

#### Guardrails
- Nunca aprovar onboarding PEP sem EDD completa.
- Registar todas as decisões no audit trail.

## Process: Refund Exception

Category: Operations
Tratamento de pedidos de reembolso fora da política standard.

#### Triggers
- refund
- reembolso
- exceção

1. Validar elegibilidade e valor da transação.
2. Escalar para supervisor se valor > limite diário.
3. Documentar motivo da exceção no ticket.

#### Guardrails
- Não prometer reembolso antes de validação completa.
