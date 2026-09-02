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

# fontconfig + the font packages are NOT optional: captions are burned in with
# ffmpeg's subtitles filter (libass), and the aspnet base image ships no fonts
# at all - without these every caption renders as tofu boxes. DejaVu covers
# Vietnamese diacritics; Noto Core covers most other scripts.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        fontconfig \
        fonts-dejavu-core \
        fonts-noto-core \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

RUN mkdir -p /app/storage
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AiContentFactory.Api.dll"]
