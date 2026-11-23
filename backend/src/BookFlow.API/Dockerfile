# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/BookFlow.Domain/BookFlow.Domain.csproj src/BookFlow.Domain/
COPY src/BookFlow.Application/BookFlow.Application.csproj src/BookFlow.Application/
COPY src/BookFlow.Infrastructure/BookFlow.Infrastructure.csproj src/BookFlow.Infrastructure/
COPY src/BookFlow.API/BookFlow.API.csproj src/BookFlow.API/
RUN dotnet restore src/BookFlow.API/BookFlow.API.csproj
COPY src/ src/
RUN dotnet publish src/BookFlow.API/BookFlow.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata && rm -rf /var/lib/apt/lists/* \
    && groupadd -r bookflow && useradd -r -g bookflow bookflow
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Kolkata
COPY --from=build /app/publish .
USER bookflow
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "BookFlow.API.dll"]
