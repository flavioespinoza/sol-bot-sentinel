FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SolBotSentinel/SolBotSentinel.csproj src/SolBotSentinel/
RUN dotnet restore src/SolBotSentinel/SolBotSentinel.csproj
COPY src/ src/
RUN dotnet publish src/SolBotSentinel/SolBotSentinel.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SolBotSentinel.dll"]
