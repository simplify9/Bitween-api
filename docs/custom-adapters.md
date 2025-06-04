# Custom Adapters

Custom adapters are the heart of Bitween's extensibility. They allow you to implement custom business logic for validation, transformation, and processing of messages. This guide covers everything you need to know about building and deploying custom adapters.

> 💡 **Quick Start**: Before building custom adapters, check out our [pre-built adapters repository](https://github.com/simplify9/BitweenAdapters) which covers many common integration scenarios including SFTP, HTTP, email, and cloud storage operations.

## Overview

Bitween supports four types of custom adapters:

- **Validators**: Validate and enrich incoming messages
- **Mappers**: Transform data between different formats
- **Handlers**: Execute business logic and processing
- **Receivers**: Retrieve data from external sources

All adapters are implemented as serverless functions that expose HTTP endpoints.

## Adapter Interfaces

### IInfolinkValidator

Validates incoming messages and can enrich them with additional data.

```csharp
public interface IInfolinkValidator
{
    Task<object> Validate(XchangeFile input);
}
```

**Use Cases:**
- Data validation and schema checking
- Business rule validation
- Data enrichment from external sources
- Format standardization

### IInfolinkMapper

Transforms data from one format to another.

```csharp
public interface IInfolinkMapper
{
    Task<XchangeFile> Map(XchangeFile input);
}
```

**Use Cases:**
- Format conversion (JSON ↔ XML ↔ EDI)
- Data structure transformation
- Field mapping and renaming
- Data aggregation

### IInfolinkHandler

Executes the main business logic for processing messages.

```csharp
public interface IInfolinkHandler
{
    Task<XchangeFile> Handle(XchangeFile input);
}
```

**Use Cases:**
- Business process execution
- External API calls
- Database operations
- File generation and processing

### IInfolinkReceiver

Retrieves data from external sources on a scheduled basis.

```csharp
public interface IInfolinkReceiver
{
    Task<IEnumerable<XchangeFile>> Receive();
}
```

**Use Cases:**
- Scheduled data polling
- File system monitoring
- Database change detection
- API data synchronization

## Before You Start: Pre-built Adapters

Before developing custom adapters, consider using our comprehensive collection of pre-built, production-ready adapters available in the [BitweenAdapters repository](https://github.com/simplify9/BitweenAdapters):

### Available Pre-built Adapters

**Receivers (Data Input):**
- **SFTP/FTP Receiver**: File monitoring and polling
- **HTTP Receiver**: REST API polling and webhooks  
- **POP3 Email Receiver**: Email processing with attachments
- **Azure Blob/S3 Receivers**: Cloud storage monitoring

**Mappers (Data Transformation):**
- **Liquid Mapper**: Template-based transformation using Shopify Liquid
- **JSON to Delimited Mapper**: Convert JSON to CSV/TSV formats

**Handlers (Output & Processing):**
- **HTTP Handler**: REST API calls and webhooks
- **SMTP/SendGrid Handlers**: Email delivery and notifications
- **SFTP/FTP Handlers**: File uploads and transfers
- **Azure Blob/S3 Handlers**: Cloud storage operations
- **Microsoft Teams Handler**: Teams notifications

### Quick Setup with Pre-built Adapters
```bash
# Clone the adapters repository
git clone https://github.com/simplify9/BitweenAdapters.git

# Build and deploy specific adapter (example: SFTP)
cd BitweenAdapters/SW.InfolinkAdapters.Handlers.Ftp
docker build -t bitween-sftp-handler .
docker run -p 7000:80 bitween-sftp-handler
```

For more details, see our [Ecosystem Documentation](ecosystem.md).

## Creating Custom Adapters

### Project Structure

Create a new .NET project for your adapter:

```bash
dotnet new console -n MyCustomAdapter
cd MyCustomAdapter
dotnet add package SW.Serverless.Sdk
dotnet add package SW.PrimitiveTypes
```

### Basic Handler Example

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCustomAdapter
{
    public class OrderProcessingHandler : IInfolinkHandler
    {
        public OrderProcessingHandler()
        {
            // Configure expected content type
            Runner.Expect("ContentType", "application/json");
        }

        public async Task<XchangeFile> Handle(XchangeFile input)
        {
            try
            {
                // Parse input data
                var orderData = JsonSerializer.Deserialize<OrderData>(input.Data.ToString());
                
                // Execute business logic
                var processedOrder = await ProcessOrder(orderData);
                
                // Return result
                return new XchangeFile
                {
                    Data = processedOrder,
                    ContentType = "application/json"
                };
            }
            catch (Exception ex)
            {
                // Handle errors appropriately
                throw new ProcessingException($"Order processing failed: {ex.Message}", ex);
            }
        }

        private async Task<ProcessedOrder> ProcessOrder(OrderData order)
        {
            // Validate order data
            ValidateOrder(order);
            
            // Calculate totals
            var calculatedTotal = CalculateTotal(order);
            
            // Save to external system
            var orderId = await SaveToExternalSystem(order);
            
            // Send notifications
            await SendNotifications(order, orderId);
            
            return new ProcessedOrder
            {
                OriginalOrderId = order.Id,
                ProcessedOrderId = orderId,
                ProcessedTotal = calculatedTotal,
                ProcessedDate = DateTime.UtcNow,
                Status = "Processed"
            };
        }

        private void ValidateOrder(OrderData order)
        {
            if (string.IsNullOrEmpty(order.CustomerId))
                throw new ValidationException("Customer ID is required");
                
            if (order.Items == null || !order.Items.Any())
                throw new ValidationException("Order must contain at least one item");
        }

        private decimal CalculateTotal(OrderData order)
        {
            var subtotal = order.Items.Sum(item => item.Quantity * item.UnitPrice);
            var tax = subtotal * 0.1m; // 10% tax
            return subtotal + tax;
        }

        private async Task<string> SaveToExternalSystem(OrderData order)
        {
            // Implement external system integration
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsJsonAsync(
                "https://external-system.com/api/orders", 
                order);
            
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ExternalOrderResponse>();
            return result.OrderId;
        }

        private async Task SendNotifications(OrderData order, string processedOrderId)
        {
            // Send email notification
            await SendEmailNotification(order.CustomerEmail, processedOrderId);
            
            // Send webhook notification
            await SendWebhookNotification(order, processedOrderId);
        }
    }

    // Data models
    public class OrderData
    {
        public string Id { get; set; }
        public string CustomerId { get; set; }
        public string CustomerEmail { get; set; }
        public List<OrderItem> Items { get; set; }
    }

    public class OrderItem
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class ProcessedOrder
    {
        public string OriginalOrderId { get; set; }
        public string ProcessedOrderId { get; set; }
        public decimal ProcessedTotal { get; set; }
        public DateTime ProcessedDate { get; set; }
        public string Status { get; set; }
    }
}
```

### Validator Example

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using FluentValidation;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCustomAdapter
{
    public class OrderValidator : IInfolinkValidator
    {
        private readonly IValidator<OrderData> _validator;

        public OrderValidator()
        {
            _validator = new OrderDataValidator();
        }

        public async Task<object> Validate(XchangeFile input)
        {
            // Parse input data
            var orderData = JsonSerializer.Deserialize<OrderData>(input.Data.ToString());
            
            // Validate using FluentValidation
            var validationResult = await _validator.ValidateAsync(orderData);
            
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                throw new ValidationException($"Validation failed: {errors}");
            }
            
            // Enrich data if needed
            await EnrichOrderData(orderData);
            
            return orderData;
        }

        private async Task EnrichOrderData(OrderData order)
        {
            // Add customer information from external source
            var customer = await GetCustomerData(order.CustomerId);
            order.CustomerEmail = customer.Email;
            order.CustomerName = customer.Name;
            
            // Validate inventory availability
            foreach (var item in order.Items)
            {
                var availability = await CheckInventory(item.ProductId);
                if (availability.Available < item.Quantity)
                {
                    throw new ValidationException(
                        $"Insufficient inventory for product {item.ProductId}. " +
                        $"Requested: {item.Quantity}, Available: {availability.Available}");
                }
            }
        }
    }

    public class OrderDataValidator : AbstractValidator<OrderData>
    {
        public OrderDataValidator()
        {
            RuleFor(x => x.CustomerId)
                .NotEmpty()
                .WithMessage("Customer ID is required");
                
            RuleFor(x => x.Items)
                .NotNull()
                .Must(items => items.Any())
                .WithMessage("Order must contain at least one item");
                
            RuleForEach(x => x.Items)
                .SetValidator(new OrderItemValidator());
        }
    }

    public class OrderItemValidator : AbstractValidator<OrderItem>
    {
        public OrderItemValidator()
        {
            RuleFor(x => x.ProductId)
                .NotEmpty()
                .WithMessage("Product ID is required");
                
            RuleFor(x => x.Quantity)
                .GreaterThan(0)
                .WithMessage("Quantity must be greater than 0");
                
            RuleFor(x => x.UnitPrice)
                .GreaterThan(0)
                .WithMessage("Unit price must be greater than 0");
        }
    }
}
```

### Mapper Example

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System.Text.Json;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace MyCustomAdapter
{
    public class JsonToXmlMapper : IInfolinkMapper
    {
        public JsonToXmlMapper()
        {
            Runner.Expect("ContentType", "application/json");
        }

        public async Task<XchangeFile> Map(XchangeFile input)
        {
            // Parse JSON input
            var jsonData = JsonSerializer.Deserialize<OrderData>(input.Data.ToString());
            
            // Convert to XML
            var xmlOutput = ConvertToXml(jsonData);
            
            return new XchangeFile
            {
                Data = xmlOutput.ToString(),
                ContentType = "application/xml"
            };
        }

        private XElement ConvertToXml(OrderData order)
        {
            return new XElement("Order",
                new XAttribute("id", order.Id),
                new XElement("Customer",
                    new XAttribute("id", order.CustomerId),
                    new XElement("Email", order.CustomerEmail),
                    new XElement("Name", order.CustomerName)
                ),
                new XElement("Items",
                    order.Items.Select(item => 
                        new XElement("Item",
                            new XAttribute("productId", item.ProductId),
                            new XElement("Quantity", item.Quantity),
                            new XElement("UnitPrice", item.UnitPrice),
                            new XElement("Total", item.Quantity * item.UnitPrice)
                        )
                    )
                ),
                new XElement("OrderTotal", order.Items.Sum(i => i.Quantity * i.UnitPrice)),
                new XElement("ProcessedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            );
        }
    }
}
```

### Receiver Example

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCustomAdapter
{
    public class FileSystemReceiver : IInfolinkReceiver
    {
        private readonly string _watchPath;
        private readonly string _processedPath;

        public FileSystemReceiver()
        {
            _watchPath = Environment.GetEnvironmentVariable("WATCH_PATH") ?? "./incoming";
            _processedPath = Environment.GetEnvironmentVariable("PROCESSED_PATH") ?? "./processed";
            
            // Ensure directories exist
            Directory.CreateDirectory(_watchPath);
            Directory.CreateDirectory(_processedPath);
        }

        public async Task<IEnumerable<XchangeFile>> Receive()
        {
            var files = new List<XchangeFile>();
            
            // Get all files in watch directory
            var fileInfos = new DirectoryInfo(_watchPath)
                .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                .Where(f => f.CreationTime < DateTime.Now.AddMinutes(-1)) // Wait 1 minute for complete uploads
                .OrderBy(f => f.CreationTime);

            foreach (var fileInfo in fileInfos)
            {
                try
                {
                    // Read file content
                    var content = await File.ReadAllTextAsync(fileInfo.FullName);
                    
                    // Create XchangeFile
                    var xchangeFile = new XchangeFile
                    {
                        Data = content,
                        ContentType = "application/json",
                        FileName = fileInfo.Name,
                        Reference = Path.GetFileNameWithoutExtension(fileInfo.Name)
                    };
                    
                    files.Add(xchangeFile);
                    
                    // Move to processed directory
                    var processedFilePath = Path.Combine(_processedPath, fileInfo.Name);
                    File.Move(fileInfo.FullName, processedFilePath);
                    
                    // Log successful processing
                    Console.WriteLine($"Processed file: {fileInfo.Name}");
                }
                catch (Exception ex)
                {
                    // Log error and continue with next file
                    Console.WriteLine($"Error processing file {fileInfo.Name}: {ex.Message}");
                    
                    // Move to error directory
                    var errorPath = Path.Combine(_watchPath, "errors");
                    Directory.CreateDirectory(errorPath);
                    var errorFilePath = Path.Combine(errorPath, fileInfo.Name);
                    File.Move(fileInfo.FullName, errorFilePath);
                }
            }
            
            return files;
        }
    }
}
```

## Deployment

### Local Development

For local development, run your adapter as a simple console application:

```csharp
// Program.cs
using SW.Serverless.Sdk;

namespace MyCustomAdapter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Register your adapter
            Runner.Register<OrderProcessingHandler>();
            
            // Start the server
            await Runner.Run(args);
        }
    }
}
```

Run with:
```bash
dotnet run --urls="http://localhost:7000"
```

### Serverless Deployment

#### Azure Functions

1. Create Azure Function project:
```bash
func init MyCustomAdapter --dotnet
cd MyCustomAdapter
func new --name OrderProcessor --template "HTTP trigger"
```

2. Implement the function:
```csharp
[FunctionName("OrderProcessor")]
public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
    ILogger log)
{
    var handler = new OrderProcessingHandler();
    
    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
    var input = JsonSerializer.Deserialize<XchangeFile>(requestBody);
    
    var result = await handler.Handle(input);
    
    return new OkObjectResult(result);
}
```

#### AWS Lambda

1. Create Lambda project:
```bash
dotnet new lambda.EmptyFunction -n MyCustomAdapter
cd MyCustomAdapter
```

2. Implement the function:
```csharp
public async Task<APIGatewayProxyResponse> FunctionHandler(
    APIGatewayProxyRequest request, 
    ILambdaContext context)
{
    var handler = new OrderProcessingHandler();
    
    var input = JsonSerializer.Deserialize<XchangeFile>(request.Body);
    var result = await handler.Handle(input);
    
    return new APIGatewayProxyResponse
    {
        StatusCode = 200,
        Body = JsonSerializer.Serialize(result),
        Headers = new Dictionary<string, string> 
        { 
            { "Content-Type", "application/json" } 
        }
    };
}
```

### Docker Deployment

Create a Dockerfile for your adapter:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MyCustomAdapter.csproj", "./"]
RUN dotnet restore "MyCustomAdapter.csproj"
COPY . .
RUN dotnet build "MyCustomAdapter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyCustomAdapter.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyCustomAdapter.dll"]
```

Build and run:
```bash
docker build -t my-custom-adapter .
docker run -p 7000:80 my-custom-adapter
```

## Configuration in Bitween

Once your adapter is deployed, configure it in Bitween:

### Create Subscription with Custom Adapter

```json
{
  "name": "Order Processing Workflow",
  "documentId": 1,
  "partnerId": 1,
  "type": "Internal",
  "validatorEndpoint": "http://your-validator-endpoint",
  "mapperEndpoint": "http://your-mapper-endpoint", 
  "handlerEndpoint": "http://your-handler-endpoint",
  "filterExpression": {
    "orderType": { "operator": "equals", "value": "standard" }
  }
}
```

### Environment Variables

Configure your adapters using environment variables:

```bash
# Database connections
DATABASE_URL=postgresql://user:pass@host:5432/db

# External service URLs
EXTERNAL_API_URL=https://api.example.com
EXTERNAL_API_KEY=your-api-key

# File paths
WATCH_PATH=/app/incoming
PROCESSED_PATH=/app/processed

# Notification settings
SMTP_HOST=smtp.example.com
SMTP_USER=notifications@example.com
SMTP_PASS=smtp-password
```

## Best Practices

### Error Handling

```csharp
public async Task<XchangeFile> Handle(XchangeFile input)
{
    try
    {
        // Processing logic
        return await ProcessMessage(input);
    }
    catch (ValidationException ex)
    {
        // Re-throw validation errors for proper handling
        throw;
    }
    catch (HttpRequestException ex)
    {
        // Handle external service errors
        throw new ProcessingException("External service unavailable", ex);
    }
    catch (Exception ex)
    {
        // Log unexpected errors
        Console.WriteLine($"Unexpected error: {ex}");
        throw new ProcessingException("Processing failed", ex);
    }
}
```

### Logging

```csharp
public class OrderProcessingHandler : IInfolinkHandler
{
    private readonly ILogger<OrderProcessingHandler> _logger;

    public OrderProcessingHandler(ILogger<OrderProcessingHandler> logger)
    {
        _logger = logger;
    }

    public async Task<XchangeFile> Handle(XchangeFile input)
    {
        _logger.LogInformation("Processing order {OrderId}", input.Reference);
        
        try
        {
            var result = await ProcessOrder(input);
            _logger.LogInformation("Successfully processed order {OrderId}", input.Reference);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", input.Reference);
            throw;
        }
    }
}
```

### Performance

- Use async/await for I/O operations
- Implement connection pooling for database access
- Cache frequently accessed data
- Use streaming for large files
- Implement circuit breaker pattern for external services

### Security

- Validate all input data
- Use HTTPS for all communications
- Implement proper authentication/authorization
- Sanitize data before external system calls
- Log security events for auditing

## Testing

### Unit Testing

```csharp
[TestClass]
public class OrderProcessingHandlerTests
{
    [TestMethod]
    public async Task Handle_ValidOrder_ReturnsProcessedOrder()
    {
        // Arrange
        var handler = new OrderProcessingHandler();
        var input = new XchangeFile
        {
            Data = JsonSerializer.Serialize(new OrderData
            {
                Id = "ORDER-001",
                CustomerId = "CUST-123",
                Items = new List<OrderItem>
                {
                    new OrderItem { ProductId = "PROD-1", Quantity = 2, UnitPrice = 50.00m }
                }
            }),
            ContentType = "application/json"
        };

        // Act
        var result = await handler.Handle(input);

        // Assert
        Assert.IsNotNull(result);
        var processedOrder = JsonSerializer.Deserialize<ProcessedOrder>(result.Data.ToString());
        Assert.AreEqual("ORDER-001", processedOrder.OriginalOrderId);
        Assert.AreEqual("Processed", processedOrder.Status);
    }
}
```

### Integration Testing

```csharp
[TestMethod]
public async Task IntegrationTest_EndToEnd()
{
    // Setup test environment
    var testServer = new TestServer();
    var client = testServer.CreateClient();

    // Send test message to Bitween
    var response = await client.PostAsJsonAsync("/api/xchanges", testMessage);
    
    // Verify processing results
    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    
    // Check that adapter was called
    // Verify external system integration
    // Confirm notifications were sent
}
```

This comprehensive guide should help you build powerful custom adapters that extend Bitween's capabilities to meet your specific integration requirements.
