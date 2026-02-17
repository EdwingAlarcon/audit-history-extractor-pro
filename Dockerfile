# Multi-stage Dockerfile for Audit History Extractor Pro

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln .
COPY src/AuditHistoryExtractorPro.Domain/*.csproj ./src/AuditHistoryExtractorPro.Domain/
COPY src/AuditHistoryExtractorPro.Application/*.csproj ./src/AuditHistoryExtractorPro.Application/
COPY src/AuditHistoryExtractorPro.Infrastructure/*.csproj ./src/AuditHistoryExtractorPro.Infrastructure/
COPY src/AuditHistoryExtractorPro.CLI/*.csproj ./src/AuditHistoryExtractorPro.CLI/
COPY src/AuditHistoryExtractorPro.UI/*.csproj ./src/AuditHistoryExtractorPro.UI/

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build CLI
WORKDIR /src/src/AuditHistoryExtractorPro.CLI
RUN dotnet build -c Release -o /app/cli/build

# Build UI
WORKDIR /src/src/AuditHistoryExtractorPro.UI
RUN dotnet build -c Release -o /app/ui/build

# Stage 2: Publish CLI
FROM build AS publish-cli
WORKDIR /src/src/AuditHistoryExtractorPro.CLI
RUN dotnet publish -c Release -o /app/cli/publish

# Stage 3: Publish UI
FROM build AS publish-ui
WORKDIR /src/src/AuditHistoryExtractorPro.UI
RUN dotnet publish -c Release -o /app/ui/publish

# Stage 4: Final CLI Image
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS cli
WORKDIR /app
COPY --from=publish-cli /app/cli/publish .
COPY config.example.yaml ./config.yaml

ENV DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "audit-extractor.dll"]

# Stage 5: Final UI Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS ui
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY --from=publish-ui /app/ui/publish .
COPY config.example.yaml ./config.yaml

ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "AuditHistoryExtractorPro.UI.dll"]

# Default to UI image
FROM ui AS final
