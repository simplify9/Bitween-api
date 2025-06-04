# API Reference

This document provides comprehensive API documentation for the Bitween integration middleware. All endpoints follow REST conventions and use JSON for request/response payloads.

## Base URL

```
http://localhost:5000/api
```

## Authentication

Bitween uses JWT (JSON Web Tokens) for authentication. Include the token in the Authorization header:

```
Authorization: Bearer <your-jwt-token>
```

### Get Authentication Token

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "password"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiration": "2025-06-04T18:30:00Z"
}
```

## Partners

Partners represent external systems that integrate with Bitween.

### List Partners

```http
GET /api/partners
```

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "name": "External System A",
      "description": "Our main ERP system",
      "apiKey": "partner-api-key-123",
      "isActive": true,
      "createdOn": "2025-06-01T10:00:00Z"
    }
  ],
  "totalCount": 1,
  "pageSize": 50,
  "pageNumber": 1
}
```

### Get Partner

```http
GET /api/partners/{id}
```

**Response:**
```json
{
  "id": 1,
  "name": "External System A",
  "description": "Our main ERP system",
  "apiKey": "partner-api-key-123",
  "settings": {
    "endpoint": "https://erp.company.com/api",
    "timeout": 30000
  },
  "isActive": true,
  "createdOn": "2025-06-01T10:00:00Z"
}
```

### Create Partner

```http
POST /api/partners
Content-Type: application/json

{
  "name": "New Partner",
  "description": "Partner description",
  "settings": {
    "endpoint": "https://partner.com/api",
    "apiKey": "partner-key"
  }
}
```

**Response:**
```json
{
  "id": 2,
  "name": "New Partner",
  "description": "Partner description",
  "apiKey": "generated-api-key-456",
  "settings": {
    "endpoint": "https://partner.com/api",
    "apiKey": "partner-key"
  },
  "isActive": true,
  "createdOn": "2025-06-04T12:00:00Z"
}
```

### Update Partner

```http
PUT /api/partners/{id}
Content-Type: application/json

{
  "name": "Updated Partner Name",
  "description": "Updated description",
  "isActive": false
}
```

### Delete Partner

```http
DELETE /api/partners/{id}
```

## Documents

Documents define message types and their structure.

### List Documents

```http
GET /api/documents
```

**Query Parameters:**
- `pageNumber` (int): Page number (default: 1)
- `pageSize` (int): Page size (default: 50)
- `search` (string): Search term

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "name": "CustomerOrder",
      "format": "JSON",
      "promotedProperties": {
        "customerId": "$.customer.id",
        "orderTotal": "$.order.total",
        "orderDate": "$.order.date"
      },
      "createdOn": "2025-06-01T10:00:00Z"
    }
  ],
  "totalCount": 1,
  "pageSize": 50,
  "pageNumber": 1
}
```

### Get Document

```http
GET /api/documents/{id}
```

### Create Document

```http
POST /api/documents
Content-Type: application/json

{
  "name": "CustomerOrder",
  "format": "JSON",
  "description": "Customer order document type",
  "promotedProperties": {
    "customerId": "$.customer.id",
    "orderTotal": "$.order.total",
    "orderDate": "$.order.date",
    "orderType": "$.order.type"
  }
}
```

**Response:**
```json
{
  "id": 1,
  "name": "CustomerOrder",
  "format": "JSON",
  "description": "Customer order document type",
  "promotedProperties": {
    "customerId": "$.customer.id",
    "orderTotal": "$.order.total",
    "orderDate": "$.order.date",
    "orderType": "$.order.type"
  },
  "createdOn": "2025-06-04T12:00:00Z"
}
```

### Update Document

```http
PUT /api/documents/{id}
Content-Type: application/json

{
  "name": "Updated Document Name",
  "description": "Updated description",
  "promotedProperties": {
    "customerId": "$.customer.id",
    "orderTotal": "$.order.total"
  }
}
```

### Delete Document

```http
DELETE /api/documents/{id}
```

## Subscriptions

Subscriptions define how messages should be processed.

### List Subscriptions

```http
GET /api/subscriptions
```

**Query Parameters:**
- `partnerId` (int): Filter by partner
- `documentId` (int): Filter by document
- `type` (string): Filter by subscription type

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "name": "Process Customer Orders",
      "type": "Internal",
      "documentId": 1,
      "partnerId": 1,
      "filterExpression": {
        "customerId": { "operator": "exists" }
      },
      "validatorEndpoint": "http://localhost:7001",
      "mapperEndpoint": "http://localhost:7002",
      "handlerEndpoint": "http://localhost:7003",
      "isActive": true,
      "createdOn": "2025-06-01T10:00:00Z"
    }
  ]
}
```

### Get Subscription

```http
GET /api/subscriptions/{id}
```

