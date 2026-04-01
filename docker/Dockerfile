FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY . .
RUN dotnet restore

# Build both Service (includes WebUI) and CLI
RUN dotnet publish src/EggPdf.Service/EggPdf.Service.csproj \
    -c Release \
    -o /app \
    --no-restore

RUN dotnet publish src/EggPdf.Cli/EggPdf.Cli.csproj \
    -c Release \
    -o /app/cli \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Install common fonts
RUN apk add --no-cache \
    fontconfig \
    font-noto \
    font-noto-cjk \
    font-noto-emoji \
    font-liberation \
    ttf-dejavu \
    && fc-cache -f

# Create non-root user
RUN adduser -D -h /app eggpdf
USER eggpdf

COPY --from=build /app .

# Add CLI as a command in PATH
ENV PATH="/app/cli:${PATH}"

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget -q --spider http://localhost:8080/health || exit 1

# Default: start the service (WebUI + API)
# Override with: docker run eggspot/eggpdf dotnet /app/cli/EggPdf.Cli.dll input.html -o output.pdf
ENTRYPOINT ["dotnet", "EggPdf.Service.dll"]
