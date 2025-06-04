# Docker Deployment

This guide covers deploying Bitween using Docker containers, including single-container deployments, Docker Compose, and production considerations.

## Prerequisites

- Docker 20.10 or later
- Docker Compose 2.0 or later (optional)
- 2GB RAM minimum, 4GB recommended
- Database (PostgreSQL, MySQL, or SQL Server)

## Quick Start with Docker

### 1. Pull the Image

```bash
# Pull the latest stable image
docker pull simplify9/bitween:latest

# Or pull a specific version
docker pull simplify9/bitween:v1.0.0
```

### 2. Run with Existing Database

```bash
docker run -d \
  --name bitween \
  -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=your-db-host;Database=bitween;Username=user;Password=pass" \
  -e Jwt__Key="your-jwt-secret-key-minimum-32-characters-long" \
  -e FileStorage__Provider="Local" \
  -e FileStorage__BasePath="/app/storage" \
  -v bitween-storage:/app/storage \
  simplify9/bitween:latest
```

### 3. Access the Application

- Web UI: http://localhost:8080
- API: http://localhost:8080/api
- Swagger: http://localhost:8080/swagger

## Docker Compose Deployment

### Complete Stack with PostgreSQL

Create `docker-compose.yml`:

```yaml
version: '3.8'

services:
  bitween:
    image: simplify9/bitween:latest
    container_name: bitween-app
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=bitween;Username=bitween;Password=bitween123
      - Jwt__Key=your-super-secret-jwt-key-must-be-at-least-32-characters-long
      - Jwt__Issuer=bitween
      - Jwt__Audience=bitween-api
      - FileStorage__Provider=Local
      - FileStorage__BasePath=/app/storage
      - Logging__LogLevel__Default=Information
    volumes:
      - bitween-storage:/app/storage
      - bitween-logs:/app/logs
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

  postgres:
    image: postgres:15-alpine
    container_name: bitween-postgres
    environment:
      - POSTGRES_DB=bitween
      - POSTGRES_USER=bitween
      - POSTGRES_PASSWORD=bitween123
    volumes:
      - postgres-data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U bitween -d bitween"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    container_name: bitween-redis
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

volumes:
  postgres-data:
  redis-data:
  bitween-storage:
  bitween-logs:

networks:
  default:
    name: bitween-network
```

### Run the Stack

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f bitween

# Check status
docker-compose ps

# Stop all services
docker-compose down
```

## Building from Source

### 1. Clone Repository

```bash
git clone https://github.com/simplify9/bitween.git
cd bitween
```

### 2. Build Docker Image

```bash
# Build with default settings
docker build -t bitween:local .

# Build with specific tag
docker build -t bitween:v1.0.0 .

# Build with build arguments
docker build \
  --build-arg BUILD_CONFIGURATION=Release \
  --build-arg ASPNET_VERSION=8.0 \
  -t bitween:custom .
```

### 3. Multi-stage Build Details

The Dockerfile uses multi-stage builds for optimization:

```dockerfile
# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

# Build image with SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SW.Bitween.Web/SW.Bitween.Web.csproj", "SW.Bitween.Web/"]
COPY ["SW.Bitween.Api/SW.Bitween.Api.csproj", "SW.Bitween.Api/"]
# ... other project files
RUN dotnet restore "SW.Bitween.Web/SW.Bitween.Web.csproj"

COPY . .
WORKDIR "/src/SW.Bitween.Web"
RUN dotnet build "SW.Bitween.Web.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "SW.Bitween.Web.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SW.Bitween.Web.dll"]
```

## Configuration

### Environment Variables

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | Development | No |
| `ConnectionStrings__DefaultConnection` | Database connection string | - | Yes |
| `Jwt__Key` | JWT signing key (min 32 chars) | - | Yes |
| `Jwt__Issuer` | JWT issuer | bitween | No |
| `Jwt__Audience` | JWT audience | bitween-api | No |
| `FileStorage__Provider` | File storage provider (Local/Azure/AWS) | Local | No |
| `FileStorage__BasePath` | Local storage path | /app/storage | No |
| `Logging__LogLevel__Default` | Log level | Information | No |

### Database Providers

#### PostgreSQL

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Host=postgres;Database=bitween;Username=bitween;Password=pass123
  - DatabaseProvider=PostgreSQL
```