### Create Subscription

```http
POST /api/subscriptions
Content-Type: application/json

{
  "name": "Process Customer Orders",
  "type": "Internal",
  "documentId": 1,
  "partnerId": 1,
  "filterExpression": {
    "customerId": { "operator": "exists" },
    "orderTotal": { "operator": "greaterThan", "value": 100 }
  },
  "validatorEndpoint": "http://localhost:7001",
  "mapperEndpoint": "http://localhost:7002",
  "handlerEndpoint": "http://localhost:7003",
  "schedule": {
    "type": "Recurring",
    "intervalMinutes": 60
  }
}
```

**Subscription Types:**
- `ApiCall`: Synchronous processing
- `Internal`: Asynchronous processing
- `Receiving`: Scheduled data retrieval
- `Aggregation`: Batch processing

**Filter Expression Operators:**
- `exists`: Property exists
- `equals`: Exact match
- `notEquals`: Not equal
- `greaterThan`: Greater than
- `lessThan`: Less than
- `contains`: String contains
- `startsWith`: String starts with
- `endsWith`: String ends with
- `in`: Value in list
- `notIn`: Value not in list

### Update Subscription

```http
PUT /api/subscriptions/{id}
Content-Type: application/json

{
  "name": "Updated Subscription",
  "isActive": false,
  "filterExpression": {
    "orderType": { "operator": "equals", "value": "priority" }
  }
}
```

### Delete Subscription

```http
DELETE /api/subscriptions/{id}
```

## Xchanges (Messages)

Xchanges represent individual message processing transactions.

### List Xchanges

```http
GET /api/xchanges
```

**Query Parameters:**
- `pageNumber` (int): Page number
- `pageSize` (int): Page size
- `documentId` (int): Filter by document
- `partnerId` (int): Filter by partner
- `status` (string): Filter by status
- `fromDate` (datetime): Filter from date
- `toDate` (datetime): Filter to date
- `reference` (string): Filter by reference
- `correlationId` (string): Filter by correlation ID

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "reference": "ORDER-001",
      "correlationId": "corr-123",
      "documentId": 1,
      "partnerId": 1,
      "status": "Processed",
      "promotedProperties": {
        "customerId": "CUST-123",
        "orderTotal": "299.99",
        "orderDate": "2025-06-04T10:30:00Z"
      },
      "inputFileId": "file-input-123",
      "outputFileId": "file-output-456",
      "responseFileId": "file-response-789",
      "createdOn": "2025-06-04T10:30:00Z",
      "processedOn": "2025-06-04T10:31:00Z"
    }
  ],
  "totalCount": 1,
  "pageSize": 50,
  "pageNumber": 1
}
```

### Get Xchange

```http
GET /api/xchanges/{id}
```

### Create Xchange

```http
POST /api/xchanges
Content-Type: application/json

{
  "documentId": 1,
  "reference": "ORDER-002",
  "correlationId": "corr-456",
  "data": {
    "customer": {
      "id": "CUST-456",
      "name": "Jane Smith",
      "email": "jane@example.com"
    },
    "order": {
      "total": 149.99,
      "date": "2025-06-04T14:30:00Z",
      "type": "standard",
      "items": [
        {
          "productId": "PROD-1",
          "quantity": 1,
          "unitPrice": 149.99
        }
      ]
    }
  }
}
```

**Response:**
```json
{
  "id": 2,
  "reference": "ORDER-002",
  "correlationId": "corr-456",
  "documentId": 1,
  "status": "Processing",
  "promotedProperties": {
    "customerId": "CUST-456",
    "orderTotal": "149.99",
    "orderDate": "2025-06-04T14:30:00Z"
  },
  "inputFileId": "file-input-789",
  "createdOn": "2025-06-04T14:30:00Z"
}
```

### Get Xchange Results

```http
GET /api/xchanges/{id}/results
```

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "xchangeId": 1,
      "subscriptionId": 1,
      "status": "Success",
      "processingTime": 1250,
      "outputFileId": "file-output-456",
      "responseFileId": "file-response-789",
      "errorMessage": null,
      "createdOn": "2025-06-04T10:31:00Z"
    }
  ]
}
```

### Get File Content

```http
GET /api/xchanges/{id}/files/{fileType}
```

**File Types:**
- `input`: Original input file
- `output`: Processed output file
- `response`: Response file

**Response:**
```json
{
  "fileName": "order-001.json",
  "contentType": "application/json",
  "content": "{ \"customer\": { \"id\": \"CUST-123\" } }",
  "size": 1024,
  "createdOn": "2025-06-04T10:30:00Z"
}
```

### Reprocess Xchange

```http
POST /api/xchanges/{id}/reprocess
```

This will reprocess the message through all matching subscriptions.

## Search

### Search Messages

```http
GET /api/search
```

