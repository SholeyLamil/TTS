# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Tts.Api/Tts.Api.csproj src/Tts.Api/
RUN dotnet restore src/Tts.Api/Tts.Api.csproj

COPY src/ src/
RUN dotnet publish src/Tts.Api/Tts.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tts.Api.dll"]
