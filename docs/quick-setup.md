# Quick Setup Guide

This guide will help you get Bitween running with all ecosystem components in under 10 minutes.

## Prerequisites

Ensure you have these tools installed:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- [Docker](https://www.docker.com/get-started) (optional but recommended)
- [Git](https://git-scm.com/)

## Option 1: Full Setup with UI (Recommended)

### Step 1: Clone All Repositories

```bash
# Create workspace directory
mkdir bitween-workspace && cd bitween-workspace

# Clone all repositories
git clone https://github.com/simplify9/Bitween.git
git clone https://github.com/simplify9/BitweenUI.git
git clone https://github.com/simplify9/BitweenAdapters.git
```

### Step 2: Start Database

```bash
# Start PostgreSQL with Docker
docker run --name bitween-db \
  -e POSTGRES_PASSWORD=bitween123 \
  -e POSTGRES_DB=bitween \
  -p 5432:5432 \
  -d postgres:15
```

### Step 3: Setup and Start Bitween Core

```bash
cd Bitween

# Configure database connection
cat > SW.Bitween.Web/appsettings.Development.json << 'EOF'
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=bitween;Username=postgres;Password=bitween123"
  },
  "Jwt": {
    "Key": "bitween-super-secret-key-for-development-use-only-32chars",
    "Issuer": "bitween",
    "Audience": "bitween-api"
  },
  "FileStorage": {
    "Provider": "Local",
    "BasePath": "./storage"
  }
}
EOF

# Restore packages and build
dotnet restore
dotnet build

# Run database migrations
chmod +x migratedb.sh
./migratedb.sh

# Start the application
cd SW.Bitween.Web
dotnet run
```

### Step 4: Setup and Start Bitween UI

```bash
# In a new terminal
cd ../BitweenUI

# Install dependencies
npm install

# Configure environment
cat > .env.local << 'EOF'
REACT_APP_API_URL=http://localhost:5000
REACT_APP_AUTH_CLIENT_ID=bitween-dev
REACT_APP_AUTH_TENANT_ID=common
EOF

# Start the UI
npm start
```

### Step 5: Access Your Installation

After both applications start:

- **Bitween Core API**: http://localhost:5000
- **Swagger Documentation**: http://localhost:5000/swagger
- **Bitween UI Dashboard**: http://localhost:3000

## Option 2: Docker Compose (Fastest)

For the quickest setup, use Docker Compose:

```bash
# Clone main repository
git clone https://github.com/simplify9/Bitween.git
cd Bitween

# Start everything with Docker Compose
docker-compose up -d

# Check status
docker-compose ps
```

Services will be available at:
- **Bitween API**: http://localhost:8080
- **Database**: localhost:5432

## Option 3: Core Only (Minimal)

If you only need the core integration platform:

```bash
# Clone and setup
git clone https://github.com/simplify9/Bitween.git
cd Bitween

# Quick database setup
docker run --name bitween-db \
  -e POSTGRES_PASSWORD=bitween123 \
  -e POSTGRES_DB=bitween \
  -p 5432:5432 \
  -d postgres:15

# Configure and run
dotnet restore
./migratedb.sh
cd SW.Bitween.Web && dotnet run
```

## Verify Installation

### 1. Test API Health

```bash
# Check if API is responding
curl http://localhost:5000/health

# Expected response: {"status":"Healthy"}
```

### 2. Access Swagger UI

Open http://localhost:5000/swagger in your browser to explore the API documentation.

### 3. Create First Integration

Using the API or UI, create your first document type and subscription:

```bash
# Create a document type
curl -X POST http://localhost:5000/api/documents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "TestDocument",
    "description": "My first document type"
  }'
```

## Pre-built Adapters Setup

To use pre-built adapters for common scenarios:

```bash
cd ../BitweenAdapters

# Build specific adapter (example: HTTP Handler)
cd SW.InfolinkAdapters.Handlers.Http
dotnet build

# Run adapter
dotnet run --urls="http://localhost:7000"
```

## Troubleshooting

### Common Issues

#### Database Connection Failed
```bash
# Check if PostgreSQL is running
docker ps | grep postgres

# Check logs
docker logs bitween-db
```

#### Port Already in Use
```bash
# Check what's using port 5000
lsof -i :5000

# Use different port
dotnet run --urls="http://localhost:5001"
```

#### .NET SDK Not Found
```bash
# Check .NET version
dotnet --version

# Should be 8.0 or higher
```

#### Node.js Issues
```bash
# Check Node.js version
node --version

# Should be 18.0 or higher
```

### Getting Help

- **Documentation**: [Full documentation](docs/)
- **Issues**: [GitHub Issues](https://github.com/simplify9/bitween/issues)
- **Discussions**: [GitHub Discussions](https://github.com/simplify9/bitween/discussions)

## Next Steps

Once everything is running:

1. **Explore the UI**: Navigate through the dashboard and configuration screens
2. **Read the Documentation**: Check out [getting-started.md](getting-started.md) for detailed guides
3. **Try Sample Integrations**: Follow [integration-patterns.md](integration-patterns.md) for common scenarios
4. **Build Custom Adapters**: See [custom-adapters.md](custom-adapters.md) for extending functionality
5. **Join the Community**: Contribute to the project and help others

---

**⏱️ Total Setup Time**: 5-10 minutes
**🚀 You're ready to start integrating!**
