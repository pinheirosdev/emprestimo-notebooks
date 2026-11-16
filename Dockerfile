FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS codigo
WORKDIR /projeto

COPY src/EmprestimoNotebooks/EmprestimoNotebooks.csproj src/EmprestimoNotebooks/
RUN dotnet restore src/EmprestimoNotebooks/EmprestimoNotebooks.csproj
COPY src/EmprestimoNotebooks/ src/EmprestimoNotebooks/

FROM codigo AS testes
COPY EmprestimoNotebooks.slnx ./
COPY tests/EmprestimoNotebooks.Tests/ tests/EmprestimoNotebooks.Tests/
RUN dotnet test EmprestimoNotebooks.slnx --nologo

FROM codigo AS build
RUN dotnet publish src/EmprestimoNotebooks/EmprestimoNotebooks.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
RUN install -d -m 0700 -o app -g app /dados
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmprestimoNotebooks.dll"]
