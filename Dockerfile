# ===== Build (.NET 9) =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiamos primero el .csproj para aprovechar caché en restore
COPY ./ApiCatalogo.csproj ./
RUN dotnet restore ./ApiCatalogo.csproj

# Copiamos el resto del código y publicamos
COPY . .
RUN dotnet publish ./ApiCatalogo.csproj -c Release -o /out /p:UseAppHost=false

# ===== Runtime (.NET 9) =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out ./

# App Runner usa 8080 por defecto
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet","ApiCatalogo.dll"]
