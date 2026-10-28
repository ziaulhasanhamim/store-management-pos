# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

# Copy solution and project definitions for optimized layer caching
COPY JASIntStoreManagement.sln .
COPY JASIntStoreManagement/JASIntStoreManagement.csproj JASIntStoreManagement/
COPY JASIntStoreManagement.Client/JASIntStoreManagement.Client.csproj JASIntStoreManagement.Client/
COPY tests/JASIntStoreManagement.Tests.Unit/JASIntStoreManagement.Tests.Unit.csproj tests/JASIntStoreManagement.Tests.Unit/
COPY tests/JASIntStoreManagement.Tests.Integration/JASIntStoreManagement.Tests.Integration.csproj tests/JASIntStoreManagement.Tests.Integration/

RUN dotnet restore "JASIntStoreManagement/JASIntStoreManagement.csproj"

# Copy all source files and publish
COPY . .
WORKDIR "/src/JASIntStoreManagement"
RUN dotnet publish "JASIntStoreManagement.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    AUTO_MIGRATE=true

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "JASIntStoreManagement.dll"]
