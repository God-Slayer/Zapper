FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Zapper.sln ./
COPY src/Zapper.Api/Zapper.Api.csproj src/Zapper.Api/
COPY src/Zapper.Api.Controllers/Zapper.Api.Controllers.csproj src/Zapper.Api.Controllers/
COPY src/Zapper.Application/Zapper.Application.csproj src/Zapper.Application/
COPY src/Zapper.Domain/Zapper.Domain.csproj src/Zapper.Domain/
COPY src/Zapper.Infrastructure/Zapper.Infrastructure.csproj src/Zapper.Infrastructure/
RUN dotnet restore src/Zapper.Api/Zapper.Api.csproj

COPY src/ src/
RUN dotnet publish src/Zapper.Api/Zapper.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Zapper.Api.dll"]