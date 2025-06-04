# Getting Started

This guide will help you set up Bitween for development and understand the basic concepts through practical examples.

## Prerequisites

Before you begin, ensure you have the following installed:

- **.NET 8.0 SDK** or later
- **Docker** (optional, for containerized deployment)
- **Git** for source control
- **Database** (PostgreSQL, MySQL, or SQL Server)
- **IDE/Editor** (Visual Studio, VS Code, or JetBrains Rider)
- **Node.js 18+** (if using the Bitween UI)

## Installation

### 1. Clone the Repositories

```bash
# Clone the main Bitween platform
git clone https://github.com/simplify9/bitween.git
cd bitween

# Optionally clone the UI and adapters
git clone https://github.com/simplify9/BitweenUI.git
git clone https://github.com/simplify9/BitweenAdapters.git
```

### 2. Development Environment Setup

#### Option A: Local Development

```bash
# Restore NuGet packages
dotnet restore

# Build the solution
dotnet build

# Run tests to verify setup
dotnet test
```

#### Option B: Docker Development

```bash
# Build Docker image
docker build -t bitween .

# Run with Docker Compose (includes database)
docker-compose up -d
```

### 3. Database Configuration

#### PostgreSQL (Recommended)

```bash
# Install PostgreSQL locally or use Docker
docker run --name bitween-postgres -e POSTGRES_PASSWORD=password -e POSTGRES_DB=bitween -p 5432:5432 -d postgres:15

# Update connection string in appsettings.json
```

#### Connection String Format

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=bitween;Username=postgres;Password=password"
  }
}
```

### 4. Run Database Migrations

```bash
# Make migration script executable
chmod +x migratedb.sh

# Run migrations
./migratedb.sh
```

### 5. Start the Application

```bash
# Navigate to web project
cd SW.Bitween.Web

# Run the application
dotnet run
```

The application will be available at:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- Swagger UI: `http://localhost:5000/swagger`

## Setting Up the Bitween UI (Optional)

For a better management experience, you can set up the Bitween UI which provides a modern web interface for managing your integration platform.

### 1. Clone and Setup UI

```bash
# In a new terminal window, clone the UI repository
git clone https://github.com/simplify9/BitweenUI.git
cd BitweenUI

# Install dependencies
npm install
```

### 2. Configure Environment

```bash
# Create environment configuration
cat > .env.local << EOF
REACT_APP_API_URL=http://localhost:5000
REACT_APP_AUTH_CLIENT_ID=your-azure-ad-client-id
REACT_APP_AUTH_TENANT_ID=your-azure-ad-tenant-id
EOF
```

### 3. Start the UI

```bash
# Start the development server
npm start
```

The UI will be available at `http://localhost:3000` and will connect to your Bitween API running on port 5000.

### 4. UI Features

Once running, you can access:
- **Dashboard**: Real-time processing metrics and system health
- **Configuration**: Manage document types, partners, and subscriptions
- **Monitoring**: Track message flows and processing status
- **Audit Logs**: View detailed processing history and troubleshooting

## Basic Configuration

### Environment Settings

Create or update `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=bitween;Username=postgres;Password=password"
  },
  "Jwt": {
    "Key": "your-secret-key-here-must-be-at-least-32-chars",
    "Issuer": "bitween",
    "Audience": "bitween-api"
  },
  "FileStorage": {
    "Provider": "Local",
    "BasePath": "./storage"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### Creating Your First Partner

Partners represent external systems that will integrate with Bitween.

```bash
# Use the API to create a partner
curl -X POST "http://localhost:5000/api/partners" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "My First Partner",
    "description": "Test partner for development"
  }'
```

## First Integration Example

Let's create a simple message processing workflow:

### 1. Define a Document Type

Documents define the structure and properties of messages:

```bash
curl -X POST "http://localhost:5000/api/documents" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "CustomerOrder",
    "format": "JSON",
    "promotedProperties": {
      "customerId": "$.customer.id",
      "orderTotal": "$.order.total",
      "orderDate": "$.order.date"
    }
  }'
```

### 2. Create a Subscription

Subscriptions define how messages should be processed:

```bash
curl -X POST "http://localhost:5000/api/subscriptions" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Process Customer Orders",
    "documentId": 1,
    "partnerId": 1,
    "type": "Internal",
    "filterExpression": {
      "customerId": { "operator": "exists" }
    },
    "handlerEndpoint": "http://localhost:7000"
  }'
```

### 3. Send Your First Message

```bash
curl -X POST "http://localhost:5000/api/xchanges" \
  -H "Content-Type: application/json" \
  -d '{
    "documentId": 1,
    "reference": "ORDER-001",
    "data": {
      "customer": {
        "id": "CUST-123",
        "name": "John Doe"
      },
      "order": {
        "total": 299.99,
        "date": "2025-06-04T10:30:00Z",
        "items": [
          {"product": "Widget A", "quantity": 2, "price": 149.99}
        ]
      }
    }
  }'
