FROM node:20-alpine AS build
WORKDIR /app

COPY web/package.json ./package.json
COPY web/package-lock.json ./package-lock.json
RUN npm ci --no-audit --no-fund

COPY web/ .
RUN npm run build

FROM nginx:1.27-alpine
COPY web/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=4 \
	CMD wget --quiet --spider http://localhost/health || exit 1

CMD ["nginx", "-g", "daemon off;"]
