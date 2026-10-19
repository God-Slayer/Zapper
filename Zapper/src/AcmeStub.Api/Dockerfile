FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/AcmeStub.Api/AcmeStub.Api.csproj src/AcmeStub.Api/
RUN dotnet restore src/AcmeStub.Api/AcmeStub.Api.csproj

COPY src/AcmeStub.Api/ src/AcmeStub.Api/
RUN dotnet publish src/AcmeStub.Api/AcmeStub.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AcmeStub.Api.dll"]