```

### 4. Check Processing Results

```bash
# Get exchange details
curl "http://localhost:5000/api/xchanges/1"

# Get processing results
curl "http://localhost:5000/api/xchanges/1/results"
```

## Building Custom Adapters

Bitween's power comes from custom serverless adapters. Here's how to create them:

### Sample Handler Structure

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System.Threading.Tasks;

namespace MyCustomHandler
{
    public class OrderHandler : IInfolinkHandler
    {
        public OrderHandler()
        {
            // Configure expected content type
            Runner.Expect("ContentType", "application/json");
        }

        public async Task<XchangeFile> Handle(XchangeFile input)
        {
            // Your business logic here
            var orderData = input.Data;
            
            // Process the order
            var result = await ProcessOrder(orderData);
            
            // Return processed result
            return new XchangeFile 
            { 
                Data = result,
                ContentType = "application/json"
            };
        }

        private async Task<object> ProcessOrder(object orderData)
        {
            // Implement your order processing logic
            return new { status = "processed", orderId = Guid.NewGuid() };
        }
    }
}
```

### Sample Validator

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using FluentValidation;
using System.Threading.Tasks;

namespace MyCustomValidator
{
    public class OrderValidator : IInfolinkValidator
    {
        public async Task<object> Validate(XchangeFile input)
        {
            var validator = new OrderDataValidator();
            var orderData = JsonSerializer.Deserialize<OrderData>(input.Data.ToString());
            
            var result = await validator.ValidateAsync(orderData);
            
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
            
            return orderData;
        }
    }

    public class OrderDataValidator : AbstractValidator<OrderData>
    {
        public OrderDataValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.OrderTotal).GreaterThan(0);
            RuleFor(x => x.OrderDate).NotEmpty();
        }
    }
}
```

## Development Workflow

### 1. Project Structure Understanding

```
SW.Bitween.Web/          # Main web application
SW.Bitween.Api/          # Core business logic
SW.Bitween.Sdk/          # Client SDK
SW.Bitween.PgSql/        # PostgreSQL data layer
SW.Bitween.SampleHandler/ # Example handler
```

### 2. Making Changes

```bash
# Create a feature branch
git checkout -b feature/my-new-feature

# Make your changes
# ... edit files ...

# Build and test
dotnet build
dotnet test

# Run the application
dotnet run --project SW.Bitween.Web
```

### 3. Testing Your Changes

```bash
# Run unit tests
dotnet test SW.Bitween.UnitTests

# Test API endpoints
curl -X GET "http://localhost:5000/api/health"

# Check Swagger documentation
# Navigate to http://localhost:5000/swagger
```

## Common Development Tasks

### Adding a New API Endpoint

1. Create controller in `SW.Bitween.Web/Controllers/`
2. Add service logic in `SW.Bitween.Api/Services/`
3. Update any required models in `SW.Bitween.Sdk/Model/`

### Adding Database Changes

1. Modify entities in `SW.Bitween.Api/Domain/`
2. Update `DbContext` if needed
3. Run migrations:
   ```bash
   dotnet ef migrations add MyMigration --project SW.Bitween.PgSql
   ```

### Adding Custom Business Logic

1. Create new adapter project
2. Implement required interfaces
3. Deploy as serverless function
4. Configure subscription to use new endpoint

## Debugging and Troubleshooting

### Common Issues

1. **Database Connection Failed**
   - Check connection string
   - Verify database is running
   - Run migrations

2. **Build Errors**
   - Restore NuGet packages: `dotnet restore`
   - Clean and rebuild: `dotnet clean && dotnet build`

3. **Authentication Issues**
   - Check JWT configuration
   - Verify API keys
   - Check partner setup

### Logging

Enable detailed logging in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "SW.Bitween": "Trace"
    }
  }
}
```

### Health Checks

Monitor application health:

```bash
# Application health
curl "http://localhost:5000/health"

# Database health
curl "http://localhost:5000/health/db"
```

## Next Steps

- Review the [Architecture Overview](architecture.md) to understand system design
- Explore [API Reference](api-reference.md) for detailed endpoint documentation
- Learn about [Custom Adapters](custom-adapters.md) for advanced integrations
- Check out [Integration Patterns](integration-patterns.md) for common scenarios

## Getting Help

- **Documentation**: Check the `docs/` folder for detailed guides
- **Issues**: Report bugs on GitHub Issues
- **Discussions**: Join community discussions
- **Examples**: Review sample adapters in the repository