#### MySQL

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Server=mysql;Database=bitween;Uid=bitween;Pwd=pass123
  - DatabaseProvider=MySQL
```

#### SQL Server

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Server=sqlserver;Database=bitween;User Id=sa;Password=Pass123!
  - DatabaseProvider=SqlServer
```

### File Storage Configuration

#### Local Storage

```yaml
environment:
  - FileStorage__Provider=Local
  - FileStorage__BasePath=/app/storage
volumes:
  - bitween-storage:/app/storage
```

#### Azure Blob Storage

```yaml
environment:
  - FileStorage__Provider=Azure
  - FileStorage__Azure__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...
  - FileStorage__Azure__ContainerName=bitween-files
```

#### AWS S3

```yaml
environment:
  - FileStorage__Provider=AWS
  - FileStorage__AWS__AccessKey=AKIAIOSFODNN7EXAMPLE
  - FileStorage__AWS__SecretKey=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
  - FileStorage__AWS__Region=us-east-1
  - FileStorage__AWS__BucketName=bitween-files
```

## Production Deployment

### Docker Swarm

Create `docker-stack.yml`:

```yaml
version: '3.8'

services:
  bitween:
    image: simplify9/bitween:latest
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=${DATABASE_CONNECTION}
      - Jwt__Key=${JWT_SECRET_KEY}
    deploy:
      replicas: 3
      update_config:
        parallelism: 1
        delay: 10s
      restart_policy:
        condition: on-failure
        delay: 5s
        max_attempts: 3
      resources:
        limits:
          memory: 1G
          cpus: '0.5'
        reservations:
          memory: 512M
          cpus: '0.25'
    volumes:
      - bitween-storage:/app/storage
    networks:
      - bitween-network

  postgres:
    image: postgres:15-alpine
    environment:
      - POSTGRES_DB=bitween
      - POSTGRES_USER=bitween
      - POSTGRES_PASSWORD_FILE=/run/secrets/postgres_password
    volumes:
      - postgres-data:/var/lib/postgresql/data
    deploy:
      replicas: 1
      placement:
        constraints: [node.role == manager]
    secrets:
      - postgres_password
    networks:
      - bitween-network

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./ssl:/etc/nginx/ssl:ro
    deploy:
      replicas: 2
    depends_on:
      - bitween
    networks:
      - bitween-network

secrets:
  postgres_password:
    external: true

volumes:
  postgres-data:
  bitween-storage:

networks:
  bitween-network:
    driver: overlay
    attachable: true
```

Deploy the stack:

```bash
# Create secrets
echo "your-postgres-password" | docker secret create postgres_password -

# Deploy stack
docker stack deploy -c docker-stack.yml bitween

# Check services
docker service ls

# Scale services
docker service scale bitween_bitween=5
```

### Load Balancer Configuration

#### Nginx Configuration

Create `nginx.conf`:

```nginx
events {
    worker_connections 1024;
}

http {
    upstream bitween_backend {
        least_conn;
        server bitween:8080 max_fails=3 fail_timeout=30s;
    }

    server {
        listen 80;
        server_name your-domain.com;
        
        # Redirect HTTP to HTTPS
        return 301 https://$server_name$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name your-domain.com;

        # SSL Configuration
        ssl_certificate /etc/nginx/ssl/cert.pem;
        ssl_certificate_key /etc/nginx/ssl/key.pem;
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_ciphers HIGH:!aNULL:!MD5;

        # Security Headers
        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
        add_header X-Frame-Options DENY always;
        add_header X-Content-Type-Options nosniff always;
        add_header X-XSS-Protection "1; mode=block" always;

        # Proxy Configuration
        location / {
            proxy_pass http://bitween_backend;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection keep-alive;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_cache_bypass $http_upgrade;
            
            # Timeouts
            proxy_connect_timeout 30s;
            proxy_send_timeout 30s;
            proxy_read_timeout 30s;
            
            # File upload size
            client_max_body_size 100M;
        }

        # Health check endpoint
        location /health {
            proxy_pass http://bitween_backend/health;
            access_log off;
        }
    }
}
```

