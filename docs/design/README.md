# Design documents

These are proposals and plans written while features were being designed. They explain the reasoning behind the external broker and database features, but they are not a description of how Bitween works today. Much of what they propose was built differently or not at all.

| Document | Status on 11 September 2026 |
|---|---|
| [external-brokers-architecture.md](external-brokers-architecture.md) | Proposal. Written against an earlier branch. Shipped differently: resident serverless adapters instead of in-process plugins, and no cluster API, node registry or outbox. |
| [provider-plan-rabbitmq-kafka.md](provider-plan-rabbitmq-kafka.md) | Plan. RabbitMQ shipped in a smaller form. Kafka was not built. |
| [resident-adapters-design.md](resident-adapters-design.md) | Design for SW-Serverless's resident adapters and protocol 2, written for Bitween's brokers and databases. Built, in SW-Serverless 10.x; the SW-Serverless docs describe what shipped. |
| [provider-plan-databases.md](provider-plan-databases.md) | Plan with partial status notes, some of them out of date. Four engines, statements and the UI shipped. Explain, bulk load, push ingress and several metrics did not. |

For how the features actually behave, read [Data sources](../data-sources.md), [External brokers](../external-brokers.md) and [Databases](../databases.md).
