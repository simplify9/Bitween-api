# Contributing to Bitween

We welcome contributions from the community! This guide explains how to contribute to the Bitween project.

## 🚀 Ways to Contribute

- 🐛 **Bug Reports**: Help us identify and fix issues
- 💡 **Feature Requests**: Suggest new features and improvements
- 📝 **Documentation**: Improve documentation and examples
- 🔧 **Code Contributions**: Submit bug fixes and new features
- 🧪 **Testing**: Help expand test coverage
- 🌐 **Translations**: Add localization support

## 🛠️ Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- Git
- IDE/Editor (Visual Studio, VS Code, or JetBrains Rider)
- Docker (optional, for integration testing)
- Database (PostgreSQL, MySQL, or SQL Server)

### Fork and Clone

1. Fork the repository on GitHub
2. Clone your fork locally:

```bash
git clone https://github.com/YOUR-USERNAME/bitween.git
cd bitween
```

3. Add the upstream remote:

```bash
git remote add upstream https://github.com/simplify9/bitween.git
```

### Build and Test

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the application
dotnet run --project SW.Bitween.Web
```

## 📋 Development Guidelines

### Code Style

We follow standard C# coding conventions:

- Use PascalCase for class names, method names, and properties
- Use camelCase for local variables and method parameters
- Use meaningful names for variables and methods
- Add XML documentation comments for public APIs

Example:

```csharp
/// <summary>
/// Processes an incoming exchange message
/// </summary>
/// <param name="xchange">The exchange to process</param>
/// <returns>The processing result</returns>
public async Task<XchangeResult> ProcessXchange(Xchange xchange)
{
    // Implementation here
}
```

### Project Structure

- `SW.Bitween.Web/` - Main web application
- `SW.Bitween.Api/` - Core business logic and domain models
- `SW.Bitween.Sdk/` - Client SDK and shared models
- `SW.Bitween.{Database}/` - Database-specific implementations
- `SW.Bitween.UnitTests/` - Unit tests
- `docs/` - Documentation

### Commit Messages

Follow the conventional commit format:

```
type(scope): description

[optional body]

[optional footer]
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

Examples:
```
feat(api): add support for XML document processing
fix(subscriptions): resolve filter expression parsing issue
docs(readme): update installation instructions
test(xchange): add unit tests for message validation
```

## 🐛 Reporting Bugs

### Before Submitting

1. Check existing issues to avoid duplicates
2. Update to the latest version to see if the issue persists
3. Gather relevant information about your environment

### Bug Report Template

```markdown
**Describe the bug**
A clear and concise description of what the bug is.

**To Reproduce**
Steps to reproduce the behavior:
1. Go to '...'
2. Click on '....'
3. Scroll down to '....'
4. See error

**Expected behavior**
A clear and concise description of what you expected to happen.

**Screenshots**
If applicable, add screenshots to help explain your problem.

**Environment:**
- OS: [e.g. Windows 10, Ubuntu 20.04]
- .NET Version: [e.g. 8.0]
- Bitween Version: [e.g. v1.0.0]
- Database: [e.g. PostgreSQL 15]

**Additional context**
Add any other context about the problem here.
```

## 💡 Feature Requests

### Before Submitting

1. Check if the feature already exists
2. Search existing feature requests
3. Consider if the feature fits the project scope

### Feature Request Template

```markdown
**Is your feature request related to a problem? Please describe.**
A clear and concise description of what the problem is.

**Describe the solution you'd like**
A clear and concise description of what you want to happen.

**Describe alternatives you've considered**
A clear and concise description of any alternative solutions or features you've considered.

**Additional context**
Add any other context or screenshots about the feature request here.
```

## 🔧 Pull Requests

### Before Submitting

1. Create a feature branch from `main`
2. Make your changes
3. Add tests for new functionality
4. Update documentation if needed
5. Ensure all tests pass
6. Follow the code style guidelines

### Pull Request Process

1. **Create a Branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make Changes**
   - Write clean, maintainable code
   - Add appropriate tests
   - Update documentation

3. **Test Your Changes**
   ```bash
   dotnet test
   dotnet run --project SW.Bitween.Web
   ```

4. **Commit Changes**
   ```bash
   git add .
   git commit -m "feat(scope): description of changes"
   ```

5. **Push to Your Fork**
   ```bash
   git push origin feature/your-feature-name
   ```

