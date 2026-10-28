# syntax=docker/dockerfile:1.7
# One Dockerfile, five images.
#
# PROJECT selects which host is published. Because the project files are copied
# before the source, the NuGet restore is a cached layer shared by all five
# builds: only the service that changed is recompiled.

# ---------- stage 1: restore (cached across every service) -------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props ./
COPY src/MaltaShop.Contracts/MaltaShop.Contracts.csproj      src/MaltaShop.Contracts/
COPY src/MaltaShop.Shared/MaltaShop.Shared.csproj            src/MaltaShop.Shared/
COPY src/MaltaShop.Order/MaltaShop.Order.csproj              src/MaltaShop.Order/
COPY src/MaltaShop.Orchestration/MaltaShop.Orchestration.csproj src/MaltaShop.Orchestration/
COPY src/MaltaShop.Inventory/MaltaShop.Inventory.csproj      src/MaltaShop.Inventory/
COPY src/MaltaShop.Payment/MaltaShop.Payment.csproj          src/MaltaShop.Payment/
COPY src/MaltaShop.Notification/MaltaShop.Notification.csproj src/MaltaShop.Notification/
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"

# ---------- stage 2: compile and publish -------------------------------------
FROM restore AS build
ARG PROJECT
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" \
      -c Release -o /app --no-restore /p:UseAppHost=false

# ---------- stage 3: the image that ships ------------------------------------
# The ASP.NET runtime image carries the framework but no SDK, no compiler and no
# NuGet cache: about 110 MB against the 800 MB of the build image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
ARG PROJECT
ARG PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT} \
    DOTNET_EnableDiagnostics=0 \
    PORT=${PORT} \
    ENTRY_DLL=${PROJECT}.dll
WORKDIR /app
RUN apk add --no-cache curl \
 && adduser --disabled-password --no-create-home --uid 10101 service
COPY --from=build --chown=service:service /app ./
# Runs as an unprivileged account, so a breakout starts with no rights.
USER 10101:10101
EXPOSE ${PORT}
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS "http://127.0.0.1:${PORT}/health/ready" || exit 1
# `exec` keeps the application as PID 1, so SIGTERM reaches it and shutdown is clean.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet /app/$ENTRY_DLL"]