**Query Parameters:**
- `query` (string): Search term
- `documentId` (int): Filter by document
- `partnerId` (int): Filter by partner
- `fromDate` (datetime): Date range start
- `toDate` (datetime): Date range end
- `pageNumber` (int): Page number
- `pageSize` (int): Page size

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "reference": "ORDER-001",
      "documentName": "CustomerOrder",
      "partnerName": "External System A",
      "status": "Processed",
      "promotedProperties": {
        "customerId": "CUST-123",
        "orderTotal": "299.99"
      },
      "createdOn": "2025-06-04T10:30:00Z"
    }
  ],
  "totalCount": 1,
  "pageSize": 50,
  "pageNumber": 1
}
```

### Advanced Search

```http
POST /api/search/advanced
Content-Type: application/json

{
  "criteria": [
    {
      "field": "promotedProperties.customerId",
      "operator": "equals",
      "value": "CUST-123"
    },
    {
      "field": "promotedProperties.orderTotal",
      "operator": "greaterThan",
      "value": "100"
    }
  ],
  "logicalOperator": "and",
  "fromDate": "2025-06-01T00:00:00Z",
  "toDate": "2025-06-04T23:59:59Z",
  "pageNumber": 1,
  "pageSize": 20
}
```

## Health Checks

### Application Health

```http
GET /api/health
```

**Response:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "database": {
      "status": "Healthy",
      "duration": "00:00:00.0050000"
    },
    "file_storage": {
      "status": "Healthy",
      "duration": "00:00:00.0020000"
    }
  }
}
```

### Database Health

```http
GET /api/health/db
```

### Ready Check

```http
GET /api/health/ready
```

### Live Check

```http
GET /api/health/live
```

## Notifications

### List Notifications

```http
GET /api/notifications
```

### Create Notification

```http
POST /api/notifications
Content-Type: application/json

{
  "xchangeId": 1,
  "type": "ProcessingComplete",
  "recipient": "admin@company.com",
  "subject": "Order Processing Complete",
  "message": "Order ORDER-001 has been processed successfully"
}
```

## Statistics

### Dashboard Statistics

```http
GET /api/statistics/dashboard
```

**Response:**
```json
{
  "totalMessages": 1250,
  "processedToday": 45,
  "successRate": 98.5,
  "averageProcessingTime": 850,
  "activeSubscriptions": 12,
  "recentActivity": [
    {
      "time": "2025-06-04T14:30:00Z",
      "message": "Processed ORDER-002",
      "status": "Success"
    }
  ]
}
```

### Processing Statistics

```http
GET /api/statistics/processing
```

**Query Parameters:**
- `fromDate` (datetime): Date range start
- `toDate` (datetime): Date range end
- `groupBy` (string): Group by hour/day/month

## Error Handling

All API endpoints return standard HTTP status codes and error responses:

### Success Responses
- `200 OK`: Successful request
- `201 Created`: Resource created successfully
- `204 No Content`: Successful request with no content

### Error Responses
- `400 Bad Request`: Invalid request data
- `401 Unauthorized`: Authentication required
- `403 Forbidden`: Insufficient permissions
- `404 Not Found`: Resource not found
- `409 Conflict`: Resource conflict
- `422 Unprocessable Entity`: Validation errors
- `500 Internal Server Error`: Server error

### Error Response Format

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "The request is invalid",
    "details": [
      {
        "field": "name",
        "message": "Name is required"
      }
    ],
    "traceId": "0HN7SRLF8R2QK:00000001"
  }
}
```

## Rate Limiting

API endpoints are rate limited:
- **Standard endpoints**: 1000 requests per hour per API key
- **File upload endpoints**: 100 requests per hour per API key
- **Search endpoints**: 500 requests per hour per API key

Rate limit information is included in response headers:
```
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 999
X-RateLimit-Reset: 1622832000
```

## Versioning

The API is versioned using the URL path:
```
/api/v1/partners
/api/v2/partners
```

Current version: `v1`

## Webhooks

Configure webhooks to receive notifications about processing events:

### Create Webhook

```http
POST /api/webhooks
Content-Type: application/json

{
  "name": "Order Processing Webhook",
  "url": "https://your-system.com/webhooks/bitween",
  "events": ["xchange.created", "xchange.processed", "xchange.failed"],
  "secret": "your-webhook-secret"
}
```

### Webhook Event Format

```json
{
  "id": "webhook-event-123",
  "type": "xchange.processed",
  "timestamp": "2025-06-04T14:30:00Z",
  "data": {
    "xchangeId": 1,
    "reference": "ORDER-001",
    "status": "Processed",
    "processingTime": 1250
  }
}
```

This API reference provides comprehensive documentation for integrating with Bitween. For more examples and detailed integration guides, see the other documentation files.
