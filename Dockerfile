# ESTÁGIO 1: Build (A cozinha)
# Usamos o SDK do .NET para compilar o código
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia os arquivos de projeto (.csproj) e restaura as dependências
# Isso é feito separadamente para aproveitar o cache do Docker
COPY ["DotNetOllamaIntegration.csproj", "./"]
RUN dotnet restore

# Copia o restante dos arquivos e compila a aplicação
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ESTÁGIO 2: Runtime (O prato pronto)
# Usamos uma imagem muito mais leve, apenas com o necessário para rodar a API
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Copia apenas os binários compilados do estágio anterior
COPY --from=build /app/publish .

# Define o comando de inicialização
ENTRYPOINT ["dotnet", "DotNetOllamaIntegration.dll"]