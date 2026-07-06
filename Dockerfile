# Stage 1: Build the .NET API
# Updated to SDK 9.0 for .NET 9 support
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["GetOtpAPI.csproj", "./"]
RUN dotnet restore "GetOtpAPI.csproj"
COPY . .
RUN dotnet publish "GetOtpAPI.csproj" -c Release -o /app/publish

# Stage 2: Final image with matching Playwright dependencies
# Updated to v1.57.0 to resolve the "Executable doesn't exist" error
FROM mcr.microsoft.com/playwright/dotnet:v1.57.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# Standardize port for Container Apps (.NET 9 uses 8080 by default)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "GetOtpAPI.dll"]