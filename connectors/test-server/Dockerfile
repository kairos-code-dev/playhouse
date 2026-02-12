# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared build files and source tree used by PlayHouse.TestServer references
COPY Directory.Build.props ./
COPY PlayHouse.sln ./
COPY src/ src/
COPY tests/ tests/
COPY extensions/ extensions/

# Copy test-server project files
COPY connectors/test-server/proto/ connectors/test-server/proto/
COPY connectors/test-server/src/ connectors/test-server/src/

# Restore dependencies
WORKDIR /src/connectors/test-server/src/PlayHouse.TestServer
RUN dotnet restore

# Build and publish the application
RUN dotnet publish -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published application
COPY --from=build /app .

# Expose ports
# 8080: HTTP + WebSocket
# 8443: HTTPS + WSS
# 34001: TCP Play Server
# 34002: TCP+TLS Play Server
EXPOSE 8080 8443 34001 34002

# Set default environment variables
ENV ASPNETCORE_URLS=http://+:8080 \
    ENABLE_WEBSOCKET=true \
    ENABLE_TLS=false \
    TCP_PORT=34001 \
    TCP_TLS_PORT=34002 \
    HTTP_PORT=8080 \
    HTTPS_PORT=8443 \
    ZMQ_PLAY_PORT=15000 \
    ZMQ_API_PORT=15300

# Health check endpoint
HEALTHCHECK --interval=5s --timeout=3s --retries=5 --start-period=10s \
  CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "PlayHouse.TestServer.dll"]
