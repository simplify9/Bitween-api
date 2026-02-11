# Bitween Development Configuration Guide

## Prerequisites

Before running Bitween in development mode, ensure you have:

### 1. **PostgreSQL Database**
```bash
# Using Docker
docker run --name bitween-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bitween_dev -p 5432:5432 -d postgres:15

# Or install locally on macOS
brew install postgresql@15
brew services start postgresql@15
createdb bitween_dev
```

### 2. **MinIO (S3-Compatible Storage)**
```bash
# Using Docker
docker run --name bitween-minio \
  -p 9000:9000 \
  -p 9001:9001 \
  -e MINIO_ROOT_USER=minioadmin \
  -e MINIO_ROOT_PASSWORD=minioadmin \
  -d minio/minio server /data --console-address ":9001"

# Access MinIO Console at http://localhost:9001
# Login: minioadmin / minioadmin
# Create bucket named: bitween-dev
```

### 3. **RabbitMQ (Message Bus)**
```bash
# Using Docker
docker run --name bitween-rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  -d rabbitmq:3-management

# Access RabbitMQ Management UI at http://localhost:15672
# Login: guest / guest
```

## Quick Start with Docker Compose

Create a `docker-compose.yml` file in the root:

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:15
    container_name: bitween-postgres
    environment:
      POSTGRES_DB: bitween_dev
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  minio:
    image: minio/minio
    container_name: bitween-minio
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - minio_data:/data

  rabbitmq:
    image: rabbitmq:3-management
    container_name: bitween-rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

volumes:
  postgres_data:
  minio_data:
  rabbitmq_data:
```

Start all services:
```bash
docker-compose up -d
```

## Configuration Sections Explained

### **ConnectionStrings**
- **BitweenDb**: PostgreSQL connection string
  - Format: `Host=localhost;Port=5432;Database=bitween_dev;Username=postgres;Password=postgres`
  - Change `DatabaseType` in Bitween section if using MySQL or SQL Server

### **Bitween Section**
Core platform configuration:
- **DatabaseType**: `PgSql` | `MySql` | `MsSql`
- **AdminCredentials**: Format `username:password` for initial admin user
- **StorageProvider**: `S3` | `AS` (Azure Storage) | `OC` (Oracle Cloud)
- **DocumentPrefix**: Path prefix for file storage (e.g., `bitween-dev/documents`)
- **QueuePrefix**: RabbitMQ queue prefix (e.g., `bitween-dev`)
- **JwtExpiryMinutes**: JWT token expiration (1440 = 24 hours)

### **S3CloudFiles Section**
MinIO/S3 configuration for file storage:
- **ServiceUrl**: MinIO endpoint (http://localhost:9000 for local)
- **AccessKeyId**: MinIO access key (minioadmin)
- **SecretAccessKey**: MinIO secret key (minioadmin)
- **BucketName**: S3 bucket name (bitween-dev)
- **ForcePathStyle**: true for MinIO, false for AWS S3

### **Bus Section**
RabbitMQ message bus configuration:
- **ConnectionString**: `amqp://guest:guest@localhost:5672`
- For production, use proper credentials and connection pooling

### **Serverless Section**
Adapter execution configuration:
- **AdapterRemotePath**: Path in S3 where external adapters are stored
- **CommandTimeout**: Timeout for adapter execution (300 seconds)

### **JwtTokenParameters**
Authentication configuration:
- **Key**: Must be at least 32 characters
- **Issuer**: Token issuer identifier
- **Audience**: Token audience identifier

## Database Migration

After configuration, run database migrations:

```bash
cd SW.Bitween.Web

# For PostgreSQL
dotnet ef database update --context PgSql.BitweenDbContext

# For MySQL
dotnet ef database update --context MySql.BitweenDbContext

# For SQL Server
dotnet ef database update --context MsSql.BitweenDbContext
```

Or use the migration script:
```bash
./migratedb.sh
```

## Running Bitween

```bash
cd SW.Bitween.Web
dotnet run
```

The API will be available at:
- HTTP: http://localhost:5000
- HTTPS: https://localhost:5001
- Swagger: http://localhost:5000/swagger

## Default Admin Login

After first run, log in with:
- **Username**: admin
- **Password**: Admin@123456

Change this immediately in production!

## Troubleshooting

### Port Already in Use
```bash
# Check what's using the port
lsof -i :5000
# Kill the process
kill -9 <PID>
```

### Database Connection Failed
- Ensure PostgreSQL is running: `pg_isready`
- Check connection string in appsettings.Development.json
- Verify database exists: `psql -l`

### MinIO Connection Failed
- Check MinIO is running: `curl http://localhost:9000/minio/health/live`
- Verify bucket exists via MinIO Console (http://localhost:9001)
- Check ForcePathStyle=true in configuration

### RabbitMQ Connection Failed
- Check RabbitMQ is running: `curl http://localhost:15672`
- Verify guest user is enabled
- Check firewall/network settings

## Environment-Specific Settings

### For MySQL
```json
{
  "ConnectionStrings": {
    "BitweenDb": "Server=localhost;Database=bitween_dev;Uid=root;Pwd=password;"
  },
  "Bitween": {
    "DatabaseType": "MySql"
  }
}
```

### For SQL Server
```json
{
  "ConnectionStrings": {
    "BitweenDb": "Server=localhost;Database=bitween_dev;User Id=sa;Password=YourStrong@Passw0rd;"
  },
  "Bitween": {
    "DatabaseType": "MsSql"
  }
}
```

### For Azure Storage
```json
{
  "Bitween": {
    "StorageProvider": "AS"
  },
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "ContainerName": "bitween-dev"
  }
}
```

## Production Considerations

1. **Change default passwords** in all services
2. **Use environment variables** for sensitive data
3. **Enable SSL/TLS** for all connections
4. **Set UseAzureManagedIdentity=true** when running in Azure
5. **Configure proper logging** (ElasticSearch URL in SWLogger section)
6. **Use production-grade message broker** settings
7. **Set AreXChangeFilesPrivate=true** for sensitive data
8. **Reduce BusDefaultQueuePrefetch** in high-load scenarios

## Next Steps

1. Start the UI project (Bitween-UI)
2. Create your first Document
3. Configure Partners
4. Set up Subscriptions
5. Deploy external adapters or use native adapters (native.http)

For more details, see the [full documentation](../docs/getting-started.md).
