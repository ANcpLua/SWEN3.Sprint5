FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview@sha256:ed557d471a2b702b72fd1fd4835040bbbdfbd2532ae78cfc90546773d88a91d7 AS base
USER root
RUN apt-get update && apt-get install -y \
    ghostscript \
    tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview@sha256:1d5e6f2c1ece7d5826bafc8a7f2d54db2c6478a0f2bd1c995d05a37c0be4783e AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
RUN dotnet restore "PaperlessServices/PaperlessServices.csproj"
COPY . .
WORKDIR "/src/PaperlessServices"
RUN dotnet build "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaperlessServices.dll"]