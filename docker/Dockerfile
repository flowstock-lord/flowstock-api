FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FlowStock.slnx ./
COPY src/FlowStock.Api/FlowStock.Api.csproj src/FlowStock.Api/
COPY src/FlowStock.Application/FlowStock.Application.csproj src/FlowStock.Application/
COPY src/FlowStock.Domain/FlowStock.Domain.csproj src/FlowStock.Domain/
COPY src/FlowStock.Infrastructure/FlowStock.Infrastructure.csproj src/FlowStock.Infrastructure/
RUN dotnet restore src/FlowStock.Api/FlowStock.Api.csproj

COPY src/ src/
RUN dotnet publish src/FlowStock.Api/FlowStock.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FlowStock.Api.dll"]
