# Stage 1: Build Blazor WASM app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DuplessFinder.Web/DuplessFinder.Web.csproj DuplessFinder.Web/
RUN dotnet restore DuplessFinder.Web/DuplessFinder.Web.csproj

COPY DuplessFinder.Web/ DuplessFinder.Web/
RUN dotnet publish DuplessFinder.Web/DuplessFinder.Web.csproj -c Release -o /app/publish

# Stage 2: Serve with nginx
FROM nginx:alpine
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

USER nginx
