# Stage 1: Build Blazor WASM app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*
# RUN apt-get update && apt-get install -y git python3 && rm -rf /var/lib/apt/lists/*
# RUN dotnet workload install wasm-tools
RUN git clone --depth 1 --branch master https://github.com/kekal/Dupless_finder.git .

RUN dotnet restore DuplessFinder.Web/DuplessFinder.Web.csproj
# RUN dotnet publish DuplessFinder.Web/DuplessFinder.Web.csproj
RUN dotnet publish DuplessFinder.Web/DuplessFinder.Web.csproj -c Debug -o /app/publish -p:RunAOTCompilation=false -p:PublishTrimmed=false

# Stage 2: Serve with nginx
FROM nginx:alpine
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY --from=build /src/nginx.conf /etc/nginx/conf.d/default.conf

RUN mkdir -p /var/cache/nginx/client_temp /var/cache/nginx/proxy_temp /var/cache/nginx/fastcgi_temp /var/cache/nginx/uwsgi_temp /var/cache/nginx/scgi_temp && \
    chown -R nginx:nginx /var/cache/nginx && \
    chown nginx:nginx /run

EXPOSE 8888

USER nginx
