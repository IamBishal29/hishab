# syntax=docker/dockerfile:1
# Hishab API. Build context: the repository root (shared build config lives there).
#   docker build -f src/api/Dockerfile -t hishab-api .

# --- build: full SDK, never shipped -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from project files only: this layer is reused until a dependency changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/api/Hishab.Domain/Hishab.Domain.csproj src/api/Hishab.Domain/
COPY src/api/Hishab.Application/Hishab.Application.csproj src/api/Hishab.Application/
COPY src/api/Hishab.Infrastructure/Hishab.Infrastructure.csproj src/api/Hishab.Infrastructure/
COPY src/api/Hishab.Api/Hishab.Api.csproj src/api/Hishab.Api/
RUN dotnet restore src/api/Hishab.Api/Hishab.Api.csproj

COPY src/api/ src/api/
RUN dotnet publish src/api/Hishab.Api/Hishab.Api.csproj -c Release --no-restore -o /app

# --- runtime: ASP.NET only, plus headless Chromium for labels and invoices (ADR 0008) -----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=build /app .

# Chromium's headless shell (all the PDF renderer launches; the full browser is ~400 MB more) and its Linux libraries via the Playwright driver that ships with the app (same version as
# the package; its node needs libatomic1), and curl for the container health check. Installed as root, then dropped.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libatomic1 \
    && ./.playwright/node/*/node ./.playwright/package/cli.js install --with-deps --only-shell chromium \
    && chmod -R a+rX /ms-playwright \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hishab.Api.dll"]
