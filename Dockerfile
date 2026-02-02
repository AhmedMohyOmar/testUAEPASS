# Stage 1: Build the .NET API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["GetOtpAPI.csproj", "./"]
RUN dotnet restore "GetOtpAPI.csproj"
COPY . .
RUN dotnet publish "GetOtpAPI.csproj" -c Release -o /app/publish

# Stage 2: Final image with Playwright dependencies
# We use the official Playwright image as the base to ensure Chromium works
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# Standardize port for Container Apps
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "GetOtpAPI.dll"]