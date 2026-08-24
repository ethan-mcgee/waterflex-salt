FROM node:26-alpine@sha256:aadf416b2cdce311a8811ba3f0608a61b77dbf997500e2eafe781b51f6a0b019 AS build
WORKDIR /app

COPY web/package.json ./package.json
COPY web/package-lock.json ./package-lock.json
RUN npm ci --no-audit --no-fund

COPY web/ .
RUN npm run build

FROM nginx:1.28.3-alpine@sha256:a8b39bd9cf0f83869a2162827a0caf6137ddf759d50a171451b335cecc87d236
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