### Resource Requirements

#### Minimum Requirements

```yaml
deploy:
  resources:
    limits:
      memory: 512M
      cpus: '0.25'
    reservations:
      memory: 256M
      cpus: '0.1'
```

#### Recommended Production

```yaml
deploy:
  resources:
    limits:
      memory: 2G
      cpus: '1.0'
    reservations:
      memory: 1G
      cpus: '0.5'
```

## Monitoring and Logging

### Health Checks

```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 60s
```

### Logging Configuration

#### Centralized Logging with ELK Stack

```yaml
logging:
  driver: "json-file"
  options:
    max-size: "10m"
    max-file: "3"
    labels: "service,version"

# Or use Fluentd
logging:
  driver: fluentd
  options:
    fluentd-address: fluentd:24224
    tag: bitween.{{.Name}}
```

#### Log Volume Mount

```yaml
volumes:
  - ./logs:/app/logs
environment:
  - Logging__File__Path=/app/logs/bitween.log
  - Logging__File__RollingInterval=Day
  - Logging__File__RetainedFileCountLimit=30
```

## Security Considerations

### Secrets Management

```bash
# Create Docker secrets
echo "your-jwt-key" | docker secret create jwt_key -
echo "db-password" | docker secret create db_password -

# Use in compose file
secrets:
  - jwt_key
  - db_password

environment:
  - Jwt__Key_FILE=/run/secrets/jwt_key
  - Database__Password_FILE=/run/secrets/db_password
```

### Network Security

```yaml
networks:
  frontend:
    driver: overlay
  backend:
    driver: overlay
    internal: true  # No external access

services:
  bitween:
    networks:
      - frontend
      - backend
  
  postgres:
    networks:
      - backend  # Only internal network
```

### SSL/TLS Configuration

Mount SSL certificates:

```yaml
volumes:
  - ./ssl/cert.pem:/app/ssl/cert.pem:ro
  - ./ssl/key.pem:/app/ssl/key.pem:ro

environment:
  - ASPNETCORE_URLS=https://+:443;http://+:80
  - ASPNETCORE_Kestrel__Certificates__Default__Path=/app/ssl/cert.pem
  - ASPNETCORE_Kestrel__Certificates__Default__KeyPath=/app/ssl/key.pem
```

## Backup and Recovery

### Database Backup

```bash
# PostgreSQL backup
docker exec bitween-postgres pg_dump -U bitween bitween > backup.sql

# Restore
docker exec -i bitween-postgres psql -U bitween bitween < backup.sql
```

### File Storage Backup

```bash
# Create backup of storage volume
docker run --rm -v bitween-storage:/data -v $(pwd):/backup alpine tar czf /backup/storage-backup.tar.gz -C /data .

# Restore backup
docker run --rm -v bitween-storage:/data -v $(pwd):/backup alpine tar xzf /backup/storage-backup.tar.gz -C /data
```

## Troubleshooting

### Common Issues

1. **Container won't start**
   ```bash
   # Check logs
   docker logs bitween-app
   
   # Check configuration
   docker exec bitween-app env | grep ConnectionStrings
   ```

2. **Database connection failed**
   ```bash
   # Test database connectivity
   docker exec bitween-app curl -f http://localhost:8080/health/db
   
   # Check network connectivity
   docker exec bitween-app ping postgres
   ```

3. **File permissions**
   ```bash
   # Fix storage permissions
   docker exec bitween-app chown -R app:app /app/storage
   ```

### Performance Tuning

#### Database Connection Pooling

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Host=postgres;Database=bitween;Username=bitween;Password=pass;Pooling=true;MinPoolSize=5;MaxPoolSize=100
```

#### Memory Limits

```yaml
deploy:
  resources:
    limits:
      memory: 2G
    reservations:
      memory: 1G
```

#### JVM Tuning (if applicable)

```yaml
environment:
  - DOTNET_gcServer=1
  - DOTNET_GCRetainVM=1
  - DOTNET_EnableDiagnostics=0  # Disable for production
```

This guide provides comprehensive coverage of Docker deployment scenarios for Bitween, from development to production environments.
