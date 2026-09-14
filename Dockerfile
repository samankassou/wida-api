FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Wida.Api/Wida.Api.csproj Wida.Api/
COPY Wida.Bll/Wida.Bll.csproj Wida.Bll/
COPY Wida.Dal/Wida.Dal.csproj Wida.Dal/
RUN dotnet restore Wida.Api/Wida.Api.csproj
COPY Wida.Api/ Wida.Api/
COPY Wida.Bll/ Wida.Bll/
COPY Wida.Dal/ Wida.Dal/
RUN dotnet publish Wida.Api/Wida.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
RUN mkdir -p /app/uploads && chown app:app /app/uploads
USER app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Wida.Api.dll"]
