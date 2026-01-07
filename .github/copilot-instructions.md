# Indigo-AiTools Repository Guidelines

## Repository Purpose
This repository develops **MCP-based tools** and **AI-powered solutions** using Large Language Models (LLMs), providing reusable services for both internal use and external client delivery.

## Technology Stack
- **.NET 9.0** - Primary development framework
- **Model Context Protocol (MCP)** - Core AI tooling protocol
- **Microsoft Semantic Kernel** - AI orchestration and embeddings
- **Ollama** - Local LLM integration
- **Azure OpenAI** - Cloud-based LLM services
- **CommandLineParser** - CLI application argument parsing
- **HtmlAgilityPack** - HTML parsing and manipulation
- **Newtonsoft.Json** - JSON serialization

## Coding Standards

### Project Structure
- Use solution folders to organize related projects
- Follow namespace pattern: `Tools.ExternalDevServices.<Area>.<ProjectName>`
- Console applications should have `OutputType=Exe`
- Library projects should use default output type

### Naming Conventions
- **Projects**: PascalCase descriptive names (e.g., `ConfluenceEmbeddingsGenerator`)
- **Namespaces**: Match project structure with dots as separators
- **Classes**: PascalCase with descriptive names
- **Methods**: PascalCase with action verbs (e.g., `GetDocumentAsync`, `CacheEmbeddingsAsync`)
- **Fields**: camelCase with underscore prefix for private fields

### Code Patterns
- Enable nullable reference types: `<Nullable>enable</Nullable>`
- Use implicit usings: `<ImplicitUsings>enable</ImplicitUsings>`
- Prefer async/await patterns for I/O operations
- Use cancellation tokens for long-running operations
- Implement proper disposal patterns with `IDisposable`

### Dependencies
- Reference projects using relative paths
- Prefer Microsoft.Extensions packages for logging and DI
- Use latest stable versions of NuGet packages
- Avoid circular dependencies between projects

### Integration Patterns
- Integration projects should encapsulate external API communication
- Use proper configuration patterns for API endpoints and credentials
- Implement robust error handling and logging
- Support both synchronous and asynchronous operations where appropriate

### AI/LLM Integration
- Use Model Context Protocol for AI tool interfaces
- Implement proper embeddings storage and retrieval patterns
- Support both local (Ollama) and cloud (Azure OpenAI) LLM providers
- Use semantic chunking for document processing
- Implement vector similarity search for relevant content retrieval