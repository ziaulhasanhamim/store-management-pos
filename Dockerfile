# Stage 1: Build Tailwind CSS
FROM node:22-slim AS css-build
WORKDIR /src
COPY package*.json ./
RUN npm ci || npm install
COPY . .
RUN npm run css:build

# Stage 2: Build & Publish .NET App
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

# Copy solution and project definitions for optimized layer caching
COPY POSApp.sln .
COPY POSApp/POSApp.csproj POSApp/
COPY POSApp.Client/POSApp.Client.csproj POSApp.Client/
COPY tests/POSApp.Tests.Unit/POSApp.Tests.Unit.csproj tests/POSApp.Tests.Unit/
COPY tests/POSApp.Tests.Integration/POSApp.Tests.Integration.csproj tests/POSApp.Tests.Integration/

RUN dotnet restore "POSApp/POSApp.csproj"

# Copy all source files
COPY . .
# Overwrite tailwind.css with the freshly compiled output
COPY --from=css-build /src/POSApp/wwwroot/tailwind.css ./POSApp/wwwroot/tailwind.css

WORKDIR "/src/POSApp"
RUN dotnet publish "POSApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    AUTO_MIGRATE=true

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "POSApp.dll"]
