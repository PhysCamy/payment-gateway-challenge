# Instructions for candidates

This is the .NET version of the Payment Gateway challenge. If you haven't already read this [README.md](https://github.com/cko-recruitment/) on the details of this exercise, please do so now. 

## Template structure
```
src/
    PaymentGateway.Api - a skeleton ASP.NET Core Web API
test/
    PaymentGateway.Api.Tests - an empty xUnit test project
imposters/ - contains the bank simulator configuration. Don't change this

.editorconfig - don't change this. It ensures a consistent set of rules for submissions when reformatting code
docker-compose.yml - configures the bank simulator
PaymentGateway.sln
```

Feel free to change the structure of the solution, use a different test library etc.

## Running the application

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (for the bank simulator)

### 1. Start the bank simulator

```bash
docker-compose up bank_simulator -d
```

### 2. Run the API

```bash
cd src/PaymentGateway.Api
dotnet run
```

The API will be available at:
- HTTP: `http://localhost:5067`
- HTTPS: `https://localhost:7092`
- Swagger UI: `https://localhost:7092/swagger`

### Running the tests

```bash
dotnet test
```