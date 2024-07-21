# SemanticKernel.Connectors.Oracle

[Semantic Kernel](https://github.com/microsoft/semantic-kernel) memory built on top of Oracle. Requires [Oracle Database 23ai](https://www.oracle.com/database/23ai/#ai-ml)

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Giorgi/SemanticKernel.Connectors.Oracle/dotnet.yml?branch=main&logo=GitHub&style=for-the-badge)](https://github.com/Giorgi/SemanticKernel.Connectors.Oracle/actions/workflows/dotnet.yml)
[![Coveralls](https://img.shields.io/coveralls/github/Giorgi/SemanticKernel.Connectors.Oracle?logo=coveralls&style=for-the-badge)](https://coveralls.io/github/Giorgi/SemanticKernel.Connectors.Oracle)
[![License](https://img.shields.io/badge/License-Mit-blue.svg?style=for-the-badge&logo=mit)](LICENSE.md)
[![Ko-Fi](https://img.shields.io/static/v1?style=for-the-badge&message=Support%20the%20Project&color=success&logo=ko-fi&label=$$)](https://ko-fi.com/U6U81LHU8)

[![NuGet SemanticKernel.Connectors.Oracle](https://img.shields.io/nuget/dt/SemanticKernel.Connectors.Oracle.svg?label=SemanticKernel.Connectors.Oracle&style=for-the-badge&logo=NuGet)](https://www.nuget.org/packages/SemanticKernel.Connectors.Oracle/)

![Project Icon](https://raw.githubusercontent.com/Giorgi/SemanticKernel.Connectors.Oracle/main/SemanticKernel.Connectors.Oracle/Logo.png "SemanticKernel.Connectors.Oracle Project Icon")

## Usage

```sh
dotnet add package SemanticKernel.Connectors.Oracle
```

```cs
var memoryWithOracle = new MemoryBuilder()
    .WithOpenAITextEmbeddingGeneration("text-embedding-3-small", "your-api-key")
    .WithMemoryStore(new OracleMemoryStore("Your-Oracle-Connection-String", 1536))
    .Build();
```
