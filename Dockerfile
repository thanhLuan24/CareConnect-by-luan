# =====================================================================
# CareConnect - Google Cloud Run Production Dockerfile (.NET 8.0)
# =====================================================================

# 1. Base runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Cloud Run mặc định truyền biến PORT=8080
ENV PORT=8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV EnableSwagger=true
ENV DOTNET_EnableDiagnostics=0

# Tạo thư mục dữ liệu dự phòng và phân quyền cho non-root user ($APP_UID)
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app

# 2. Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["web.csproj", "./"]
RUN dotnet restore "web.csproj"
COPY . .
RUN dotnet build "web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 3. Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 4. Final production image (Non-root security)
FROM base AS final
WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "web.dll"]
