# Production Deployment Guide

This guide covers deploying the complete Bitween ecosystem (Core, UI, and Adapters) to production environments.

## Deployment Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Load Balancer │    │   Bitween UI    │    │ Bitween Adapters│
│   (nginx/ALB)   │───▶│   (React SPA)   │    │  (Microservices)│
└─────────────────┘    └─────────────────┘    └─────────────────┘
          │                       │                       │
          │                       ▼                       │
          │            ┌─────────────────┐                │
          └───────────▶│ Bitween Core API│◀───────────────┘
                       │  (.NET Web API) │
                       └─────────────────┘
                                 │
                                 ▼
                       ┌─────────────────┐
                       │    Database     │
                       │ (PostgreSQL)    │
                       └─────────────────┘
```

## Option 1: Docker Compose Deployment

### Complete Stack with Docker Compose

Create a production-ready `docker-compose.prod.yml`:

```yaml
version: '3.8'

services:
  # Database
  bitween-db:
    image: postgres:15
    environment:
      POSTGRES_DB: bitween
      POSTGRES_USER: bitween
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init.sql:/docker-entrypoint-initdb.d/init.sql
    ports:
      - "5432:5432"
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U bitween"]
      interval: 30s
      timeout: 10s
      retries: 3

  # Bitween Core API
  bitween-api:
    build:
      context: ./Bitween
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Host=bitween-db;Database=bitween;Username=bitween;Password=${POSTGRES_PASSWORD}
      - Jwt__Key=${JWT_SECRET_KEY}
      - Jwt__Issuer=bitween-prod
      - Jwt__Audience=bitween-api
      - FileStorage__Provider=Local
      - FileStorage__BasePath=/app/storage
    volumes:
      - bitween_storage:/app/storage
    ports:
      - "8080:8080"
    depends_on:
      bitween-db:
        condition: service_healthy
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  # Bitween UI
  bitween-ui:
    build:
      context: ./BitweenUI
      dockerfile: Dockerfile
      args:
        - REACT_APP_API_URL=${API_URL}
        - REACT_APP_AUTH_CLIENT_ID=${AUTH_CLIENT_ID}
        - REACT_APP_AUTH_TENANT_ID=${AUTH_TENANT_ID}
    ports:
      - "3000:80"
    depends_on:
      - bitween-api
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--spider", "http://localhost:80"]
      interval: 30s
      timeout: 10s
      retries: 3

  # Pre-built Adapters
  sftp-handler:
    build:
      context: ./BitweenAdapters/SW.InfolinkAdapters.Handlers.Ftp
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    ports:
      - "7001:80"
    restart: unless-stopped

  http-handler:
    build:
      context: ./BitweenAdapters/SW.InfolinkAdapters.Handlers.Http
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    ports:
      - "7002:80"
    restart: unless-stopped

  email-handler:
    build:
      context: ./BitweenAdapters/SW.InfolinkAdapters.Handlers.Smtp
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - SMTP_HOST=${SMTP_HOST}
      - SMTP_PORT=${SMTP_PORT}
      - SMTP_USERNAME=${SMTP_USERNAME}
      - SMTP_PASSWORD=${SMTP_PASSWORD}
    ports:
      - "7003:80"
    restart: unless-stopped

  # Reverse Proxy
  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
      - ./ssl:/etc/nginx/ssl
    depends_on:
      - bitween-ui
      - bitween-api
    restart: unless-stopped

volumes:
  postgres_data:
  bitween_storage:

networks:
  default:
    driver: bridge
```

### Environment Configuration

Create `.env.prod`:

```bash
# Database
POSTGRES_PASSWORD=your-secure-database-password

# JWT Configuration
JWT_SECRET_KEY=your-super-secure-jwt-key-at-least-32-characters-long

# API Configuration
API_URL=https://api.yourcompany.com

# Authentication (Azure AD)
AUTH_CLIENT_ID=your-azure-ad-client-id
AUTH_TENANT_ID=your-azure-ad-tenant-id

# Email Configuration
SMTP_HOST=smtp.yourcompany.com
SMTP_PORT=587
SMTP_USERNAME=notifications@yourcompany.com
SMTP_PASSWORD=your-smtp-password
```

### Nginx Configuration

Create `nginx.conf`:

```nginx
events {
    worker_connections 1024;
}

