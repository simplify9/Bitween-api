# Bitween Ecosystem

The Bitween integration platform is part of a comprehensive ecosystem of tools and components designed to simplify enterprise integration. This guide covers all companion projects and how they work together.

## 🏗️ Architecture Overview

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Bitween UI    │───▶│  Bitween Core   │───▶│ Bitween Adapters│
│  (Management)   │    │  (Processing)   │    │ (Integration)   │
│                 │    │                 │    │                 │
│ • Dashboard     │    │ • Message Hub   │    │ • SFTP/FTP      │
│ • Configuration │    │ • Processing    │    │ • HTTP/REST     │
│ • Monitoring    │    │ • Orchestration │    │ • Email/SMTP    │
│ • Audit Logs    │    │ • Storage       │    │ • Cloud Storage │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## 🖥️ Bitween UI - Web Management Interface

### Overview
BitweenUI is a modern, responsive web interface built with React that provides comprehensive management and monitoring capabilities for your Bitween integration platform.

**Repository**: [https://github.com/simplify9/BitweenUI](https://github.com/simplify9/BitweenUI)

### Key Features

#### 📊 Dashboard & Monitoring
- **Real-time Processing Metrics**: Message throughput, success rates, error counts
- **System Health Monitoring**: Service status, resource utilization
- **Performance Analytics**: Processing times, bottleneck identification
- **Interactive Charts**: Powered by Recharts for data visualization

#### ⚙️ Configuration Management
- **Document Types**: Visual schema designer with promoted properties
- **Partner Management**: Partner profiles and relationship mapping
- **Subscription Configuration**: Processing pipeline setup with drag-and-drop
- **Adapter Registration**: Visual endpoint configuration and testing

#### 🔍 Operations & Debugging
- **Message Tracking**: End-to-end message flow visibility
- **Audit Logs**: Comprehensive activity logging with search and filtering
- **Error Analysis**: Detailed error reports with stack traces
- **Message Replay**: Reprocess failed messages with one-click

#### 👥 User Management
- **Azure AD Integration**: Single sign-on with Microsoft 365
- **Role-based Access Control**: Granular permissions management
- **Multi-tenant Support**: Organization isolation and data security

### Technology Stack

```json
{
  "frontend": {
    "framework": "React 19",
    "language": "TypeScript",
    "styling": "Tailwind CSS",
    "state": "Redux Toolkit",
    "routing": "React Router",
    "auth": "Azure MSAL"
  },
  "development": {
    "bundler": "Vite",
    "testing": "Jest & React Testing Library",
    "linting": "ESLint",
    "package_manager": "npm/yarn/bun"
  }
}
```

### Installation & Setup

#### Development Environment

1. **Prerequisites**
   ```bash
   # Required tools
   node --version  # v18+ required
   npm --version   # v8+ required
   ```

2. **Clone and Install**
   ```bash
   git clone https://github.com/simplify9/BitweenUI.git
   cd BitweenUI
   npm install
   ```

3. **Environment Configuration**
   ```bash
   # Create environment file
   cp .env.example .env.local
   
   # Configure API endpoints
   echo "REACT_APP_API_URL=http://localhost:8080" >> .env.local
   echo "REACT_APP_AUTH_CLIENT_ID=your-azure-ad-client-id" >> .env.local
   ```

4. **Start Development Server**
   ```bash
   npm start
   # or for better performance with Bun
   npm run dev:bun
   ```

#### Production Deployment

**Docker Deployment**
```dockerfile
# Use the provided Dockerfile
FROM node:18-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/build /usr/share/nginx/html
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

```bash
# Build and run
docker build -t bitween-ui .
docker run -p 3000:80 -e REACT_APP_API_URL=https://api.yourcompany.com bitween-ui
```

**Kubernetes Deployment**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bitween-ui
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
        env:
        - name: REACT_APP_API_URL
          value: "http://bitween-api:8080"
---
apiVersion: v1
kind: Service
metadata:
  name: bitween-ui-service
spec:
  selector:
    app: bitween-ui
  ports:
  - port: 80
    targetPort: 80
  type: LoadBalancer
```

### Configuration Options

#### Authentication Setup
```javascript
// src/config/auth.js
export const msalConfig = {
  auth: {
    clientId: process.env.REACT_APP_AUTH_CLIENT_ID,
    authority: "https://login.microsoftonline.com/your-tenant-id",
    redirectUri: window.location.origin
  }
};
```

#### API Configuration
```javascript
// src/config/api.js
export const apiConfig = {
  baseURL: process.env.REACT_APP_API_URL,
  timeout: 30000,
  retries: 3
};
```

## 🔌 Bitween Adapters - Pre-built Integration Components

### Overview
BitweenAdapters is a comprehensive collection of production-ready, open-source adapters that cover the most common integration scenarios. These adapters are designed to be deployed as serverless functions or containerized services.

**Repository**: [https://github.com/simplify9/BitweenAdapters](https://github.com/simplify9/BitweenAdapters)

### Architecture Patterns

#### Adapter Types
```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ Receivers   │───▶│ Validators  │───▶│  Mappers    │───▶│  Handlers   │
│             │    │             │    │             │    │             │
│ • SFTP      │    │ • Schema    │    │ • Liquid    │    │ • HTTP      │
│ • HTTP      │    │ • Business  │    │ • JSON→CSV  │    │ • Email     │
│ • Email     │    │ • Custom    │    │ • Custom    │    │ • Files     │
│ • Cloud     │    │ • Rules     │    │ • Transform │    │ • Cloud     │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
```

### Available Adapters

#### 📥 Receivers (Data Input)

**FTP/SFTP Receiver**
- **Purpose**: Monitor FTP/SFTP servers for new files
- **Features**: Scheduled polling, file pattern matching, processed file handling
- **Configuration**:
  ```json
  {
    "host": "ftp.example.com",
    "port": 22,
    "username": "user",
    "password": "pass",
    "watchPath": "/incoming",
    "filePattern": "*.xml",
    "pollInterval": "00:05:00"
  }
  ```

**HTTP Receiver**
- **Purpose**: Webhook endpoints and scheduled API polling
- **Features**: Authentication, rate limiting, retry logic
- **Use Cases**: REST API polling, webhook handling

**POP3 Email Receiver**
- **Purpose**: Process emails with attachments
- **Features**: Email parsing, attachment extraction, spam filtering
- **Use Cases**: Invoice processing, support ticket creation

**Cloud Storage Receivers**
- **Azure Blob Receiver**: Monitor Azure storage containers
- **S3 Receiver**: Monitor AWS S3 buckets for new objects
- **Features**: Event-driven triggers, metadata extraction

#### 🔄 Mappers (Data Transformation)

**Liquid Mapper**
- **Purpose**: Template-based data transformation
- **Engine**: Shopify Liquid templating
- **Features**: Conditional logic, loops, filters, custom functions
- **Example**:
  ```liquid
  {
    "orderId": "{{ data.order.id }}",
    "customer": {
      "name": "{{ data.customer.firstName }} {{ data.customer.lastName }}",
      "email": "{{ data.customer.email | downcase }}"
    },
    "items": [
      {% for item in data.items %}
      {
        "sku": "{{ item.productCode }}",
        "quantity": {{ item.qty }},
        "price": {{ item.unitPrice | times: item.qty }}
      }{% unless forloop.last %},{% endunless %}
      {% endfor %}
    ]
  }
  ```

**JSON to Delimited Mapper**
- **Purpose**: Convert JSON to CSV, TSV, or custom delimited formats
- **Features**: Header customization, data type conversion, escaping
- **Configuration**:
  ```json
  {
    "delimiter": ",",
    "includeHeaders": true,
    "fields": [
      { "name": "orderId", "path": "order.id" },
      { "name": "customerName", "path": "customer.name" },
      { "name": "total", "path": "order.total", "type": "decimal" }
    ]
  }
  ```

#### 📤 Handlers (Output & Processing)

**HTTP Handler**
- **Purpose**: Make REST API calls, webhooks
- **Features**: Authentication methods, retry policies, response handling
- **Support**: GET, POST, PUT, DELETE, PATCH
- **Configuration**:
  ```json
  {
    "url": "https://api.example.com/orders",
    "method": "POST",
    "headers": {
      "Authorization": "Bearer {{token}}",
      "Content-Type": "application/json"
    },
    "retryPolicy": {
      "maxAttempts": 3,
      "backoffStrategy": "exponential"
    }
  }
  ```

**Email Handlers**
- **SMTP Handler**: Direct SMTP email sending
- **SendGrid Handler**: Professional email service integration
- **Features**: Templates, attachments, bulk sending, tracking

**File Transfer Handlers**
- **SFTP Handler**: Secure file uploads
- **Azure Blob Handler**: Cloud storage operations
- **S3 Handler**: AWS storage integration
- **Features**: Directory structure creation, file versioning, metadata

**Notification Handlers**
- **Microsoft Teams Handler**: Teams channel notifications
- **Slack Handler**: Slack messaging integration
- **Features**: Rich formatting, threading, mentions

### Deployment Patterns

#### Containerized Deployment
```bash
# Build specific adapter
cd SW.InfolinkAdapters.Handlers.Http
docker build -t bitween-http-handler .

# Run with configuration
docker run -p 7000:80 \
  -e HTTP_ENDPOINT="https://api.example.com" \
  -e AUTH_TYPE="Bearer" \
  -e AUTH_TOKEN="your-token" \
  bitween-http-handler
```

#### Serverless Deployment

**Azure Functions**
```csharp
[FunctionName("HttpHandler")]
public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
    ILogger log)
{
    var handler = new HttpHandler();
    var input = await req.ReadFromJsonAsync<XchangeFile>();
    var result = await handler.Handle(input);
    return new OkObjectResult(result);
}
```

**AWS Lambda**
```csharp
public async Task<APIGatewayProxyResponse> FunctionHandler(
    APIGatewayProxyRequest request, 
    ILambdaContext context)
{
    var handler = new HttpHandler();
    var input = JsonSerializer.Deserialize<XchangeFile>(request.Body);
    var result = await handler.Handle(input);
    
    return new APIGatewayProxyResponse
    {
        StatusCode = 200,
        Body = JsonSerializer.Serialize(result)
    };
}
```

#### Kubernetes Deployment
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bitween-adapters
spec:
  replicas: 3
  selector:
    matchLabels:
      app: bitween-adapters
  template:
    metadata:
      labels:
        app: bitween-adapters
    spec:
      containers:
      - name: http-handler
        image: bitween-http-handler:latest
        ports:
        - containerPort: 80
        env:
        - name: HTTP_ENDPOINT
          valueFrom:
            secretKeyRef:
              name: adapter-secrets
              key: http-endpoint
```

### Configuration in Bitween

#### Subscription Setup
```json
{
  "name": "Order Processing Pipeline",
  "documentId": 1,
  "partnerId": 1,
  "type": "Internal",
  "receiverEndpoint": "http://sftp-receiver:7000",
  "validatorEndpoint": "http://order-validator:7001",
  "mapperEndpoint": "http://liquid-mapper:7002",
  "handlerEndpoint": "http://http-handler:7003",
  "schedule": "0 */5 * * * *",
  "filterExpression": {
    "orderType": { "operator": "equals", "value": "standard" }
  }
}
```

#### Environment Configuration
```bash
# Adapter-specific configuration
SFTP_HOST=ftp.example.com
SFTP_PORT=22
SFTP_USERNAME=integration_user
SFTP_PASSWORD=secure_password
SFTP_WATCH_PATH=/incoming
SFTP_PROCESSED_PATH=/processed

# HTTP endpoint configuration
HTTP_BASE_URL=https://api.example.com
HTTP_AUTH_TYPE=Bearer
HTTP_AUTH_TOKEN=your-api-token
HTTP_TIMEOUT=30000

# Email configuration
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_USERNAME=notifications@company.com
SMTP_PASSWORD=email_password
```

## 🚀 Complete Integration Example

### Scenario: EDI Order Processing
Let's set up a complete integration pipeline using all ecosystem components:

#### 1. Setup Infrastructure
```bash
# Start Bitween Core
docker run -d --name bitween-core -p 8080:8080 bitween:latest

# Start Bitween UI
docker run -d --name bitween-ui -p 3000:80 \
  -e REACT_APP_API_URL=http://localhost:8080 \
  bitween-ui:latest

# Start Adapters
docker run -d --name sftp-receiver -p 7000:80 bitween-sftp-receiver:latest
docker run -d --name liquid-mapper -p 7001:80 bitween-liquid-mapper:latest
docker run -d --name http-handler -p 7002:80 bitween-http-handler:latest
```

#### 2. Configure via UI
1. Open Bitween UI at `http://localhost:3000`
2. Create Document Type for EDI orders
3. Configure Partner for trading partner
4. Setup Subscription with adapter endpoints

#### 3. Create Processing Pipeline
```json
{
  "subscription": {
    "name": "EDI Order Processing",
    "receiverEndpoint": "http://sftp-receiver:7000",
    "mapperEndpoint": "http://liquid-mapper:7001",
    "handlerEndpoint": "http://http-handler:7002",
    "schedule": "0 */2 * * * *"
  },
  "liquidTemplate": {
    "orderNumber": "{{ edi.order_number }}",
    "customerCode": "{{ edi.customer_id }}",
    "items": "{{ edi.line_items | map: 'sku' }}"
  }
}
```

#### 4. Monitor and Manage
- View real-time processing in the dashboard
- Track message flow through audit logs
- Handle errors via the UI error management
- Scale adapters based on throughput metrics

## 🤝 Contributing to the Ecosystem

### Repository Structure
```
Bitween Ecosystem/
├── Bitween/                 # Core integration platform
├── BitweenUI/              # Web management interface
├── BitweenAdapters/        # Pre-built integration components
└── Documentation/          # Comprehensive guides
```

### Contribution Guidelines

#### Core Platform (Bitween)
- Backend development (.NET)
- API enhancements
- Performance optimizations
- Database improvements

#### UI Platform (BitweenUI) 
- Frontend development (React/TypeScript)
- User experience improvements
- Dashboard enhancements
- Accessibility features

#### Adapters (BitweenAdapters)
- New adapter implementations
- Protocol support expansion
- Performance optimizations
- Documentation improvements

### Development Workflow
1. **Fork** the relevant repository
2. **Create** a feature branch
3. **Implement** changes with tests
4. **Document** new features
5. **Submit** pull request
6. **Collaborate** on code review

## 📈 Roadmap & Future Development

### Planned Features
- **GraphQL API**: Modern query interface
- **Real-time WebSockets**: Live monitoring and notifications
- **Advanced Analytics**: Machine learning insights
- **Multi-cloud Support**: Enhanced cloud-native capabilities
- **Low-code Designer**: Visual integration builder

### Community Initiatives
- **Adapter Marketplace**: Community-contributed adapters
- **Template Library**: Pre-built integration templates
- **Training Materials**: Video tutorials and workshops
- **Certification Program**: Professional integration expertise

---

The Bitween ecosystem provides a complete, production-ready integration platform with modern management tools and extensive adapter library. Whether you're building simple file transfers or complex B2B integrations, the ecosystem has the components you need to succeed.
