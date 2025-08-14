# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MMOGameServer.csproj", "./"]
RUN dotnet restore "MMOGameServer.csproj"
COPY . .
RUN dotnet build "MMOGameServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "MMOGameServer.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

# Copy terrain data
COPY terrain/ ./terrain/

# Expose port (Fly.io will override this with PORT env var)
EXPOSE 8080

ENTRYPOINT ["dotnet", "MMOGameServer.dll"]