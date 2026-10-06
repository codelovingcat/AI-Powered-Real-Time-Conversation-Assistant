FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Conversa.slnx
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="/root/.dotnet/tools:$PATH"
RUN dotnet publish src/Conversa.Api/Conversa.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Conversa.Api.dll"]
