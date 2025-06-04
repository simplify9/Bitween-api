<div align="center">
  <img src="icon.png" alt="Bitween Logo" width="120" height="120">
  <h1>Bitween</h1>
  <p><strong>Open-Source Integration Middleware Platform</strong></p>
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
  [![Docker](https://img.shields.io/badge/Docker-Supported-blue.svg)](https://www.docker.com/)
  [![Kubernetes](https://img.shields.io/badge/Kubernetes-Ready-green.svg)](https://kubernetes.io/)
</div>

---

## 🚀 Overview

Bitween is a powerful, open-source integration middleware designed to simplify data exchange between different systems, regardless of protocols and data formats. Built with enterprise-grade scalability and customization in mind, Bitween acts as a central hub for all your integration needs.

### Why Bitween?

- **🔗 Protocol Agnostic**: Handle any data format (JSON, XML, EDI, etc.) and communication protocol
- **🎯 Serverless Adapters**: Custom business logic through pluggable serverless components  
- **📊 Real-time Processing**: Event-driven architecture for high-throughput message processing
- **🔒 Enterprise Security**: JWT authentication, multi-tenant isolation, and access controls
- **📈 Scalable**: Cloud-native design with Kubernetes support and horizontal scaling
- **🔍 Observable**: Complete audit trails, monitoring, and processing visibility

## 🏗️ Architecture

Bitween follows a hub-and-spoke architecture where messages flow through configurable processing pipelines:

```
External Systems → API Gateway → Processing Pipeline → Target Systems
                                      ↓
              Validation → Filtering → Mapping → Handling → Response
```

### Core Components

- **Web API**: RESTful endpoints for message ingestion and management
- **Processing Engine**: Asynchronous message processing with domain events
- **Serverless Framework**: Custom adapter execution environment
- **Multi-Database Support**: PostgreSQL, MySQL, SQL Server compatibility
- **Cloud Storage**: File persistence with configurable storage backends

## ⚡ Quick Start

### Prerequisites

- .NET 8.0 SDK
- Docker (optional)
- Database (PostgreSQL, MySQL, or SQL Server)

### 1. Clone the Repository

```bash
git clone https://github.com/simplify9/bitween.git
cd bitween
```

### 2. Run with Docker

```bash
# Build and run with Docker Compose
docker-compose up -d

# Or build Docker image manually
docker build -t bitween .
docker run -p 8080:8080 bitween
```

### 3. Local Development Setup

```bash
# Restore dependencies
dotnet restore

# Update database connection in appsettings.json
# Run database migrations
./migratedb.sh

# Start the application
dotnet run --project SW.Bitween.Web
```

The application will be available at `http://localhost:8080`

## 🌐 Ecosystem & Companion Projects

Bitween is part of a comprehensive integration ecosystem with additional tools and components:

### 🖥️ Bitween UI - Web Management Interface
A modern React-based web interface for managing and monitoring your Bitween integration platform.

- **Repository**: [BitweenUI](https://github.com/simplify9/BitweenUI)
- **Features**: Dashboard, configuration management, monitoring, audit logs
- **Technology**: React, TypeScript, Tailwind CSS, Redux Toolkit

#### Quick Setup
```bash
# Clone the UI repository
git clone https://github.com/simplify9/BitweenUI.git
cd BitweenUI

# Install dependencies
npm install

# Configure API endpoint (create .env file)
echo "REACT_APP_API_URL=http://localhost:8080" > .env

# Start development server
npm start
```

The UI will be available at `http://localhost:3000`

#### Production Deployment
```bash
# Build for production
npm run build

# Deploy with Docker
docker build -t bitween-ui .
docker run -p 3000:80 bitween-ui
```

### 🔌 Bitween Adapters - Pre-built Integration Components
A comprehensive collection of production-ready adapters for common integration scenarios.

- **Repository**: [BitweenAdapters](https://github.com/simplify9/BitweenAdapters)
- **License**: MIT (Open Source)
- **Technology**: .NET 6+, Serverless-ready

#### Available Adapters

**Handlers** (Business Logic & Output):
- **HTTP Handler**: REST API calls and webhooks
- **SMTP Handler**: Email notifications and bulk sending
- **FTP/SFTP Handler**: File uploads and transfers
- **Azure Blob Handler**: Cloud storage operations
- **S3 Handler**: AWS storage integration
- **Microsoft Teams Handler**: Teams notifications
- **SendGrid Handler**: Professional email delivery

**Receivers** (Data Input Sources):
- **FTP/SFTP Receiver**: Scheduled file polling
- **HTTP Receiver**: Webhook endpoints and API polling
- **POP3 Receiver**: Email processing
- **Azure Blob Receiver**: Cloud storage monitoring
- **S3 Receiver**: AWS storage file detection
- **Elasticsearch Receiver**: Search and analytics data

**Mappers** (Data Transformation):
- **Liquid Mapper**: Template-based transformation
- **JSON to Delimited Mapper**: Format conversion (CSV, TSV)

#### Quick Start with Adapters
```bash
# Clone the adapters repository
git clone https://github.com/simplify9/BitweenAdapters.git
cd BitweenAdapters

# Build specific adapter (example: SFTP Handler)
dotnet build SW.InfolinkAdapters.Handlers.Ftp/

# Run adapter locally
cd SW.InfolinkAdapters.Handlers.Ftp
dotnet run

# Deploy as Docker container
docker build -t bitween-sftp-handler .
docker run -p 7000:80 bitween-sftp-handler
```

#### Using Pre-built Adapters
Configure adapters in your Bitween subscriptions:

```json
{
  "name": "SFTP File Processing",
  "receiverEndpoint": "http://sftp-receiver:7000",
  "mapperEndpoint": "http://liquid-mapper:7001", 
  "handlerEndpoint": "http://sftp-handler:7002"
}
```

### 🆚 Ecosystem Comparison

| Component | Core Only | + UI | + Adapters | Complete Ecosystem |
|-----------|-----------|------|------------|-------------------|
| **Integration Platform** | ✅ | ✅ | ✅ | ✅ |
| **REST API** | ✅ | ✅ | ✅ | ✅ |
| **Web Dashboard** | ❌ | ✅ | ❌ | ✅ |
| **Visual Configuration** | ❌ | ✅ | ❌ | ✅ |
| **Pre-built Connectors** | ❌ | ❌ | ✅ | ✅ |
| **Monitoring & Analytics** | ❌ | ✅ | ❌ | ✅ |
| **User Management** | ❌ | ✅ | ❌ | ✅ |
| **Time to Production** | Days | Hours | Hours | Minutes |
| **Development Effort** | High | Medium | Low | Minimal |

> 💡 **Recommendation**: Use the complete ecosystem for the best developer experience and fastest time to production.

## 📚 Documentation

### 🌐 Ecosystem & Platform
- [**Ecosystem Overview**](docs/ecosystem.md) - Complete platform ecosystem and companion projects
- [**Architecture Overview**](docs/architecture.md) - System design and components
- [**Domain Model**](docs/domain-model.md) - Core entities and relationships
- [**Processing Pipeline**](docs/processing-pipeline.md) - Message flow and transformation

### 🛠️ Development Guides
- [**Quick Setup**](docs/quick-setup.md) - Get started in under 10 minutes
- [**Getting Started**](docs/getting-started.md) - Development environment setup
- [**API Reference**](docs/api-reference.md) - REST API documentation
- [**Custom Adapters**](docs/custom-adapters.md) - Building serverless components
- [**Configuration**](docs/configuration.md) - Application settings and options

### 🚀 Deployment
- [**Quick Setup**](docs/quick-setup.md) - Fast development setup
- [**Production Deployment**](docs/production-deployment.md) - Complete ecosystem deployment
- [**Docker Deployment**](docs/docker-deployment.md) - Container-based deployment
- [**Kubernetes**](docs/kubernetes-deployment.md) - Orchestrated deployment
- [**Database Setup**](docs/database-setup.md) - Multi-database configuration

### 💡 Examples
- [**Integration Patterns**](docs/integration-patterns.md) - Common usage scenarios
- [**Sample Adapters**](docs/sample-adapters.md) - Reference implementations

## 🔧 Key Features

### Message Processing
- **Document Types**: Define message schemas with promoted properties
- **Subscriptions**: Configure processing rules and routing logic
- **Filtering**: Route messages based on content and metadata
- **Transformation**: Custom data mapping and format conversion

### Integration Patterns
- **Synchronous**: Request-response API calls
- **Asynchronous**: Event-driven message processing
- **Scheduled**: Time-based data retrieval and batch processing
- **Aggregation**: Multi-message consolidation and reporting

### Extensibility
- **Custom Validators**: Input validation logic
- **Custom Mappers**: Data transformation rules
- **Custom Handlers**: Business processing logic
- **Custom Receivers**: External data source integration

## 🏢 Use Cases

- **B2B Integration**: EDI processing and partner data exchange
- **API Gateway**: Protocol translation and message routing
- **Microservice Integration**: Event-driven service communication
- **Data Synchronization**: Scheduled batch processing between systems
- **Workflow Automation**: Process orchestration and business rule execution

## 🤝 Contributing

We welcome contributions from the community! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

### Development Setup

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

### Areas for Contribution

- 🐛 Bug fixes and improvements
- 📚 Documentation enhancements
- 🔌 New adapter implementations
- 🧪 Test coverage expansion
- 🌐 Localization support

## 📞 Support & Community

- **Documentation**: [Comprehensive guides and API reference](docs/)
- **Issues**: [Report bugs and request features](https://github.com/simplify9/bitween/issues)
- **Discussions**: [Community discussions and Q&A](https://github.com/simplify9/bitween/discussions)
- **Company**: [Simplify9](https://www.simplify9.com/)

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

Built with ❤️ by [Simplify9](https://www.simplify9.com/) and the open-source community.

---

<div align="center">
  <p>⭐ Star this repository if you find Bitween useful!</p>
  <p>Made with ❤️ for the integration community</p>
</div>

