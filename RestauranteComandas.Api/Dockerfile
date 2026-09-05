FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["RestauranteComandas.Api.csproj", "./"]
RUN dotnet restore "RestauranteComandas.Api.csproj"

COPY . .

RUN dotnet publish "RestauranteComandas.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "RestauranteComandas.Api.dll"]