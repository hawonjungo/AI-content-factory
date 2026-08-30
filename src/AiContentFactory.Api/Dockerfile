FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AiContentFactory.sln ./
COPY src/AiContentFactory.Domain/*.csproj src/AiContentFactory.Domain/
COPY src/AiContentFactory.Application/*.csproj src/AiContentFactory.Application/
COPY src/AiContentFactory.Infrastructure/*.csproj src/AiContentFactory.Infrastructure/
COPY src/AiContentFactory.Api/*.csproj src/AiContentFactory.Api/
RUN dotnet restore src/AiContentFactory.Api/AiContentFactory.Api.csproj

COPY src/ src/
RUN dotnet publish src/AiContentFactory.Api/AiContentFactory.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AiContentFactory.Api.dll"]