6. **Create Pull Request**
   - Go to GitHub and create a pull request
   - Use the pull request template
   - Provide a clear description
   - Link related issues

### Pull Request Template

```markdown
## Description
Brief description of the changes made.

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## Testing
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Manual testing completed

## Checklist
- [ ] My code follows the style guidelines of this project
- [ ] I have performed a self-review of my own code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes

## Related Issues
Fixes #(issue number)
```

## 🧪 Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test SW.Bitween.UnitTests

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Writing Tests

#### Unit Tests

```csharp
[TestClass]
public class XchangeServiceTests
{
    [TestMethod]
    public async Task ProcessXchange_ValidInput_ReturnsSuccess()
    {
        // Arrange
        var service = new XchangeService(mockRepo, mockLogger);
        var xchange = new Xchange { /* test data */ };

        // Act
        var result = await service.ProcessXchange(xchange);

        // Assert
        Assert.IsTrue(result.Success);
    }
}
```

#### Integration Tests

```csharp
[TestClass]
public class ApiIntegrationTests
{
    [TestMethod]
    public async Task PostXchange_ValidData_ReturnsCreated()
    {
        // Arrange
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/xchanges", testData);

        // Assert
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
```

### Test Guidelines

- Write tests for all public APIs
- Use descriptive test names
- Follow AAA pattern (Arrange, Act, Assert)
- Mock external dependencies
- Test both success and failure scenarios

## 📚 Documentation

### Types of Documentation

1. **API Documentation**: Inline XML comments
2. **User Guides**: Markdown files in `docs/`
3. **Code Comments**: For complex logic
4. **Examples**: Sample implementations

### Documentation Standards

- Use clear, concise language
- Provide code examples
- Include prerequisites and assumptions
- Keep documentation up to date with code changes

### Adding Documentation

```bash
# Add new documentation file
touch docs/new-feature.md

# Update existing documentation
vim docs/existing-file.md

# Test documentation locally
# (if using a static site generator)
```

## 🏗️ Architecture Decisions

### Adding New Features

Consider these questions:

1. Does this feature align with Bitween's core purpose?
2. Will it benefit the majority of users?
3. Can it be implemented without breaking existing functionality?
4. Does it follow established patterns in the codebase?

### Breaking Changes

- Avoid breaking changes when possible
- If necessary, provide migration path
- Update major version number
- Document breaking changes in release notes

## 🎯 Areas for Contribution

### High Priority

- [ ] Performance optimizations
- [ ] Enhanced error handling
- [ ] Additional database providers
- [ ] Improved monitoring/observability
- [ ] Security enhancements

### Medium Priority

- [ ] UI/UX improvements
- [ ] Additional adapter examples
- [ ] Integration with more external systems
- [ ] Enhanced search capabilities
- [ ] Better documentation

### Low Priority

- [ ] Additional language bindings
- [ ] Mobile applications
- [ ] Advanced analytics
- [ ] Machine learning features

## 🎉 Recognition

Contributors will be recognized in:

- Release notes
- Contributors section in README
- GitHub contributors page
- Project website (if applicable)

## 📞 Getting Help

If you need help with contributing:

- 💬 **Discussions**: Use GitHub Discussions for questions
- 🐛 **Issues**: Create an issue for bugs or feature requests
- 📧 **Email**: Contact maintainers directly for sensitive issues

## 📜 Code of Conduct

By participating in this project, you agree to abide by our Code of Conduct:

### Our Pledge

We pledge to make participation in our project a harassment-free experience for everyone, regardless of age, body size, disability, ethnicity, gender identity and expression, level of experience, nationality, personal appearance, race, religion, or sexual identity and orientation.

### Our Standards

Examples of behavior that contributes to creating a positive environment include:

- Using welcoming and inclusive language
- Being respectful of differing viewpoints and experiences
- Gracefully accepting constructive criticism
- Focusing on what is best for the community
- Showing empathy towards other community members

### Enforcement

Instances of abusive, harassing, or otherwise unacceptable behavior may be reported by contacting the project team. All complaints will be reviewed and investigated and will result in a response that is deemed necessary and appropriate to the circumstances.

## 📄 License

By contributing to Bitween, you agree that your contributions will be licensed under the MIT License.

---

Thank you for contributing to Bitween! 🚀