http {
    upstream bitween-api {
        server bitween-api:8080;
    }

    upstream bitween-ui {
        server bitween-ui:80;
    }

    # UI Server
    server {
        listen 80;
        server_name yourcompany.com www.yourcompany.com;
        
        # Redirect to HTTPS
        return 301 https://$server_name$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name yourcompany.com www.yourcompany.com;

        ssl_certificate /etc/nginx/ssl/cert.pem;
        ssl_certificate_key /etc/nginx/ssl/key.pem;

        # Security headers
        add_header X-Frame-Options DENY;
        add_header X-Content-Type-Options nosniff;
        add_header X-XSS-Protection "1; mode=block";

        # Serve UI
        location / {
            proxy_pass http://bitween-ui;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }

    # API Server
    server {
        listen 80;
        server_name api.yourcompany.com;
        
        # Redirect to HTTPS
        return 301 https://$server_name$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name api.yourcompany.com;

        ssl_certificate /etc/nginx/ssl/cert.pem;
        ssl_certificate_key /etc/nginx/ssl/key.pem;

        # API routes
        location / {
            proxy_pass http://bitween-api;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            
            # CORS headers
            add_header Access-Control-Allow-Origin https://yourcompany.com;
            add_header Access-Control-Allow-Methods "GET, POST, PUT, DELETE, OPTIONS";
            add_header Access-Control-Allow-Headers "Authorization, Content-Type";
        }
    }
}
```

### Deploy with Docker Compose

```bash
# Clone all repositories
git clone https://github.com/simplify9/Bitween.git
git clone https://github.com/simplify9/BitweenUI.git
git clone https://github.com/simplify9/BitweenAdapters.git

# Setup environment
cp .env.prod .env

# Deploy the stack
docker-compose -f docker-compose.prod.yml up -d

# Check status
docker-compose -f docker-compose.prod.yml ps
```

## Option 2: Kubernetes Deployment

### Namespace Setup

```yaml
# namespace.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: bitween
---
apiVersion: v1
kind: Secret
metadata:
  name: bitween-secrets
  namespace: bitween
type: Opaque
stringData:
  postgres-password: "your-secure-password"
  jwt-secret: "your-jwt-secret-key"
  smtp-password: "your-smtp-password"
```

### Database Deployment

```yaml
# postgres.yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-pvc
  namespace: bitween
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 20Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
  namespace: bitween
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
      - name: postgres
        image: postgres:15
        env:
        - name: POSTGRES_DB
          value: bitween
        - name: POSTGRES_USER
          value: bitween
        - name: POSTGRES_PASSWORD
          valueFrom:
            secretKeyRef:
              name: bitween-secrets
              key: postgres-password
        ports:
        - containerPort: 5432
        volumeMounts:
        - name: postgres-storage
          mountPath: /var/lib/postgresql/data
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
      volumes:
      - name: postgres-storage
        persistentVolumeClaim:
          claimName: postgres-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: postgres-service
  namespace: bitween
spec:
  selector:
    app: postgres
  ports:
  - port: 5432
    targetPort: 5432
```

### Bitween Core API

```yaml
# bitween-api.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bitween-api
  namespace: bitween
spec:
  replicas: 3
  selector:
    matchLabels:
      app: bitween-api
  template:
    metadata:
      labels:
        app: bitween-api
    spec:
      containers:
      - name: bitween-api
        image: bitween:latest
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          value: "Host=postgres-service;Database=bitween;Username=bitween;Password=$(POSTGRES_PASSWORD)"
        - name: POSTGRES_PASSWORD
          valueFrom:
            secretKeyRef:
              name: bitween-secrets
              key: postgres-password
        - name: Jwt__Key
          valueFrom:
            secretKeyRef:
              name: bitween-secrets
              key: jwt-secret
        ports:
        - containerPort: 8080
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: bitween-api-service
  namespace: bitween
spec:
  selector:
    app: bitween-api
  ports:
  - port: 8080
    targetPort: 8080
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: bitween-api-ingress
  namespace: bitween
  annotations:
    kubernetes.io/ingress.class: nginx
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - api.yourcompany.com
    secretName: bitween-api-tls
  rules:
  - host: api.yourcompany.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: bitween-api-service
            port:
              number: 8080
```

### Bitween UI

```yaml
# bitween-ui.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bitween-ui
  namespace: bitween
spec:
  replicas: 2
  selector:
    matchLabels:
      app: bitween-ui
  template:
    metadata:
      labels:
        app: bitween-ui
    spec:
      containers:
      - name: bitween-ui
        image: bitween-ui:latest
        ports:
        - containerPort: 80
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "200m"
---
apiVersion: v1
kind: Service
metadata:
  name: bitween-ui-service
  namespace: bitween
spec:
  selector:
    app: bitween-ui
  ports:
  - port: 80
    targetPort: 80
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: bitween-ui-ingress
  namespace: bitween
  annotations:
    kubernetes.io/ingress.class: nginx
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - yourcompany.com
    secretName: bitween-ui-tls
  rules:
  - host: yourcompany.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: bitween-ui-service
            port:
              number: 80
```

### Deploy to Kubernetes

```bash
# Create namespace and secrets
kubectl apply -f namespace.yaml

# Deploy database
kubectl apply -f postgres.yaml

# Wait for database to be ready
kubectl wait --for=condition=ready pod -l app=postgres -n bitween --timeout=300s

# Deploy API
kubectl apply -f bitween-api.yaml

# Deploy UI
kubectl apply -f bitween-ui.yaml

# Check deployment status
kubectl get pods -n bitween
kubectl get services -n bitween
kubectl get ingress -n bitween
```

## Option 3: Cloud-Specific Deployments

### Azure Container Apps

```bash
# Create resource group
az group create --name bitween-rg --location eastus

# Create Container Apps environment
az containerapp env create \
  --name bitween-env \
  --resource-group bitween-rg \
  --location eastus

# Deploy database (Azure Database for PostgreSQL)
az postgres flexible-server create \
  --resource-group bitween-rg \
  --name bitween-db \
  --admin-user bitween \
  --admin-password your-password \
  --sku-name Standard_B1ms

# Deploy Bitween API
az containerapp create \
  --name bitween-api \
  --resource-group bitween-rg \
  --environment bitween-env \
  --image bitween:latest \
  --target-port 8080 \
  --ingress external \
  --env-vars ConnectionStrings__DefaultConnection="Host=bitween-db.postgres.database.azure.com;Database=bitween;Username=bitween;Password=your-password"

# Deploy Bitween UI
az containerapp create \
  --name bitween-ui \
  --resource-group bitween-rg \
  --environment bitween-env \
  --image bitween-ui:latest \
  --target-port 80 \
  --ingress external
```

### AWS ECS with Fargate

```json
{
  "family": "bitween-api",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "executionRoleArn": "arn:aws:iam::account:role/ecsTaskExecutionRole",
  "containerDefinitions": [
    {
      "name": "bitween-api",
      "image": "bitween:latest",
      "portMappings": [
        {
          "containerPort": 8080,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "ConnectionStrings__DefaultConnection",
          "value": "Host=bitween-db.cluster-xyz.us-east-1.rds.amazonaws.com;Database=bitween;Username=bitween;Password=password"
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/bitween-api",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]
}
```

## Monitoring and Observability

### Health Checks

```yaml
# health-check.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: healthcheck-script
  namespace: bitween
data:
  healthcheck.sh: |
    #!/bin/bash
    API_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" http://bitween-api-service:8080/health)
    UI_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" http://bitween-ui-service:80)
    
    if [ "$API_HEALTH" = "200" ] && [ "$UI_HEALTH" = "200" ]; then
      echo "All services healthy"
      exit 0
    else
      echo "Health check failed - API: $API_HEALTH, UI: $UI_HEALTH"
      exit 1
    fi
---
apiVersion: batch/v1
kind: CronJob
metadata:
  name: health-check
  namespace: bitween
spec:
  schedule: "*/5 * * * *"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: health-check
            image: curlimages/curl:latest
            command: ["/bin/sh"]
            args: ["/scripts/healthcheck.sh"]
            volumeMounts:
            - name: script
              mountPath: /scripts
          volumes:
          - name: script
            configMap:
              name: healthcheck-script
              defaultMode: 0755
          restartPolicy: OnFailure
```

### Logging with ELK Stack

```yaml
# logging.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: filebeat-config
  namespace: bitween
data:
  filebeat.yml: |
    filebeat.inputs:
    - type: container
      paths:
        - /var/log/containers/bitween-*.log
      processors:
        - add_kubernetes_metadata:
            host: ${NODE_NAME}
            matchers:
            - logs_path:
                logs_path: "/var/log/containers/"
    
    output.elasticsearch:
      hosts: ["elasticsearch:9200"]
    
    setup.kibana:
      host: "kibana:5601"
```

## Security Considerations

### SSL/TLS Configuration

```bash
# Generate SSL certificates with Let's Encrypt
certbot certonly --webroot \
  -w /var/www/html \
  -d yourcompany.com \
  -d api.yourcompany.com \
  --email admin@yourcompany.com \
  --agree-tos \
  --non-interactive
```

### Network Security

```yaml
# network-policy.yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: bitween-network-policy
  namespace: bitween
spec:
  podSelector: {}
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    ports:
    - protocol: TCP
      port: 8080
    - protocol: TCP
      port: 80
  egress:
  - to:
    - podSelector:
        matchLabels:
          app: postgres
    ports:
    - protocol: TCP
      port: 5432
  - to: []
    ports:
    - protocol: TCP
      port: 443
    - protocol: TCP
      port: 80
```

## Backup and Disaster Recovery

### Database Backup

```bash
#!/bin/bash
# backup-db.sh

BACKUP_DIR="/backups"
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="bitween_backup_$DATE.sql"

# Create backup
pg_dump -h postgres-service -U bitween -d bitween > "$BACKUP_DIR/$BACKUP_FILE"

# Compress backup
gzip "$BACKUP_DIR/$BACKUP_FILE"

# Upload to S3 (optional)
aws s3 cp "$BACKUP_DIR/$BACKUP_FILE.gz" s3://your-backup-bucket/

# Clean old backups (keep last 7 days)
find $BACKUP_DIR -name "bitween_backup_*.sql.gz" -mtime +7 -delete
```

### Automated Backup CronJob

```yaml
# backup-cronjob.yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: database-backup
  namespace: bitween
spec:
  schedule: "0 2 * * *"  # Daily at 2 AM
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: backup
            image: postgres:15
            command: ["/bin/bash"]
            args: ["/scripts/backup-db.sh"]
            env:
            - name: PGPASSWORD
              valueFrom:
                secretKeyRef:
                  name: bitween-secrets
                  key: postgres-password
            volumeMounts:
            - name: backup-script
              mountPath: /scripts
            - name: backup-storage
              mountPath: /backups
          volumes:
          - name: backup-script
            configMap:
              name: backup-script
              defaultMode: 0755
          - name: backup-storage
            persistentVolumeClaim:
              claimName: backup-pvc
          restartPolicy: OnFailure
```

## Performance Optimization

### Application Settings

```json
{
  "Kestrel": {
    "Limits": {
      "MaxConcurrentConnections": 100,
      "MaxRequestBodySize": 52428800,
      "KeepAliveTimeout": "00:02:00"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Database=bitween;Username=bitween;Password=password;Pooling=true;Maximum Pool Size=50;Connection Idle Lifetime=300"
  }
}
```

### Resource Limits

```yaml
resources:
  requests:
    memory: "512Mi"
    cpu: "250m"
  limits:
    memory: "2Gi"
    cpu: "1000m"
```

### Horizontal Pod Autoscaler

```yaml
# hpa.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: bitween-api-hpa
  namespace: bitween
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: bitween-api
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

## Troubleshooting Production Issues

### Common Issues and Solutions

#### High Memory Usage
```bash
# Check memory usage
kubectl top pods -n bitween

# Increase memory limits
kubectl patch deployment bitween-api -n bitween -p '{"spec":{"template":{"spec":{"containers":[{"name":"bitween-api","resources":{"limits":{"memory":"2Gi"}}}]}}}}'
```

#### Database Connection Issues
```bash
# Check database connectivity
kubectl exec -it deployment/bitween-api -n bitween -- curl postgres-service:5432

# Check database logs
kubectl logs deployment/postgres -n bitween
```

#### SSL Certificate Issues
```bash
# Check certificate expiry
openssl x509 -in /etc/nginx/ssl/cert.pem -text -noout | grep "Not After"

# Renew certificate
certbot renew --dry-run
```

This comprehensive deployment guide provides multiple options for deploying the complete Bitween ecosystem to production environments with proper security, monitoring, and scalability considerations.
