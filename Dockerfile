FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY src/EmprestimoNotebooks/EmprestimoNotebooks.csproj ./
RUN dotnet restore EmprestimoNotebooks.csproj

COPY src/EmprestimoNotebooks/ ./
RUN dotnet publish EmprestimoNotebooks.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
RUN install -d -m 0700 -o app -g app /dados
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmprestimoNotebooks.dll"]
