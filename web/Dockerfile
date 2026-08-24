FROM node:20-alpine@sha256:fb4cd12c85ee03686f6af5362a0b0d56d50c58a04632e6c0fb8363f609372293 AS build
WORKDIR /app

COPY web/package.json ./package.json
COPY web/package-lock.json ./package-lock.json
RUN npm ci --no-audit --no-fund

COPY web/ .
RUN npm run build

FROM nginx:1.31.4-alpine@sha256:db35bfc6b2951e7f8a72db5db120288c127ffaeeb4a6d4b95a26fead017d5913
RUN apk del \
		nginx-module-acme \
		nginx-module-geoip \
		nginx-module-image-filter \
		nginx-module-njs \
		nginx-module-xslt \
		nginx \
	&& apk add --no-cache nginx \
	&& apk upgrade --no-cache \
	&& rm -f /etc/nginx/conf.d/default.conf \
	&& sed -i '/^user[[:space:]]/d' /etc/nginx/nginx.conf \
	&& sed -i -E '/^[[:space:]]*pid[[:space:]]/d' /etc/nginx/nginx.conf \
	&& sed -i '1ipid /tmp/nginx.pid;' /etc/nginx/nginx.conf \
	&& sed -i -E '/^[[:space:]]*ssl_(protocols|session_cache|session_timeout|session_tickets)[[:space:]]/d' /etc/nginx/nginx.conf \
	&& mkdir -p /usr/share/nginx/html \
	&& chown -R nginx:nginx /var/cache/nginx /var/run /etc/nginx /usr/share/nginx/html
COPY web/nginx/default.conf /etc/nginx/http.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

USER nginx
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=4 \
	CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8080/health || exit 1

CMD ["nginx", "-g", "daemon off;"]
