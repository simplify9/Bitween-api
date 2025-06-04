# Architecture Overview

Bitween is built on a modern, cloud-native architecture designed for scalability, reliability, and extensibility. This document provides a comprehensive overview of the system architecture and its components.

## High-Level Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   External      │    │    Bitween      │    │   Target        │
│   Systems       │───▶│   Middleware    │───▶│   Systems       │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                              │
                              ▼
                      ┌─────────────────┐
                      │   Serverless    │
                      │   Adapters      │
                      └─────────────────┘
```

## Core Components

### 1. Web API Layer (`SW.Bitween.Web`)

The main entry point for the system, providing:

- **RESTful APIs** for message ingestion and management
- **JWT Authentication** for secure access
- **Swagger Documentation** for API exploration
- **Health Checks** for monitoring

**Key Features:**
- ASP.NET Core 8.0 based
- OpenAPI/Swagger documentation
- Rate limiting and throttling
- CORS support for web clients

### 2. Business Logic Layer (`SW.Bitween.Api`)

Contains the core domain logic and services:

- **Domain Models** (Xchange, Subscription, Document, Partner)
- **Business Services** (XchangeService, FilterService, etc.)
- **Event Handlers** for asynchronous processing
- **Specifications** for complex queries

**Key Patterns:**
- Domain-Driven Design (DDD)
- CQRS with MediatR
- Repository Pattern
- Specification Pattern

### 3. Data Access Layer

Multi-database support through separate projects:

- **`SW.Bitween.PgSql`** - PostgreSQL support
- **`SW.Bitween.MySql`** - MySQL support  
- **`SW.Bitween.MsSql`** - SQL Server support

**Features:**
- Entity Framework Core
- Database migrations
- Connection pooling
- Query optimization

### 4. SDK Layer (`SW.Bitween.Sdk`)

Client libraries and shared models:

- **Client SDK** for external integrations
- **Shared Models** for data contracts
- **JSON Converters** for serialization
- **Mock Implementations** for testing

### 5. Serverless Framework

Custom adapter execution environment:

- **Handler Interface** (`IInfolinkHandler`)
- **Validator Interface** (`IInfolinkValidator`)
- **Mapper Interface** (`IInfolinkMapper`)
- **Receiver Interface** (`IInfolinkReceiver`)

## Processing Architecture

### Message Flow

```
[Input Message] 
    ↓
[File Storage] 
    ↓
[Validation] ──── Serverless Validator
    ↓
[Document Matching] 
    ↓
[Subscription Filtering] 
    ↓
[Mapping] ──── Serverless Mapper
    ↓
[Processing] ──── Serverless Handler
    ↓
[Response Generation]
    ↓
[Result Storage]
```

### Event-Driven Architecture

Bitween uses domain events for asynchronous processing:

```
[Domain Event] → [Message Bus] → [Event Handler] → [Background Service]
```

**Event Types:**
- `XchangeCreated` - New message received
- `XchangeProcessed` - Processing completed
- `XchangeError` - Processing failed
- `NotificationRequired` - Notification needed

## Data Architecture

### Core Domain Entities

```mermaid
erDiagram
    DOCUMENT {
        int Id
        string Name
        string Format
        json PromotedProperties
        datetime CreatedOn
    }
    
    SUBSCRIPTION {
        int Id
        string Name
        string Type
        json FilterExpression
        string ValidatorEndpoint
        string MapperEndpoint
        string HandlerEndpoint
        int PartnerId
        datetime CreatedOn
    }
    
    PARTNER {
        int Id
        string Name
        string ApiKey
        json Settings
        datetime CreatedOn
    }
    
    XCHANGE {
        int Id
        string CorrelationId
        string Reference
        int DocumentId
        json PromotedProperties
        string Status
        datetime CreatedOn
    }
    
    XCHANGERESULT {
        int Id
        int XchangeId
        int SubscriptionId
        string Status
        string ErrorMessage
        datetime CreatedOn
    }
    
    DOCUMENT ||--o{ XCHANGE : "processes"
    SUBSCRIPTION ||--o{ XCHANGERESULT : "creates"
    PARTNER ||--o{ SUBSCRIPTION : "owns"
    XCHANGE ||--o{ XCHANGERESULT : "generates"
```

### File Storage Architecture

Bitween uses a flexible file storage abstraction:

```
[Cloud Storage Provider]
    ├── Input Files (Original messages)
    ├── Output Files (Processed results)
    ├── Response Files (System responses)
    └── Temporary Files (Processing artifacts)
```

**Storage Features:**
- Provider-agnostic interface
- Configurable retention policies
- Access control and security
- Automatic cleanup

## Security Architecture

### Authentication & Authorization

- **JWT Tokens** for API authentication
- **Partner-based Access Control** for multi-tenancy
- **Role-based Permissions** for system access
- **API Key Management** for external systems

### Data Security

- **Encryption at Rest** for sensitive data
- **TLS/HTTPS** for data in transit
- **Audit Logging** for compliance
- **Data Isolation** between partners

## Scalability Architecture

### Horizontal Scaling

- **Stateless Design** enables multiple instances
- **Event-driven Processing** for load distribution
- **Database Connection Pooling** for efficiency
- **Caching Strategies** for performance

### Cloud-Native Features

- **Container Support** (Docker)
- **Kubernetes Deployment** with Helm charts
- **Health Checks** for orchestration
- **Configuration Management** via environment variables

## Integration Architecture

### Serverless Adapters

Custom business logic through pluggable components:

```
[Bitween Core] ←→ [Adapter Interface] ←→ [Custom Implementation]
```

**Adapter Types:**
- **Validators**: Input validation and enrichment
- **Mappers**: Data transformation and format conversion
- **Handlers**: Business logic execution
- **Receivers**: External data source integration

### External System Integration

Multiple integration patterns supported:

- **Synchronous**: REST API calls with immediate response
- **Asynchronous**: Message queue based processing
- **Scheduled**: Time-based data retrieval
- **Event-driven**: Webhook and notification based

## Monitoring & Observability

### Logging

- **Structured Logging** with correlation IDs
- **Multiple Log Levels** for different environments
- **Centralized Logging** support
- **Performance Metrics** tracking

### Health Monitoring

- **Application Health Checks** for readiness/liveness
- **Database Health Checks** for connectivity
- **External Service Health** for dependencies
- **Custom Health Checks** for business logic

### Metrics & Monitoring

- **Processing Metrics** (throughput, latency, errors)
- **System Metrics** (CPU, memory, disk)
- **Business Metrics** (message counts, partner activity)
- **Custom Metrics** via extensible framework

This architecture provides a solid foundation for enterprise-grade integration scenarios while maintaining flexibility for customization and scaling.
