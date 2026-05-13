FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DCF.ScoreScraper/DCF.ScoreScraper.csproj DCF.ScoreScraper/
COPY DCF.Data/DCF.Data.csproj DCF.Data/
COPY DCF.Api/DCF.Api.csproj DCF.Api/
RUN dotnet restore DCF.Api/DCF.Api.csproj
COPY DCF.ScoreScraper/ DCF.ScoreScraper/
COPY DCF.Data/ DCF.Data/
COPY DCF.Api/ DCF.Api/
WORKDIR /src/DCF.Api
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DCF.Api.dll"]
