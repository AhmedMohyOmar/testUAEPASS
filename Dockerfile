# Stage 1: Build the .NET API
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["GetOtpAPI.csproj", "./"]
RUN dotnet restore "GetOtpAPI.csproj"
COPY . .
RUN dotnet publish "GetOtpAPI.csproj" -c Release -o /app/publish

# Stage 2: Final image with matching Playwright dependencies
FROM mcr.microsoft.com/playwright/dotnet:v1.57.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# Standardize port for Container Apps
ENV ASPNETCORE_URLS=http://+:8080
# Force Playwright to use the container's built-in browsers
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
EXPOSE 8080

ENTRYPOINT ["dotnet", "GetOtpAPI.dll"]